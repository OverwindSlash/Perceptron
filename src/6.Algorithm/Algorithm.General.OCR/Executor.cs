using Algorithm.Common;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Serilog;
using System.Collections.Concurrent;

namespace Algorithm.General.OCR;

public class Executor : AlgorithmBase
{
    public string OcrType { get; private set; } = string.Empty;
    public string OcrBearerType { get; private set; } = string.Empty;
    public string OcrDevice { get; private set; } = "cuda";
    public int OcrDeviceId { get; private set; }
    public float ScoreThresh { get; private set; } = 0.5f;

    private PaddleOcrAll? _all;
    private readonly ConcurrentDictionary<string, float> _maxBearerConfidenceById = new();

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

        _all = CreateOcrEngine();
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        foreach (var ocrObject in frame.DetectedObjects)
        {
            if (!ocrObject.IsUnderAnalysis) continue;

            if (ocrObject.Label != OcrType) continue;
            var shouldPerformOcr = false;

            foreach (var bearerObject in frame.DetectedObjects)
            {
                if (!bearerObject.IsUnderAnalysis) continue;

                if (bearerObject == ocrObject) continue;

                if (bearerObject.Label != OcrBearerType) continue;

                if (!bearerObject.Bbox.Contains(ocrObject.Bbox)) continue;

                if (!IsBetterConfidence(bearerObject)) continue;

                PerformOcr(ocrObject);
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

    private void PerformOcr(DetectedObject ocrObject)
    {
        if (ocrObject.Snapshot is null || ocrObject.Snapshot.Empty()) return;

        var paddleOcrResult = RunOcr(ocrObject.Snapshot);

        foreach (var resultRegion in paddleOcrResult.Regions)
        {
            if (resultRegion.Score < ScoreThresh) continue;

            var text = resultRegion.Text;

            Log.Information("License recognized:{license}", text);

            //ocrObject.Snapshot.SaveImage($"{text}.jpg");
        }
    }

    private PaddleOcrResult RunOcr(Mat snapshot)
    {
        using var ocrInput = PrepareSnapshotForOcr(snapshot);

        try
        {
            var result = EnsureOcrEngine().Run(ocrInput);

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
    }

    protected override void DisposeCore()
    {
        _maxBearerConfidenceById.Clear();
        _all?.Dispose();
        _all = null;
    }
}
