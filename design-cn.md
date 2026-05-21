# 视觉 LLM 异步确认架构设计

## 1. 背景

当前项目是一个 AI 视频分析流水线，主要使用传统视觉模型，尤其是基于 YOLO 的目标检测模型，对视频帧中的目标对象进行识别，然后再执行后续业务分析算法。

传统模型链路的优势是速度足够快：

- 常规帧处理期望控制在 40 ms 以内。
- 复杂业务算法可以通过跳帧降低计算压力。
- 下游业务算法依赖 `Frame`、`DetectedObject`、目标跟踪 ID、标注信息和领域事件。

当前主要问题是精度不足。传统模型可能出现误报和漏报，因此希望引入视觉 LLM 作为二次确认层，提高最终事件和对象属性判断的可靠性。

但视觉 LLM 的推理速度明显更慢，通常每次请求需要 1-6 秒。如果实时流水线同步等待 LLM，帧处理会被阻塞，无法保持实时性。如果异步调用 LLM，则必须解决一个核心问题：LLM 在数秒后返回结果时，如何准确关联到当时的帧、目标对象和候选事件上下文。

本文档记录当前阶段形成的架构结论，便于后续在其他机器上继续使用 Codex 分析、设计和实施改造。

## 2. 现有参考实现

相关代码路径：

- `src/6.Algorithm/Algorithm.Ship.LabelsByLLM/`
- `src/6.Algorithm/Algorithm.General.LLM/`
- `src/6.Algorithm/Algorithm.General.ObjectOccurrenceByLLM/`
- `src/2.Service/Perceptron.Service/Pipeline/VideoFrameSlideWindow.cs`

当前协作方式：

1. 业务算法判断是否需要 LLM 分析。
2. 业务算法在 `Frame` 或 `DetectedObject` 上设置属性：
   - `LLMAnalysis`
   - `LLMAnalysisType`
   - `LLMAnalysisPrompt`
3. 后置的 `Algorithm.General.LLM` 在流水线中识别这些属性。
4. `Algorithm.General.LLM` 将推理任务加入异步队列。
5. LLM 推理完成后发布 `LLMInferenceResultEvent`。
6. 业务算法订阅并消费 LLM 结果事件，更新自身状态或生成业务事件。

当前 `Algorithm.General.LLM` 中已经有一些正确的雏形：

- 帧级任务按 `SourceId` 保留最新帧。
- 对象级任务按对象 ID 保留最新或更优的对象任务。
- LLM 推理由后台线程异步处理。

这个方向是正确的，但需要进一步架构化，避免生命周期竞态，并明确“候选事件如何被 LLM 确认”的语义。

## 3. 已识别的关键问题

### 3.1 实时流水线绝不能等待 LLM

LLM 链路不能阻塞 `Analyze(Frame frame)`。传统算法应继续快速产生候选事实，LLM 确认应在独立的异步链路中完成。

### 3.2 LLM 结果必须能关联到历史证据

当 LLM 结果在数秒后返回时，原始帧可能已经从滑动窗口中过期并被释放。LLM 结果不能依赖“再次找到原始的活跃 `Frame`”。

每个 LLM 请求和结果都必须携带足够完整的身份信息：

- `RequestId`
- `CandidateEventId`
- `SourceId`
- `FrameId`
- `OffsetMilliSec`
- `UtcTimeStamp`
- `ObjectId`，如果是对象级分析
- `TrackKey` 或其他跟踪身份，如果可用
- 分析范围：帧级或对象级

### 3.3 对象过期不是所有事件的统一发布点

有些事件是对象生命周期总结型事件。例如船舶标签汇总，可以很自然地在对象过期时发布。

但另一些事件必须在 LLM 确认完成后立即发布，例如：

- 火灾检测
- 烟雾检测
- 跌倒检测
- 入侵检测
- 危险行为检测

这类事件的流程应为：

```text
传统模型触发候选事件
-> 固化证据
-> 提交 LLM 确认
-> LLM 确认成立
-> 立即发布事件
```

这类事件不应等待 `ObjectExpiredEvent`。

### 3.4 当前对象过期流程可能丢失迟到的 LLM 结果

在 `Algorithm.Ship.LabelsByLLM` 中，最终事件当前是在 `ObjectExpiredEvent` 到来时生成的。如果对象先过期，而 LLM 结果之后才返回，则结果可能被写入缓存，但之后不会再有新的对象过期事件来消费它。

这是异步 LLM 确认和对象生命周期事件之间的竞态问题。

### 3.5 可以克隆完整 `Frame`，但不建议作为主要设计

建立一个待处理队列，并把原始 `Frame` 克隆进去，确实可以避免原始帧过期后上下文丢失。但不建议直接、无差别地保存完整 `Frame` 实例。

原因：

- 完整帧 `Mat` 内存占用较高。
- 多路视频叠加秒级 LLM 延迟时，容易积压大量帧。
- 当前 `Frame.Clone()` 只克隆 `Scene`，不会深拷贝检测对象、属性、标注和对象快照。
- LLM 通常真正需要的是不可变证据：编码后的整帧 JPEG、对象裁剪 JPEG、bbox、标签、置信度、时间戳和候选事件元数据。

推荐的设计是克隆证据，而不是必须克隆完整领域帧对象。

## 4. 核心架构结论

系统应拆分为三条逻辑链路：

```text
实时链路 Fast Lane       : YOLO / tracker / 传统规则 / 候选事件生成
证据链路 Evidence Lane   : 为 LLM 固化不可变历史证据
协调链路 Reconcile Lane  : 将 LLM 结果合并回候选事件或对象状态
```

实时流水线负责产生候选。LLM 负责分析不可变证据。协调器负责处理数秒后返回的结果，并决定如何影响业务状态和事件发布。

不要把系统设计成“LLM 返回后继续处理旧的 Frame”。应设计为：

```text
LLM 返回结果
-> 通过 RequestId 或 CandidateEventId 找到 CandidateEvent / ObjectVerificationState
-> 校验结果是否过期，以及业务生命周期状态是否仍可处理
-> 确认、拒绝、超时或完成最终化
-> 如业务要求，发布事件
```

## 5. 推荐组件

### 5.1 CandidateEventStore

用于保存等待 LLM 确认或等待超时的业务候选事件。

职责：

- 创建候选事件记录。
- 跟踪候选事件生命周期状态。
- 支持按 `CandidateEventId` 查询。
- 支持超时扫描。
- 保存事件发布所需的业务上下文。

建议状态：

```csharp
public enum CandidateEventStatus
{
    PendingLLM,
    Confirmed,
    Rejected,
    TimedOut,
    Published,
    Cancelled
}
```

建议模型：

```csharp
public sealed class CandidateEventState
{
    public string CandidateEventId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public long FrameId { get; init; }
    public long OffsetMilliSec { get; init; }
    public DateTime UtcTimeStamp { get; init; }
    public string AlgorithmName { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string? ObjectId { get; init; }
    public CandidateEventStatus Status { get; set; }
    public string? PendingRequestId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime DeadlineUtc { get; init; }
    public object? TraditionalPayload { get; init; }
    public object? LLMResultPayload { get; set; }
}
```

### 5.2 PendingEvidenceStore

用于保存 LLM 推理和后续事件发布所需的不可变证据。

它替代“原始待处理克隆 `Frame` 队列”的概念。

建议模型：

```csharp
public sealed record PendingLLMEvidence(
    string RequestId,
    string CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    LLMAnalysisScope Scope,
    byte[] FrameJpeg,
    byte[]? ObjectCropJpeg,
    IReadOnlyList<DetectedObjectEvidence> Objects,
    string Prompt,
    DateTime ExpireAtUtc);

public sealed record DetectedObjectEvidence(
    string ObjectId,
    string LocalId,
    string Label,
    int LabelId,
    int TrackingId,
    float Confidence,
    int X,
    int Y,
    int Width,
    int Height);
```

优点：

- 不依赖活跃 `Frame` 生命周期。
- 原始帧被 `VideoFrameSlideWindow` 过期释放后仍然安全。
- 如果图片用 JPEG 编码，内存压力更可控。
- 更容易持久化和重放。
- 更容易在日志和测试中检查。

### 5.3 LLMAnalysisRequest

LLM 子系统的统一输入模型。

```csharp
public enum LLMAnalysisScope
{
    Frame,
    Object
}

public enum LLMQueuePolicy
{
    LatestPerSource,
    LatestBestPerObject,
    EventAnchored,
    DropOldest
}

public sealed record LLMAnalysisRequest(
    string RequestId,
    string? CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    string? ObjectId,
    string? ObjectLocalId,
    string? TrackKey,
    LLMAnalysisScope Scope,
    LLMQueuePolicy QueuePolicy,
    string Prompt,
    byte[] ImageJpeg,
    float? DetectorConfidence,
    double? EvidenceQualityScore,
    DateTime CreatedAtUtc,
    DateTime ExpireAtUtc);
```

### 5.4 LLMAnalysisResult

LLM 子系统的统一输出模型。

```csharp
public sealed record LLMAnalysisResult(
    string RequestId,
    string? CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    string? ObjectId,
    LLMAnalysisScope Scope,
    string ModelName,
    TimeSpan InferenceTime,
    string JsonResult,
    bool IsSuccess,
    bool IsExpiredResult,
    string? ErrorCode,
    DateTime RequestedAtUtc,
    DateTime CompletedAtUtc);
```

可以选择扩展现有 `LLMInferenceResultEvent`，也可以引入更完整的新结果事件，同时保持对旧逻辑的兼容。

### 5.5 LLMRequestScheduler

根据业务优先级和替换策略调度 LLM 请求。

队列策略：

| 策略 | 使用场景 | 行为 |
| --- | --- | --- |
| `LatestPerSource` | 场景概览、周期性整帧分析 | 每个视频源只保留最新请求 |
| `LatestBestPerObject` | 对象属性确认 | 每个对象只保留最佳证据请求 |
| `EventAnchored` | 火灾、烟雾、跌倒、入侵等事件确认 | 不被无关的新帧替换，必须完成或超时 |
| `DropOldest` | 低优先级诊断任务 | 队列满时丢弃旧任务 |

对 `EventAnchored` 请求，不应使用当前“每个 SourceId 只保留最新帧”的策略。因为 LLM 必须确认触发候选事件的那份精确证据，而不是几秒后的最新画面。

### 5.6 LLMWorkerPool

负责执行视觉 LLM 调用。

推荐 .NET 实现方式：

- 使用 `Channel<LLMAnalysisRequest>` 替代 `BlockingCollection`，便于异步和有界队列管理。
- 使用 `BackgroundService` 或统一的后台 Worker 抽象。
- 使用 `SemaphoreSlim` 控制模型并发。
- 支持取消和请求超时。
- 发布 `LLMAnalysisResult` 或增强后的 `LLMInferenceResultEvent`。

推荐限制：

- `MaxConcurrentFrameRequests`：1 或较低值。
- `MaxConcurrentObjectRequests`：根据 GPU/API 能力设置为 2-4。
- `PerSourceRateLimit`：避免单个视频源淹没队列。
- `PerObjectOnlyOnePending`：避免同一对象重复提交确认。

### 5.7 LLMResultReconciler

消费 LLM 结果，并决定最终业务动作。

职责：

- 按 `RequestId` 和 `CandidateEventId` 匹配结果。
- 检查结果是否已经过期。
- 检查候选事件或对象的生命周期状态。
- 解析 LLM JSON。
- 确认或拒绝候选事件。
- 对需要即时响应的事件立即发布。
- 对对象生命周期型事件执行最终化。
- 释放证据资源。

该组件应统一承载以下规则：

```text
LLM 当前确认成立 -> 如果事件类型要求即时发布，则立即发布。
对象已过期但仍在等待 LLM -> 结果到达或等待超时时再发布或降级处理。
LLM 结果到达太晚 -> 忽略，或作为迟到诊断结果持久化。
```

## 6. 事件类型与发布时间

### 6.1 即时确认型事件

示例：

- 火灾
- 烟雾
- 跌倒
- 入侵
- PPE 违规
- 危险行为

流程：

```text
传统算法检测到候选
-> 创建 CandidateEventState(PendingLLM)
-> 固化证据
-> 提交 EventAnchored LLM 请求
-> LLM 返回 true
-> 立即发布事件
-> 标记为 Published
```

超时策略应可配置：

```text
OnTimeout = PublishTraditional | Drop | PublishUnknown | Retry
```

### 6.2 对象生命周期总结型事件

示例：

- 船舶标签总结
- 对象最佳快照标签
- 对象属性聚合

流程：

```text
对象出现
-> 更新 ObjectVerificationState
-> 当证据质量提升时提交对象级 LLM
-> 缓存最新可用 LLM 结果
-> 对象过期
-> 如果已有可用结果：发布最终事件
-> 如果仍有 LLM 请求未完成：等待到 deadline
-> 如果结果在 deadline 前返回：发布最终事件
-> 如果超时：发布 Unknown / 使用传统结果降级 / 丢弃
```

对象过期依然有价值，但只适合生命周期总结型事件，不应承担所有事件的发布职责。

## 7. 对象确认状态机

推荐对象状态：

```text
Tracking
-> PendingLLM
-> Verified
-> ExpiredWaitingLLM
-> Finalized
```

状态含义：

- `Tracking`：对象仍在滑动窗口中存活。
- `PendingLLM`：至少有一个 LLM 请求正在处理中。
- `Verified`：已有可用的 LLM 结果。
- `ExpiredWaitingLLM`：对象已经过期，但仍有 LLM 请求未返回。
- `Finalized`：最终事件已发布、已拒绝或已超时处理。

建议模型：

```csharp
public sealed class ObjectVerificationState
{
    public string ObjectId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public long BestFrameId { get; set; }
    public float BestDetectorConfidence { get; set; }
    public double BestQualityScore { get; set; }
    public string? PendingRequestId { get; set; }
    public string? LatestResultRequestId { get; set; }
    public object? VerifiedPayload { get; set; }
    public bool IsExpired { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    public DateTime? FinalizeDeadlineUtc { get; set; }
}
```

## 8. 对象级 LLM 的证据选择

不要把同一对象的每一帧都提交给 LLM。

推荐触发条件：

- 目标对象首次出现。
- 当前证据质量显著优于之前的待处理证据。
- 检测置信度超过旧值一定阈值，例如 `+0.08`。
- 对象已经存在超过配置时长，但仍没有 LLM 结果。
- 对象即将过期或已经过期，且没有有效结果。

推荐质量评分：

```text
QualityScore =
  detectorConfidence * 0.45
+ bboxAreaRatio * 0.25
+ sharpnessScore * 0.15
+ centerScore * 0.10
- occlusionPenalty * 0.05
```

这比只看检测置信度更合理。对 LLM 来说，最佳证据往往是更清晰、更大、更居中的裁剪图，而不一定是 YOLO 置信度最高的那一帧。

## 9. 帧过期与待处理证据

`VideoFrameSlideWindow` 基于滑动窗口使帧过期，并发布：

- `ObjectExpiredEvent`
- `FrameExpiredEvent`

当需要 LLM 异步确认时，不应依赖原始帧仍然存在于滑动窗口中。

推荐做法：

1. 在候选事件创建时，将整帧或对象裁剪编码为 JPEG。
2. 存入 `PendingEvidenceStore`。
3. 设置 TTL 和容量上限。
4. 允许原始帧按原机制正常过期。
5. 使用证据字节进行 LLM 推理和事件发布。

如果后续代码确实必须依赖类似 `Frame` 的结构，建议引入明确的证据快照类型，而不是依赖当前 `Frame.Clone()`。

可选类型：

```csharp
public sealed class FrameEvidence
{
    public string SourceId { get; init; } = string.Empty;
    public long FrameId { get; init; }
    public long OffsetMilliSec { get; init; }
    public DateTime UtcTimeStamp { get; init; }
    public byte[] FrameJpeg { get; init; } = [];
    public IReadOnlyList<DetectedObjectEvidence> Objects { get; init; } = [];
    public string? AnnotationJson { get; init; }
}
```

## 10. 对现有模块的推荐改造

### 10.1 Algorithm.General.LLM

当前文件：

- `src/6.Algorithm/Algorithm.General.LLM/Executor.cs`

推荐演进：

1. 暂时保留当前基于属性标记的集成方式，保证兼容。
2. 将被标记的 `Frame` / `DetectedObject` 转换为 `LLMAnalysisRequest`。
3. 立即编码并固化证据。
4. 将不可变请求加入队列。
5. 发布增强后的结果事件。
6. 避免长期持有完整 `Frame` 对象。

当前的替换策略可以保留，但应重命名并显式化：

- `_latestFrameInferenceTasks` -> `LatestPerSource` 调度策略。
- `_latestObjectInferenceTasks` -> `LatestBestPerObject` 调度策略。

需要为即时确认型事件增加独立的 `EventAnchored` 调度策略。

### 10.2 Algorithm.Ship.LabelsByLLM

当前文件：

- `src/6.Algorithm/Algorithm.Ship.LabelsByLLM/Executor.cs`

推荐演进：

1. 用 `ObjectVerificationState` 替代 `_cachedShipLabels`。
2. 检测到对象时，更新最佳证据，并只在证据质量提升时提交 LLM。
3. LLM 结果返回时，更新 `VerifiedPayload`。
4. 收到 `ObjectExpiredEvent` 时：
   - 如果已有可用结果，立即发布最终事件。
   - 如果仍有待完成请求，标记为 `ExpiredWaitingLLM`。
   - 设置最终化 deadline。
5. 收到迟到 LLM 结果时：
   - 如果状态为 `ExpiredWaitingLLM` 且未超过 deadline，则发布最终事件。
   - 如果已经 `Finalized`，则忽略或持久化为迟到诊断结果。
6. 超时时执行配置化降级策略。

这可以修复“对象先过期、LLM 后返回”导致结果无人消费的竞态。

### 10.3 Algorithm.General.ObjectOccurrenceByLLM

当前文件：

- `src/6.Algorithm/Algorithm.General.ObjectOccurrenceByLLM/Executor.cs`

推荐演进：

1. 当传统出现条件达到 `MinDurationSec` 时，创建 `CandidateEventId`。
2. 固化触发事件的精确帧证据。
3. 使用 `QueuePolicy.EventAnchored` 提交 `LLMAnalysisRequest`。
4. 不允许后续更新的视频源最新帧替换该请求。
5. LLM 确认后立即发布事件。
6. 持久化被拒绝和超时的候选事件，便于观测和调试。

## 11. 推荐实施路线

### 阶段 1：消息与证据模型

- 新增 `LLMAnalysisRequest`。
- 新增 `LLMAnalysisResult`，或增强 `LLMInferenceResultEvent`。
- 新增 `PendingLLMEvidence`。
- 新增 `DetectedObjectEvidence`。
- 新增从 `Frame` 构建证据的辅助方法。

### 阶段 2：调度器显式化

- 从 `Algorithm.General.LLM` 中抽取请求调度逻辑。
- 实现：
  - `LatestPerSource`
  - `LatestBestPerObject`
  - `EventAnchored`
- 增加有界容量和 TTL。

### 阶段 3：结果协调

- 新增 `CandidateEventStore`。
- 新增 `LLMResultReconciler`。
- 支持 LLM 确认后立即发布事件。
- 支持超时策略。

### 阶段 4：修复船舶标签生命周期

- 引入 `ObjectVerificationState`。
- 处理 `ExpiredWaitingLLM`。
- 在结果返回或超时后最终化对象标签事件。

### 阶段 5：对象出现、火灾等即时事件流程

- 将对象出现确认改造成基于 `CandidateEventId` 的流程。
- 使用 `EventAnchored` 队列策略。
- LLM 确认后立即发布。

### 阶段 6：可观测性

增加指标和日志：

- 按策略统计队列长度。
- 被丢弃的请求数量。
- 被替换的请求数量。
- LLM 延迟分位数。
- 待确认候选事件数量。
- 过期证据数量。
- 超时数量。
- 迟到结果数量。
- LLM 拒绝或判 false 的确认数量。

## 12. 运行保护与配置建议

推荐配置：

```text
LLM.MaxConcurrentFrameRequests = 1
LLM.MaxConcurrentObjectRequests = 2
LLM.RequestTimeoutSeconds = 10
LLM.CandidateEventTimeoutSeconds = 12
LLM.ObjectExpiredWaitSeconds = 8
LLM.MaxPendingEvidencePerSource = 30
LLM.MaxPendingEvidenceTotalBytes = configurable
LLM.FrameJpegQuality = 80
LLM.ObjectCropPaddingRatio = 0.10
```

背压行为必须显式：

- 即时安全事件不应被静默丢弃。
- 场景概览类任务可以只保留最新请求。
- 对象标签类任务可以只保留最佳证据。
- 诊断类任务可以在压力下丢弃旧任务。

## 13. 最终设计原则

核心原则是：

```text
实时算法产生候选。
LLM 分析不可变的历史证据。
协调器将迟到结果合并回候选事件或对象生命周期状态。
事件按照业务时效要求发布，而不是按照 LLM 返回时机或帧生命周期被动发布。
```

该设计可以保持视频流水线实时性，保留 LLM 确认所需的正确历史上下文，同时支持整帧分析和对象级分析，并避免在帧或对象过期后丢失 LLM 推理结果。
