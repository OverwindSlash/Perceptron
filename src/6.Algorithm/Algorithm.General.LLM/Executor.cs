﻿﻿﻿using Algorithm.Common;
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

    public string ServerUrl { get; private set; }
    public string ApiKey { get; private set; }
    public string ModelName { get; private set; }

    public string SystemPrompt { get; private set; }

    private IPublisher<LLMInferenceResultEvent> _inferenceResultEventPublisher = null!;
    private ChatClient _chatClient = null!;

    private readonly BlockingCollection<Frame> _frameInferenceBuffer = new(new ConcurrentQueue<Frame>());
    private readonly BlockingCollection<string> _objectInferenceIdBuffer = new(new ConcurrentQueue<string>());
    private readonly ConcurrentDictionary<string, ObjectInferenceTask> _latestObjectInferenceTasks = new();
    private readonly ConcurrentDictionary<string, byte> _queuedObjectInferenceIds = new();
    private readonly object _objectInferenceSync = new();
    private Thread? _inferenceWorkerThread;
    private int _isDisposing;

    private sealed class ObjectInferenceTask
    {
        public ObjectInferenceTask(Frame frame, string objectId)
        {
            Frame = frame;
            ObjectId = objectId;
        }

        public Frame Frame { get; }
        public string ObjectId { get; }
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
                if (TryTakeNextInferenceWork(out frame, out objectId))
                {
                    ProcessInference(frame, objectId);
                }
                else if (IsInferenceCompleted())
                {
                    return;
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

        if (_frameInferenceBuffer.TryTake(out frame, 20))
        {
            return true;
        }

        if (!_objectInferenceIdBuffer.TryTake(out var nextObjectId, 20))
        {
            return false;
        }

        _queuedObjectInferenceIds.TryRemove(nextObjectId, out _);
        if (!_latestObjectInferenceTasks.TryRemove(nextObjectId, out var objectTask))
        {
            return false;
        }

        frame = objectTask.Frame;
        objectId = objectTask.ObjectId;
        return true;
    }

    private bool IsInferenceCompleted()
    {
        return _frameInferenceBuffer.IsCompleted &&
               _objectInferenceIdBuffer.IsCompleted &&
               _latestObjectInferenceTasks.IsEmpty;
    }

    private void ProcessInference(Frame frame, string? objectId = null)
    {
        if (!frame.HasProperty(LLMAnalysisPromptPropertyName))
        {
            return;
        }

        string userPrompt = frame.GetProperty<string>(LLMAnalysisPromptPropertyName);
        string analysisType = frame.GetProperty<string>(LLMAnalysisType);

        // 针对帧进行推理
        if (analysisType == "frame")
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
            inferenceEvent.Frame = frame;

            frame.Retain();
            _inferenceResultEventPublisher.Publish(inferenceEvent);
            frame.Dispose();
        }

        // 针对帧中识别到的对象进行推理
        if (analysisType == "object")
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

                Cv2.ImEncode(".jpg", detectedObject.Snapshot, out var imageBytes);
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
                inferenceEvent.Frame = frame;
                inferenceEvent.Snapshot = detectedObject.Snapshot.Clone();

                frame.Retain();
                _inferenceResultEventPublisher.Publish(inferenceEvent);
                frame.Dispose();
            }
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
        ChatCompletion completion = _chatClient.CompleteChat(messages);

        if (completion.Content.Count == 0)
        {
            return string.Empty;
        }

        return string.Concat(completion.Content.Select(item => item.Text));
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
        frame.Retain();

        try
        {
            _frameInferenceBuffer.Add(frame);
            return true;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    private bool EnqueueObjectLevelInference(Frame frame)
    {
        var objectIds = frame.DetectedObjects
            .Where(detectedObject => detectedObject.GetProperty<bool>(LLMAnalysisPropertyName))
            .Select(detectedObject => detectedObject.Id)
            .Distinct()
            .ToList();

        if (objectIds.Count == 0)
        {
            return true;
        }

        lock (_objectInferenceSync)
        {
            foreach (var objectId in objectIds)
            {
                frame.Retain();
                var objectTask = new ObjectInferenceTask(frame, objectId);

                if (_latestObjectInferenceTasks.TryGetValue(objectId, out var oldTask))
                {
                    _latestObjectInferenceTasks[objectId] = objectTask;
                    DisposeObjectInferenceTask(oldTask);
                }
                else
                {
                    _latestObjectInferenceTasks[objectId] = objectTask;
                }

                if (_queuedObjectInferenceIds.TryAdd(objectId, 0))
                {
                    try
                    {
                        _objectInferenceIdBuffer.Add(objectId);
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

    private bool IsFrameLevelAnalysis(Frame frame)
    {
        return string.Equals(
            frame.GetProperty<string>(LLMAnalysisType),
            "frame",
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsObjectLevelAnalysis(Frame frame)
    {
        return string.Equals(
            frame.GetProperty<string>(LLMAnalysisType),
            "object",
            StringComparison.OrdinalIgnoreCase);
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposing, 1) == 1)
        {
            return;
        }

        _frameInferenceBuffer.CompleteAdding();
        _objectInferenceIdBuffer.CompleteAdding();
        _inferenceWorkerThread?.Join();

        while (_frameInferenceBuffer.TryTake(out var pendingFrame))
        {
            pendingFrame.Dispose();
        }

        while (_objectInferenceIdBuffer.TryTake(out _))
        {
        }

        foreach (var objectTask in _latestObjectInferenceTasks.Values)
        {
            DisposeObjectInferenceTask(objectTask);
        }

        _latestObjectInferenceTasks.Clear();
        _queuedObjectInferenceIds.Clear();
        _frameInferenceBuffer.Dispose();
        _objectInferenceIdBuffer.Dispose();
        base.Dispose();
    }
}
