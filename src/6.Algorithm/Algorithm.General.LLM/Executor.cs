using Algorithm.Common;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using Algorithm.Common.Event;

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
        foreach (var frame in _inferenceBuffer.GetConsumingEnumerable())
        {
            try
            {
                ProcessInference(frame);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing LLM inference. SourceId: {SourceId}, FrameId: {FrameId}", frame.SourceId, frame.FrameId);
            }
            finally
            {
                frame.Dispose(); // 和 EnqueueFrameForInference 方法中的 Retain 配对
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

        var base64Image = frame.Scene.ToBase64String();
        var userMessage = $"{userPrompt}\n\ndata:image/jpeg;base64,{base64Image}";
        var stopwatch = Stopwatch.StartNew();
        var inferenceResult = CallLLMInferenceAPI(userMessage);
        stopwatch.Stop();

        Log.Information("LLM inference completed. Model: {ModelName}, Result: {Result}", ModelName, inferenceResult);

        var inferenceEvent = new LLMInferenceResultEvent(
            sourceId: frame.SourceId,
            eventType: LLMInferenceResultEvent.EventType,
            eventName: EventName,
            algorithmName: AlgorithmName,
            modelName: ModelName,
            inferenceTime: stopwatch.Elapsed,
            jsonResult: inferenceResult);
        _inferenceResultEventPublisher.Publish(inferenceEvent);
    }

    private string CallLLMInferenceAPI(string message)
    {
        List<ChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            messages.Add(new SystemChatMessage(SystemPrompt));
        }

        messages.Add(new UserChatMessage(message));
        ChatCompletion completion = _chatClient.CompleteChat(messages);

        if (completion.Content.Count == 0)
        {
            return string.Empty;
        }

        return string.Concat(completion.Content.Select(item => item.Text));
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

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
            _inferenceBuffer.Add(frame);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            frame.Dispose();
            Log.Warning(ex, "LLM inference buffer is closed. FrameId: {FrameId}", frame.FrameId);
            return false;
        }
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
