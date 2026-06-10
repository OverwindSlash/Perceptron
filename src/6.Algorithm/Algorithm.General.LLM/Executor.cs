﻿using Algorithm.Common;
using Algorithm.Common.Event;
using Algorithm.Common.LLM;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;
using OpenCvSharp;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.ClientModel;
using System.Diagnostics;

namespace Algorithm.General.LLM;

public class Executor : AlgorithmBase
{
    private const string DefaultModelName = "unsloth/qwen3-vl-30b-a3b-instruct";
    private const string FrameAnalysisType = "frame";
    private const string ObjectAnalysisType = "object";
    private const int DefaultInferenceRequestTimeoutSeconds = 10;

    public string ServerUrl { get; private set; } = string.Empty;
    public string ApiKey { get; private set; } = string.Empty;
    public string ModelName { get; private set; } = string.Empty;

    public string SystemPrompt { get; private set; } = string.Empty;
    public int MaxQueueCapacity { get; private set; }
    public int FrameJpegQuality { get; private set; }
    public int ObjectCropJpegQuality { get; private set; }
    public double ObjectCropPaddingRatio { get; private set; }
    public int RequestTimeoutSeconds { get; private set; }
    public int DefaultRequestTtlSeconds { get; private set; }
    public int MaxPendingEvidencePerSource { get; private set; }
    public long MaxPendingEvidenceTotalBytes { get; private set; }
    public int MaxConcurrentFrameRequests { get; private set; }
    public int MaxConcurrentObjectRequests { get; private set; }

    private IPublisher<LLMInferenceResultEvent> _inferenceResultEventPublisher = null!;
    private ChatClient _chatClient = null!;

    private LLMRequestScheduler _requestScheduler = null!;
    private PendingEvidenceStore _pendingEvidenceStore = null!;
    private readonly LLMRuntimeMetrics _metrics = new();
    private SemaphoreSlim _frameInferenceConcurrency = null!;
    private SemaphoreSlim _objectInferenceConcurrency = null!;
    private CancellationTokenSource _disposeCancellationTokenSource = null!;
    private Thread? _inferenceWorkerThread;
    private int _isDisposing;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "LLM Inference Module";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Call LLM inference using OpenAI API.";
    }

    public Executor(
        AlgorithmRuntimeDependencies dependencies,
        Dictionary<string, string> preferences)
        : base(dependencies, preferences)
    {
        AlgorithmName = "LLM Inference Module";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Call LLM inference using OpenAI API.";
    }

    protected override void InitializeCore()
    {
        _inferenceResultEventPublisher =
            Services.GetRequiredService<IPublisher<LLMInferenceResultEvent>>();
        ServerUrl = PreferenceParser.ParseStringValue(Preferences, "ServerUrl", string.Empty);
        ServerUrl = NormalizeServerUrl(ServerUrl);
        ApiKey = PreferenceParser.ParseStringValue(Preferences, "ApiKey", string.Empty);
        ModelName = PreferenceParser.ParseStringValue(Preferences, "ModelName", DefaultModelName);

        SystemPrompt = PreferenceParser.ParseStringValue(Preferences, "SystemPrompt", string.Empty);
        MaxQueueCapacity = PreferenceParser.ParseIntValue(Preferences, "MaxQueueCapacity", 100);
        FrameJpegQuality = PreferenceParser.ParseIntValue(Preferences, "FrameJpegQuality", LLMEvidenceBuilder.DefaultFrameJpegQuality);
        ObjectCropJpegQuality = PreferenceParser.ParseIntValue(Preferences, "ObjectCropJpegQuality", LLMEvidenceBuilder.DefaultObjectCropJpegQuality);
        ObjectCropPaddingRatio = PreferenceParser.ParseFloatValue(Preferences, "ObjectCropPaddingRatio", (float)LLMEvidenceBuilder.DefaultObjectCropPaddingRatio);
        RequestTimeoutSeconds = PreferenceParser.ParseIntValue(Preferences, "RequestTimeoutSeconds", DefaultInferenceRequestTimeoutSeconds);
        DefaultRequestTtlSeconds = PreferenceParser.ParseIntValue(Preferences, "DefaultRequestTtlSeconds", 120);
        MaxPendingEvidencePerSource = PreferenceParser.ParseIntValue(Preferences, "MaxPendingEvidencePerSource", 30);
        MaxPendingEvidenceTotalBytes = PreferenceParser.ParseIntValue(Preferences, "MaxPendingEvidenceTotalBytes", 128 * 1024 * 1024);
        MaxConcurrentFrameRequests = PreferenceParser.ParseIntValue(Preferences, "MaxConcurrentFrameRequests", 1);
        MaxConcurrentObjectRequests = PreferenceParser.ParseIntValue(Preferences, "MaxConcurrentObjectRequests", 2);

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is empty.");
        }

        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            _chatClient = new ChatClient(ModelName, ApiKey);
        }
        else
        {
            Log.Information("Using LLM server endpoint: {ServerUrl}", ServerUrl);
            _chatClient = new ChatClient(
                model: ModelName,
                credential: new ApiKeyCredential(ApiKey),
                options: new OpenAIClientOptions { Endpoint = new Uri(ServerUrl) });
        }

        Volatile.Write(ref _isDisposing, 0);
        _disposeCancellationTokenSource = new CancellationTokenSource();
        _frameInferenceConcurrency = new SemaphoreSlim(Math.Max(1, MaxConcurrentFrameRequests));
        _objectInferenceConcurrency = new SemaphoreSlim(Math.Max(1, MaxConcurrentObjectRequests));
        _requestScheduler = new LLMRequestScheduler(MaxQueueCapacity);
        _pendingEvidenceStore = new PendingEvidenceStore(
            MaxPendingEvidencePerSource,
            MaxPendingEvidenceTotalBytes);
        StartInferenceWorker();
    }

    private void StartInferenceWorker()
    {
        if (_inferenceWorkerThread != null)
        {
            return;
        }

        _inferenceWorkerThread = new Thread(ProcessInferenceBuffer)
        {
            IsBackground = true,
            Name = $"LLM-InferenceWorker"
        };

        _inferenceWorkerThread.Start();
    }

    private void ProcessInferenceBuffer()
    {
        while (true)
        {
            LLMAnalysisRequest? request = null;

            try
            {
                request = _requestScheduler
                    .TakeAsync(_disposeCancellationTokenSource.Token)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                if (request == null)
                {
                    if (Volatile.Read(ref _isDisposing) == 1)
                    {
                        return;
                    }

                    continue;
                }

                ProcessInference(request);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref _isDisposing) == 1)
            {
                if (request != null)
                {
                    Log.Information("LLM inference canceled during shutdown. RequestId: {RequestId}, SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                        request.RequestId, request.SourceId, request.FrameId, request.ObjectId);
                }

                return;
            }
            catch (OperationCanceledException ex)
            {
                if (request != null)
                {
                    Log.Warning(ex, "LLM inference canceled or timed out. RequestId: {RequestId}, SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                        request.RequestId, request.SourceId, request.FrameId, request.ObjectId);
                    PublishFailedResult(request, "cancelled_or_timeout");
                }
            }
            catch (Exception ex)
            {
                if (request != null)
                {
                    Log.Error(ex, "Error processing LLM inference. RequestId: {RequestId}, SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                        request.RequestId, request.SourceId, request.FrameId, request.ObjectId);
                    PublishFailedResult(request, "inference_error");
                }
            }
        }
    }

    private void ProcessInference(LLMAnalysisRequest request)
    {
        var concurrency = request.Scope == LLMAnalysisScope.Frame
            ? _frameInferenceConcurrency
            : _objectInferenceConcurrency;
        concurrency.Wait(_disposeCancellationTokenSource.Token);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var inferenceResult = CallLLMInferenceAPI(request.Prompt, BinaryData.FromBytes(request.ImageJpeg));
            stopwatch.Stop();

            var completedAtUtc = DateTime.UtcNow;
            var result = new LLMAnalysisResult(
                request.RequestId,
                request.RequesterAlgorithmName,
                request.CandidateEventId,
                request.SourceId,
                request.FrameId,
                request.OffsetMilliSec,
                request.UtcTimeStamp,
                request.ObjectId,
                request.Scope,
                ModelName,
                stopwatch.Elapsed,
                inferenceResult,
                IsSuccess: true,
                IsExpiredResult: completedAtUtc > request.ExpireAtUtc,
                ErrorCode: null,
                RequestedAtUtc: request.CreatedAtUtc,
                CompletedAtUtc: completedAtUtc);

        Log.Debug("LLM inference completed. RequestId: {RequestId}, Model: {ModelName}, Result: {Result}, Elapse: {InferTime}",
            request.RequestId, ModelName, inferenceResult, stopwatch.Elapsed);
        _metrics.Increment("llm_inference_completed_total", request.SourceId, request.QueuePolicy, request.RequesterAlgorithmName);

        PublishResult(result, request);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private void PublishFailedResult(LLMAnalysisRequest request, string errorCode)
    {
        var completedAtUtc = DateTime.UtcNow;
        var result = new LLMAnalysisResult(
            request.RequestId,
            request.RequesterAlgorithmName,
            request.CandidateEventId,
            request.SourceId,
            request.FrameId,
            request.OffsetMilliSec,
            request.UtcTimeStamp,
            request.ObjectId,
            request.Scope,
            ModelName,
            TimeSpan.Zero,
            string.Empty,
            IsSuccess: false,
            IsExpiredResult: completedAtUtc > request.ExpireAtUtc,
            ErrorCode: errorCode,
            RequestedAtUtc: request.CreatedAtUtc,
            CompletedAtUtc: completedAtUtc);

        PublishResult(result, request);
    }

    private void PublishResult(LLMAnalysisResult result, LLMAnalysisRequest request)
    {
        var inferenceEvent = LLMInferenceResultEvent.FromAnalysisResult(
            result,
            EventName,
            request.DetectorConfidence ?? 0);
        inferenceEvent.QueuePolicy = request.QueuePolicy;
        inferenceEvent.TrackKey = request.TrackKey;
        inferenceEvent.ExpireAtUtc = request.ExpireAtUtc;
        if (request.Scope == LLMAnalysisScope.Object && request.ImageJpeg.Length > 0)
        {
            inferenceEvent.Snapshot = Cv2.ImDecode(request.ImageJpeg, ImreadModes.Color);
        }

        _inferenceResultEventPublisher.Publish(inferenceEvent);
        _pendingEvidenceStore.TryRemove(request.RequestId, out _);
    }

    private string CallLLMInferenceAPI(string userPrompt, BinaryData imageBytes)
    {
        List<ChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            messages.Add(new SystemChatMessage(SystemPrompt));
        }

        messages.Add(new UserChatMessage(new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(userPrompt),
            ChatMessageContentPart.CreateImagePart(imageBytes, "image/jpeg")
        }));

        using var requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellationTokenSource.Token);
        requestCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));

        ChatCompletion completion = _chatClient.CompleteChat(messages, null, requestCancellationTokenSource.Token);

        if (completion.Content.Count == 0)
        {
            return string.Empty;
        }

        return string.Concat(completion.Content.Select(item => item.Text));
    }

    private static string NormalizeAnalysisType(string? analysisType)
    {
        if (string.IsNullOrWhiteSpace(analysisType))
        {
            return string.Empty;
        }

        return analysisType.Trim().ToLowerInvariant();
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
        {
            return serverUrl;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return serverUrl;
        }

        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            var builder = new UriBuilder(uri)
            {
                Path = "v1"
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        return serverUrl.TrimEnd('/');
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        // 显示当前 _inferenceBuffer 中待处理图片的数量
        // Log.Information("LLM inference buffer size: {BufferSize}", _inferenceBuffer.Count);

        if (!frame.HasProperty(LLMPropertyNames.Analysis))
        {
            return new AnalysisResult(true);
        }

        return new AnalysisResult(EnqueueFrameForInference(frame));
    }

    private bool EnqueueFrameForInference(Frame frame)
    {
        try
        {
            if (IsFrameLevelAnalysis(frame))
            {
                return EnqueueFrameLevelInference(frame);
            }

            if (IsObjectLevelAnalysis(frame))
            {
                return EnqueueObjectLevelInference(frame);
            }

            return true;
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "LLM inference buffer is closed. FrameId: {FrameId}", frame.FrameId);
            return false;
        }
    }

    private bool EnqueueFrameLevelInference(Frame frame)
    {
        if (!LLMEvidenceBuilder.TryBuildFrameJpeg(frame, FrameJpegQuality, out var imageBytes))
        {
            Log.Warning("Skip LLM frame inference because frame evidence cannot be built. SourceId: {SourceId}, FrameId: {FrameId}",
                frame.SourceId, frame.FrameId);
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        var request = new LLMAnalysisRequest(
            RequestId: GetOrCreateRequestId(frame),
            RequesterAlgorithmName: GetRequesterAlgorithmName(frame),
            CandidateEventId: frame.GetProperty<string>(LLMPropertyNames.CandidateEventId),
            SourceId: frame.SourceId,
            FrameId: frame.FrameId,
            OffsetMilliSec: frame.OffsetMilliSec,
            UtcTimeStamp: frame.UtcTimeStamp,
            ObjectId: null,
            ObjectLocalId: null,
            TrackKey: null,
            Scope: LLMAnalysisScope.Frame,
            QueuePolicy: GetQueuePolicy(frame, LLMQueuePolicy.LatestPerSource),
            Prompt: frame.GetProperty<string>(LLMPropertyNames.AnalysisPrompt) ?? string.Empty,
            ImageJpeg: imageBytes,
            DetectorConfidence: null,
            EvidenceQualityScore: null,
            CreatedAtUtc: nowUtc,
            ExpireAtUtc: GetExpireAtUtc(frame, nowUtc));

        if (!TryAddPendingEvidence(request, frame, imageBytes, null))
        {
            return false;
        }

        Log.Debug("Create LLM frame request. RequestId: {RequestId}, SourceId: {SourceId}, Policy: {Policy}, ImageBytes: {ImageBytes}",
            request.RequestId, request.SourceId, request.QueuePolicy, imageBytes.Length);
        _metrics.Increment("llm_request_created_total", request.SourceId, request.QueuePolicy, request.RequesterAlgorithmName);

        if (!_requestScheduler.TrySubmit(request))
        {
            _pendingEvidenceStore.TryRemove(request.RequestId, out _);
            return false;
        }

        return true;
    }

    private bool EnqueueObjectLevelInference(Frame frame)
    {
        var objectCandidates = frame.DetectedObjects
            .Where(detectedObject => detectedObject.GetProperty<bool>(LLMPropertyNames.Analysis))
            .GroupBy(detectedObject => detectedObject.Id)
            .Select(group => group
                .OrderByDescending(detectedObject => detectedObject.Confidence)
                .First())
            .ToList();

        if (objectCandidates.Count == 0)
        {
            return true;
        }

        foreach (var detectedObject in objectCandidates)
        {
            if (!LLMEvidenceBuilder.TryBuildObjectCropJpeg(
                    frame,
                    detectedObject,
                    ObjectCropJpegQuality,
                    ObjectCropPaddingRatio,
                    out var imageBytes))
            {
                Log.Warning("Skip LLM object inference because object evidence cannot be built. SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                    frame.SourceId, frame.FrameId, detectedObject.Id);
                continue;
            }

            var nowUtc = DateTime.UtcNow;
            var request = new LLMAnalysisRequest(
                RequestId: detectedObject.GetProperty<string>(LLMPropertyNames.RequestId) ?? GetOrCreateRequestId(frame),
                RequesterAlgorithmName: detectedObject.GetProperty<string>(LLMPropertyNames.RequesterAlgorithmName)
                    ?? GetRequesterAlgorithmName(frame),
                CandidateEventId: detectedObject.GetProperty<string>(LLMPropertyNames.CandidateEventId)
                    ?? frame.GetProperty<string>(LLMPropertyNames.CandidateEventId),
                SourceId: frame.SourceId,
                FrameId: frame.FrameId,
                OffsetMilliSec: frame.OffsetMilliSec,
                UtcTimeStamp: frame.UtcTimeStamp,
                ObjectId: detectedObject.Id,
                ObjectLocalId: detectedObject.LocalId,
                TrackKey: detectedObject.TrackKey,
                Scope: LLMAnalysisScope.Object,
                QueuePolicy: GetQueuePolicy(detectedObject.GetProperty<string>(LLMPropertyNames.QueuePolicy)
                    ?? frame.GetProperty<string>(LLMPropertyNames.QueuePolicy), LLMQueuePolicy.LatestBestPerObject),
                Prompt: frame.GetProperty<string>(LLMPropertyNames.AnalysisPrompt) ?? string.Empty,
                ImageJpeg: imageBytes,
                DetectorConfidence: detectedObject.Confidence,
                EvidenceQualityScore: LLMEvidenceBuilder.CalculateObjectEvidenceQuality(detectedObject, frame.Scene.Width, frame.Scene.Height),
                CreatedAtUtc: nowUtc,
                ExpireAtUtc: GetExpireAtUtc(frame, nowUtc));

            if (!TryAddPendingEvidence(request, frame, null, imageBytes))
            {
                continue;
            }

            Log.Information("Create LLM object request. RequestId: {RequestId}, SourceId: {SourceId}, ObjectId: {ObjectId}, Policy: {Policy}, ImageBytes: {ImageBytes}",
                request.RequestId, request.SourceId, request.ObjectId, request.QueuePolicy, imageBytes.Length);
            _metrics.Increment("llm_request_created_total", request.SourceId, request.QueuePolicy, request.RequesterAlgorithmName);

            if (!_requestScheduler.TrySubmit(request))
            {
                _pendingEvidenceStore.TryRemove(request.RequestId, out _);
            }
        }

        return true;
    }

    private bool TryAddPendingEvidence(
        LLMAnalysisRequest request,
        Frame frame,
        byte[]? frameJpeg,
        byte[]? objectCropJpeg)
    {
        _pendingEvidenceStore.CleanupExpired(DateTime.UtcNow);

        var evidence = new PendingLLMEvidence(
            request.RequestId,
            request.CandidateEventId,
            request.SourceId,
            request.FrameId,
            request.OffsetMilliSec,
            request.UtcTimeStamp,
            request.Scope,
            frameJpeg,
            objectCropJpeg,
            frame.DetectedObjects.Select(DetectedObjectEvidence.FromDetectedObject).ToList(),
            request.Prompt,
            request.ExpireAtUtc);

        if (_pendingEvidenceStore.TryAdd(evidence))
        {
            Log.Debug("LLM evidence stored. RequestId: {RequestId}, SourceId: {SourceId}, Scope: {Scope}, FrameBytes: {FrameBytes}, ObjectBytes: {ObjectBytes}, TotalBytes: {TotalBytes}",
                request.RequestId,
                request.SourceId,
                request.Scope,
                frameJpeg?.Length ?? 0,
                objectCropJpeg?.Length ?? 0,
                _pendingEvidenceStore.TotalBytes);
            _metrics.SetGauge("llm_pending_evidence_bytes", _pendingEvidenceStore.TotalBytes, request.SourceId, request.QueuePolicy, request.RequesterAlgorithmName);
            _metrics.SetGauge("llm_pending_evidence_count", _pendingEvidenceStore.Count, request.SourceId, request.QueuePolicy, request.RequesterAlgorithmName);
            return true;
        }

        Log.Warning("Reject LLM request because pending evidence store is full. RequestId: {RequestId}, SourceId: {SourceId}, Policy: {Policy}",
            request.RequestId, request.SourceId, request.QueuePolicy);
        _metrics.Increment("llm_request_rejected_total", request.SourceId, request.QueuePolicy, request.RequesterAlgorithmName);
        return false;
    }

    private string GetOrCreateRequestId(Frame frame)
    {
        var requestId = frame.GetProperty<string>(LLMPropertyNames.RequestId);
        return string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
    }

    private string GetRequesterAlgorithmName(Frame frame)
    {
        var requester = frame.GetProperty<string>(LLMPropertyNames.RequesterAlgorithmName);
        return string.IsNullOrWhiteSpace(requester) ? string.Empty : requester;
    }

    private DateTime GetExpireAtUtc(Frame frame, DateTime nowUtc)
    {
        var expireAtUtc = frame.GetProperty<DateTime?>(LLMPropertyNames.ExpireAtUtc);
        return expireAtUtc.HasValue && expireAtUtc.Value.Kind == DateTimeKind.Utc
            ? expireAtUtc.Value
            : nowUtc.AddSeconds(DefaultRequestTtlSeconds);
    }

    private static LLMQueuePolicy GetQueuePolicy(Frame frame, LLMQueuePolicy defaultPolicy)
    {
        return GetQueuePolicy(frame.GetProperty<string>(LLMPropertyNames.QueuePolicy), defaultPolicy);
    }

    private static LLMQueuePolicy GetQueuePolicy(string? queuePolicy, LLMQueuePolicy defaultPolicy)
    {
        if (string.IsNullOrWhiteSpace(queuePolicy))
        {
            return defaultPolicy;
        }

        return Enum.TryParse<LLMQueuePolicy>(queuePolicy, ignoreCase: true, out var parsed)
            ? parsed
            : defaultPolicy;
    }

    private bool IsFrameLevelAnalysis(Frame frame)
    {
        return NormalizeAnalysisType(frame.GetProperty<string>(LLMPropertyNames.AnalysisType)) == FrameAnalysisType;
    }

    private bool IsObjectLevelAnalysis(Frame frame)
    {
        return NormalizeAnalysisType(frame.GetProperty<string>(LLMPropertyNames.AnalysisType)) == ObjectAnalysisType;
    }

    protected override void DisposeCore()
    {
        if (Interlocked.Exchange(ref _isDisposing, 1) == 1)
        {
            return;
        }

        var cancellationTokenSource = _disposeCancellationTokenSource;
        var requestScheduler = _requestScheduler;
        var inferenceWorkerThread = _inferenceWorkerThread;
        var frameInferenceConcurrency = _frameInferenceConcurrency;
        var objectInferenceConcurrency = _objectInferenceConcurrency;

        cancellationTokenSource?.Cancel();
        requestScheduler?.Complete();
        inferenceWorkerThread?.Join();

        _disposeCancellationTokenSource = null!;
        _requestScheduler = null!;
        _inferenceWorkerThread = null;
        _frameInferenceConcurrency = null!;
        _objectInferenceConcurrency = null!;
        _pendingEvidenceStore = null!;

        requestScheduler?.Dispose();
        frameInferenceConcurrency?.Dispose();
        objectInferenceConcurrency?.Dispose();
        cancellationTokenSource?.Dispose();
    }
}
