# 线程安全高性能泛型有界队列（覆盖最旧）— 优化版规格说明（Spec v1.1）

> 本规格用于实现与测试的唯一依据。除非明确声明为“可选扩展”，否则均为**强制语义（MUST）**。

---

## 1. 范围与目标

### 1.1 目标

* 提供一个**线程安全**、支持**多生产者/多消费者**的泛型有界队列。
* 容量满时采用**覆盖最旧元素（Overwrite Oldest）**策略：新元素总能入队，最旧元素被丢弃。
* 被丢弃（覆盖/清空）的元素将触发用户回调，并由队列负责调用 `Dispose()` 释放资源。
* 提供可观测指标（计数/峰值/吞吐）与**快照枚举**能力，适用于缓存、日志缓冲、实时数据流等允许丢弃旧数据的场景。

### 1.2 非目标

* 不保证严格公平性（公平唤醒、等待队列等）。
* 不提供阻塞式 `Dequeue`（如需要阻塞/等待语义，应在上层实现）。
* 不保证指标读取的强一致性（只保证单字段单调/线程安全，跨字段可能存在瞬时不一致）。

---

## 2. 类型与所有权模型

### 2.1 类型约束

* `T` 必须为 **引用类型** 且实现 `IDisposable`：`where T : class, IDisposable`
* 队列**不接受** `null` 入队。

### 2.2 资源所有权（非常关键）

* **入队成功后**，元素在仍处于队列内部期间，其生命周期由队列管理。
* 元素被以下原因移出队列内部时，资源归属如下：

  * **TryDequeue 成功出队**：元素所有权转移给调用者，队列**不得**再调用该元素的 `Dispose()`。
  * **被覆盖（满队列写入）或被 Clear/Dispose 清理**：元素仍归队列所有，队列必须触发丢弃回调并调用 `Dispose()`。

---

## 3. 公共接口

### 3.1 接口

```csharp
public interface IConcurrentBoundedQueue<T> : IEnumerable<T>, IDisposable
    where T : class, IDisposable
{
    int Count { get; }
    int Capacity { get; }
    bool IsFull { get; }
    double Utilization { get; }

    long TotalEnqueued { get; }
    long TotalDequeued { get; }
    long TotalOverwritten { get; }
    long TotalDropped { get; }
    int MaxUtilization { get; }

    double EnqueueThroughput { get; }
    double DequeueThroughput { get; }

    event Action<T>? OnItemDropped;

    void Enqueue(T item);
    void EnqueueRange(IEnumerable<T> items);
    bool TryDequeue(out T? item);
    bool TryPeek(out T? item);
    void Clear();
}
```

### 3.2 实现类构造函数

```csharp
public sealed class ConcurrentBoundedQueue<T> : IConcurrentBoundedQueue<T>
    where T : class, IDisposable
{
    public ConcurrentBoundedQueue(int capacity, Action<T>? onItemDropped = null);
}
```

* `onItemDropped` 非空则等价于 `OnItemDropped += onItemDropped`。

---

## 4. 通用规则与异常

### 4.1 参数校验

* `capacity <= 0`：抛 `ArgumentOutOfRangeException(nameof(capacity))`
* `Enqueue(null)`：抛 `ArgumentNullException(nameof(item))`

### 4.2 释放与可用性

* `Dispose()` 后对象进入不可用状态：

  * `Enqueue/TryDequeue/Clear/GetEnumerator` **MUST** 抛 `ObjectDisposedException`。
  * **允许** 读取纯数值指标属性（如 `Count`, `Total*`, `Throughput` 等），以便于事后诊断（Post-mortem analysis）。
* `Dispose()` **必须幂等**（重复调用无副作用且不抛异常）。

### 4.3 回调异常处理

* `OnItemDropped` 的任一订阅者抛出的异常：

  * **必须被捕获并吞掉**，不得影响队列结构与可用性。
  * 建议逐个订阅者隔离捕获（实现细节，但必须保证“异常不外泄”）。

---

## 5. 并发与性能约束（实现必须满足）

### 5.1 并发策略

* 使用**单一互斥锁（single lock）**保护环形缓冲区的结构性变更：`head/tail/count/buffer[]`。
* 允许多线程同时调用 `Enqueue/TryDequeue/Clear/GetEnumerator/Dispose`。

### 5.2 禁止在锁内执行用户代码（优化点）

为避免死锁、长时间持锁与重入风险：

* **不得在持有内部锁时调用**：

  * `OnItemDropped(...)`
  * `item.Dispose()`
* 正确做法：在锁内仅“摘出需要丢弃的元素引用列表”，释放锁后再依次回调+释放资源。

> 语义要求：`Enqueue/Clear/Dispose` 在返回前，必须完成本次触发的丢弃回调与 `Dispose()` 调用（即同步完成），但执行过程应在锁外完成。

---

## 6. 数据结构与不变量

### 6.1 环形缓冲区

* 内部使用 `T[] buffer`，并维护：

  * `head`：当前最旧元素索引
  * `tail`：下一次写入位置索引
  * `count`：当前元素数量（0..Capacity）

### 6.2 不变量（MUST）

* `0 <= count <= Capacity`
* `head, tail` 始终在 `[0, Capacity)` 内
* 逻辑顺序始终为：`head` → … → 最新元素（按入队时间递增）

---

## 7. 方法语义

### 7.1 Enqueue(T item)

**前置条件**：未 Dispose，`item != null`

**行为**：

* **非满**（`count < Capacity`）：

  * 将 `item` 写入 `buffer[tail]`
  * `tail = (tail + 1) % Capacity`
  * `count++`
  * `TotalEnqueued++`
  * 更新 `MaxUtilization = max(MaxUtilization, count)`
* **已满**（`count == Capacity`）：

  * 定位最旧元素 `old = buffer[head]`
  * 在锁内完成结构性覆盖：

    * `buffer[head] = item`
    * `head = (head + 1) % Capacity`
    * `tail = head`（或等价维护方式；要求写入后逻辑正确）
    * `count` 保持为 `Capacity`
    * `TotalEnqueued++`
    * `TotalOverwritten++`
  * 在锁外按顺序执行：

    * 触发 `OnItemDropped(old)`
    * 调用 `old.Dispose()`

**顺序要求**（MUST）：

* 对单次覆盖，必须先回调再 Dispose：`OnItemDropped(old)` → `old.Dispose()`。

---

### 7.2 EnqueueRange(IEnumerable<T> items)

**前置条件**：未 Dispose

**行为**：

* 遍历 `items` 集合。
* 对于集合中的每个非 `null` 元素，调用 `Enqueue(item)`。
* 遇到 `null` 元素时跳过（不抛异常，也不入队）。
* 遇到空集合或 `null` 集合，直接返回，不执行任何操作。

---

### 7.3 TryDequeue(out T? item)

**前置条件**：未 Dispose

* **空队列**（`count == 0`）：

  * 返回 `false`
  * `item = null`
  * 不修改任何计数器
* **非空**：

  * 在锁内摘出最旧元素 `item = buffer[head]` 并移除：

    * `buffer[head] = null!`（实现建议，用于避免悬挂引用）
    * `head = (head + 1) % Capacity`
    * `count--`
    * `TotalDequeued++`
  * 返回 `true`

**资源语义**：

* 成功出队的 `item` **不得**由队列 Dispose；由调用者自行管理其释放时机。

---

### 7.4 TryPeek(out T? item)

**前置条件**：未 Dispose

* **空队列**（`count == 0`）：

  * 返回 `false`
  * `item = null`
* **非空**：

  * 在锁内读取最旧元素 `item = buffer[head]`
  * **不移除**元素，不修改 `head` 或 `count`
  * 返回 `true`

---

### 7.5 Clear()

**前置条件**：未 Dispose

* 在锁内摘出当前所有元素（按“最旧→最新”的逻辑顺序），并将队列置空：

  * `count = 0`，`head = 0`，`tail = 0`（或等价复位）
  * 清空 `buffer` 中对应引用（避免内存泄漏）
* 计数器：

  * `TotalDropped += 本次清理元素数量`
* 在锁外依次对每个被清理元素执行：

  * `OnItemDropped(x)` → `x.Dispose()`

> Clear 必须保证丢弃顺序为“最旧→最新”。

---

### 7.4 GetEnumerator()（快照枚举）

**前置条件**：未 Dispose

* `GetEnumerator()` 返回一个**快照枚举器**：

  * 在锁内复制当前队列逻辑序列（最旧→最新）到一个独立数组/列表中
  * 枚举该快照，不受后续入队/出队影响

**一致性说明**：

* 快照仅保证“引用集合与顺序”稳定；如果其他线程随后触发覆盖/Clear/Dispose，快照中的对象可能已被 Dispose（这在监控场景下可接受）。如需强一致“可用对象快照”，应由调用者在外部进行更高层同步。

---

### 7.7 Dispose()

**前置条件**：无（可随时调用）

* 必须幂等。
* 首次 Dispose 时：

  * 调用 `Clear()`（因此会触发 `OnItemDropped` 并 Dispose 队列内残留元素）
  * 停止内部计时（Stopwatch 不再前进的语义即可；实现可仅记录终值）
  * 标记 disposed，使后续访问抛 `ObjectDisposedException`

---

## 8. 监控指标与计时

### 8.1 计时

* 构造时启动 `Stopwatch`（或等价高精度计时）。
* `ElapsedSeconds` 若极小或为 0，吞吐计算必须避免除零：

  * 使用 `max(ElapsedSeconds, 1e-6)` 或等价阈值。

### 8.2 指标定义

* `Count`：当前队列内元素数量（0..Capacity）
* `Capacity`：固定容量
* `IsFull`：`Count == Capacity`
* `Utilization`：`Count / (double)Capacity`
* `TotalEnqueued`：累计入队次数（包括覆盖情况下的入队）
* `TotalDequeued`：累计成功出队次数
* `TotalOverwritten`：累计覆盖最旧元素次数（满队列时发生）。**注意：此类丢弃不计入 `TotalDropped`**。
* `TotalDropped`：累计因 `Clear()` 或 `Dispose()` 主动清理而丢弃的元素数量（**不包含** `TotalOverwritten`）。
* `MaxUtilization`：历史最大 `Count` 值
* `EnqueueThroughput`：`TotalEnqueued / ElapsedSeconds`
* `DequeueThroughput`：`TotalDequeued / ElapsedSeconds`

### 8.3 线程安全与一致性要求

* 所有指标读取必须线程安全（不抛异常/不破坏内部状态）。
* 指标允许并发下的“近似快照”（跨字段不要求同一时刻一致），但以下必须成立：

  * `TotalEnqueued/TotalDequeued/TotalOverwritten/TotalDropped` 单调不减
  * `0 <= Count <= Capacity` 始终成立

---

## 9. 事件 OnItemDropped 语义

* 触发场景（必须覆盖）：

  1. 满队列入队导致覆盖最旧元素
  2. `Clear()`
  3. `Dispose()`（通过 Clear）
* 触发顺序：

  * 对单个元素：先 `OnItemDropped(item)` 再 `item.Dispose()`
  * 对批量清理：按“最旧→最新”的逻辑顺序逐个触发
* 回调执行线程：

  * 由触发操作的调用线程同步执行（不得偷偷切线程或异步化，除非在“可选扩展”明确说明）。
* 回调异常处理：

  * 必须吞掉，且不得影响其他元素的回调与 Dispose。

---

## 10. 测试与验收标准（NUnit + Moq）

实现必须通过以下测试类别（不少于这些）：

### 10.1 构造与参数

* capacity 非法抛 `ArgumentOutOfRangeException`
* `onItemDropped` 订阅生效

### 10.2 基本入队/出队

* 入队后 Count 增加，TryDequeue 顺序正确（FIFO）
* 空队列 TryDequeue 返回 false 且 item 为 null

### 10.3 覆盖语义

* 满队列入队：

  * `TotalOverwritten++`、`TotalEnqueued++` 正确
  * 被覆盖元素触发回调且被 Dispose
  * 队列最终包含最新 `Capacity` 个元素，顺序正确

### 10.4 Clear/Dispose 释放语义

* Clear：

  * 对队列内每个元素按顺序触发回调并 Dispose
  * `TotalDropped` 增加正确，Count 归零
* Dispose：

  * 幂等（重复调用不抛）
  * Dispose 后任何公共方法/属性访问抛 `ObjectDisposedException`

### 10.5 回调异常隔离

* 回调抛异常：

  * 不影响队列结构
  * 不影响后续元素的回调与 Dispose
  * 不影响后续 Enqueue/TryDequeue 可用性

### 10.6 快照枚举

* 枚举顺序“最旧→最新”
* 枚举内容为调用瞬间快照，后续入队/出队不改变枚举序列
* 并发下不抛异常、不死锁

### 10.7 并发压力

* 多生产者多消费者高并发：

  * 不出现数据损坏（Count 越界、重复引用、丢失结构不变量等）
  * 无死锁
  * 计数器单调性成立（尤其 Total*）

---

## 11. 可选扩展（不改变既有语义时可添加）

> 以下仅作为后续优化方向，不属于 v1.1 强制要求。

* `TryPeek(out T? item)`：读取最旧但不移除
* `Drain(int max, ICollection<T> output)`：批量出队以降低锁竞争
* `GetSnapshot()`：返回快照数组（避免 IEnumerator 分配/虚调用）
* 更细粒度指标（如 DropRate、平均占用等）
* `OnItemDropped` 增加 `DropReason` 参数（Overwritten, Cleared, Disposed）以区分丢弃原因

---

