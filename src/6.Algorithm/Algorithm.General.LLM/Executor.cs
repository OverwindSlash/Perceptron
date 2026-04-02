using Algorithm.Common;
using Algorithm.General.LLM.Event;
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
using System.Diagnostics;

namespace Algorithm.General.LLM;

public class Executor : AlgorithmBase
{
    private const string DefaultModelName = "gpt-4o-mini";

    public string ServerUrl { get; private set; }
    public string ApiKey { get; private set; }
    public string ModelName { get; private set; }

    public string SystemPrompt { get; private set; }
    public string UserPrompt { get; private set; }
    private IPublisher<InferenceResultEvent> _inferenceResultEventPublisher = null!;
    private ChatClient _chatClient = null!;

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
        _inferenceResultEventPublisher = provider.GetRequiredService<IPublisher<InferenceResultEvent>>();

        ServerUrl = PreferenceParser.ParseStringValue(Preferences, "ServerUrl", string.Empty);
        ApiKey = PreferenceParser.ParseStringValue(Preferences, "ApiKey", string.Empty);
        ModelName = PreferenceParser.ParseStringValue(Preferences, "ModelName", DefaultModelName);

        SystemPrompt = PreferenceParser.ParseStringValue(Preferences, "SystemPrompt", string.Empty);
        UserPrompt = PreferenceParser.ParseStringValue(Preferences, "UserPrompt", string.Empty);

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

        return base.Initialize();
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        var base64Image = frame.Scene.ToBase64String();
        var userMessage = $"{UserPrompt}\n\ndata:image/jpeg;base64,{base64Image}";
        var stopwatch = Stopwatch.StartNew();
        var inferenceResult = CallLLMInferenceAPI(userMessage);
        stopwatch.Stop();

        Log.Information("LLM inference completed. Model: {ModelName}, Result: {Result}", ModelName, inferenceResult);

        var inferenceEvent = new InferenceResultEvent(
            sourceId: frame.SourceId,
            eventType: InferenceResultEvent.EventType,
            eventName: EventName,
            algorithmName: AlgorithmName,
            modelName: ModelName,
            inferenceTime: stopwatch.Elapsed,
            jsonResult: inferenceResult);
        _inferenceResultEventPublisher.Publish(inferenceEvent);

        frame.Dispose();
        return new AnalysisResult(true);
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
}
