using Algorithm.Common;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Serilog;
using System.Collections.Concurrent;
using System.Globalization;

namespace Algorithm.General.OCR;

public class Executor : AlgorithmBase
{
    private sealed class OcrSnapshotRecord : IDisposable
    {
        public string OcrObjectId { get; }
        public float Confidence { get; private set; }
        public Mat Snapshot { get; private set; }

        public OcrSnapshotRecord(string ocrObjectId, float confidence, Mat snapshot)
        {
            OcrObjectId = ocrObjectId;
            Confidence = confidence;
            Snapshot = snapshot;
        }

        public void Update(float confidence, Mat snapshot)
        {
            Snapshot.Dispose();
            Snapshot = snapshot;
            Confidence = confidence;
        }

        public void Dispose()
        {
            if (!Snapshot.IsDisposed)
            {
                Snapshot.Dispose();
            }
        }
    }

    private sealed class BearerSnapshotDirectoryState : IDisposable
    {
        public object SyncRoot { get; } = new();
        public float MaxBearerConfidence { get; private set; } = float.MinValue;
        public Mat? BearerSnapshot { get; private set; }
        public Dictionary<string, OcrSnapshotRecord> OcrSnapshotsByObjectId { get; } = new(StringComparer.Ordinal);

        public void UpdateBearerSnapshot(float confidence, Mat snapshot)
        {
            BearerSnapshot?.Dispose();
            BearerSnapshot = snapshot;
            MaxBearerConfidence = confidence;
        }

        public void Dispose()
        {
            BearerSnapshot?.Dispose();
            BearerSnapshot = null;

            foreach (var snapshotRecord in OcrSnapshotsByObjectId.Values)
            {
                snapshotRecord.Dispose();
            }

            OcrSnapshotsByObjectId.Clear();
        }
    }

    private const string OcrBearerObjectIdPropertyName = "OcrBearerObjectId";
    private const string OcrBearerObjectLocalIdPropertyName = "OcrBearerObjectLocalId";
    private const string OcrBearerObjectTrackingIdPropertyName = "OcrBearerObjectTrackingId";
    private const string DefaultOcrSnapshotsDir = "OcrSnapshots";

    public string OcrType { get; private set; } = string.Empty;
    public string OcrBearerType { get; private set; } = string.Empty;
    public string OcrDevice { get; private set; } = "cuda";
    public int OcrDeviceId { get; private set; }
    public float ScoreThresh { get; private set; } = 0.5f;
    public string OcrSnapshotsDir { get; private set; } = DefaultOcrSnapshotsDir;

    private PaddleOcrAll? _all;
    private readonly ConcurrentDictionary<string, float> _maxBearerConfidenceById = new();
    private readonly ConcurrentDictionary<string, BearerSnapshotDirectoryState> _snapshotDirectoryStateByBearerId = new();

    private readonly Mat _kernelSharp = Mat.FromPixelData(3, 3, MatType.CV_32F, new float[,]
    {
        { 0, -1, 0 },
        { -1, 5, -1 },
        { 0, -1, 0 }
    });

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences) 
        : base(pipeline, preferences)
    {
        AlgorithmName = "OCR";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Optical character recognition algorithm.";
    }

    protected override void InitializeCore()
    {
        Subscribe(
            Services.GetRequiredService<ISubscriber<ObjectExpiredEvent>>(),
            ProcessEvent);

        OcrType = PreferenceParser.ParseStringValue(
            Preferences,
            "OcrType",
            "sign");

        OcrBearerType = PreferenceParser.ParseStringValue(
            Preferences,
            "OcrBearerType",
            "ship");

        OcrDevice = PreferenceParser.ParseStringValue(
            Preferences,
            "OcrDevice",
            "cuda");

        OcrDeviceId = PreferenceParser.ParseIntValue(
            Preferences,
            "OcrDeviceId",
            0);

        ScoreThresh = PreferenceParser.ParseFloatValue(
            Preferences,
            "ScoreThresh",
            0.5f);

        OcrSnapshotsDir = PreferenceParser.ParseStringValue(
            Preferences,
            "OcrSnapshotsDir",
            DefaultOcrSnapshotsDir);
        OcrSnapshotsDir.EnsureDirExistence();

        _all = CreateOcrEngine();
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var candidateOcrObjects = frame.DetectedObjects
            .Where(ocrObject => ocrObject.IsUnderAnalysis && ocrObject.Label == OcrType)
            .ToList();

        foreach (var bearerObject in frame.DetectedObjects)
        {
            if (!bearerObject.IsUnderAnalysis) continue;
            if (bearerObject.Label != OcrBearerType) continue;

            var relatedOcrObjects = candidateOcrObjects
                .Where(ocrObject => bearerObject != ocrObject && bearerObject.Bbox.Contains(ocrObject.Bbox))
                .OrderByDescending(ocrObject => ocrObject.Confidence)
                .ToList();

            if (relatedOcrObjects.Count == 0) continue;

            UpdateSnapshotDirectory(bearerObject, relatedOcrObjects);

            if (!IsBetterConfidence(bearerObject)) continue;

            foreach (var ocrObject in relatedOcrObjects)
            {
                RecordOcrBearerRelation(ocrObject, bearerObject);
                PerformOcr(ocrObject, bearerObject);
            }
        }

        return new AnalysisResult(true);
    }

    private bool IsBetterConfidence(DetectedObject bearerObject)
    {
        while (true)
        {
            if (!_maxBearerConfidenceById.TryGetValue(bearerObject.Id, out var currentMaxConfidence))
            {
                if (_maxBearerConfidenceById.TryAdd(bearerObject.Id, bearerObject.Confidence))
                {
                    return true;
                }

                continue;
            }

            if (bearerObject.Confidence <= currentMaxConfidence)
            {
                return false;
            }

            if (_maxBearerConfidenceById.TryUpdate(
                    bearerObject.Id,
                    bearerObject.Confidence,
                    currentMaxConfidence))
            {
                return true;
            }
        }
    }

    private static void RecordOcrBearerRelation(DetectedObject ocrObject, DetectedObject bearerObject)
    {
        ocrObject.SetProperty(OcrBearerObjectIdPropertyName, bearerObject.Id);
        ocrObject.SetProperty(OcrBearerObjectLocalIdPropertyName, bearerObject.LocalId);
        ocrObject.SetProperty(OcrBearerObjectTrackingIdPropertyName, bearerObject.TrackingId);
    }

    private void UpdateSnapshotDirectory(DetectedObject bearerObject, IReadOnlyList<DetectedObject> relatedOcrObjects)
    {
        var bearerObjectId = bearerObject.Id;
        var state = _snapshotDirectoryStateByBearerId.GetOrAdd(
            bearerObjectId,
            _ => new BearerSnapshotDirectoryState());

        lock (state.SyncRoot)
        {
            var hasChanges = TryUpdateBearerSnapshot(state, bearerObject);

            foreach (var ocrObject in relatedOcrObjects)
            {
                hasChanges |= TryUpdateOcrSnapshot(state, ocrObject);
            }

            if (hasChanges)
            {
                RewriteSnapshotDirectory(bearerObjectId, state);
            }
        }
    }

    private static bool TryUpdateBearerSnapshot(BearerSnapshotDirectoryState state, DetectedObject bearerObject)
    {
        if (bearerObject.Snapshot is null || bearerObject.Snapshot.Empty()) return false;
        if (bearerObject.Confidence <= state.MaxBearerConfidence) return false;

        state.UpdateBearerSnapshot(bearerObject.Confidence, bearerObject.Snapshot.Clone());
        return true;
    }

    private static bool TryUpdateOcrSnapshot(BearerSnapshotDirectoryState state, DetectedObject ocrObject)
    {
        if (ocrObject.Snapshot is null || ocrObject.Snapshot.Empty()) return false;

        var ocrObjectId = ocrObject.Id;
        var clonedSnapshot = ocrObject.Snapshot.Clone();

        if (!state.OcrSnapshotsByObjectId.TryGetValue(ocrObjectId, out var snapshotRecord))
        {
            state.OcrSnapshotsByObjectId[ocrObjectId] = new OcrSnapshotRecord(
                ocrObjectId,
                ocrObject.Confidence,
                clonedSnapshot);
            return true;
        }

        if (ocrObject.Confidence <= snapshotRecord.Confidence)
        {
            clonedSnapshot.Dispose();
            return false;
        }

        snapshotRecord.Update(ocrObject.Confidence, clonedSnapshot);
        return true;
    }

    private void RewriteSnapshotDirectory(string bearerObjectId, BearerSnapshotDirectoryState state)
    {
        var bearerDirectoryPath = Path.Combine(OcrSnapshotsDir, SanitizePathSegment(bearerObjectId));
        bearerDirectoryPath.EnsureDirExistence();

        foreach (var filePath in Directory.GetFiles(bearerDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly))
        {
            File.Delete(filePath);
        }

        if (state.BearerSnapshot is { IsDisposed: false } bearerSnapshot && !bearerSnapshot.Empty())
        {
            var bearerImagePath = Path.Combine(
                bearerDirectoryPath,
                $"000_bearer_{SanitizePathSegment(bearerObjectId)}_{FormatConfidence(state.MaxBearerConfidence)}.jpg");
            bearerSnapshot.SaveImage(bearerImagePath);
        }

        var orderedOcrSnapshots = state.OcrSnapshotsByObjectId.Values
            .OrderByDescending(snapshotRecord => snapshotRecord.Confidence)
            .ThenBy(snapshotRecord => snapshotRecord.OcrObjectId, StringComparer.Ordinal)
            .ToList();

        for (var index = 0; index < orderedOcrSnapshots.Count; index++)
        {
            var snapshotRecord = orderedOcrSnapshots[index];
            if (snapshotRecord.Snapshot.IsDisposed || snapshotRecord.Snapshot.Empty()) continue;

            var ocrImagePath = Path.Combine(
                bearerDirectoryPath,
                $"{index + 1:000}_{SanitizePathSegment(snapshotRecord.OcrObjectId)}_{FormatConfidence(snapshotRecord.Confidence)}.jpg");
            snapshotRecord.Snapshot.SaveImage(ocrImagePath);
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var sanitizedChars = value
            .Select(character => invalidFileNameChars.Contains(character) ? '_' : character)
            .ToArray();

        return new string(sanitizedChars);
    }

    private static string FormatConfidence(float confidence)
    {
        return confidence.ToString("F4", CultureInfo.InvariantCulture);
    }

    private void PerformOcr(DetectedObject ocrObject, DetectedObject bearerObject)
    {
        if (ocrObject.Snapshot is null || ocrObject.Snapshot.Empty()) return;

        var paddleOcrResult = RunOcr(ocrObject.Snapshot);

        foreach (var resultRegion in paddleOcrResult.Regions)
        {
            if (resultRegion.Score < ScoreThresh) continue;

            var text = resultRegion.Text;

            Log.Information(
                "'{bearerObjectId}' contains '{ocrObjectId}', content:{text}",
                bearerObject.Id,
                ocrObject.Id,
                text);

        }
    }

    private PaddleOcrResult RunOcr(Mat snapshot)
    {
        using var ocrInput = PrepareSnapshotForOcr(snapshot);
        using var ocrEnhancedInput = SharpenImageText(ocrInput);

        try
        {
            var result = EnsureOcrEngine().Run(ocrEnhancedInput);

            return result;
        }
        catch (Exception ex) when (IsOneDnnLayoutError(ex))
        {
            Log.Warning(
                ex,
                "OCR engine hit a oneDNN layout error. Recreating PaddleOCR engine and retrying once.");
            RecreateOcrEngine();
            var retriedResult = EnsureOcrEngine().Run(ocrInput);

            return retriedResult;
        }
    }

    private PaddleOcrAll EnsureOcrEngine()
    {
        return _all ??= CreateOcrEngine();
    }

    private void RecreateOcrEngine()
    {
        _all?.Dispose();
        _all = CreateOcrEngine();
    }

    private PaddleOcrAll CreateOcrEngine()
    {
        var baseModel = LocalFullModels.ChineseV5;
        var detectorDevice = ResolveOcrDevice();
        var classifierDevice = ResolveClassifierDevice();
        var recognizerDevice = ResolveOcrDevice();

        var paddleOcrAll = new PaddleOcrAll(baseModel, detectorDevice, classifierDevice, recognizerDevice)
        {
            AllowRotateDetection = true,
            Enable180Classification = true,
        };

        return paddleOcrAll;
    }

    private static Mat PrepareSnapshotForOcr(Mat snapshot)
    {
        return snapshot.Clone();
    }

    private Action<PaddleConfig> ResolveOcrDevice()
    {
        var normalizedDevice = OcrDevice.ToLowerInvariant();

        return normalizedDevice switch
        {
            "gpu" or "cuda" => PaddleDevice.Gpu(deviceId: OcrDeviceId),
            "cpu" => PaddleDevice.Blas(),
            _ => throw new NotSupportedException(
                $"Unsupported OCR device '{OcrDevice}'. Supported values: gpu, cuda, cpu"),
        };
    }

    private Action<PaddleConfig> ResolveClassifierDevice()
    {
        return IsGpuDevice(OcrDevice)
            ? PaddleDevice.Blas().And(config =>
            {
                config.MkldnnEnabled = false;
            })
            : ResolveOcrDevice();
    }

    private static bool IsGpuDevice(string device)
    {
        return device.Equals("gpu", StringComparison.OrdinalIgnoreCase) ||
               device.Equals("cuda", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOneDnnLayoutError(Exception ex)
    {
        return ex.ToString().Contains(
                   "layout should be ONEDNN",
                   StringComparison.OrdinalIgnoreCase) ||
               ex.ToString().Contains(
                   "Filter tensor's layout should be ONEDNN",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void ProcessEvent(ObjectExpiredEvent @event)
    {
        _maxBearerConfidenceById.TryRemove(@event.Id, out _);

        if (_snapshotDirectoryStateByBearerId.TryRemove(@event.Id, out var state))
        {
            if (string.Equals(@event.Label, OcrBearerType, StringComparison.OrdinalIgnoreCase))
            {
                ExportExpiredBearerEventArtifacts(@event, state);
            }

            state.Dispose();
        }
    }

    protected override void DisposeCore()
    {
        _maxBearerConfidenceById.Clear();

        foreach (var state in _snapshotDirectoryStateByBearerId.Values)
        {
            state.Dispose();
        }

        _snapshotDirectoryStateByBearerId.Clear();
        _all?.Dispose();
        _all = null;
    }

    private Mat SharpenImageText(Mat ocrSnapshot)
    {
        // 1. 转换为灰度图
        using Mat gray = new Mat();
        Cv2.CvtColor(ocrSnapshot, gray, ColorConversionCodes.BGR2GRAY);

        // 2. 去除噪声
        using Mat denoised = new Mat();
        Cv2.GaussianBlur(gray, denoised, new Size(3, 3), 0);

        // 3. 图像锐化
        Mat denoisedSharpened = new Mat();
        Cv2.Filter2D(denoised, denoisedSharpened, -1, _kernelSharp);

        return denoisedSharpened;
    }

    private void ExportExpiredBearerEventArtifacts(ObjectExpiredEvent @event, BearerSnapshotDirectoryState state)
    {
        if (string.IsNullOrWhiteSpace(EventSnapshotDir))
        {
            return;
        }

        var eventDirectoryPath = Path.Combine(EventSnapshotDir, SanitizePathSegment(@event.Id));
        eventDirectoryPath.EnsureDirExistence();

        lock (state.SyncRoot)
        {
            foreach (var filePath in Directory.GetFiles(eventDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly))
            {
                File.Delete(filePath);
            }

            if (state.BearerSnapshot is { IsDisposed: false } bearerSnapshot && !bearerSnapshot.Empty())
            {
                var bearerImagePath = Path.Combine(
                    eventDirectoryPath,
                    $"000_{SanitizePathSegment(@event.Id)}_{FormatConfidence(state.MaxBearerConfidence)}.jpg");
                bearerSnapshot.SaveImage(bearerImagePath);
            }

            var topOcrSnapshots = state.OcrSnapshotsByObjectId.Values
                .OrderByDescending(snapshotRecord => snapshotRecord.Confidence)
                .ThenBy(snapshotRecord => snapshotRecord.OcrObjectId, StringComparer.Ordinal)
                .Take(3)
                .ToList();

            for (var index = 0; index < topOcrSnapshots.Count; index++)
            {
                var snapshotRecord = topOcrSnapshots[index];
                if (snapshotRecord.Snapshot.IsDisposed || snapshotRecord.Snapshot.Empty()) continue;

                var ocrImagePath = Path.Combine(
                    eventDirectoryPath,
                    $"{index + 1:000}_{SanitizePathSegment(snapshotRecord.OcrObjectId)}_{FormatConfidence(snapshotRecord.Confidence)}.jpg");
                snapshotRecord.Snapshot.SaveImage(ocrImagePath);
            }
        }
    }
}
