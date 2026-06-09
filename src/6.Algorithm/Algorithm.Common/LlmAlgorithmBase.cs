using Algorithm.Common.Event;
using Algorithm.Common.LLM;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;

namespace Algorithm.Common;

public abstract class LlmAlgorithmBase : AlgorithmBase, ILLMResultHandler
{
    protected bool WillPerformLlmAnalysis { get; private set; }
    protected string LlmPromptFile { get; private set; } = string.Empty;
    protected string UserPrompt { get; private set; } = string.Empty;

    public string RequesterAlgorithmName => AlgorithmName;

    protected LlmAlgorithmBase(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
    }

    protected LlmAlgorithmBase(
        AlgorithmRuntimeDependencies dependencies,
        Dictionary<string, string> preferences)
        : base(dependencies, preferences)
    {
    }

    protected override void InitializeMode()
    {
        WillPerformLlmAnalysis = PreferenceParser.ParseBoolValue(
            Preferences,
            "PerformLLMAnalysis",
            AlgorithmConstants.DefaultWillPerformLLMAnalysis);
        if (!WillPerformLlmAnalysis)
        {
            return;
        }

        var configuredPromptFile = PreferenceParser.ParseStringValue(
            Preferences,
            "LLMPromptFile",
            AlgorithmConstants.DefaultLLMPromptFile);
        LlmPromptFile = Path.GetFullPath(configuredPromptFile);
        if (!File.Exists(LlmPromptFile))
        {
            throw new FileNotFoundException(
                $"LLM prompt file for algorithm '{AlgorithmName}' was not found at '{LlmPromptFile}'.",
                LlmPromptFile);
        }

        UserPrompt = File.ReadAllText(LlmPromptFile);
        Subscribe(
            Services.GetRequiredService<ISubscriber<LLMInferenceResultEvent>>(),
            RouteLlmResult);
    }

    protected virtual bool CanHandleLlmResult(LLMInferenceResultEvent result) =>
        string.Equals(
            result.RequesterAlgorithmName,
            RequesterAlgorithmName,
            StringComparison.Ordinal);

    protected abstract void HandleLlmResult(LLMInferenceResultEvent result);

    protected string MarkFrameForLlm(Frame frame, LlmRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Scope != LLMAnalysisScope.Frame)
        {
            throw new ArgumentException(
                "Frame requests must use the frame analysis scope.",
                nameof(options));
        }

        var requestId = GetRequestId(options);
        SetFrameProtocol(frame, options, requestId, "frame");
        return requestId;
    }

    protected string MarkObjectForLlm(
        Frame frame,
        DetectedObject detectedObject,
        LlmRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(detectedObject);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Scope != LLMAnalysisScope.Object)
        {
            throw new ArgumentException(
                "Object requests must use the object analysis scope.",
                nameof(options));
        }

        var requestId = GetRequestId(options);
        SetFrameProtocol(frame, options, requestId, "object");
        detectedObject.SetProperty(LLMPropertyNames.Analysis, true);
        detectedObject.SetProperty(LLMPropertyNames.RequestId, requestId);
        detectedObject.SetProperty(
            LLMPropertyNames.RequesterAlgorithmName,
            RequesterAlgorithmName);
        detectedObject.SetProperty(
            LLMPropertyNames.CandidateEventId,
            options.CandidateEventId);
        detectedObject.SetProperty(
            LLMPropertyNames.QueuePolicy,
            options.QueuePolicy.ToString());
        detectedObject.SetProperty(
            LLMPropertyNames.ExpireAtUtc,
            options.ExpireAtUtc);
        return requestId;
    }

    public virtual bool CanHandle(LLMAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return string.Equals(
            result.RequesterAlgorithmName,
            RequesterAlgorithmName,
            StringComparison.Ordinal);
    }

    public virtual Task HandleAsync(
        LLMAnalysisResult result,
        LLMReconcileContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        RouteLlmResult(
            LLMInferenceResultEvent.FromAnalysisResult(
                result,
                EventName));
        return Task.CompletedTask;
    }

    private void RouteLlmResult(LLMInferenceResultEvent result)
    {
        if (CanHandleLlmResult(result))
        {
            HandleLlmResult(result);
        }
    }

    private void SetFrameProtocol(
        Frame frame,
        LlmRequestOptions options,
        string requestId,
        string analysisType)
    {
        frame.SetProperty(LLMPropertyNames.Analysis, true);
        frame.SetProperty(LLMPropertyNames.AnalysisType, analysisType);
        frame.SetProperty(
            LLMPropertyNames.AnalysisPrompt,
            options.Prompt ?? UserPrompt);
        frame.SetProperty(
            LLMPropertyNames.AnalysisImageJpeg,
            options.ImageJpeg);
        frame.SetProperty(LLMPropertyNames.RequestId, requestId);
        frame.SetProperty(
            LLMPropertyNames.RequesterAlgorithmName,
            RequesterAlgorithmName);
        frame.SetProperty(
            LLMPropertyNames.CandidateEventId,
            options.CandidateEventId);
        frame.SetProperty(
            LLMPropertyNames.QueuePolicy,
            options.QueuePolicy.ToString());
        frame.SetProperty(
            LLMPropertyNames.ExpireAtUtc,
            options.ExpireAtUtc);
    }

    private static string GetRequestId(LlmRequestOptions options) =>
        string.IsNullOrWhiteSpace(options.RequestId)
            ? Guid.NewGuid().ToString("N")
            : options.RequestId;
}
