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
    private const string LLMAnalysisPropertyName = "LLMAnalysis";
    private const string LLMAnalysisPromptPropertyName = "LLMAnalysisPrompt";

    public string ServerUrl { get; private set; }
    public string ApiKey { get; private set; }
    public string ModelName { get; private set; }

    public string SystemPrompt { get; private set; }

    private IPublisher<LLMInferenceResultEvent> _inferenceResultEventPublisher = null!;
    private ChatClient _chatClient = null!;

    private readonly BlockingCollection<Frame> _inferenceBuffer = new(new ConcurrentQueue<Frame>());
    private readonly object _inferenceBufferSync = new();
    private Thread? _inferenceWorkerThread;
    private int _isDisposing;

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

            try
            {
                lock (_inferenceBufferSync)
                {
                    if (_inferenceBuffer.IsCompleted)
                    {
                        return;
                    }

                    _inferenceBuffer.TryTake(out frame);
                }

                if (frame == null)
                {
                    Thread.Sleep(50);
                    continue;
                }

                ProcessInference(frame);
            }
            catch (Exception ex)
            {
                if (frame != null)
                {
                    Log.Error(ex, "Error processing LLM inference. SourceId: {SourceId}, FrameId: {FrameId}", frame.SourceId, frame.FrameId);
                }
            }
            finally
            {
                frame?.Dispose(); // 和 EnqueueFrameForInference 方法中的 Retain 配对
            }
        }
    }

    private void ProcessInference(Frame frame)
    {
        if (!frame.HasProperty(LLMAnalysisPromptPropertyName))
        {
            return;
        }

        string userPrompt = frame.GetProperty<string>(LLMAnalysisPromptPropertyName);

        foreach (var detectedObject in frame.DetectedObjects)
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
            inferenceEvent.Snapshot = detectedObject.Snapshot;
            _inferenceResultEventPublisher.Publish(inferenceEvent);
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
        frame.Retain(); // 和 ProcessInferenceBuffer 中的 finally 块的 Dispose 配对

        try
        {
            lock (_inferenceBufferSync)
            {
                FilterAndRebuildInferenceBuffer(frame);
            }

            return true;
        }
        catch (InvalidOperationException ex)
        {
            frame.Dispose();
            Log.Warning(ex, "LLM inference buffer is closed. FrameId: {FrameId}", frame.FrameId);
            return false;
        }
    }

    private void FilterAndRebuildInferenceBuffer(Frame newFrame)
    {
        List<Frame> pendingFrames = [];
        while (_inferenceBuffer.TryTake(out var pendingFrame))
        {
            pendingFrames.Add(pendingFrame);
        }

        pendingFrames.Add(newFrame);

        Dictionary<string, DetectedObject> bestDetectedObjectById = [];
        foreach (var frame in pendingFrames)
        {
            foreach (var detectedObject in frame.DetectedObjects)
            {
                if (!detectedObject.GetProperty<bool>(LLMAnalysisPropertyName))
                {
                    continue;
                }

                if (!bestDetectedObjectById.TryGetValue(detectedObject.Id, out var currentBestDetectedObject) ||
                    detectedObject.Confidence > currentBestDetectedObject.Confidence)
                {
                    bestDetectedObjectById[detectedObject.Id] = detectedObject;
                }
            }
        }

        int removedFrameCount = 0;
        int removedObjectCount = 0;
        foreach (var frame in pendingFrames)
        {
            bool hasLLMObjectToProcess = false;

            foreach (var detectedObject in frame.DetectedObjects)
            {
                if (!detectedObject.GetProperty<bool>(LLMAnalysisPropertyName))
                {
                    continue;
                }

                if (bestDetectedObjectById.TryGetValue(detectedObject.Id, out var bestDetectedObject) &&
                    !ReferenceEquals(detectedObject, bestDetectedObject))
                {
                    detectedObject.SetProperty(LLMAnalysisPropertyName, false);
                    removedObjectCount++;
                    continue;
                }

                hasLLMObjectToProcess = true;
            }

            if (hasLLMObjectToProcess)
            {
                _inferenceBuffer.Add(frame);
            }
            else
            {
                frame.Dispose();
                removedFrameCount++;
            }
        }

        //if (removedFrameCount > 0 || removedObjectCount > 0)
        //{
        //    Log.Information(
        //        "LLM inference buffer filtered. BufferSize: {BufferSize}, RemovedFrames: {RemovedFrames}, RemovedObjects: {RemovedObjects}",
        //        _inferenceBuffer.Count,
        //        removedFrameCount,
        //        removedObjectCount);
        //}
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposing, 1) == 1)
        {
            return;
        }

        _inferenceBuffer.CompleteAdding();
        _inferenceWorkerThread?.Join();

        while (_inferenceBuffer.TryTake(out var pendingFrame))
        {
            pendingFrame.Dispose();
        }

        _inferenceBuffer.Dispose();
        base.Dispose();
    }
}
