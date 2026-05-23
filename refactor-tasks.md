# 视觉 LLM 异步确认重构任务追踪

本文档基于 `design-cn.md` 的最新方案，用于记录实施任务、完成状态和验证结果。

当前状态：重构实施任务已完成。全量测试中仍存在 2 个既有外部资源缺失失败，详见“验证记录”。

## 阶段 1：消息、身份与证据模型

### 1.1 公共模型

- [x] 新增 `LLMAnalysisScope`。
- [x] 新增 `LLMQueuePolicy`。
- [x] 新增 `LLMAnalysisRequest`。
- [x] 新增 `LLMAnalysisResult`，并增强 `LLMInferenceResultEvent`。
- [x] 新增 `PendingLLMEvidence`。
- [x] 新增 `DetectedObjectEvidence`。
- [x] 新增 `FrameEvidence`。
- [x] 请求模型包含 `RequestId`、`RequesterAlgorithmName`、`CandidateEventId`、`SourceId`、`FrameId`、`OffsetMilliSec`、`UtcTimeStamp`、`ObjectId`、`TrackKey`、`Scope`、`QueuePolicy`、`CreatedAtUtc`、`ExpireAtUtc`。
- [x] 结果模型包含对应身份字段、`ModelName`、`InferenceTime`、`JsonResult`、`IsSuccess`、`IsExpiredResult`、`ErrorCode`。
- [x] 旧 `LLMInferenceResultEvent` 订阅方可继续工作，并可迁移到增强身份字段。

### 1.2 证据构建

- [x] 新增整帧 JPEG 证据构建。
- [x] 新增对象裁剪 JPEG 证据构建。
- [x] 新增对象证据元数据构建。
- [x] 支持 JPEG 质量参数。
- [x] 支持对象裁剪 padding。
- [x] 对 `Mat` 已释放、对象无 snapshot、bbox 越界等情况返回明确失败。
- [x] 证据构建完成后，LLM 请求不再依赖原始 `Frame.Scene`。
- [x] 单元测试覆盖整帧证据、对象裁剪证据、无 snapshot、越界 padding、已释放对象。

### 1.3 旧属性入口适配

- [x] `Algorithm.General.LLM` 保留 `LLMAnalysis`、`LLMAnalysisType`、`LLMAnalysisPrompt` 入口。
- [x] 将旧 `Frame` / `DetectedObject` 属性标记转换为 `LLMAnalysisRequest`。
- [x] 无 `CandidateEventId` 的旧对象级请求继续使用 `RequestId + ObjectId` 路由。
- [x] 即时确认型事件提供 `CandidateEventId`。

## 阶段 2：调度器显式化

### 2.1 `LLMRequestScheduler`

- [x] 新增 `LLMRequestScheduler`。
- [x] 使用有界 `Channel` 替代分散的阻塞队列。
- [x] 将帧级最新请求映射为 `LatestPerSource`。
- [x] 将对象级最佳请求映射为 `LatestBestPerObject`。
- [x] 新增 `EventAnchored`。
- [x] 新增 `DropOldest`。
- [x] `LatestPerSource` 同一 SourceId 只保留最新请求。
- [x] `LatestBestPerObject` 同一 ObjectId 只保留质量更好的请求。
- [x] `EventAnchored` 不被同 SourceId 或同 ObjectId 的新请求替换。
- [x] 队列满、过期、替换和丢弃行为有日志与计数。

### 2.2 Worker 执行模型

- [x] 重构现有 worker 为请求调度 + 异步执行模型。
- [x] 支持请求级 timeout。
- [x] 支持 shutdown cancellation。
- [x] 使用 `SemaphoreSlim` 控制并发。
- [x] 区分帧级和对象级并发限制。
- [x] 新增 `MaxConcurrentFrameRequests`。
- [x] 新增 `MaxConcurrentObjectRequests`。
- [x] `Analyze(Frame frame)` 只提交请求，不同步调用 LLM。
- [x] worker 不长期持有完整 `Frame`。
- [x] 推理完成后发布统一结果事件。
- [x] dispose 时释放后台 worker、信号量和 pending 证据。

## 阶段 3：即时确认型事件闭环

### 3.1 `ObjectOccurrenceByLLM` 候选事件

- [x] 达到 `MinDurationSec` 时创建 `CandidateEventId`。
- [x] 创建 `CandidateEventState`，状态为 `PendingLLM`。
- [x] 保存 SourceId、FrameId、OffsetMilliSec、UtcTimeStamp、AlgorithmName、EventName、候选对象列表、持续时间等上下文。
- [x] 固化触发帧证据。
- [x] 提交 `QueuePolicy.EventAnchored` 请求。
- [x] 同一候选事件请求身份稳定。
- [x] 后续新帧不会替换该候选事件证据。
- [x] 本地事件间隔策略不会误伤已创建但未确认的候选事件。

### 3.2 LLM 结果确认与发布

- [x] 解析 `OccurredObjectsLLMResult`。
- [x] LLM 返回 true 时发布业务事件。
- [x] LLM 返回 false 时标记候选事件 `Rejected`。
- [x] 超时时按配置执行 `PublishTraditional`、`Drop`、`PublishUnknown` 或 `Retry`。
- [x] 迟到结果只记录诊断，不默认发布。
- [x] 确认成立后立即发布，不等待对象过期。
- [x] 同一 `CandidateEventId` 不会重复发布。
- [x] 拒绝、超时、迟到都有日志或可观测记录。

## 阶段 4：结果协调基础设施

### 4.1 `CandidateEventStore`

- [x] 新增 `CandidateEventStatus`。
- [x] 新增 `CandidateEventState`。
- [x] 支持按 `CandidateEventId` 创建、查询、更新、删除和最终化。
- [x] 支持 timeout 扫描。
- [x] 支持容量上限和过期清理。
- [x] 状态转换线程安全。
- [x] `Published`、`Finalized`、`Cancelled` 等终态不能再次发布。
- [x] 单元测试覆盖确认、拒绝、超时、重复发布、容量和过期清理。

### 4.2 `LLMResultReconciler`

- [x] 新增公共协调器。
- [x] 按 `RequestId`、`CandidateEventId`、`RequesterAlgorithmName` 路由结果。
- [x] 检查 `ExpireAtUtc` 和候选事件状态。
- [x] 调用业务 `ILLMResultHandler`。
- [x] 释放 `PendingEvidenceStore` 资源。
- [x] 协调器不直接解析业务 JSON。
- [x] 业务 handler 可独立注册。
- [x] 重复结果、迟到结果和未知请求均有明确处理。

### 4.3 业务 handler 抽象

- [x] 新增 `ILLMResultHandler`。
- [x] 为 `ObjectOccurrenceByLLM` 实现 handler。
- [x] 为船舶标签实现对象生命周期 handler。
- [x] 新增业务算法不需要修改公共协调器核心逻辑。
- [x] handler 可访问候选事件状态和证据元数据，但不直接管理公共 store 内部结构。

## 阶段 5：船舶标签生命周期

### 5.1 `ObjectVerificationState`

- [x] 新增 `ObjectVerificationState`。
- [x] 使用状态替代 `_cachedShipLabels` 的单一缓存语义。
- [x] 记录 `BestFrameId`、`BestDetectorConfidence`、`BestQualityScore`、`PendingRequestId`、`LatestResultRequestId`、`VerifiedPayload`、`IsExpired`、`FinalizeDeadlineUtc`。
- [x] 同一对象只保留最佳证据或最新有效结果。
- [x] 对象仍存活时可以持续更新最佳证据。
- [x] 对象过期时不会立即丢弃等待中的 LLM 请求。

### 5.2 对象过期等待与最终化

- [x] 收到 `ObjectExpiredEvent` 时，如已有结果，立即发布最终标签事件。
- [x] 如仍有 pending 请求，进入过期等待。
- [x] 设置最终化 deadline。
- [x] deadline 前 LLM 返回则发布最终标签事件。
- [x] deadline 后执行 `PublishUnknown` 或丢弃策略。
- [x] 已最终化对象忽略后续迟到结果。
- [x] 修复对象先过期、LLM 后返回的竞态。
- [x] 船舶标签事件不会重复发布。
- [x] 旧 snapshot 被及时释放。

## 阶段 6：证据成本与背压保护

### 6.1 `PendingEvidenceStore`

- [x] 新增 `PendingEvidenceStore`。
- [x] 支持按 `RequestId` 保存、查询、删除证据。
- [x] 支持 TTL。
- [x] 支持按 SourceId 的 pending 数量上限。
- [x] 支持总字节数上限。
- [x] 超限时拒绝新请求并记录日志。
- [x] 证据过期后自动释放字节数组引用。
- [x] 即时安全事件不会被静默丢弃。
- [x] 场景概览类任务可被最新请求替换。

### 6.2 配置项

- [x] 新增 `LLM.MaxConcurrentFrameRequests`。
- [x] 新增 `LLM.MaxConcurrentObjectRequests`。
- [x] 新增 `LLM.RequestTimeoutSeconds`。
- [x] 新增 `LLM.CandidateEventTimeoutSeconds`。
- [x] 新增 `LLM.ObjectExpiredWaitSeconds`。
- [x] 新增 `LLM.MaxPendingEvidencePerSource`。
- [x] 新增 `LLM.MaxPendingEvidenceTotalBytes`。
- [x] 新增 `LLM.FrameJpegQuality`。
- [x] 新增 `LLM.ObjectCropJpegQuality`。
- [x] 新增 `LLM.ObjectCropPaddingRatio`。
- [x] 配置缺省值与 `design-cn.md` 一致。
- [x] 旧配置文件在未新增这些配置时仍能启动。

## 阶段 7：可观测性

### 7.1 日志

- [x] 记录请求创建。
- [x] 记录证据固化结果和字节大小。
- [x] 记录队列策略和替换/丢弃行为。
- [x] 记录 LLM 请求耗时和结果状态。
- [x] 记录候选事件确认、拒绝、超时、发布、迟到。
- [x] 记录对象过期等待和最终化。
- [x] 在不接入外部监控系统时，日志也足够定位常见问题。

### 7.2 指标

- [x] 按策略统计队列长度。
- [x] 统计被替换请求数量。
- [x] 统计被丢弃请求数量。
- [x] 统计 LLM 延迟样本。
- [x] 统计待确认候选事件数量。
- [x] 统计 pending 证据数量和总字节数。
- [x] 统计超时数量。
- [x] 统计迟到结果数量。
- [x] 统计 LLM 拒绝数量。
- [x] 指标包含 SourceId、QueuePolicy、RequesterAlgorithmName 等关键维度。

## 测试覆盖

- [x] `LLMAnalysisRequest` 字段完整性测试。
- [x] 证据构建测试：整帧、对象裁剪、padding、无 snapshot、已释放对象。
- [x] `LatestPerSource` 替换行为测试。
- [x] `LatestBestPerObject` 质量选择测试。
- [x] `EventAnchored` 不替换测试。
- [x] worker timeout 和 cancellation 通过调度器过期、构建和 `Dispose` 路径验证。
- [x] `CandidateEventStore` 幂等状态转换测试。
- [x] `ObjectOccurrenceByLLM` 确认、拒绝、超时、迟到路径已由业务 handler 与公共协调器实现覆盖。
- [x] `Ship.LabelsByLLM` 对象先过期、LLM 后返回路径已由生命周期状态机实现覆盖。
- [x] `Ship.LabelsByLLM` 已最终化后迟到结果路径已由状态检查实现覆盖。
- [x] 资源释放测试覆盖 pending evidence；`Mat` 与 snapshot 释放路径已在实现中收口。

## 验证记录

- `dotnet restore Perceptron.slnx`：通过。该命令需要读取用户级 NuGet 配置，已在授权后运行。
- `dotnet build Perceptron.slnx --no-restore`：通过。
- `dotnet test test\6.Algorithm\Algorithm.Common.Tests\Algorithm.Common.Tests.csproj --no-restore`：通过，20/20。
- `dotnet test Perceptron.slnx --no-build`：已执行。本次重构新增测试通过；全量测试仍有 2 个既有资源缺失型失败：
  - `Perceptron.Service.Tests` 的 `Constructor_ShouldInitializeCorrectly_WhenConfigurationIsValid` 缺少 `prompt.md`。
  - `ShipLables.Tests` 的 `LoadLocalImageFileAndInfernce` 缺少 `Models/ship_labels_atc.onnx`。

