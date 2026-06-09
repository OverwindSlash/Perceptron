# 算法模块实现指南

## 同步算法模板

```csharp
public sealed class Executor : AlgorithmBase
{
    public Executor(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Example";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Example algorithm.";
    }

    protected override void InitializeCore()
    {
        // 解析业务配置并从 Services 获取业务依赖。
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        // 不要手动 Retain/Dispose frame。
        return new AnalysisResult(true);
    }

    protected override void DisposeCore()
    {
        // 释放模型、缓存和业务资源。
    }
}
```

## LLM 请求方模板

```csharp
public sealed class Executor : LlmAlgorithmBase
{
    public Executor(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "LLM Example";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Example LLM requester.";
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        MarkFrameForLlm(frame, new LlmRequestOptions
        {
            Scope = LLMAnalysisScope.Frame,
            QueuePolicy = LLMQueuePolicy.EventAnchored
        });
        return new AnalysisResult(true);
    }

    protected override void HandleLlmResult(
        LLMInferenceResultEvent result)
    {
        // 当前结果已通过 RequesterAlgorithmName 过滤。
        // 命中处理器负责 snapshot 的释放或所有权转移。
    }
}
```

## 订阅

在 `InitializeCore()` 中使用基类注册器：

```csharp
Subscribe(
    Services.GetRequiredService<ISubscriber<ObjectExpiredEvent>>(),
    HandleObjectExpired);
```

不要暴露独立 `SetSubscriber()`，也不要手动保存订阅 `IDisposable`。

## 事件

事件类实现 `IAnnotatedAlgorithmEvent`：

```csharp
public sealed class ExampleEvent
    : DomainEvent, IAnnotatedAlgorithmEvent
{
    public string Annotations { get; set; } = string.Empty;
}
```

提交事件：

```csharp
TryQueueEvent(new EventPublicationRequest<ExampleEvent>
{
    Event = eventMessage,
    AnnotationJson = annotationJson,
    CloneSnapshot = () => frame.Scene.Clone(),
    FrameId = frame.FrameId,
    FilePrefix = "example",
    SaveSnapshot = WillSaveEventSnapshot,
    SaveVideoClip = WillSaveEventVideoClip
});
```

## 约束

- 不覆盖公共 `Initialize/Analyze/Dispose`。
- 不在派生算法中手动管理帧引用。
- 不使用未被公共调度器跟踪的事件保存 `Task.Run`。
- 同步算法不读取 prompt，也不订阅 LLM 结果。
- LLM 请求方必须使用 `MarkFrameForLlm` 或 `MarkObjectForLlm`。
- 非目标 LLM 结果的 snapshot 保持原所有者管理。
- 缓存 `Mat` 时必须明确所有权，并在替换、过期和 `DisposeCore()` 中释放。
