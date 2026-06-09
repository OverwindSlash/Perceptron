# Algorithm.Common

`Algorithm.Common` 提供算法模块的公共生命周期、订阅管理、事件调度和 LLM 请求方协议。

## 核心类型

| 类型 | 职责 |
| --- | --- |
| `AlgorithmBase` | 固化初始化、帧引用和释放、公共配置、依赖解析及资源清理 |
| `LlmAlgorithmBase` | 仅供 LLM 请求方使用，负责 prompt、请求标记和结果路由 |
| `AlgorithmRuntimeDependencies` | 提供可测试的运行时依赖集合 |
| `AlgorithmSubscriptionRegistry` | 统一登记并释放 MessagePipe 订阅 |
| `AlgorithmEventDispatcher` | 克隆证据并异步保存、投递和发布领域事件 |
| `LLMPropertyNames` | 定义 Frame/DetectedObject 上的 LLM 协议属性 |
| `LlmRequestOptions` | 描述 LLM 请求范围、队列策略、ID、TTL、prompt 和 JPEG |

## 生命周期

派生类不能覆盖 `Initialize()`、`Analyze()` 或 `Dispose()`。扩展点如下：

1. `ConfigureDefaultPreferences()`：写入算法默认配置。
2. `InitializeMode()`：中间基类初始化模式能力。
3. `InitializeCore()`：解析业务配置并创建业务资源。
4. `AnalyzeCore(Frame)`：实现单帧业务逻辑。
5. `DisposeCore()`：释放模型、缓存和后台资源。

`AlgorithmBase.Analyze()` 在调用 `AnalyzeCore()` 前保留帧，并在所有返回和异常路径释放帧。派生类不得再次调用 `frame.Retain()` 或 `frame.Dispose()`。

## 事件发布

业务事件应实现 `IAnnotatedAlgorithmEvent`，并通过以下方法提交：

- `TryQueueEvent(...)`
- `TryQueueThrottledEvent(...)`

`AlgorithmEventDispatcher` 在实时线程中同步克隆 snapshot，后台任务负责：

1. 保存截图和标注。
2. 生成可选视频片段。
3. 保存领域事件。
4. 投递外部消息。
5. 发布进程内事件。

派生算法不应自行创建未跟踪的事件保存 `Task.Run`。

## LLM 请求方

只有发起请求并消费结果的业务算法继承 `LlmAlgorithmBase`：

- `Algorithm.General.ObjectOccurrenceByLLM`
- `Algorithm.General.SequenceToImage`
- `Algorithm.Ship.LabelsByLLM`

`Algorithm.General.LLM` 是推理提供者，直接继承 `AlgorithmBase`，不会订阅自己发布的结果。

`LlmAlgorithmBase` 仅在 `PerformLLMAnalysis=true` 时读取 `LLMPromptFile` 并订阅 `LLMInferenceResultEvent`。非目标算法不会调用业务处理器，也不得释放事件 snapshot。

请求方使用：

```csharp
MarkFrameForLlm(frame, new LlmRequestOptions
{
    Scope = LLMAnalysisScope.Frame,
    QueuePolicy = LLMQueuePolicy.EventAnchored,
    CandidateEventId = candidateEventId,
    ExpireAtUtc = deadlineUtc
});
```

或：

```csharp
MarkObjectForLlm(frame, detectedObject, new LlmRequestOptions
{
    Scope = LLMAnalysisScope.Object,
    QueuePolicy = LLMQueuePolicy.LatestBestPerObject,
    RequestId = requestId
});
```

派生类实现 `HandleLlmResult()`；需要更严格范围时覆盖 `CanHandleLlmResult()`。`ILLMResultHandler` 适配由基类提供，算法可按业务需要覆盖。

## 测试

公共和迁移测试位于：

```text
test/6.Algorithm/Algorithm.Common.Tests
```

覆盖生命周期、订阅释放、事件调度、LLM 协议、结果归属、重复结果、错误重试、迟到结果和 snapshot 所有权。
