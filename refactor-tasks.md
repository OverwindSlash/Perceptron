# 视觉 LLM 异步确认重构任务追踪

本文档基于 `design-cn.md` 的最新方案，用于拆解实施任务、记录进度和验收标准。

## 状态说明

- `[ ]` 未开始
- `[~]` 进行中
- `[x]` 已完成
- `[!]` 阻塞或需要重新设计

## 总体目标

将现有基于 `Frame` / `DetectedObject` 属性标记的 LLM 异步推理链路，演进为：

```text
实时链路产生候选
-> 证据链路固化不可变证据
-> LLM 请求调度器按策略执行
-> 结果协调器按请求身份合并结果
-> 业务 handler 幂等发布或最终化事件
```

关键约束：

- `Analyze(Frame frame)` 不等待 LLM。
- LLM 请求不长期持有完整 `Frame`。
- 即时确认型事件使用 `CandidateEventId + EventAnchored`。
- 对象生命周期型事件允许对象过期后等待 LLM 到 deadline。
- 所有发布路径必须幂等，避免重复发布。

## 阶段 1：消息、身份与证据模型

目标：建立统一请求、结果和证据快照模型，同时保持旧属性标记入口兼容。

### 1.1 新增公共模型

- [ ] 新增 `LLMAnalysisScope`。
- [ ] 新增 `LLMQueuePolicy`。
- [ ] 新增 `LLMAnalysisRequest`。
- [ ] 新增 `LLMAnalysisResult`，或增强现有 `LLMInferenceResultEvent`。
- [ ] 新增 `PendingLLMEvidence`。
- [ ] 新增 `DetectedObjectEvidence`。
- [ ] 新增 `FrameEvidence`，如实现中确实需要帧级证据快照。

建议位置：

- `src/6.Algorithm/Algorithm.Common/`
- 或 `src/3.Domain/Perceptron.Domain/` 中更通用的 `Abstraction/LLM`、`Entity/LLM` 命名空间。

验收标准：

- [ ] 请求模型包含 `RequestId`、`RequesterAlgorithmName`、`CandidateEventId`、`SourceId`、`FrameId`、`OffsetMilliSec`、`UtcTimeStamp`、`ObjectId`、`TrackKey`、`Scope`、`QueuePolicy`、`CreatedAtUtc`、`ExpireAtUtc`。
- [ ] 结果模型包含对应身份字段、`ModelName`、`InferenceTime`、`JsonResult`、`IsSuccess`、`IsExpiredResult`、`ErrorCode`。
- [ ] 旧 `LLMInferenceResultEvent` 的订阅方可以继续工作，或有明确迁移适配层。

### 1.2 证据构建辅助方法

- [ ] 新增从 `Frame` 构建整帧 JPEG 证据的方法。
- [ ] 新增从 `DetectedObject` 构建对象裁剪 JPEG 证据的方法。
- [ ] 新增从 `DetectedObject` 构建 `DetectedObjectEvidence` 的方法。
- [ ] 支持 JPEG 质量参数。
- [ ] 支持对象裁剪 padding 参数。
- [ ] 对 `Mat` 已释放、对象无 snapshot、bbox 越界等情况返回明确失败结果。

验收标准：

- [ ] 不依赖 `Frame.Clone()` 复制检测对象和属性。
- [ ] 证据构建完成后，LLM 请求不需要访问原始 `Frame.Scene`。
- [ ] 单元测试覆盖整帧证据、对象裁剪证据、无 snapshot、越界 padding。

### 1.3 旧属性入口适配

- [ ] 在 `Algorithm.General.LLM` 中保留 `LLMAnalysis`、`LLMAnalysisType`、`LLMAnalysisPrompt` 入口。
- [ ] 将被标记的 `Frame` / `DetectedObject` 转换成 `LLMAnalysisRequest`。
- [ ] 无 `CandidateEventId` 的旧对象级请求使用 `RequestId + ObjectId` 路由。
- [ ] 新即时确认型事件必须提供 `CandidateEventId`。

验收标准：

- [ ] 现有船舶标签 LLM 配置可以继续触发对象级请求。
- [ ] 现有火灾烟雾 LLM 配置可以继续触发帧级请求，后续阶段再切换到 `EventAnchored`。

## 阶段 2：调度器显式化

目标：从 `Algorithm.General.LLM` 中抽取请求调度逻辑，并显式支持不同队列策略。

### 2.1 抽取调度器

- [ ] 新增 `LLMRequestScheduler`。
- [ ] 使用 `Channel<LLMAnalysisRequest>` 或等价有界队列替代当前分散的 `BlockingCollection<string>`。
- [ ] 将当前 `_latestFrameInferenceTasks` 映射为 `LatestPerSource`。
- [ ] 将当前 `_latestObjectInferenceTasks` 映射为 `LatestBestPerObject`。
- [ ] 新增 `EventAnchored`。
- [ ] 新增 `DropOldest`，如当前阶段需要低优先级诊断任务。

验收标准：

- [ ] `LatestPerSource` 同一 SourceId 只保留最新请求。
- [ ] `LatestBestPerObject` 同一 ObjectId 只保留质量更好的请求。
- [ ] `EventAnchored` 不会被同 SourceId 或同 ObjectId 的新请求替换。
- [ ] 队列满时行为可配置且有日志。

### 2.2 Worker 执行模型

- [ ] 新增 `LLMWorkerPool` 或重构现有 worker。
- [ ] 支持请求级 timeout。
- [ ] 支持 shutdown cancellation。
- [ ] 使用 `SemaphoreSlim` 控制并发。
- [ ] 区分帧级和对象级并发限制，或至少预留配置字段。

验收标准：

- [ ] `Analyze(Frame frame)` 只提交请求，不同步调用 LLM。
- [ ] worker 不长期持有完整 `Frame`。
- [ ] 推理完成后发布统一结果事件。
- [ ] dispose 时不会遗留后台线程或未释放 `Mat`。

## 阶段 3：即时确认型事件闭环

目标：优先改造 `Algorithm.General.ObjectOccurrenceByLLM`，验证 `CandidateEventId + EventAnchored + 立即协调发布` 的完整链路。

### 3.1 候选事件创建

- [ ] 在传统出现条件达到 `MinDurationSec` 时创建 `CandidateEventId`。
- [ ] 创建 `CandidateEventState`，状态为 `PendingLLM`。
- [ ] 保存 `SourceId`、`FrameId`、`OffsetMilliSec`、`UtcTimeStamp`、`AlgorithmName`、`EventName`、候选对象列表、持续时间等业务上下文。
- [ ] 固化触发帧证据。
- [ ] 提交 `QueuePolicy.EventAnchored` 请求。

验收标准：

- [ ] 同一候选事件的请求身份稳定。
- [ ] 后续新帧不会替换该候选事件证据。
- [ ] 本地事件间隔策略不会误伤已创建但未确认的候选事件。

### 3.2 LLM 结果确认与发布

- [ ] 解析 `OccurredObjectsLLMResult`。
- [ ] LLM 返回 true 时发布业务事件。
- [ ] LLM 返回 false 时标记候选事件 `Rejected`。
- [ ] 超时时按配置执行 `PublishTraditional`、`Drop`、`PublishUnknown` 或 `Retry`。
- [ ] 迟到结果只记录诊断，不默认发布。

验收标准：

- [ ] 确认成立后立即发布，不等待对象过期。
- [ ] 同一 `CandidateEventId` 不会重复发布。
- [ ] 拒绝、超时、迟到都有日志或可观测记录。

## 阶段 4：结果协调基础设施

目标：沉淀公共候选事件存储、状态机和业务 handler 抽象。

### 4.1 CandidateEventStore

- [ ] 新增 `CandidateEventStatus`。
- [ ] 新增 `CandidateEventState`。
- [ ] 支持按 `CandidateEventId` 创建、查询、更新、删除或最终化。
- [ ] 支持 timeout 扫描。
- [ ] 支持容量上限和过期清理。

验收标准：

- [ ] 状态转换线程安全。
- [ ] `Published`、`Finalized`、`Cancelled` 等终态不能再次发布。
- [ ] 单元测试覆盖确认、拒绝、超时、重复结果、迟到结果。

### 4.2 LLMResultReconciler

- [ ] 新增公共协调器。
- [ ] 按 `RequestId`、`CandidateEventId`、`RequesterAlgorithmName` 路由结果。
- [ ] 检查 `ExpireAtUtc` 和候选事件状态。
- [ ] 调用业务 `ILLMResultHandler`。
- [ ] 释放 `PendingEvidenceStore` 资源。

验收标准：

- [ ] 协调器不直接解析所有业务 JSON。
- [ ] 业务 handler 可以独立注册。
- [ ] 重复结果、迟到结果和未知请求均有明确处理。

### 4.3 业务 handler 抽象

- [ ] 新增 `ILLMResultHandler` 或等价接口。
- [ ] 为 `ObjectOccurrenceByLLM` 实现 handler。
- [ ] 为船舶标签预留对象生命周期 handler。

验收标准：

- [ ] 新增业务算法不需要修改公共协调器核心逻辑。
- [ ] handler 可以访问候选事件状态和证据元数据，但不直接管理公共 store 的内部结构。

## 阶段 5：修复船舶标签生命周期

目标：解决对象先过期、LLM 后返回导致标签结果无人消费的问题。

### 5.1 ObjectVerificationState

- [ ] 新增 `ObjectVerificationState`。
- [ ] 使用状态替代 `_cachedShipLabels` 的单一缓存语义。
- [ ] 记录 `BestFrameId`、`BestDetectorConfidence`、`BestQualityScore`、`PendingRequestId`、`LatestResultRequestId`、`VerifiedPayload`、`IsExpired`、`FinalizeDeadlineUtc`。

验收标准：

- [ ] 同一对象只保留最佳证据或最新有效结果。
- [ ] 对象仍存活时可以持续更新最佳证据。
- [ ] 对象过期时不会立即丢弃等待中的 LLM 请求。

### 5.2 对象过期等待与最终化

- [ ] 收到 `ObjectExpiredEvent` 时，如果已有结果，立即发布最终标签事件。
- [ ] 如果仍有 pending 请求，进入 `ExpiredWaitingLLM`。
- [ ] 设置最终化 deadline。
- [ ] deadline 前 LLM 返回则发布最终标签事件。
- [ ] deadline 后执行 `Unknown`、传统降级或丢弃策略。
- [ ] 已 `Finalized` 的对象忽略后续迟到结果。

验收标准：

- [ ] 修复对象先过期、LLM 后返回的竞态。
- [ ] 船舶标签事件不会重复发布。
- [ ] 旧 snapshot 被及时释放。

## 阶段 6：证据成本与背压保护

目标：控制 LLM 证据链路对实时流水线的 CPU、内存和队列压力。

### 6.1 PendingEvidenceStore

- [ ] 新增 `PendingEvidenceStore`。
- [ ] 支持按 `RequestId` 保存、查询、删除证据。
- [ ] 支持 TTL。
- [ ] 支持按 SourceId 的 pending 数量上限。
- [ ] 支持总字节数上限。
- [ ] 超限时按策略拒绝、替换或降级。

验收标准：

- [ ] 证据过期后自动释放字节数组和 `Mat` 资源。
- [ ] 即时安全事件不会被静默丢弃。
- [ ] 场景概览类任务可被最新请求替换。

### 6.2 配置项

- [ ] 新增 `LLM.MaxConcurrentFrameRequests`。
- [ ] 新增 `LLM.MaxConcurrentObjectRequests`。
- [ ] 新增 `LLM.RequestTimeoutSeconds`。
- [ ] 新增 `LLM.CandidateEventTimeoutSeconds`。
- [ ] 新增 `LLM.ObjectExpiredWaitSeconds`。
- [ ] 新增 `LLM.MaxPendingEvidencePerSource`。
- [ ] 新增 `LLM.MaxPendingEvidenceTotalBytes`。
- [ ] 新增 `LLM.FrameJpegQuality`。
- [ ] 新增 `LLM.ObjectCropJpegQuality`。
- [ ] 新增 `LLM.ObjectCropPaddingRatio`。

验收标准：

- [ ] 配置缺省值与 `design-cn.md` 一致。
- [ ] 旧配置文件在未新增这些配置时仍能启动。

## 阶段 7：可观测性

目标：让异步 LLM 链路的积压、替换、丢弃、超时和迟到都可见。

### 7.1 日志

- [ ] 记录请求创建。
- [ ] 记录证据固化结果和字节大小。
- [ ] 记录队列策略和替换/丢弃行为。
- [ ] 记录 LLM 请求耗时和结果状态。
- [ ] 记录候选事件确认、拒绝、超时、发布、迟到。
- [ ] 记录对象过期等待和最终化。

### 7.2 指标

- [ ] 按策略统计队列长度。
- [ ] 统计被替换请求数量。
- [ ] 统计被丢弃请求数量。
- [ ] 统计 LLM 延迟分位数。
- [ ] 统计待确认候选事件数量。
- [ ] 统计 pending 证据数量和总字节数。
- [ ] 统计超时数量。
- [ ] 统计迟到结果数量。
- [ ] 统计 LLM 拒绝数量。

验收标准：

- [ ] 在不接入外部监控系统时，日志也足够定位常见问题。
- [ ] 指标命名包含 SourceId、QueuePolicy、RequesterAlgorithmName 等关键维度。

## 推荐测试清单

- [ ] `LLMAnalysisRequest` 字段完整性测试。
- [ ] 证据构建测试：整帧、对象裁剪、padding、无 snapshot、已释放对象。
- [ ] `LatestPerSource` 替换行为测试。
- [ ] `LatestBestPerObject` 质量选择测试。
- [ ] `EventAnchored` 不替换测试。
- [ ] worker timeout 和 cancellation 测试。
- [ ] `CandidateEventStore` 幂等状态转换测试。
- [ ] `ObjectOccurrenceByLLM` 确认后立即发布测试。
- [ ] `ObjectOccurrenceByLLM` 拒绝、超时、迟到测试。
- [ ] `Ship.LabelsByLLM` 对象先过期、LLM 后返回测试。
- [ ] `Ship.LabelsByLLM` 已最终化后迟到结果测试。
- [ ] 资源释放测试：`Mat`、snapshot、pending evidence。

## 迁移注意事项

- 优先保持现有配置文件可运行，不在第一阶段强制所有算法迁移到新模型。
- 旧属性标记方式可以作为兼容层保留一段时间，但新增即时确认型事件必须使用 `CandidateEventId`。
- 不要在公共协调器中直接写死火灾、烟雾、船舶标签等业务 JSON 结构。
- 不要把 `ObjectExpiredEvent` 作为所有 LLM 事件的统一发布点。
- 不要把完整 `Frame` 克隆队列作为长期方案。
