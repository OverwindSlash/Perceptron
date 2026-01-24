# IVideoFrameBuffer 需求规格说明书

## 1. 概述
`IVideoFrameBuffer` 作为视频帧的缓冲区，用于解耦帧生产者（如视频加载器）与消费者（如处理器）。它继承自 `IConcurrentBoundedQueue<Frame>`。

## 2. 工作模式
为了处理缓冲区为空的情况，缓冲区必须支持两种检索模式。

### 2.1. 阻塞等待模式 (Blocking Wait Mode) - 默认
- **行为**：当调用 `RetrieveFrame()` 且缓冲区为空时，方法将**阻塞**并高效等待（使用 `Monitor` 或 `Semaphore` 等同步原语，避免忙等待），直到有新帧可用。
- **适用场景**：处理流程应在数据可用前暂停的场景，以最小化 CPU 占用。

### 2.2. 空白帧模式 (Blank Frame Mode)
- **行为**：当调用 `RetrieveFrame()` 且缓冲区为空时，方法将**立即返回**一个预定义的“空白帧”（或默认帧）。
- **空白帧特性**：
  - 必须是一个有效的 `Frame` 对象。
  - 应具有特定的标志表明它是空白/默认帧（例如 `Frame.IsBlankFrame`）。
  - 为避免性能开销，应复用同一个空白帧实例（或高效管理的实例），避免频繁的内存分配/释放。
- **适用场景**：实时显示或处理流水线，即使源端滞后，也必须维持恒定帧率。

## 3. 接口变更

### 3.1. Perceptron.Domain.Entity.VideoStream.Frame
添加一个标志以指示帧是否为生成的空白帧。

```csharp
public class Frame : PropertiesBag, IDisposable
{
    // ... 现有成员 ...

    /// <summary>
    /// 获取或设置一个值，指示此帧是否为在 BlankFrameMode 模式下缓冲区为空时返回的生成空白/默认帧。
    /// </summary>
    public bool IsBlankFrame { get; set; }
}
```

### 3.2. Perceptron.Domain.Abstraction.FrameBuffer.IVideoFrameBuffer

引入 `FrameBufferMode` 枚举和一个属性来控制行为。**`Mode` 属性为只读，应在实现类的构造函数中初始化，以防止运行时被误修改。**

```csharp
public enum FrameBufferMode
{
    /// <summary>
    /// 当缓冲区为空时阻塞并等待帧。
    /// </summary>
    BlockingWait,

    /// <summary>
    /// 当缓冲区为空时立即返回默认/空白帧。
    /// </summary>
    ReturnBlankFrame
}

public interface IVideoFrameBuffer : IConcurrentBoundedQueue<Frame>
{
    // ... 现有成员 ...

    /// <summary>
    /// 获取缓冲区的当前工作模式。
    /// 该值在初始化时确定，运行时不可变。
    /// </summary>
    FrameBufferMode Mode { get; }
}
```

## 4. 实现计划

### 阶段 1：领域定义（当前步骤）
- [x] 创建/更新 `IVideoFrameBuffer-Spec.md`（已本地化为中文）。
- [x] 更新 `Frame.cs` 以包含 `IsBlankFrame` 属性。
- [x] 创建 `FrameBufferMode` 枚举。
- [x] 更新 `IVideoFrameBuffer` 接口以包含只读的 `Mode` 属性。

### 阶段 2：实现（后续步骤）
- [ ] 创建/更新 `VideoFrameBuffer` 类实现 `IVideoFrameBuffer`。
    - [ ] 构造函数应接受 `FrameBufferMode` 参数。
- [ ] 实现 `BlockingWait` 逻辑，使用 `Monitor.Wait` 或 `SemaphoreSlim`。
- [ ] 实现 `ReturnBlankFrame` 逻辑：
    - [ ] 管理空白帧的单例或缓存 `Frame` 实例（尺寸/格式与视频流匹配）。
    - [ ] 确保当计数为 0 时 `RetrieveFrame` 返回此实例。
- [ ] 确保线程安全。

### 阶段 3：验证
- [ ] `BlockingWait` 的单元测试：确保线程阻塞并在帧推入时唤醒。
- [ ] `ReturnBlankFrame` 的单元测试：确保为空时立即返回空白帧，且 `IsBlankFrame` 设置为 true。
- [ ] 性能测试：验证 `BlockingWait` 不会导致高 CPU 占用（无忙等待）。
