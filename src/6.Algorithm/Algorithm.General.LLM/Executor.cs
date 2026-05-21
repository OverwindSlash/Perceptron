﻿using Algorithm.Common;
using Algorithm.Common.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Algorithm.General.LLM;

public class Executor : AlgorithmBase
{
    private const string DefaultModelName = "unsloth/qwen3-vl-30b-a3b-instruct";
    private const string FrameAnalysisType = "frame";
    private const string ObjectAnalysisType = "object";
    private static readonly TimeSpan InferenceRequestTimeout = TimeSpan.FromMinutes(2);

    public string ServerUrl { get; private set; }
    public string ApiKey { get; private set; }
    public string ModelName { get; private set; }

    public string SystemPrompt { get; private set; }

    private IPublisher<LLMInferenceResultEvent> _inferenceResultEventPublisher = null!;
    private ChatClient _chatClient = null!;

    private readonly BlockingCollection<string> _frameInferenceSourceIdBuffer = new(new ConcurrentQueue<string>());
    private readonly ConcurrentDictionary<string, FrameInferenceTask> _latestFrameInferenceTasks = new();
    private readonly ConcurrentDictionary<string, byte> _queuedFrameInferenceSourceIds = new();
    private readonly BlockingCollection<string> _objectInferenceIdBuffer = new(new ConcurrentQueue<string>());
    private readonly ConcurrentDictionary<string, ObjectInferenceTask> _latestObjectInferenceTasks = new();
    private readonly ConcurrentDictionary<string, byte> _queuedObjectInferenceIds = new();
    private readonly object _frameInferenceSync = new();
    private readonly object _objectInferenceSync = new();
    private readonly SemaphoreSlim _pendingInferenceSignal = new(0);
    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    private Thread? _inferenceWorkerThread;
    private int _isDisposing;
    private bool _preferFrameInferenceWork = true;

    private sealed class ObjectInferenceTask
    {
        public ObjectInferenceTask(Frame frame, string objectId, float confidence)
        {
            Frame = frame;
            ObjectId = objectId;
            Confidence = confidence;
        }

        public Frame Frame { get; }
        public string ObjectId { get; }
        public float Confidence { get; }
    }

    private sealed class FrameInferenceTask
    {
        public FrameInferenceTask(Frame frame)
        {
            Frame = frame;
            SourceId = frame.SourceId;
        }

        public Frame Frame { get; }
        public string SourceId { get; }
    }

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "LLM Inference Module";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Call LLM inference using OpenAI API.";
    }

    public override bool Initialize()
    {
        var provider = Pipeline.Provider;
        _inferenceResultEventPublisher = provider.GetRequiredService<IPublisher<LLMInferenceResultEvent>>();

        ServerUrl = PreferenceParser.ParseStringValue(Preferences, "ServerUrl", string.Empty);
        ServerUrl = NormalizeServerUrl(ServerUrl);
        ApiKey = PreferenceParser.ParseStringValue(Preferences, "ApiKey", string.Empty);
        ModelName = PreferenceParser.ParseStringValue(Preferences, "ModelName", DefaultModelName);

        SystemPrompt = PreferenceParser.ParseStringValue(Preferences, "SystemPrompt", string.Empty);

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

        StartInferenceWorker();

        return base.Initialize();
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
            Frame? frame = null;
            string? objectId = null;

            try
            {
                _pendingInferenceSignal.Wait();

                if (TryTakeNextInferenceWork(out frame, out objectId))
                {
                    ProcessInference(frame!, objectId);
                }
                else if (IsInferenceCompleted())
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (Volatile.Read(ref _isDisposing) == 1)
            {
                if (frame != null)
                {
                    Log.Information("LLM inference canceled during shutdown. SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                        frame.SourceId, frame.FrameId, objectId);
                }

                return;
            }
            catch (OperationCanceledException ex)
            {
                if (frame != null)
                {
                    Log.Warning(ex, "LLM inference canceled or timed out. SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                        frame.SourceId, frame.FrameId, objectId);
                }
            }
            catch (Exception ex)
            {
                if (frame != null)
                {
                    Log.Error(ex, "Error processing LLM inference. SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                        frame.SourceId, frame.FrameId, objectId);
                }
            }
            finally
            {
                frame?.Dispose();
            }
        }
    }

    private bool TryTakeNextInferenceWork(out Frame? frame, out string? objectId)
    {
        frame = null;
        objectId = null;

        if (_preferFrameInferenceWork)
        {
            if (TryTakeFrameInferenceWork(out frame))
            {
                _preferFrameInferenceWork = false;
                return true;
            }

            if (TryTakeObjectInferenceWork(out frame, out objectId))
            {
                _preferFrameInferenceWork = true;
                return true;
            }
        }
        else
        {
            if (TryTakeObjectInferenceWork(out frame, out objectId))
            {
                _preferFrameInferenceWork = true;
                return true;
            }

            if (TryTakeFrameInferenceWork(out frame))
            {
                _preferFrameInferenceWork = false;
                return true;
            }
        }

        return false;
    }

    private bool TryTakeFrameInferenceWork(out Frame? frame)
    {
        frame = null;

        while (_frameInferenceSourceIdBuffer.TryTake(out var sourceId))
        {
            _queuedFrameInferenceSourceIds.TryRemove(sourceId, out _);
            if (!_latestFrameInferenceTasks.TryRemove(sourceId, out var frameTask))
            {
                continue;
            }

            frame = frameTask.Frame;
            return true;
        }

        return false;
    }

    private bool TryTakeObjectInferenceWork(out Frame? frame, out string? objectId)
    {
        frame = null;
        objectId = null;

        while (_objectInferenceIdBuffer.TryTake(out var nextObjectId))
        {
            _queuedObjectInferenceIds.TryRemove(nextObjectId, out _);
            if (!_latestObjectInferenceTasks.TryRemove(nextObjectId, out var objectTask))
            {
                continue;
            }

            frame = objectTask.Frame;
            objectId = objectTask.ObjectId;
            return true;
        }

        return false;
    }

    private bool IsInferenceCompleted()
    {
        return _frameInferenceSourceIdBuffer.IsCompleted &&
               _latestFrameInferenceTasks.IsEmpty &&
               _objectInferenceIdBuffer.IsCompleted &&
               _latestObjectInferenceTasks.IsEmpty;
    }

    private void ProcessInference(Frame frame, string? objectId = null)
    {
        if (!frame.HasProperty(LLMAnalysisPromptPropertyName))
        {
            return;
        }

        string userPrompt = frame.GetProperty<string>(LLMAnalysisPromptPropertyName) ?? string.Empty;
        string analysisType = NormalizeAnalysisType(frame.GetProperty<string>(LLMAnalysisType));

        // 针对帧进行推理
        if (analysisType == FrameAnalysisType)
        {
            Cv2.ImEncode(".jpg", frame.Scene, out var imageBytes);
            var stopwatch = Stopwatch.StartNew();
            var inferenceResult = CallLLMInferenceAPI(userPrompt, BinaryData.FromBytes(imageBytes));
            stopwatch.Stop();

            Log.Information("LLM inference completed. Model: {ModelName}, Result: {Result}, Elapse: {InferTime}", ModelName, inferenceResult, stopwatch.Elapsed);

            var inferenceEvent = new LLMInferenceResultEvent(
                sourceId: frame.SourceId,
                eventType: LLMInferenceResultEvent.EventType,
                eventName: EventName,
                algorithmName: AlgorithmName,
                modelName: ModelName,
                inferenceTime: stopwatch.Elapsed,
                detectedObjectId: string.Empty,
                confidence: 0,
                jsonResult: inferenceResult);
            inferenceEvent.FrameId = frame.FrameId;
            inferenceEvent.UtcTimeStamp = frame.UtcTimeStamp;
            _inferenceResultEventPublisher.Publish(inferenceEvent);
        }

        // 针对帧中识别到的对象进行推理
        if (analysisType == ObjectAnalysisType)
        {
            IEnumerable<DetectedObject> detectedObjects = frame.DetectedObjects;
            if (!string.IsNullOrWhiteSpace(objectId))
            {
                detectedObjects = detectedObjects.Where(detectedObject => detectedObject.Id == objectId);
            }

            foreach (var detectedObject in detectedObjects)
            {
                if (!detectedObject.GetProperty<bool>(LLMAnalysisPropertyName))
                {
                    continue;
                }

                using var snapshot = TryCloneSnapshot(detectedObject);
                if (snapshot == null)
                {
                    Log.Warning("Skip LLM object inference because snapshot is unavailable. SourceId: {SourceId}, FrameId: {FrameId}, ObjectId: {ObjectId}",
                        frame.SourceId, frame.FrameId, detectedObject.Id);
                    continue;
                }

                Cv2.ImEncode(".jpg", snapshot, out var imageBytes);
                var stopwatch = Stopwatch.StartNew();
                var inferenceResult = CallLLMInferenceAPI(userPrompt, BinaryData.FromBytes(imageBytes));
                stopwatch.Stop();

                Log.Information("LLM inference completed. Model: {ModelName}, Result: {Result}, Elapse: {InferTime}", ModelName, inferenceResult, stopwatch.Elapsed);

                var inferenceEvent = new LLMInferenceResultEvent(
                    sourceId: frame.SourceId,
                    eventType: LLMInferenceResultEvent.EventType,
                    eventName: EventName,
                    algorithmName: AlgorithmName,
                    modelName: ModelName,
                    inferenceTime: stopwatch.Elapsed,
                    detectedObjectId: detectedObject.Id,
                    confidence: detectedObject.Confidence,
                    jsonResult: inferenceResult);
                inferenceEvent.FrameId = frame.FrameId;
                inferenceEvent.UtcTimeStamp = frame.UtcTimeStamp;
                inferenceEvent.Snapshot = snapshot.Clone();

                _inferenceResultEventPublisher.Publish(inferenceEvent);
            }
        }
    }

    private static Mat? TryCloneSnapshot(DetectedObject detectedObject)
    {
        try
        {
            var snapshot = detectedObject.CloneSnapshot();
            if (snapshot == null || snapshot.Empty())
            {
                snapshot?.Dispose();
                return null;
            }

            return snapshot;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
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
        requestCancellationTokenSource.CancelAfter(InferenceRequestTimeout);

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

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        // 显示当前 _inferenceBuffer 中待处理图片的数量
        // Log.Information("LLM inference buffer size: {BufferSize}", _inferenceBuffer.Count);

        try
        {
            if (!frame.HasProperty(LLMAnalysisPropertyName))
            {
                return new AnalysisResult(true);
            }

            return new AnalysisResult(EnqueueFrameForInference(frame));
        }
        finally
        {
            frame.Dispose();
        }
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
        lock (_frameInferenceSync)
        {
            var shouldReleaseRetainedFrame = true;
            frame.Retain();

            try
            {
                var sourceId = frame.SourceId;
                if (_latestFrameInferenceTasks.TryGetValue(sourceId, out var oldTask))
                {
                    var replacementTask = new FrameInferenceTask(frame);
                    _latestFrameInferenceTasks[sourceId] = replacementTask;
                    DisposeFrameInferenceTask(oldTask);
                }
                else
                {
                    _latestFrameInferenceTasks[sourceId] = new FrameInferenceTask(frame);
                }

                if (_queuedFrameInferenceSourceIds.TryAdd(sourceId, 0))
                {
                    try
                    {
                        _frameInferenceSourceIdBuffer.Add(sourceId);
                        _pendingInferenceSignal.Release();
                    }
                    catch
                    {
                        _queuedFrameInferenceSourceIds.TryRemove(sourceId, out _);
                        if (_latestFrameInferenceTasks.TryRemove(sourceId, out var pendingTask))
                        {
                            DisposeFrameInferenceTask(pendingTask);
                            shouldReleaseRetainedFrame = false;
                        }
                        throw;
                    }
                }

                return true;
            }
            catch
            {
                if (shouldReleaseRetainedFrame)
                {
                    frame.Dispose();
                }
                throw;
            }
        }
    }

    private bool EnqueueObjectLevelInference(Frame frame)
    {
        var objectCandidates = frame.DetectedObjects
            .Where(detectedObject => detectedObject.GetProperty<bool>(LLMAnalysisPropertyName))
            .GroupBy(detectedObject => detectedObject.Id)
            .Select(group => group
                .OrderByDescending(detectedObject => detectedObject.Confidence)
                .First())
            .ToList();

        if (objectCandidates.Count == 0)
        {
            return true;
        }

        lock (_objectInferenceSync)
        {
            foreach (var detectedObject in objectCandidates)
            {
                var objectId = detectedObject.Id;
                var confidence = detectedObject.Confidence;

                if (_latestObjectInferenceTasks.TryGetValue(objectId, out var oldTask))
                {
                    if (confidence <= oldTask.Confidence)
                    {
                        continue;
                    }

                    frame.Retain();
                    var replacementTask = new ObjectInferenceTask(frame, objectId, confidence);
                    _latestObjectInferenceTasks[objectId] = replacementTask;
                    DisposeObjectInferenceTask(oldTask);
                }
                else
                {
                    frame.Retain();
                    _latestObjectInferenceTasks[objectId] = new ObjectInferenceTask(frame, objectId, confidence);
                }

                if (_queuedObjectInferenceIds.TryAdd(objectId, 0))
                {
                    try
                    {
                        _objectInferenceIdBuffer.Add(objectId);
                        _pendingInferenceSignal.Release();
                    }
                    catch
                    {
                        _queuedObjectInferenceIds.TryRemove(objectId, out _);
                        if (_latestObjectInferenceTasks.TryRemove(objectId, out var pendingTask))
                        {
                            DisposeObjectInferenceTask(pendingTask);
                        }
                        throw;
                    }
                }
            }
        }

        return true;
    }

    private static void DisposeObjectInferenceTask(ObjectInferenceTask task)
    {
        task.Frame.Dispose();
    }

    private static void DisposeFrameInferenceTask(FrameInferenceTask task)
    {
        task.Frame.Dispose();
    }

    private bool IsFrameLevelAnalysis(Frame frame)
    {
        return NormalizeAnalysisType(frame.GetProperty<string>(LLMAnalysisType)) == FrameAnalysisType;
    }

    private bool IsObjectLevelAnalysis(Frame frame)
    {
        return NormalizeAnalysisType(frame.GetProperty<string>(LLMAnalysisType)) == ObjectAnalysisType;
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposing, 1) == 1)
        {
            return;
        }

        _disposeCancellationTokenSource.Cancel();
        _frameInferenceSourceIdBuffer.CompleteAdding();
        _objectInferenceIdBuffer.CompleteAdding();
        _pendingInferenceSignal.Release();
        _inferenceWorkerThread?.Join();

        while (_frameInferenceSourceIdBuffer.TryTake(out _))
        {
        }

        while (_objectInferenceIdBuffer.TryTake(out _))
        {
        }

        foreach (var frameTask in _latestFrameInferenceTasks.Values)
        {
            DisposeFrameInferenceTask(frameTask);
        }

        foreach (var objectTask in _latestObjectInferenceTasks.Values)
        {
            DisposeObjectInferenceTask(objectTask);
        }

        _latestFrameInferenceTasks.Clear();
        _queuedFrameInferenceSourceIds.Clear();
        _latestObjectInferenceTasks.Clear();
        _queuedObjectInferenceIds.Clear();
        _frameInferenceSourceIdBuffer.Dispose();
        _objectInferenceIdBuffer.Dispose();
        _pendingInferenceSignal.Dispose();
        _disposeCancellationTokenSource.Dispose();
        base.Dispose();
    }
}
