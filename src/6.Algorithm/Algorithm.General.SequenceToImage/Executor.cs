using Algorithm.Common;
using Algorithm.Common.Event;
using Algorithm.Common.LLM;
using Algorithm.General.SequenceToImage.Event;
using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;

namespace Algorithm.General.SequenceToImage;

public class Executor : AlgorithmBase, ILLMResultHandler
{
    public const int DefaultSequenceLength = 4;
    public const int DefaultFrameStride = 1;
    public const int DefaultSequenceImageJpegQuality = 80;
    public const string SequenceImageFrameJpegPropertyName = "SequenceImageFrameJpeg";
    public const string DefaultLayout = "Horizontal";
    public const int DefaultTileWidth = 0;
    public const int DefaultTileHeight = 0;
    public const bool DefaultDrawFrameLabels = true;
    public const int DefaultRequestTtlSeconds = 120;
    public const string DefaultQueuePolicy = "EventAnchored";
    public const string DefaultTimeoutPolicy = "Drop";

    public int SequenceLength { get; private set; }
    public int FrameStride { get; private set; }
    public int SequenceImageJpegQuality { get; private set; }
    public SequenceImageLayout Layout { get; private set; }
    public int TileWidth { get; private set; }
    public int TileHeight { get; private set; }
    public bool DrawFrameLabels { get; private set; }
    public int RequestTtlSeconds { get; private set; }
    public LLMQueuePolicy QueuePolicy { get; private set; }
    public LLMTimeoutPolicy OnTimeout { get; private set; }
    public string RequesterAlgorithmName => AlgorithmName;

    private readonly object _bufferSync = new();
    private readonly Dictionary<string, Queue<BufferedFrame>> _sourceBuffers = new();
    private readonly Dictionary<string, long> _sourceFrameCounters = new();
    private readonly ConcurrentDictionary<string, PendingSequence> _pendingSequences = new();

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Sequence To Image";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Stitch consecutive video frames into one image and optionally submit it for frame-level LLM analysis.";
    }

    public override bool Initialize()
    {
        EnsureDefaultPreference("PerformLLMAnalysis", "true");
        EnsureDefaultPreference("LLMPromptFile", "sequence-to-image-prompt.md");

        SequenceLength = Math.Max(1, PreferenceParser.ParseIntValue(Preferences, "SequenceLength", DefaultSequenceLength));
        FrameStride = Math.Max(1, PreferenceParser.ParseIntValue(Preferences, "FrameStride", DefaultFrameStride));
        SequenceImageJpegQuality = Math.Clamp(
            PreferenceParser.ParseIntValue(Preferences, "SequenceImageJpegQuality", DefaultSequenceImageJpegQuality),
            1,
            100);
        Layout = ParseLayout(PreferenceParser.ParseStringValue(Preferences, "Layout", DefaultLayout));
        TileWidth = Math.Max(0, PreferenceParser.ParseIntValue(Preferences, "TileWidth", DefaultTileWidth));
        TileHeight = Math.Max(0, PreferenceParser.ParseIntValue(Preferences, "TileHeight", DefaultTileHeight));
        DrawFrameLabels = PreferenceParser.ParseBoolValue(Preferences, "DrawFrameLabels", DefaultDrawFrameLabels);
        RequestTtlSeconds = Math.Max(1, PreferenceParser.ParseIntValue(Preferences, "RequestTtlSeconds", DefaultRequestTtlSeconds));
        QueuePolicy = ParseQueuePolicy(PreferenceParser.ParseStringValue(Preferences, "QueuePolicy", DefaultQueuePolicy));
        OnTimeout = ParseTimeoutPolicy(PreferenceParser.ParseStringValue(Preferences, "OnTimeout", DefaultTimeoutPolicy));

        return base.Initialize();
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        List<BufferedFrame>? sequenceFrames = null;
        try
        {
            ProcessTimedOutSequences();

            if (frame.IsBlankFrame || frame.Scene.Empty())
            {
                return new AnalysisResult(true);
            }

            if (!TryTakeReadySequence(frame, out sequenceFrames) || sequenceFrames == null)
            {
                return new AnalysisResult(true);
            }

            using var sequenceImage = BuildSequenceImage(sequenceFrames);
            var sequenceImageJpeg = SetSequenceProperties(frame, sequenceFrames, sequenceImage);

            if (WillPerformLLMAnalysis)
            {
                MarkFrameForLLM(frame, sequenceFrames, sequenceImage, sequenceImageJpeg);
            }

            Log.Information("Sequence image built. SourceId: {SourceId}, FrameIds: {FrameIds}, FrameStride: {FrameStride}, Layout: {Layout}, Size: {Width}x{Height}",
                frame.SourceId,
                string.Join(',', sequenceFrames.Select(item => item.FrameId)),
                FrameStride,
                Layout,
                sequenceImage.Width,
                sequenceImage.Height);

            return new AnalysisResult(true);
        }
        catch (Exception ex) when (ex is OpenCVException or ObjectDisposedException or InvalidOperationException)
        {
            Log.Warning(ex, "Failed to build sequence image. SourceId: {SourceId}, FrameId: {FrameId}",
                frame.SourceId, frame.FrameId);
            return new AnalysisResult(false);
        }
        finally
        {
            DisposeBufferedFrames(sequenceFrames);
            frame.Dispose();
        }
    }

    private bool TryTakeReadySequence(Frame frame, out List<BufferedFrame>? sequenceFrames)
    {
        sequenceFrames = null;

        lock (_bufferSync)
        {
            if (!ShouldCollectFrame(frame.SourceId))
            {
                return false;
            }

            var bufferedFrame = CreateBufferedFrame(frame);

            if (!_sourceBuffers.TryGetValue(frame.SourceId, out var buffer))
            {
                buffer = new Queue<BufferedFrame>(SequenceLength);
                _sourceBuffers[frame.SourceId] = buffer;
            }

            buffer.Enqueue(bufferedFrame);
            if (buffer.Count < SequenceLength)
            {
                return false;
            }

            sequenceFrames = buffer.ToList();
            buffer.Clear();
            return true;
        }
    }

    private bool ShouldCollectFrame(string sourceId)
    {
        _sourceFrameCounters.TryGetValue(sourceId, out var frameCounter);
        _sourceFrameCounters[sourceId] = frameCounter + 1;

        return frameCounter % FrameStride == 0;
    }

    private BufferedFrame CreateBufferedFrame(Frame frame)
    {
        var scene = ClonePreparedScene(frame.Scene);
        return new BufferedFrame(
            frame.SourceId,
            frame.FrameId,
            frame.OffsetMilliSec,
            frame.UtcTimeStamp,
            scene);
    }

    private Mat ClonePreparedScene(Mat source)
    {
        var targetSize = ResolveTileSize(source.Width, source.Height);
        if (targetSize.Width == source.Width && targetSize.Height == source.Height)
        {
            return source.Clone();
        }

        var resized = new Mat();
        Cv2.Resize(source, resized, targetSize, 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private CvSize ResolveTileSize(int sourceWidth, int sourceHeight)
    {
        if (TileWidth <= 0 && TileHeight <= 0)
        {
            return new CvSize(sourceWidth, sourceHeight);
        }

        if (TileWidth > 0 && TileHeight > 0)
        {
            return new CvSize(TileWidth, TileHeight);
        }

        if (TileWidth > 0)
        {
            var height = Math.Max(1, (int)Math.Round(sourceHeight * (TileWidth / (double)Math.Max(1, sourceWidth))));
            return new CvSize(TileWidth, height);
        }

        var width = Math.Max(1, (int)Math.Round(sourceWidth * (TileHeight / (double)Math.Max(1, sourceHeight))));
        return new CvSize(width, TileHeight);
    }

    private Mat BuildSequenceImage(IReadOnlyList<BufferedFrame> frames)
    {
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("Sequence frame list is empty.");
        }

        var targetSize = frames[0].Scene.Size();
        var tiles = new List<Mat>(frames.Count);

        try
        {
            for (var i = 0; i < frames.Count; i++)
            {
                var tile = CloneTile(frames[i].Scene, targetSize);
                if (DrawFrameLabels)
                {
                    DrawFrameLabel(tile, frames[i], i + 1, frames.Count);
                }

                tiles.Add(tile);
            }

            if (tiles.Count == 1)
            {
                return tiles[0].Clone();
            }

            var sequenceImage = new Mat();
            if (Layout == SequenceImageLayout.Vertical)
            {
                Cv2.VConcat(tiles, sequenceImage);
            }
            else
            {
                Cv2.HConcat(tiles, sequenceImage);
            }

            //sequenceImage.SaveImage("seq.jpg");

            return sequenceImage;
        }
        finally
        {
            foreach (var tile in tiles)
            {
                tile.Dispose();
            }
        }
    }

    private static Mat CloneTile(Mat source, CvSize targetSize)
    {
        if (source.Width == targetSize.Width && source.Height == targetSize.Height)
        {
            return source.Clone();
        }

        var resized = new Mat();
        Cv2.Resize(source, resized, targetSize, 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private static void DrawFrameLabel(Mat image, BufferedFrame frame, int index, int total)
    {
        var text = $"{index}/{total} F:{frame.FrameId} T:{frame.OffsetMilliSec}ms";
        const HersheyFonts fontFace = HersheyFonts.HersheySimplex;
        var fontScale = Math.Clamp(Math.Min(image.Width, image.Height) / 900.0, 0.45, 1.2);
        var thickness = Math.Max(1, (int)Math.Round(fontScale * 2));
        var textSize = Cv2.GetTextSize(text, fontFace, fontScale, thickness, out var baseline);
        var backgroundWidth = Math.Min(image.Width, textSize.Width + 16);
        var backgroundHeight = Math.Min(image.Height, textSize.Height + baseline + 12);

        Cv2.Rectangle(
            image,
            new Rect(0, 0, backgroundWidth, backgroundHeight),
            Scalar.Black,
            -1);
        Cv2.PutText(
            image,
            text,
            new CvPoint(8, Math.Max(textSize.Height + 4, backgroundHeight - baseline - 4)),
            fontFace,
            fontScale,
            Scalar.White,
            thickness,
            LineTypes.AntiAlias);
    }

    private byte[] SetSequenceProperties(Frame frame, IReadOnlyList<BufferedFrame> sequenceFrames, Mat sequenceImage)
    {
        var sequenceImageJpeg = EncodeSequenceImageJpeg(sequenceImage);

        frame.SetProperty("SequenceImage", true);
        frame.SetProperty("SequenceImageLayout", Layout.ToString());
        frame.SetProperty("SequenceImageLength", sequenceFrames.Count);
        frame.SetProperty("SequenceImageFrameStride", FrameStride);
        frame.SetProperty("SequenceImageFrameIds", sequenceFrames.Select(item => item.FrameId).ToList());
        frame.SetProperty("SequenceImageWidth", sequenceImage.Width);
        frame.SetProperty("SequenceImageHeight", sequenceImage.Height);
        frame.SetProperty("SequenceImageJpegQuality", SequenceImageJpegQuality);
        frame.SetProperty(SequenceImageFrameJpegPropertyName, sequenceImageJpeg);

        return sequenceImageJpeg;
    }

    private byte[] EncodeSequenceImageJpeg(Mat sequenceImage)
    {
        var parameters = new ImageEncodingParam(ImwriteFlags.JpegQuality, SequenceImageJpegQuality);
        Cv2.ImEncode(".jpg", sequenceImage, out var imageBytes, parameters);
        if (imageBytes.Length == 0)
        {
            throw new InvalidOperationException("Sequence image cannot be encoded to JPEG.");
        }

        return imageBytes;
    }

    private void MarkFrameForLLM(Frame frame, IReadOnlyList<BufferedFrame> sequenceFrames, Mat sequenceImage, byte[] sequenceImageJpeg)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var sequenceId = Guid.NewGuid().ToString("N");
        var expireAtUtc = DateTime.UtcNow.AddSeconds(RequestTtlSeconds);
        var frameInfos = sequenceFrames
            .Select(item => new SequenceImageFrameInfo(item.FrameId, item.OffsetMilliSec, item.UtcTimeStamp))
            .ToList();

        var pending = new PendingSequence(
            requestId,
            sequenceId,
            frame.SourceId,
            Layout.ToString(),
            frameInfos,
            sequenceImage.Width,
            sequenceImage.Height,
            SerializeAnnotation(frame.Annotation),
            WillSaveEventSnapshot ? sequenceImage.Clone() : null,
            expireAtUtc);

        _pendingSequences[requestId] = pending;

        frame.SetProperty(LLMAnalysisPropertyName, true);
        frame.SetProperty(LLMAnalysisType, "frame");
        frame.SetProperty(LLMAnalysisPromptPropertyName, _userPrompt);
        frame.SetProperty(LLMAnalysisImageJpegPropertyName, sequenceImageJpeg);
        frame.SetProperty(LLMRequestIdPropertyName, requestId);
        frame.SetProperty(LLMRequesterAlgorithmNamePropertyName, AlgorithmName);
        frame.SetProperty(LLMCandidateEventIdPropertyName, sequenceId);
        frame.SetProperty(LLMQueuePolicyPropertyName, QueuePolicy.ToString());
        frame.SetProperty(LLMExpireAtUtcPropertyName, expireAtUtc);

        Log.Information("Create sequence image LLM request. RequestId: {RequestId}, SequenceId: {SequenceId}, SourceId: {SourceId}, Policy: {Policy}, ExpireAtUtc: {ExpireAtUtc}",
            requestId,
            sequenceId,
            frame.SourceId,
            QueuePolicy,
            expireAtUtc);
    }

    public override void ProcessEvent(LLMInferenceResultEvent @event)
    {
        if (@event.RequesterAlgorithmName != AlgorithmName || @event.Scope != LLMAnalysisScope.Frame)
        {
            return;
        }

        if (!_pendingSequences.TryRemove(@event.RequestId, out var pending))
        {
            Log.Debug("Ignore sequence image LLM result without pending request. RequestId: {RequestId}, SourceId: {SourceId}",
                @event.RequestId, @event.SourceId);
            return;
        }

        try
        {
            if (@event.IsExpiredResult)
            {
                Log.Warning("Ignore expired sequence image LLM result. RequestId: {RequestId}, SequenceId: {SequenceId}",
                    @event.RequestId, pending.SequenceId);
                HandleTimedOutSequence(pending);
                return;
            }

            PublishSequenceImageEvent(
                pending,
                @event.ModelName,
                @event.InferenceTime,
                @event.IsSuccess,
                @event.IsExpiredResult,
                @event.ErrorCode,
                @event.JsonResult);
        }
        finally
        {
            pending.Dispose();
        }
    }

    public bool CanHandle(LLMAnalysisResult result)
    {
        return result.RequesterAlgorithmName == AlgorithmName &&
               result.Scope == LLMAnalysisScope.Frame;
    }

    public Task HandleAsync(LLMAnalysisResult result, LLMReconcileContext context, CancellationToken cancellationToken)
    {
        ProcessEvent(LLMInferenceResultEvent.FromAnalysisResult(result, EventName));
        return Task.CompletedTask;
    }

    private void ProcessTimedOutSequences()
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var (requestId, pending) in _pendingSequences.ToArray())
        {
            if (pending.ExpireAtUtc > nowUtc)
            {
                continue;
            }

            if (_pendingSequences.TryRemove(requestId, out var removed))
            {
                try
                {
                    HandleTimedOutSequence(removed);
                }
                finally
                {
                    removed.Dispose();
                }
            }
        }
    }

    private void HandleTimedOutSequence(PendingSequence pending)
    {
        switch (OnTimeout)
        {
            case LLMTimeoutPolicy.PublishTraditional:
                PublishSequenceImageEvent(
                    pending,
                    string.Empty,
                    TimeSpan.Zero,
                    isSuccess: true,
                    isExpiredResult: true,
                    errorCode: "timeout",
                    llmJsonResult: string.Empty);
                break;
            case LLMTimeoutPolicy.PublishUnknown:
                PublishSequenceImageEvent(
                    pending,
                    string.Empty,
                    TimeSpan.Zero,
                    isSuccess: false,
                    isExpiredResult: true,
                    errorCode: "timeout",
                    llmJsonResult: "{\"status\":\"unknown\"}");
                break;
            case LLMTimeoutPolicy.Retry:
                Log.Information("Sequence image timeout policy is Retry, but retry is not implemented. RequestId: {RequestId}, SequenceId: {SequenceId}",
                    pending.RequestId, pending.SequenceId);
                break;
            case LLMTimeoutPolicy.Drop:
            default:
                Log.Information("Drop timed out sequence image LLM request. RequestId: {RequestId}, SequenceId: {SequenceId}",
                    pending.RequestId, pending.SequenceId);
                break;
        }
    }

    private void PublishSequenceImageEvent(
        PendingSequence pending,
        string modelName,
        TimeSpan inferenceTime,
        bool isSuccess,
        bool isExpiredResult,
        string? errorCode,
        string llmJsonResult)
    {
        if (!WillPublishEventMessage)
        {
            return;
        }

        if (CheckLocalEventInterval())
        {
            Log.Information("Suppress sequence image LLM event by local interval. RequestId: {RequestId}, SequenceId: {SequenceId}",
                pending.RequestId, pending.SequenceId);
            return;
        }

        var eventMessage = new SequenceImageLLMEvent(
            pending.SourceId,
            EventName,
            AlgorithmName,
            pending.RequestId,
            pending.SequenceId,
            pending.Layout,
            pending.Frames,
            modelName,
            inferenceTime,
            isSuccess,
            isExpiredResult,
            errorCode,
            llmJsonResult)
        {
            Annotations = pending.AnnotationJson
        };

        var snapshot = pending.Snapshot?.Clone();
        Task.Run(async () =>
        {
            try
            {
                await SaveAndPublishEventAsync(eventMessage, snapshot);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error publishing sequence image LLM event. RequestId: {RequestId}, SequenceId: {SequenceId}",
                    pending.RequestId, pending.SequenceId);
                snapshot?.Dispose();
            }
        });
    }

    private async Task SaveAndPublishEventAsync(SequenceImageLLMEvent eventMessage, Mat? snapshot)
    {
        try
        {
            if (WillSaveEventSnapshot && snapshot != null && !snapshot.IsDisposed)
            {
                var savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"));
                savePath.EnsureDirExistence();

                var now = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                var imagePath = Path.Combine(savePath, $"{eventMessage.SequenceId}_{now}.jpg");
                snapshot.SaveImage(imagePath);
                eventMessage.ImageLocalPath = imagePath;

                if (!string.IsNullOrWhiteSpace(eventMessage.Annotations))
                {
                    var annotationPath = Path.Combine(savePath, $"{eventMessage.SequenceId}_{now}.json");
                    await File.WriteAllTextAsync(annotationPath, eventMessage.Annotations);
                    eventMessage.ImageJsonLocalPath = annotationPath;
                }
            }

            await EventRepository.SaveDomainEventAsync(eventMessage);
            MessagePoster.PostDomainEventMessage(eventMessage);
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    private static string SerializeAnnotation(VisualAnnotation annotation)
    {
        try
        {
            return JsonSerializer.Serialize(annotation, DomainEvent.JsonOptions);
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }

    private void EnsureDefaultPreference(string key, string value)
    {
        if (!Preferences.ContainsKey(key))
        {
            Preferences[key] = value;
        }
    }

    private static SequenceImageLayout ParseLayout(string value)
    {
        if (string.Equals(value, "Vertical", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "V", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Column", StringComparison.OrdinalIgnoreCase))
        {
            return SequenceImageLayout.Vertical;
        }

        return SequenceImageLayout.Horizontal;
    }

    private static LLMQueuePolicy ParseQueuePolicy(string value)
    {
        return Enum.TryParse<LLMQueuePolicy>(value, ignoreCase: true, out var policy)
            ? policy
            : LLMQueuePolicy.EventAnchored;
    }

    private static LLMTimeoutPolicy ParseTimeoutPolicy(string value)
    {
        return Enum.TryParse<LLMTimeoutPolicy>(value, ignoreCase: true, out var policy)
            ? policy
            : LLMTimeoutPolicy.Drop;
    }

    private static void DisposeBufferedFrames(List<BufferedFrame>? frames)
    {
        if (frames == null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }

    public override void Dispose()
    {
        lock (_bufferSync)
        {
            foreach (var buffer in _sourceBuffers.Values)
            {
                while (buffer.Count > 0)
                {
                    buffer.Dequeue().Dispose();
                }
            }

            _sourceBuffers.Clear();
            _sourceFrameCounters.Clear();
        }

        foreach (var pending in _pendingSequences.Values)
        {
            pending.Dispose();
        }

        _pendingSequences.Clear();
        base.Dispose();
    }

    private sealed class BufferedFrame : IDisposable
    {
        public string SourceId { get; }
        public long FrameId { get; }
        public long OffsetMilliSec { get; }
        public DateTime UtcTimeStamp { get; }
        public Mat Scene { get; }

        public BufferedFrame(string sourceId, long frameId, long offsetMilliSec, DateTime utcTimeStamp, Mat scene)
        {
            SourceId = sourceId;
            FrameId = frameId;
            OffsetMilliSec = offsetMilliSec;
            UtcTimeStamp = utcTimeStamp;
            Scene = scene;
        }

        public void Dispose()
        {
            Scene.Dispose();
        }
    }

    private sealed class PendingSequence : IDisposable
    {
        public string RequestId { get; }
        public string SequenceId { get; }
        public string SourceId { get; }
        public string Layout { get; }
        public List<SequenceImageFrameInfo> Frames { get; }
        public int ImageWidth { get; }
        public int ImageHeight { get; }
        public string AnnotationJson { get; }
        public Mat? Snapshot { get; }
        public DateTime ExpireAtUtc { get; }

        public PendingSequence(
            string requestId,
            string sequenceId,
            string sourceId,
            string layout,
            List<SequenceImageFrameInfo> frames,
            int imageWidth,
            int imageHeight,
            string annotationJson,
            Mat? snapshot,
            DateTime expireAtUtc)
        {
            RequestId = requestId;
            SequenceId = sequenceId;
            SourceId = sourceId;
            Layout = layout;
            Frames = frames;
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            AnnotationJson = annotationJson;
            Snapshot = snapshot;
            ExpireAtUtc = expireAtUtc;
        }

        public void Dispose()
        {
            Snapshot?.Dispose();
        }
    }
}
