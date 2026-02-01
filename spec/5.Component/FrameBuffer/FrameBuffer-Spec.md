# VideoFrameBuffer 功能规范

## 1. 概述
`VideoFrameBuffer` 是一个用于视频流处理的高性能帧缓冲区组件。它实现了 `IVideoFrameBuffer` 接口，基于并发有界队列（`ConcurrentBoundedQueue`）构建，旨在解决视频生产者（如相机采集）与消费者（如算法处理、UI显示）之间的速度不匹配问题。

该组件不仅提供基本的队列功能，还封装了视频流特定的处理逻辑，如空白帧生成、阻塞策略和自动资源释放。

## 2. 核心特性

### 2.1 缓冲区管理
- **有界队列**: 维护固定容量的缓冲区（`BufferSize`），防止内存无限增长。
- **自动丢帧**: 当缓冲区满时，新入队的帧会挤占最旧的帧（FIFO），并自动触发被丢弃帧的资源释放（`Dispose`），防止非托管资源（OpenCvSharp `Mat`）泄漏。
- **线程安全**: 内部使用锁机制（`Monitor` / `lock`）保证多线程环境下的入队和出队安全。

### 2.2 工作模式 (FrameBufferMode)
组件支持两种工作模式，可通过配置指定：

1.  **BlockingWait (默认模式)**
    - 消费者在调用 `RetrieveFrame()` 时，如果缓冲区为空，线程将被阻塞，直到有新帧入队。
    - 适用于对实时性要求严格，不允许错过数据的场景，或者消费者处理速度快于生产者的场景。

2.  **ReturnBlankFrame**
    - 消费者在调用 `RetrieveFrame()` 时，如果缓冲区为空，将立即返回一个预生成的“空白帧”（Blank Frame）。
    - **空白帧机制**:
        - **懒加载**: 空白帧模板在第一帧推入时根据该帧的尺寸和类型进行初始化。
        - **内容**: 优先尝试加载 `Assets/no-video.jpg` 图片；如果加载失败，则生成全黑图像。
        - **引用计数**: 返回的空白帧增加了引用计数，调用者需负责 `Dispose`。
    - 适用于 UI 显示场景，确保在无视频信号时画面不会卡死或黑屏，而是显示特定提示图像。

### 2.3 资源生命周期管理
- **帧所有权转移**: `RetrieveFrame` 取出的帧，所有权转移给调用者，调用者负责 `Dispose`。
- **丢帧清理**: 因队列满而被丢弃的帧，`VideoFrameBuffer` 会自动调用其 `Dispose` 方法。
- **组件销毁**: `VideoFrameBuffer` 被 `Dispose` 时，会清空队列并释放其中所有帧的资源，同时销毁缓存的空白帧。

### 2.4 事件通知
- **丢帧回调**: 提供 `RegisterFrameDropHandler` 和 `OnItemDropped` 事件。当发生丢帧时，外部注册的处理程序会被调用（例如用于记录日志或统计丢帧率）。

## 3. 接口定义

主要接口 `IVideoFrameBuffer` 定义如下：

- **属性**:
    - `BufferName`: 缓冲区名称。
    - `BufferSize`: 缓冲区容量。
    - `Mode`: 当前工作模式。
- **方法**:
    - `PushFrame(Frame frame)`: 推入新帧。
    - `RetrieveFrame()`: 获取帧（根据模式可能阻塞或返回空白帧）。
    - `RegisterFrameDropHandler(Action<Frame> handler)`: 注册丢帧回调。

## 4. 配置参数

构造函数接受 `Dictionary<string, string>` 类型的配置参数：

| 参数名 | 说明 | 默认值 |
| :--- | :--- | :--- |
| `BufferName` | 缓冲区名称 | (解析逻辑决定) |
| `BufferSize` | 缓冲区最大容量 | 300 (参考测试用例与默认设置) |
| `Mode` | 工作模式 (`BlockingWait` / `ReturnBlankFrame`) | `BlockingWait` |

## 5. 实现细节与约束

- **空白帧缓存**: 为了性能，`no-video.jpg` 会在静态字段中缓存，避免重复读取磁盘。
- **引用计数**: `Frame` 对象使用引用计数管理。`Enqueue` 时会 `Retain`，`Dequeue` 时不释放（移交所有权），丢弃时 `Dispose`（减少引用）。
- **异常处理**: 组件 `Dispose` 后，调用 `Push` 或 `Retrieve` 会抛出 `ObjectDisposedException`。

## 6. 代码参考
- 接口定义: [IVideoFrameBuffer.cs](../../src/3.Domain/Perceptron.Domain/Abstraction/FrameBuffer/IVideoFrameBuffer.cs)
- 实现类: [VideoFrameBuffer.cs](../../src/5.Component/FrameBuffer.TwoModes/VideoFrameBuffer.cs)
- 单元测试: [VideoFrameBufferTests.cs](../../test/5.Component/FrameBuffer.Tests/VideoFrameBufferTests.cs)
