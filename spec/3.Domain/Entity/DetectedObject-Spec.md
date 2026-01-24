# DetectedObject 实体规范（Spec v2）

> 目标：在保持现有语义的基础上，补齐**生命周期/可变性边界**、**并发一致性**、**Mat 所有权**、**序列化边界**与**验收可测性**，让实现可直接落地且长期可维护。

---

## 1. 术语与定位

* **DetectedObject**：视频分析流水线中的“检测/跟踪目标实体”，承载：

  1. 基本检测结果（类别、置信度、BBox）
  2. 跟踪关联（TrackingId）
  3. 可选的图像快照（Mat）
  4. 运行期扩展属性（Property Bag）
* **线程安全边界**：

  * `Snapshot` 及其相关方法：**强线程安全（同一把锁互斥）**
  * Property Bag：依赖 `ConcurrentDictionary`，**并发安全**
  * 其他普通字段：**原子读写**不等于**一致性**，需通过生命周期/冻结约束保证可预测

---

## 2. 设计原则

1. **资源所有权明确**：`Mat` 由谁创建、由谁释放必须可推断且可验证。
2. **可变性可控**：允许在“构建/富化阶段”修改少数字段，但一旦进入“发布/下游处理阶段”应可冻结（只读化）。
3. **序列化“数据化”**：实体对象可持有非序列化资源（Mat / 复杂 object），对外序列化必须有清晰边界与策略。
4. **错误可诊断**：异常类型 + message 具备定位价值（字段名、范围、当前状态）。

---

## 3. 生命周期与状态机

### 3.1 状态定义

* `Alive`：可正常读写（受可变性规则约束）
* `Frozen`（可选但强烈建议）：只读状态，禁止修改可变字段与快照所有权
* `Disposed`：资源已释放，不允许再附加快照或进行任何会触及资源的操作

### 3.2 状态规则（规范性）

* `Dispose()` 之后对象进入 `Disposed`，**不可逆**。
* `Freeze()`（如实现）之后对象进入 `Frozen`，只允许：

  * 读取字段
  * `CloneSnapshot()`（只读）
  * Property Bag 的只读快照（不建议继续写入）
* **禁止**在 `Disposed` 状态调用：

  * `AttachSnapshot / DetachSnapshot / CloneSnapshot / HasSnapshot`

> 若你不打算实现 `Freeze()`，仍应在规范中明确：对象一旦进入下游共享（跨线程发布），不再修改 `LabelId/Label/TrackingId`（“约定冻结”）。

---

## 4. 数据模型

### 4.1 核心字段（推荐“构造即完整”）

| 字段                | 类型            | 约束（必须）                    | 可变性建议                 |
| ----------------- | ------------- | ------------------------- | --------------------- |
| `SourceId`        | `string`      | 非 `null` 且非空白             | **init-only/只读**      |
| `FrameId`         | `long`        | `>= 0`                    | **init-only/只读**      |
| `UtcTimeStamp`    | `DateTime`    | `Kind == Utc`（必须）         | **init-only/只读**      |
| `LabelId`         | `int`         | `>= 0`                    | 构建阶段可写；冻结后不可写         |
| `Label`           | `string`      | 非 `null` 且非空白             | 构建阶段可写；冻结后不可写         |
| `Confidence`      | `float`       | `[0,1]` 且非 `NaN/Infinity` | **只读**                |
| `Bbox`            | `BoundingBox` | `Width>0 && Height>0`     | **只读**                |
| `TrackingId`      | `int`         | `>= 0`                    | 构建阶段可写；冻结后不可写         |
| `IsUnderAnalysis` | `bool`        | 无                         | **建议原子/可见性明确**（见并发章节） |

> 建议：把“检测结果的不可变部分”（SourceId/FrameId/UtcTimeStamp/Confidence/Bbox）做成只读，能显著降低并发与缓存键的复杂度。

### 4.2 坐标与几何便捷属性（只读，序列化忽略）

* `X, Y, Width, Height, CenterX, CenterY`
* 这些属性为 `Bbox` 的投影，**不允许单独存储**以避免不一致。

---

## 5. 标识与键规范（优化版）

### 5.1 三类键，解决“可变字段导致不稳定”的问题

1. **DetectionKey（检测唯一键）**：用于同一帧内去重/聚合

   * `DetectionKey = $"{SourceId}|{FrameId}|{LabelId}|{Bbox.X},{Bbox.Y},{Bbox.Width},{Bbox.Height}"`
2. **TrackKey（跟踪键）**：用于跨帧串联同一轨迹

   * `TrackKey = $"{SourceId}|{TrackingId}"`（TrackingId==0 时表示“未分配/未知”）
3. **PublicId（对外展示/日志友好）**：

   * `Id = $"{SourceId}_{LabelId}_{TrackingId}"`
   * `LocalId = $"{LabelId}_{TrackingId}"`

### 5.2 规范性约束

* **不再推荐**把 `Label` 直接拼进任何“稳定键”（Label 可变、格式不受控、可能含分隔符）。
* 如果必须对外展示 `Label`，需额外定义 `LabelNormalized`（去空白、替换分隔符等），但**键仍优先用 LabelId**。

---

## 6. 快照（Snapshot）管理（重点优化）

### 6.1 字段与锁

* `Snapshot: Mat?`（`[JsonIgnore]`）
* `private readonly object _sync = new();`
* `private bool _disposed;`
* 快照相关操作 **必须在 `_sync` 锁内**完成（包含读取 `HasSnapshot`）。

### 6.2 所有权语义（明确且可测试）

`AttachSnapshot(Mat snapshot, bool takeOwnership = true)`

* 参数要求：

  * `snapshot` 不能为 `null`（`ArgumentNullException(nameof(snapshot))`）
  * `snapshot.Empty()` 视为非法（建议抛 `ArgumentException("Snapshot is empty.", nameof(snapshot))`）
* 状态要求：

  * 若 `_disposed == true`：抛 `ObjectDisposedException(nameof(DetectedObject))`
* 所有权规则：

  * `takeOwnership == true`：实体持有传入 `Mat` 的引用，并在 `DetachSnapshot/Dispose` 时释放它。
  * `takeOwnership == false`：实体内部存储 `snapshot.Clone()`，调用方继续拥有原 `snapshot`。
* 覆盖规则（建议明确）：

  * 若已存在 `Snapshot`，再次 `AttachSnapshot` **先释放旧快照**再设置新快照（在锁内一次性完成，避免泄漏）。

### 6.3 其他方法

* `DetachSnapshot()`

  * 锁内：若 `Snapshot != null` 则 `Dispose()` 并置空
  * **幂等**：重复调用不抛异常
  * 若 `_disposed == true`：允许幂等返回（建议不抛，便于 finally 清理）
* `CloneSnapshot(): Mat?`

  * 锁内：无快照返回 `null`；有快照返回 `Snapshot.Clone()`
  * 返回值所有权属于调用方，调用方负责释放
* `HasSnapshot: bool`

  * 必须锁内读取（保证可见性一致）

---

## 7. 自定义属性系统（Property Bag）优化

### 7.1 容器

* `ConcurrentDictionary<string, object?> _properties`

### 7.2 Key 规范

* `key` 必须非 `null` 且非空白（`ArgumentException`）
* **建议**统一 key 命名：`lowerCamel` 或 `snake_case`，并在 Spec 中固定一套风格

### 7.3 操作语义（更可预测）

* `SetProperty(string key, object? value)`

  * `value` 允许为 `null`（表示“已设置但值为空”），**不等价于 Remove**
* `GetProperty<T>(string key, T? defaultValue = default)`

  * 命中且类型为 `T`：返回
  * 命中但类型不匹配：**建议返回 defaultValue**，并提供可选的严格版本：

    * `GetRequiredProperty<T>(string key)`：类型不匹配抛 `InvalidCastException`
* `GetAllProperties()`

  * 返回**只读快照**（例如 `ToArray()` 后构造新字典），避免枚举期间被修改导致语义不清
* 序列化建议：Property Bag 默认 **不对外序列化**（见第 10 节）

---

## 8. 几何与关系计算

* `CalculateOverlapArea(DetectedObject other): float`

  * `other == null` → `ArgumentNullException`
  * 委托 `Bbox.IntersectionArea(other.Bbox)`
* `CalculateIoU(DetectedObject other): float`

  * 返回范围 `[0,1]`（若无交集则 0）
* `OverlapsWith(DetectedObject other, float threshold = 0.0f): bool`

  * `threshold` 必须在 `[0,1]`，否则 `ArgumentOutOfRangeException`
  * 语义：`CalculateIoU(other) > threshold`（阈值等于时是否算重叠需明确；建议用 `>`）

---

## 9. 校验与呈现

### 9.1 `IsValid(): bool`

必须覆盖：

* `SourceId/Label` 非空白
* `FrameId/LabelId/TrackingId >= 0`
* `Confidence` 合法且非 `NaN/Infinity`
* `Bbox` 有效（宽高 > 0）
* `UtcTimeStamp.Kind == Utc`

> `IsValid()` 只做“快速一致性检查”，不抛异常；详细异常在构造与 setter 中完成。

### 9.2 `ToString(): string`

建议格式（便于日志检索与稳定解析）：

* `"{Id} src={SourceId} frame={FrameId} t={UtcTimeStamp:O} label={Label}({LabelId}) conf={Confidence:F3} bbox={Bbox} track={TrackingId}"`
* 时间必须使用 `O`（Round-trip ISO 8601），等价于你要求的 `yyyy-MM-ddTHH:mm:ss.fffffffZ`

---

## 10. 序列化策略（强烈建议“实体 ≠ DTO”）

### 10.1 默认规则

* 必须 `[JsonIgnore]`：

  * `Snapshot`
  * 便捷几何属性
  * `DetectionKey/TrackKey`（可由字段推导）
* **建议** Property Bag 默认也 `[JsonIgnore]`，原因：

  * `object?` 值类型不可控，可能含 `Mat/复杂对象/循环引用`
  * 造成外部协议不稳定、体积不可控

### 10.2 对外数据传输（推荐方案）

* 定义 `DetectedObjectDto`（或 `DetectedObjectContract`）只包含稳定字段：

  * `SourceId, FrameId, UtcTimeStamp, LabelId, Label, Confidence, Bbox, TrackingId`
  * 如确实需要属性：限定为 `Dictionary<string, JsonElement>` 或 `Dictionary<string, string>`（协议可控）

---

## 11. 并发与内存可见性（补齐约束）

1. `Snapshot` 的所有读写必须锁 `_sync`（包括 `HasSnapshot`）。
2. `IsUnderAnalysis` 若跨线程频繁读写：

   * **建议实现为** `int _isUnderAnalysis` 并用 `Volatile.Read/Write` 或 `Interlocked.Exchange`
   * 或至少在规范中声明：仅在同一线程/同一调度上下文写入，其他线程只读
3. 可变字段（`LabelId/Label/TrackingId`）在对象发布到多线程后：

   * 要么 `Freeze()` 强制禁止修改
   * 要么工程约定：发布后不再修改（并写入验收用例与代码注释）

---

## 12. 错误处理与异常（统一且可测）

### 12.1 构造/赋值校验（推荐清单）

* `SourceId` 空/空白：`ArgumentException`
* `Label` 空/空白：`ArgumentException`
* `Label == null`：`ArgumentNullException`
* `FrameId/LabelId/TrackingId < 0`：`ArgumentOutOfRangeException`
* `Confidence`：

  * `NaN/Infinity` 或越界：`ArgumentOutOfRangeException`
* `snapshot == null`：`ArgumentNullException`
* `AttachSnapshot` 在 `Disposed`：`ObjectDisposedException`

### 12.2 异常消息规范（建议）

* 必须包含：字段名、非法值、期望范围/规则、对象关键标识（可选）
* 示例：`Confidence must be within [0,1]. Actual=1.2`

---

## 13. 非功能与性能要点

* `DetectionKey/Id/LocalId` 字符串拼接可能频繁分配：

  * 建议作为计算属性（按需生成）
  * 若确实热路径且字段不可变，可在构造时缓存（前提：相关字段冻结/只读）
* `CloneSnapshot()` 是重操作：在 Spec 中明确“调用方需谨慎使用”，并建议下游优先只传 `Bbox + FrameRef` 而非传图像。

---

## 14. 验收标准（更新版）

1. **构造与校验**

   * 合法参数成功创建
   * 非法参数抛出正确异常类型（并校验 message 包含字段名）
2. **快照**

   * `takeOwnership=true`：`DetachSnapshot/Dispose` 会释放 Mat（可通过 Mat 引用计数/访问异常间接验证）
   * `takeOwnership=false`：内部存储克隆；克隆像素一致但引用不同
   * 重复 `DetachSnapshot()` 幂等不抛异常
   * `Dispose()` 后 `AttachSnapshot()` 抛 `ObjectDisposedException`
3. **并发**

   * 多线程并发 `Attach/Detach/Clone/HasSnapshot` 不崩溃、不泄漏、无竞态（压力测试）
4. **几何**

   * 交叠面积与 IoU：无交叠/部分交叠/完全重叠/包含关系覆盖
   * `OverlapsWith` 对 threshold 边界 `[0,1]` 行为明确
5. **序列化**

   * `Snapshot` 与便捷属性不进入 JSON
   * DTO 序列化稳定可回放，反序列化后可正常参与业务逻辑
6. **ToString**

   * 包含关键字段且时间为 `Utc` 的 ISO 8601 round-trip 格式（`O`）

---

## 15. 测试建议（最小必备集）

* 参数校验：空/空白/null/负值/越界/NaN/Infinity
* 快照：所有权差异、覆盖旧快照释放、Dispose 后行为
* 并发：10~100 线程随机 Attach/Detach/Clone（含 Dispose 竞态场景的保护策略）
* 属性系统：类型匹配/不匹配、null 值语义、快照一致性
* 序列化：Entity vs DTO 两条路径都测

---

