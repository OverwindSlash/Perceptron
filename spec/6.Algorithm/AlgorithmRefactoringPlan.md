# 算法模块 Template Method 重构计划

## 1. 文档目的

本文档用于指导 `src/6.Algorithm` 下算法模块的分阶段重构。

本次重构的核心目标是使用 Template Method（模板方法）模式固定算法模块中稳定、重复且容易出错的处理步骤，同时保留各派生算法的业务判断、状态机、模型调用和标注差异。

本文档仅定义计划、目标结构、接口草案、迁移顺序和验收标准。在计划确认之前，不修改现有算法实现。

---

## 2. 背景与现状

当前算法模块以 `Algorithm.Common.AlgorithmBase` 为公共基类，并通过 `IAlgorithmModule` 接入 `AnalysisPipeline`。算法主要存在两种工作模式。

### 2.1 同步处理模式

代表模块：

- `Algorithm.Ship.Labels`
- `Algorithm.General.MotionDetection`
- `Algorithm.General.ObjectDensity`
- `Algorithm.General.ObjectOccurrence`
- `Algorithm.General.RegionAccess`
- `Algorithm.CoastGuard.SmugglingDetection`

典型流程：

1. 接收 `Frame`。
2. 对帧或检测对象执行传统视觉模型或业务规则。
3. 将分析结果写入 `Frame` 或 `DetectedObject` 属性。
4. 生成实时标注。
5. 判断是否触发业务事件。
6. 创建领域事件。
7. 生成事件标注 JSON。
8. 克隆事件截图。
9. 异步保存截图、标注和视频。
10. 保存领域事件。
11. 向外部系统投递事件。
12. 通过 MessagePipe 发布进程内事件。

### 2.2 异步 LLM 处理模式

代表模块：

- `Algorithm.Ship.LabelsByLLM`
- `Algorithm.General.ObjectOccurrenceByLLM`
- `Algorithm.General.SequenceToImage`

典型流程：

1. 传统算法先进行候选筛选。
2. 固化候选帧、对象截图或序列图像。
3. 在 `Frame` 或 `DetectedObject` 上写入 LLM 请求协议属性。
4. `Algorithm.General.LLM` 读取属性并创建异步推理请求。
5. LLM 完成分析并发布 `LLMInferenceResultEvent`。
6. 请求方算法过滤并消费属于自己的结果。
7. 请求方根据业务 JSON、候选状态和超时策略决定确认、拒绝或降级。
8. 确认后进入与同步算法相同的事件持久化和发布流程。

---

## 3. 当前主要问题

### 3.1 帧生命周期由派生类手工维护

绝大多数 `Analyze` 实现都包含：

```csharp
frame.Retain();

// 业务处理

frame.Dispose();
```

该约定没有被基类强制，已经出现提前返回时未执行 `Dispose()` 的路径。例如：

- 找不到配置区域时直接返回。
- 帧处理失败时直接返回。
- 异常发生在显式 `Dispose()` 之前。

这类问题会造成 `Frame`、`Mat` 或检测对象引用无法按预期释放。

### 3.2 事件发布流程大量重复

多个算法重复实现以下代码：

- 判断 `WillPublishEventMessage`。
- 调用 `CheckLocalEventInterval()`。
- 创建领域事件。
- 序列化 `frame.Annotation`。
- 克隆 `frame.Scene` 或对象截图。
- 使用 `Task.Run` 启动后台任务。
- 创建事件目录。
- 保存 JPG 和 JSON。
- 生成视频片段。
- 调用 `EventRepository.SaveDomainEventAsync()`。
- 调用 `MessagePoster.PostDomainEventMessage()`。
- 调用 MessagePipe `IPublisher<T>.Publish()`。
- 捕获异常并释放 `Mat`。

重复实现导致保存顺序、异常处理、文件命名和资源所有权不一致。

### 3.3 同步算法被迫依赖 LLM

当前 `AlgorithmBase.Initialize()` 会：

- 解析 `PerformLLMAnalysis`。
- 读取 `LLMPromptFile`。
- 订阅 `LLMInferenceResultEvent`。

即使同步算法不使用 LLM，也必须承担 prompt 文件和 LLM 订阅相关行为。当前实现甚至会在 `PerformLLMAnalysis=false` 时尝试读取 prompt 文件。

### 3.4 初始化和释放依赖派生类遵守调用顺序

当前派生类需要：

```csharp
public override bool Initialize()
{
    // 派生类初始化
    return base.Initialize();
}
```

以及：

```csharp
public override void Dispose()
{
    // 派生类释放
    base.Dispose();
}
```

如果遗漏 `base.Initialize()` 或 `base.Dispose()`，公共依赖、订阅或资源不会正确初始化和释放。

`Algorithm.General.RegionAccess.Executor.Dispose()` 当前还存在隐藏基类方法而非正确重写的问题。

### 3.5 LLM 结果订阅与资源所有权不清晰

当前所有 `AlgorithmBase` 派生类都会订阅同一个 `LLMInferenceResultEvent`。

风险包括：

- 每个算法都必须重复检查 `RequesterAlgorithmName`。
- 非目标算法可能误处理其他算法的结果。
- `LLMInferenceResultEvent.Snapshot` 的释放责任不明确。
- 如果非目标订阅者释放共享事件中的 `Snapshot`，目标算法可能收到已释放图像。
- 部分算法同时实现 `ProcessEvent` 和 `ILLMResultHandler`，需要避免同一结果通过两条路径被重复消费。

### 3.6 后台事件任务不可跟踪

当前大量使用未保存引用的 `Task.Run`：

- 算法关闭时无法等待已开始的保存任务完成。
- 无法统一处理后台任务异常。
- 无法统计正在处理的事件数量。
- 进程退出时可能丢失尚未完成的截图或事件。

### 3.7 通用配置和业务配置边界不清晰

`AlgorithmBase` 同时包含：

- 标注配置。
- 事件配置。
- LLM 配置。
- 服务依赖。
- 订阅管理。

继续直接向该类增加逻辑会使基类职责过重，降低测试和维护能力。

---

## 4. 重构目标

### 4.1 必须达成的目标

1. `Frame.Retain()` 和 `Frame.Dispose()` 由基类模板统一管理。
2. 派生类不再重写公共 `Analyze()`，只实现业务处理步骤。
3. 公共初始化和释放流程不能被派生类绕过。
4. 同步算法不再加载 prompt，也不再订阅 LLM 结果。
5. LLM 请求方算法通过专用基类获得统一的请求标记和结果路由能力。
6. 事件截图、标注、视频、持久化和发布流程只有一套实现。
7. 后台事件任务可跟踪、可观测，并在关闭时按策略收敛。
8. 保持现有事件类型、事件 JSON 字段、配置键和业务判断语义兼容。
9. 每一阶段均可独立构建、测试和回滚。

### 4.2 非目标

以下内容不应与本次重构捆绑修改：

- 不调整具体算法的识别阈值和业务规则。
- 不改变 LLM prompt 内容。
- 不更换 MessagePipe、OpenCV、OpenAI SDK 或事件仓储实现。
- 不重写 `AnalysisPipeline` 的整体调度模型。
- 不统一所有业务事件的数据字段。
- 不在本次重构中引入分布式消息队列。
- 不顺带修复所有 nullable 和编码警告。

---

## 5. 设计原则

### 5.1 模板固定流程，派生类表达差异

基类负责“什么时候做”和“以什么顺序做”，派生类负责“具体做什么”。

### 5.2 基类保持精简

模板方法保留在基类，但文件保存和发布细节委托给独立协作者，避免 `AlgorithmBase` 成为全能类。

### 5.3 资源所有权必须显式

需要明确：

- 谁创建 `Mat`。
- 谁负责克隆。
- 谁负责最终释放。
- 任务排队失败时由谁清理。

### 5.4 迁移阶段优先保持行为兼容

第一轮迁移只收敛结构，不主动改变：

- 事件触发条件。
- 是否生成视频。
- 是否发送 MessagePipe 事件。
- LLM 超时和候选状态机。

已明确批准的两项行为统一除外：

- MessagePipe 在持久化和外部投递成功后发布。
- 事件文件名使用 24 小时制、毫秒和序列号格式。

### 5.5 异常不应被无声吞掉

模板必须保证资源释放，但默认不把业务异常转换为成功结果。后台事件任务的异常应记录完整上下文。

---

## 6. 目标架构

```text
IAlgorithmModule
    |
    +-- AlgorithmBase
    |     |
    |     +-- 同步算法 Executor
    |     |
    |     +-- Algorithm.General.LLM.Executor
    |
    +-- LlmAlgorithmBase
          |
          +-- ObjectOccurrenceByLLM.Executor
          +-- Ship.LabelsByLLM.Executor
          +-- SequenceToImage.Executor

AlgorithmBase
    |
    +-- AlgorithmEventDispatcher
    +-- AlgorithmSubscriptionRegistry
    +-- 公共标注生成器
    +-- 公共偏好配置

LlmAlgorithmBase
    |
    +-- LLM prompt 加载
    +-- LLM 结果订阅和路由
    +-- Frame/Object 请求标记辅助方法
```

### 6.1 类职责

| 类型 | 职责 |
| --- | --- |
| `AlgorithmBase` | 生命周期模板、帧处理模板、公共配置、公共依赖、标注辅助、事件发布入口 |
| `LlmAlgorithmBase` | LLM 请求方配置、prompt、结果路由、请求属性设置 |
| `AlgorithmEventDispatcher` | 截图、标注、视频、事件仓储、外部消息和进程内发布 |
| `AlgorithmSubscriptionRegistry` | 统一保存和释放 MessagePipe 订阅 |
| `EventPublicationRequest<TEvent>` | 描述一次事件发布所需的数据和可变策略 |
| `IAnnotatedAlgorithmEvent` | 统一提供 `Annotations` 属性 |

---

## 7. `AlgorithmBase` 模板设计

以下 API 是设计草案，名称可以在实施前微调，但职责和调用方向应保持稳定。

### 7.1 初始化模板

```csharp
public abstract class AlgorithmBase : IAlgorithmModule
{
    public bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        ConfigureDefaultPreferences();
        ParseCommonPreferences();
        ResolveCommonServices();
        InitializeMode();
        InitializeCore();

        IsInitialized = true;
        return true;
    }

    protected virtual void ConfigureDefaultPreferences()
    {
    }

    protected virtual void InitializeMode()
    {
    }

    protected abstract void InitializeCore();
}
```

说明：

- `Initialize()` 不再是 `virtual`。
- 派生类不能决定是否调用公共初始化。
- `ConfigureDefaultPreferences()` 用于 `SequenceToImage` 这类需要设置默认配置的模块。
- `InitializeMode()` 为 `LlmAlgorithmBase` 等中间基类保留。
- `InitializeCore()` 只解析业务参数和创建业务资源。
- 初始化失败时应回滚已经创建的订阅和资源，不能设置 `IsInitialized=true`。

建议增加初始化状态：

```csharp
private int _initializationState;
```

用于防止重复初始化和并发初始化。

### 7.2 分析模板

```csharp
public AnalysisResult Analyze(Frame frame)
{
    ArgumentNullException.ThrowIfNull(frame);

    if (!IsInitialized)
    {
        throw new InvalidOperationException(
            $"Algorithm '{AlgorithmName}' has not been initialized.");
    }

    frame.Retain();
    try
    {
        return AnalyzeCore(frame);
    }
    finally
    {
        frame.Dispose();
    }
}

protected abstract AnalysisResult AnalyzeCore(Frame frame);
```

约束：

- 派生类不得再调用 `frame.Retain()`。
- 派生类不得再调用与本次模板对应的 `frame.Dispose()`。
- `AnalyzeCore()` 可以提前返回，基类仍会释放引用。
- `AnalyzeCore()` 抛出异常时，基类只保证释放，不默认吞掉异常。

可选增强：

```csharp
protected virtual void BeforeAnalyze(Frame frame);
protected virtual void AfterAnalyze(Frame frame, AnalysisResult? result);
```

除非迁移中发现真实需求，否则第一版不建议增加这两个钩子。

### 7.3 释放模板

```csharp
public void Dispose()
{
    if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
    {
        return;
    }

    try
    {
        DisposeCore();
    }
    finally
    {
        _subscriptions.Dispose();
        _eventDispatcher.Dispose();
    }
}

protected virtual void DisposeCore()
{
}
```

要求：

- `Dispose()` 幂等。
- 派生类只释放自己创建的模型、状态缓存、线程和信号量。
- 公共订阅和事件后台任务始终由基类释放。
- 即使 `DisposeCore()` 失败，公共资源仍必须继续释放。

---

## 8. 公共配置初始化

建议将当前 `AlgorithmBase.Initialize()` 拆为多个私有方法：

```text
ParseObjectAnnotationPreferences
ParseRegionAnnotationPreferences
ParseEventPreferences
ResolveCommonServices
```

### 8.1 保留在 `AlgorithmBase` 的配置

- `GenerateBBox`
- `BBoxStrokeColor`
- `BBoxStrokeWidth`
- `GenerateObjText`
- `ObjTextColor`
- `ObjTextFontSize`
- `ObjTextShowLabel`
- `ObjTextShowTrackingId`
- `ObjTextShowConfidence`
- `GenerateAnalysisAreas`
- `GenerateExcludeAreas`
- `GenerateLanes`
- `GenerateInterestAreas`
- `GenerateCountLines`
- 各区域和计数线颜色
- `WillPublishEventMessage`
- `WillSaveEventSnapshot`
- `WillSaveEventVideoClip`
- `LocalEventIntervalSec`
- `EventSnapshotDir`
- `EventName`

### 8.2 移出 `AlgorithmBase` 的配置

- `PerformLLMAnalysis`
- `LLMPromptFile`
- `_userPrompt`
- 所有 LLM 属性名常量
- `LLMInferenceResultEvent` 订阅

上述内容迁移到 `LlmAlgorithmBase`。

### 8.3 属性名常量

统一使用：

```csharp
Algorithm.Common.LLM.LLMPropertyNames
```

删除 `AlgorithmBase` 中重复定义的 LLM 字符串常量，避免两套常量发生漂移。

---

## 9. 事件发布模板

### 9.1 设计目标

事件发布模板应固定：

1. 验证发布条件。
2. 在调用线程同步生成标注 JSON。
3. 在调用线程同步克隆截图。
4. 将截图所有权移交给事件调度器。
5. 在后台保存截图和标注。
6. 可选生成视频。
7. 保存领域事件。
8. 投递外部消息。
9. 可选发布进程内事件。
10. 在所有路径释放截图。

同步克隆截图非常重要，因为原始 `Frame.Scene` 或对象截图在后台任务运行前可能已经被释放或复用。

### 9.2 标注事件接口

建议新增：

```csharp
public interface IAnnotatedAlgorithmEvent
{
    string Annotations { get; set; }
}
```

具有 `Annotations` 属性的业务事件实现该接口。

收益：

- 删除 `RegionAccess` 中对 `EnterRegionEvent`、`InRegionEvent` 和 `LeaveRegionEvent` 的逐类型判断。
- 调度器无需反射或 `dynamic`。
- 编译期保证事件支持标注。

如果后续发现该接口适用于算法层之外，可再将其移动到 `Perceptron.Domain`。第一阶段建议放在 `Algorithm.Common`，控制改动范围。

### 9.3 发布请求参数对象

```csharp
public sealed record EventPublicationRequest<TEvent>
    where TEvent : DomainEvent
{
    public required TEvent Event { get; init; }
    public string AnnotationJson { get; init; } = string.Empty;

    // 必须在进入后台任务前调用，返回值的所有权交给调度器。
    public Func<Mat?>? CloneSnapshot { get; init; }

    public long? FrameId { get; init; }
    public string FilePrefix { get; init; } = "event";
    public string? RelativeDirectory { get; init; }
    public string? StableArtifactId { get; init; }

    public Action<TEvent>? PublishInProcess { get; init; }

    public bool SaveSnapshot { get; init; }
    public bool SaveVideoClip { get; init; }
}
```

### 9.4 基类发布入口

```csharp
protected bool TryQueueEvent<TEvent>(
    EventPublicationRequest<TEvent> request)
    where TEvent : DomainEvent
{
    if (!WillPublishEventMessage)
    {
        return false;
    }

    return _eventDispatcher.TryQueue(request);
}
```

对于需要本地限频的算法：

```csharp
protected bool TryQueueThrottledEvent<TEvent>(
    EventPublicationRequest<TEvent> request)
    where TEvent : DomainEvent
{
    if (!WillPublishEventMessage || ShouldSuppressLocalEvent())
    {
        return false;
    }

    return _eventDispatcher.TryQueue(request);
}
```

建议将 `CheckLocalEventInterval()` 重命名为：

```csharp
ShouldSuppressLocalEvent()
```

因为当前方法返回 `true` 的含义是“抑制当前事件”，原名称容易误读。

第一阶段保持当前“单算法实例共享一个限频时间”的语义。是否改为按 `SourceId`、事件类型或对象 ID 限频，应另行设计。

### 9.5 `AlgorithmEventDispatcher`

```csharp
internal sealed class AlgorithmEventDispatcher : IDisposable
{
    public bool TryQueue<TEvent>(
        EventPublicationRequest<TEvent> request)
        where TEvent : DomainEvent;

    private Task PublishAsync<TEvent>(
        PreparedEventPublication<TEvent> publication)
        where TEvent : DomainEvent;
}
```

内部职责：

- 在 `TryQueue()` 中同步执行 `CloneSnapshot()`。
- 创建不会与同秒事件冲突的文件名。
- 跟踪每个后台任务。
- 为任务增加算法名、事件名、SourceId 等日志上下文。
- 捕获并记录后台异常。
- 释放调度器拥有的 `Mat`。
- 在 `Dispose()` 中停止接收新任务并等待已有任务。

### 9.6 建议的持久化顺序

已确认的统一顺序：

```text
保存截图/标注/视频
    -> 保存 DomainEvent
    -> MessagePoster
    -> MessagePipe
```

理由：

- 进程内订阅者收到事件时，路径字段已经完整。
- 不会发布一个最终未能入库的“成功事件”。
- 同步和异步算法具有一致语义。

所有迁移后的算法必须遵守该顺序。`RegionAccess` 等尚未迁移的模块暂时保留现状，并在各自迁移阶段统一调整。

### 9.7 文件命名

当前部分模块使用：

```text
yyyyMMddhhmmss
```

存在两个问题：

- `hh` 是 12 小时制。
- 同一秒内多个事件可能覆盖文件。

已确认统一使用：

```text
{stable-id-or-prefix}_{yyyyMMddHHmmssfff}_{sequence}
```

新事件调度器从首次实现开始采用该格式，并保留各算法现有目录层级和业务前缀。后续算法迁移到调度器时同步切换。

---

## 10. MessagePipe 订阅统一管理

建议新增订阅注册器：

```csharp
internal sealed class AlgorithmSubscriptionRegistry : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    public void Add(IDisposable subscription);

    public void Dispose();
}
```

基类提供：

```csharp
protected void Subscribe<TEvent>(Action<TEvent> handler)
{
    var subscriber = Pipeline.Provider
        .GetRequiredService<ISubscriber<TEvent>>();

    _subscriptions.Add(subscriber.Subscribe(handler));
}
```

派生类初始化示例：

```csharp
protected override void InitializeCore()
{
    Subscribe<ObjectExpiredEvent>(ProcessObjectExpired);
}
```

收益：

- 删除每个算法中的 `_subscriber` 和 `_disposableSubscriber` 字段。
- 避免漏掉释放。
- 允许一个算法注册多个订阅。
- `Dispose()` 不再依赖派生类调用 `base.Dispose()`。

---

## 11. `LlmAlgorithmBase` 设计

### 11.1 适用范围

只有“发起 LLM 请求并消费 LLM 结果”的业务算法继承 `LlmAlgorithmBase`。

`Algorithm.General.LLM` 是推理提供者，不继承该类，继续直接继承 `AlgorithmBase`。

### 11.2 配置与 prompt

```csharp
public abstract class LlmAlgorithmBase : AlgorithmBase
{
    protected bool WillPerformLlmAnalysis { get; private set; }
    protected string LlmPromptFile { get; private set; } = string.Empty;
    protected string UserPrompt { get; private set; } = string.Empty;

    protected override void InitializeMode()
    {
        ParseLlmPreferences();

        if (!WillPerformLlmAnalysis)
        {
            return;
        }

        UserPrompt = LoadPrompt(LlmPromptFile);
        Subscribe<LLMInferenceResultEvent>(RouteLlmResult);
    }
}
```

要求：

- `PerformLLMAnalysis=false` 时不读取 prompt 文件。
- prompt 路径为空或文件不存在时，错误信息包含算法名和绝对路径。
- LLM 模式算法可通过 `ConfigureDefaultPreferences()` 设置自己的默认 prompt。

### 11.3 LLM 结果路由

```csharp
private void RouteLlmResult(LLMInferenceResultEvent result)
{
    if (!CanHandleLlmResult(result))
    {
        return;
    }

    HandleLlmResult(result);
}

protected virtual bool CanHandleLlmResult(
    LLMInferenceResultEvent result)
{
    return result.RequesterAlgorithmName == AlgorithmName;
}

protected abstract void HandleLlmResult(
    LLMInferenceResultEvent result);
```

派生类不再重复最外层 `RequesterAlgorithmName` 判断，但仍可扩展：

- `CandidateEventId` 必须存在。
- `DetectedObjectId` 必须存在。
- `Scope` 必须是 `Frame` 或 `Object`。

### 11.4 Snapshot 所有权规则

必须建立以下规则：

1. 非目标算法不得释放 `LLMInferenceResultEvent.Snapshot`。
2. 命中的业务处理器获得 snapshot 的处理责任。
3. 业务处理器若需要异步保留，应先克隆或明确转移所有权。
4. 解析失败、候选已终态或结果过期时，命中的处理器负责释放。
5. 同一事件不能同时通过 MessagePipe 和 `LLMResultReconciler` 被消费两次。

建议为所有权增加代码注释和单元测试。

### 11.5 LLM 请求辅助对象

建议新增：

```csharp
public sealed record LlmRequestOptions
{
    public required LLMAnalysisScope Scope { get; init; }
    public required LLMQueuePolicy QueuePolicy { get; init; }
    public string? RequestId { get; init; }
    public string? CandidateEventId { get; init; }
    public DateTime? ExpireAtUtc { get; init; }
    public string? Prompt { get; init; }
    public byte[]? ImageJpeg { get; init; }
}
```

基类提供：

```csharp
protected string MarkFrameForLlm(
    Frame frame,
    LlmRequestOptions options);

protected string MarkObjectForLlm(
    Frame frame,
    DetectedObject detectedObject,
    LlmRequestOptions options);
```

这些方法统一设置：

- `LLMAnalysis`
- `LLMAnalysisType`
- `LLMAnalysisPrompt`
- `LLMAnalysisImageJpeg`
- `LLMRequestId`
- `LLMRequesterAlgorithmName`
- `LLMCandidateEventId`
- `LLMQueuePolicy`
- `LLMExpireAtUtc`

派生类仍负责：

- 何时发起请求。
- 请求作用于帧还是对象。
- 候选 ID 和超时时间。
- 证据质量和重新请求策略。

### 11.6 与 `ILLMResultHandler` 的关系

当前代码中存在：

- MessagePipe `LLMInferenceResultEvent` 订阅。
- `ILLMResultHandler` / `LLMResultReconciler`。

第一阶段建议：

- 运行时继续使用当前 MessagePipe 路径。
- `LlmAlgorithmBase` 负责统一过滤和转发。
- 保留 `ILLMResultHandler` 适配入口供现有测试和后续集中归并使用。
- 明确保证同一个结果只有一条运行时消费路径。

后续若 `LLMResultReconciler` 正式接入 `AnalysisPipeline`，应将 MessagePipe 订阅替换为协调器路由，而不是两者并行。

---

## 12. 派生算法最终形态

### 12.1 同步算法示例

```csharp
public sealed class Executor : AlgorithmBase
{
    protected override void InitializeCore()
    {
        Threshold = PreferenceParser.ParseIntValue(
            Preferences,
            "Threshold",
            DefaultThreshold);

        _publisher = Pipeline.Provider
            .GetRequiredService<IPublisher<MyEvent>>();
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var result = RunBusinessAnalysis(frame);
        GenerateBusinessAnnotation(frame, result);

        if (ShouldCreateEvent(result))
        {
            QueueBusinessEvent(frame, result);
        }

        return new AnalysisResult(true);
    }

    protected override void DisposeCore()
    {
        _predictor?.Dispose();
    }
}
```

### 12.2 LLM 算法示例

```csharp
public sealed class Executor : LlmAlgorithmBase
{
    protected override void InitializeCore()
    {
        CandidateTimeoutSeconds = PreferenceParser.ParseIntValue(
            Preferences,
            "CandidateTimeoutSeconds",
            12);
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var candidate = TryCreateCandidate(frame);
        if (candidate == null)
        {
            return new AnalysisResult(true);
        }

        MarkFrameForLlm(
            frame,
            new LlmRequestOptions
            {
                Scope = LLMAnalysisScope.Frame,
                QueuePolicy = LLMQueuePolicy.EventAnchored,
                CandidateEventId = candidate.Id,
                ExpireAtUtc = candidate.DeadlineUtc
            });

        return new AnalysisResult(true);
    }

    protected override void HandleLlmResult(
        LLMInferenceResultEvent result)
    {
        // 业务 JSON、候选状态、超时和最终发布
    }
}
```

---

## 13. 分阶段实施计划

### 阶段 0：建立行为基线

目标：在结构变化前记录当前可观察行为。

任务：

- [ ] 保存当前全解决方案构建结果。
- [ ] 保存 `Algorithm.Common.Tests` 测试结果。
- [ ] 为典型同步事件记录字段、标注、路径和发布顺序。
- [ ] 为典型 LLM 事件记录请求属性和结果过滤逻辑。
- [ ] 明确各算法 MessagePipe 发布是在持久化之前还是之后。
- [ ] 明确各算法 snapshot 的来源和当前释放者。
- [ ] 记录所有现有配置键及默认值。

当前已知基线：

- `dotnet build Perceptron.slnx --no-restore`：成功，0 错误。
- `Algorithm.Common.Tests`：20/20 通过。
- 解决方案存在较多既有 nullable 和隐藏成员警告。

### 阶段 1：引入公共基础设施，不迁移业务算法

新增建议文件：

```text
src/6.Algorithm/Algorithm.Common/
    AlgorithmBase.cs
    LlmAlgorithmBase.cs
    Lifecycle/
        AlgorithmSubscriptionRegistry.cs
    Event/
        IAnnotatedAlgorithmEvent.cs
        EventPublicationRequest.cs
        AlgorithmEventDispatcher.cs
```

任务：

- [ ] 实现订阅注册器。
- [ ] 实现事件发布请求参数对象。
- [ ] 实现事件调度器。
- [ ] 增加后台任务跟踪和关闭等待。
- [ ] 增加 snapshot 所有权测试。
- [ ] 增加文件名冲突测试。
- [ ] 暂不删除旧基类 API，保证项目可逐步迁移。

### 阶段 2：重构 `AlgorithmBase` 生命周期模板

任务：

- [ ] 将公共初始化拆分为私有步骤。
- [ ] 增加 `InitializeCore()`。
- [ ] 增加 `AnalyzeCore(Frame)`。
- [ ] 增加 `DisposeCore()`。
- [ ] 统一 `Frame.Retain/Dispose`。
- [ ] 将 LLM 配置和订阅移出 `AlgorithmBase`。
- [ ] 将 `CheckLocalEventInterval()` 重命名或增加语义清晰的包装方法。
- [ ] 过渡期保留公共方法的旧 override 兼容入口。
- [ ] 为未迁移算法提供默认 Core 钩子，保证分批迁移时可编译。
- [ ] 增加约束测试，确保已迁移算法不再 override 公共生命周期方法。

此阶段必须与第一批派生类迁移在同一可编译提交中完成。当前里程碑只有已迁移算法执行模板流程；未迁移算法暂时继续使用旧 override。

当全部算法迁移完成后：

- [ ] 使公共 `Initialize()`、`Analyze()` 和 `Dispose()` 不可重写。
- [ ] 将 Core 钩子收紧为最终目标约束。
- [ ] 删除旧 override 兼容入口。

### 阶段 3：迁移低风险同步算法

迁移顺序：

1. `Algorithm.GenerateDebugAnnotations`
2. `Algorithm.General.MotionDetection`
3. `Algorithm.General.ObjectDensity`

验证重点：

- `AnalyzeCore()` 提前返回仍释放 frame。
- 实时标注不变。
- Motion 和 Density 事件字段不变。
- 截图、标注、视频、仓储和消息投递正常。
- MessagePipe 发布次数保持一致。
- MessagePipe 在持久化和外部投递成功后发布。
- 事件文件名使用 24 小时制、毫秒和序列号格式。

本轮已确认的实施范围截止到阶段 3。阶段 3 完成并通过评审后，再决定是否启动阶段 4 及后续迁移。

### 阶段 4：迁移普通业务事件算法

迁移顺序：

1. `Algorithm.General.ObjectOccurrence`
2. `Algorithm.General.Classify`
3. `Algorithm.CoastGuard.SmugglingDetection`

验证重点：

- 本地事件限频行为不变。
- Classify 的 `ObjectBestSnapshotCreatedEvent` 订阅由注册器管理。
- Smuggling 的对象过期订阅和历史缓存正确释放。
- 对象截图和整帧截图的所有权清晰。

### 阶段 5：迁移多事件与对象生命周期算法

迁移顺序：

1. `Algorithm.General.RegionAccess`
2. `Algorithm.Ship.Labels`

`RegionAccess` 重点：

- 三种区域事件统一实现 `IAnnotatedAlgorithmEvent`。
- 删除 `ProcessRegionEventCommon` 中的类型判断。
- 修复隐藏基类 `Dispose()` 的问题。
- 将 Enter/In/Leave 三种事件统一调整为持久化和外部投递成功后发布 MessagePipe。
- 保持对象状态清理逻辑。

`Ship.Labels` 重点：

- 模型和对象过期订阅进入模板生命周期。
- 明确缓存中 `Frame` 与 `Snapshot` 的所有权。
- 发布任务使用克隆截图，避免将缓存原图直接交给后台任务释放。
- 对象状态移除时释放未发布的截图。

### 阶段 6：引入并迁移 `LlmAlgorithmBase`

迁移顺序：

1. `Algorithm.General.ObjectOccurrenceByLLM`
2. `Algorithm.General.SequenceToImage`
3. `Algorithm.Ship.LabelsByLLM`

任务：

- [ ] 仅 LLM 请求方加载 prompt。
- [ ] 统一 `RequesterAlgorithmName` 过滤。
- [ ] 使用 `MarkFrameForLlm()` 和 `MarkObjectForLlm()`。
- [ ] 保留各算法现有候选状态机。
- [ ] 保留各算法现有超时策略。
- [ ] 保留 `ILLMResultHandler` 适配。
- [ ] 增加重复结果和错误路由测试。
- [ ] 增加非目标算法不得释放 snapshot 的测试。

### 阶段 7：迁移 `Algorithm.General.LLM`

该模块继续直接继承 `AlgorithmBase`。

任务：

- [ ] 将 `Analyze()` 改为 `AnalyzeCore()`。
- [ ] 将 worker、scheduler、semaphore 和 cancellation 的释放移到 `DisposeCore()`。
- [ ] 验证关闭期间的请求取消和 worker join。
- [ ] 确保它不订阅自己的 `LLMInferenceResultEvent`。

### 阶段 8：删除兼容层并更新文档

任务：

- [ ] 删除旧 `SetSubscriber()`。
- [ ] 删除 `AlgorithmBase.ProcessEvent(LLMInferenceResultEvent)`。
- [ ] 删除基类中的重复 LLM 常量。
- [ ] 删除派生类中重复的 `Task.Run` 事件保存代码。
- [ ] 删除派生类中的 `frame.Retain/Dispose`。
- [ ] 更新 `Algorithm-Implementation-Guide.md`。
- [ ] 更新 `Algorithm.Common/README.md`。
- [ ] 更新 `spec/6.Algorithm/AlgorithmModule-Spec.md`。
- [ ] 增加同步算法和 LLM 算法的标准模板示例。

---

## 14. 模块迁移矩阵

| 模块 | 模式 | 主要迁移内容 | 风险 |
| --- | --- | --- | --- |
| GenerateDebugAnnotations | 同步 | Analyze 模板 | 低 |
| MotionDetection | 同步 | Analyze、事件调度、资源释放 | 中 |
| ObjectDensity | 同步 | 提前返回释放、事件调度 | 中 |
| ObjectOccurrence | 同步 | 区域判断、限频、视频、事件调度 | 中 |
| Classify | 同步 + 事件回调 | 订阅管理、对象截图事件 | 中 |
| SmugglingDetection | 同步 + 对象状态 | 订阅、历史缓存、事件调度 | 中高 |
| RegionAccess | 同步 + 多事件 | 三种事件、发布顺序、Dispose 修复 | 高 |
| Ship.Labels | 同步 + 对象过期 | 模型、缓存 Frame/Mat、对象最终事件 | 高 |
| ObjectOccurrenceByLLM | LLM 帧级候选 | 候选状态、超时、结果路由 | 高 |
| SequenceToImage | LLM 帧级自定义证据 | 帧缓冲、序列图、超时、snapshot | 高 |
| Ship.LabelsByLLM | LLM 对象级 | 对象状态机、迟到结果、snapshot | 很高 |
| General.LLM | LLM 提供者 | Analyze 模板、worker 释放 | 高 |

---

## 15. 兼容性要求

### 15.1 配置兼容

第一轮重构必须继续支持现有配置键。不得仅因内部属性重命名而要求修改部署配置。

### 15.2 事件兼容

必须保持：

- `EventType`
- `EventName`
- `AlgorithmName`
- 业务事件字段
- JSON 命名策略
- `Message`
- `ImageLocalPath`
- `ImageJsonLocalPath`
- `VideoLocalPath`

### 15.3 LLM 协议兼容

必须保持现有属性名和语义：

- `LLMAnalysis`
- `LLMAnalysisType`
- `LLMAnalysisPrompt`
- `LLMAnalysisImageJpeg`
- `LLMRequestId`
- `LLMRequesterAlgorithmName`
- `LLMCandidateEventId`
- `LLMQueuePolicy`
- `LLMExpireAtUtc`

### 15.4 行为兼容

第一轮迁移不得改变：

- 同一业务条件下是否触发事件。
- 本地限频的默认值。
- LLM queue policy。
- 候选事件 deadline。
- 对象过期后的等待时间。
- 超时后的 Drop、PublishTraditional 或 PublishUnknown 行为。

---

## 16. 测试计划

### 16.1 `AlgorithmBase` 生命周期测试

- [ ] 初始化按“默认配置、公共配置、模式配置、业务初始化”的顺序执行。
- [ ] 初始化失败时 `IsInitialized=false`。
- [ ] 重复初始化不会重复订阅。
- [ ] `AnalyzeCore()` 成功时释放 frame 引用。
- [ ] `AnalyzeCore()` 返回失败时释放 frame 引用。
- [ ] `AnalyzeCore()` 提前返回时释放 frame 引用。
- [ ] `AnalyzeCore()` 抛异常时释放 frame 引用。
- [ ] `Dispose()` 可重复调用。
- [ ] `DisposeCore()` 抛异常时公共订阅仍被释放。

### 16.2 事件调度器测试

- [ ] snapshot 在进入后台任务前完成克隆。
- [ ] snapshot 在成功路径释放。
- [ ] 图片保存失败时 snapshot 释放。
- [ ] JSON 保存失败时 snapshot 释放。
- [ ] 仓储失败时 snapshot 释放。
- [ ] MessagePoster 失败时记录错误。
- [ ] MessagePipe 发布阶段符合请求配置。
- [ ] `WillSaveEventSnapshot=false` 时不克隆图像。
- [ ] `WillSaveEventVideoClip=true` 时传递正确 frame ID。
- [ ] 同一毫秒多个事件不会覆盖文件。
- [ ] `Dispose()` 停止接收新任务。
- [ ] `Dispose()` 在超时内等待已有任务。

### 16.3 订阅测试

- [ ] 每个订阅只注册一次。
- [ ] 多种事件可以同时订阅。
- [ ] Dispose 后不再收到事件。
- [ ] 某个订阅释放失败不阻止其他订阅释放。

### 16.4 LLM 基类测试

- [ ] 同步算法不请求 `ISubscriber<LLMInferenceResultEvent>`。
- [ ] `PerformLLMAnalysis=false` 时不读取 prompt。
- [ ] prompt 不存在时错误包含算法和路径。
- [ ] 只处理 `RequesterAlgorithmName` 匹配的结果。
- [ ] Frame 和 Object scope 可以增加额外过滤。
- [ ] 非目标结果的 snapshot 不被释放。
- [ ] 目标结果解析失败时 snapshot 按所有权规则释放。
- [ ] 请求辅助方法写入完整属性。
- [ ] UTC deadline 保持不变。
- [ ] 未提供 RequestId 时自动生成唯一 ID。

### 16.5 回归测试

每迁移一个模块至少执行：

```powershell
dotnet build <algorithm-project> --no-restore
dotnet test test/6.Algorithm/Algorithm.Common.Tests/Algorithm.Common.Tests.csproj --no-restore
```

每个阶段结束执行：

```powershell
dotnet build Perceptron.slnx --no-restore
dotnet test Perceptron.slnx --no-build
```

如果全量测试存在既有外部模型或文件依赖，应单独记录，不得将其误判为本次重构回归。

---

## 17. 可观测性计划

事件调度器至少记录以下信息：

- AlgorithmName
- EventName
- EventType
- SourceId
- ObjectId 或 CandidateEventId（如果存在）
- FrameId
- 后台任务 ID
- 图片、JSON 和视频路径
- 持久化耗时
- 外部投递耗时
- MessagePipe 发布阶段
- 异常步骤

建议增加轻量计数：

- 正在执行的事件任务数。
- 已排队事件数。
- 成功事件数。
- 持久化失败数。
- 外部投递失败数。
- 关闭时未完成任务数。

第一阶段可使用日志和内存计数，不要求引入新的监控依赖。

---

## 18. 后台任务关闭策略

增加公共配置：

```text
EventTaskShutdownTimeoutSeconds
```

该配置允许覆盖，默认值为 `10` 秒。

关闭流程：

1. 标记调度器不再接受新事件。
2. 获取当前任务快照。
3. 等待任务在超时内完成。
4. 超时后记录未完成任务数量和事件标识。
5. 不使用强制线程终止。
6. 继续释放订阅和算法业务资源。

需要注意：当前 `IAlgorithmModule` 仅实现同步 `IDisposable`，因此第一阶段采用有限时同步等待。若未来需要完全异步关闭，可再评估 `IAsyncDisposable`。

---

## 19. 风险与应对

### 19.1 大范围签名变化导致一次性编译失败

风险：

`Analyze()` 从抽象方法改为模板方法后，所有派生类必须改为 `AnalyzeCore()`。

应对：

- 按可编译批次迁移。
- 基类签名变化和第一批算法放在同一阶段。
- 每批不混入业务逻辑调整。

### 19.2 发布顺序变化

风险：

部分订阅者可能依赖事件在持久化前发布。

应对：

- 迁移后的模块统一在持久化和外部投递成功后发布 MessagePipe。
- 在每个模块迁移前识别依赖旧发布时机的订阅者。
- 对发布顺序增加回归测试，并在模块迁移说明中标注这一行为变化。

### 19.3 snapshot 双重释放或泄漏

风险：

现有代码有时直接把缓存 snapshot 交给后台任务，有时 Clone，有时由 LLM event 携带。

应对：

- 调度器只接收自己拥有的克隆。
- 请求参数使用 `CloneSnapshot` 而不是模糊的 `Snapshot`。
- 为每类来源编写所有权测试。

### 19.4 LLM 结果重复消费

风险：

MessagePipe 和 `LLMResultReconciler` 同时启用时，同一结果可能被处理两次。

应对：

- 明确当前唯一运行路径。
- 使用 RequestId/CandidateEventId 幂等保护。
- 接入协调器时移除直接订阅。

### 19.5 关闭等待影响退出时间

风险：

事件视频生成可能超过关闭等待时间。

应对：

- 配置有限等待时间。
- 记录未完成任务。
- 后续评估取消令牌和独立持久化队列。

### 19.6 基类继续膨胀

风险：

将所有事件文件操作直接写入 `AlgorithmBase` 会降低可维护性。

应对：

- 基类只保留模板和受保护入口。
- IO、任务跟踪和订阅分别由协作者负责。
- 业务状态机永远留在派生类。

---

## 20. 实施完成标准

### 20.1 当前实施里程碑

当前里程碑仅包含阶段 0 至阶段 3。满足以下条件后，可提交首批同步算法评审：

1. 公共事件调度器和订阅注册器完成。
2. `AlgorithmBase` 已提供生命周期模板，三个已迁移算法均通过 Core 钩子进入模板流程。
3. `Algorithm.GenerateDebugAnnotations`、`Algorithm.General.MotionDetection` 和 `Algorithm.General.ObjectDensity` 已迁移。
4. 三个算法不再手工调用模板对应的 `frame.Retain/Dispose`。
5. Motion 和 Density 的事件保存使用 `AlgorithmEventDispatcher`。
6. MessagePipe 在持久化和外部投递成功后发布。
7. 事件文件名使用 24 小时制、毫秒和序列号格式。
8. 后台任务关闭等待可配置，默认 10 秒。
9. 当前里程碑新增测试全部通过。
10. 全解决方案构建无错误。
11. 未迁移算法仍可通过旧 override 兼容入口正常编译。

### 20.2 全部迁移完成标准

后续阶段全部实施时，满足以下全部条件后整体重构才视为完成：

1. 所有算法不再手工调用模板对应的 `frame.Retain/Dispose`。
2. 所有算法使用 `AnalyzeCore()`。
3. 所有公共事件文件保存逻辑由 `AlgorithmEventDispatcher` 执行。
4. 所有 MessagePipe 订阅由注册器统一释放。
5. 同步算法不加载 prompt、不订阅 LLM 结果。
6. LLM 请求方继承 `LlmAlgorithmBase`。
7. `Algorithm.General.LLM` 不订阅自己的结果事件。
8. 所有派生类资源通过 `DisposeCore()` 释放。
9. 不再存在隐藏 `AlgorithmBase.Dispose()` 的实现。
10. 所有事件和配置兼容性测试通过。
11. `Algorithm.Common.Tests` 新增生命周期、事件调度和 LLM 路由测试。
12. 全解决方案构建无错误。
13. 与本次重构相关的新增测试全部通过。
14. 开发指南和算法规范已更新。

---

## 21. 已确认的实施决策

### 决策 1：MessagePipe 默认发布时机

已确认：

```text
持久化和外部投递成功后发布 MessagePipe
```

迁移后的模块不再保留持久化前发布分支。尚未迁移的模块在各自迁移阶段切换。

### 决策 2：后台任务关闭等待

已确认：

- 等待时间可配置。
- 默认等待 10 秒。
- 超时只记录，不强制终止线程。

### 决策 3：事件文件命名

已确认：

- 使用 24 小时制。
- 增加毫秒和序列号。
- 新事件调度器立即采用新格式。
- 保留现有目录层级和业务前缀。

### 决策 4：LLM 结果唯一运行路径

已确认：

- 运行时继续使用 MessagePipe。
- `ILLMResultHandler` 作为适配接口保留。
- 不同时启用直接订阅和 Reconciler。

### 决策 5：迁移范围

已确认：

- 当前只实施到阶段 3。
- 当前迁移 `Algorithm.GenerateDebugAnnotations`、`Algorithm.General.MotionDetection` 和 `Algorithm.General.ObjectDensity`。
- 首批同步算法完成并评审后，再决定是否启动阶段 4 及后续迁移。

---

## 22. 推荐实施顺序摘要

```text
行为基线
  -> 事件调度器和订阅注册器
  -> AlgorithmBase 生命周期模板
  -> 低风险同步算法
  -> 首批评审检查点
  -> 普通事件算法
  -> RegionAccess / Ship.Labels
  -> LlmAlgorithmBase
  -> 三个 LLM 请求方算法
  -> General.LLM
  -> 删除兼容层
  -> 全量回归与文档更新
```

当前实施在“首批评审检查点”结束。后续顺序保留为下一阶段路线，待首批同步算法评审通过后启动。
