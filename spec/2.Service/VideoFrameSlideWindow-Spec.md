# VideoFrameSlideWindow 功能说明书

## 1. 概述
`VideoFrameSlideWindow` 类位于 `Perceptron.Service.Pipeline` 命名空间下，主要负责维护视频流处理过程中的一个滑动窗口（Slide Window）。它缓存了一定数量的视频帧（`Frame`），并跟踪这些帧中检测到的对象（`DetectedObject`）。当帧移出窗口或对象不再存在于窗口内的任何帧时，该类负责发布相应的过期事件。

## 2. 类结构与接口
*   **类名**: `VideoFrameSlideWindow`
*   **实现的接口**:
    *   `IEventPublisher<ObjectExpiredEvent>`: 用于发布对象过期事件。
    *   `IEventPublisher<FrameExpiredEvent>`: 用于发布帧过期事件。
    *   `IDisposable`: 用于资源释放。

## 3. 核心数据结构
*   **_frames**: `ConcurrentBoundedQueue<Frame>`
    *   用于存储视频帧的有界并发队列。
    *   当队列满时，自动触发清理回调。
*   **_objectInWhichFrames**: `Dictionary<string, HashSet<Frame>>`
    *   记录每个对象 ID (`string`) 出现的所有帧集合 (`HashSet<Frame>`)。
    *   用于快速查询某个对象是否存在于当前窗口以及存在于哪些帧中。
*   **_objectAliveInSlideWindow**: `ConcurrentDictionary<string, bool>`
    *   记录当前滑动窗口内所有“存活”的对象 ID 集合。

## 4. 主要功能逻辑

### 4.1 初始化
*   构造函数接受 `windowSize`（默认 100），初始化 `_frames` 队列，并绑定 `CleanupExpiredFrame` 作为元素过期/溢出时的回调函数。

### 4.2 添加新帧 (AddNewFrame)
1.  将新帧 (`Frame`) 入队 (`_frames.Enqueue`)。
2.  遍历新帧中检测到的所有对象：
    *   更新 `_objectInWhichFrames`，将新帧加入到对应对象 ID 的帧集合中。
    *   更新 `_objectAliveInSlideWindow`，标记该对象 ID 为存活状态。

### 4.3 清理过期帧 (CleanupExpiredFrame)
此方法在 `_frames` 队列触发清理机制时（如队列满挤出旧帧）被调用。
1.  **清理对象引用**: 从 `_objectInWhichFrames` 中移除该帧中包含的所有对象记录。
2.  **检测对象过期**:
    *   对于该帧中的每个对象，检查其在剩余窗口帧中的存在计数 (`GetExistenceCountByObjId`)。
    *   如果计数为 0（即该对象在当前窗口的其他帧中均未出现），则判定该对象过期。
    *   **发布事件**: 触发 `ObjectExpiredEvent`。
    *   **移除状态**: 从 `_objectInWhichFrames` 和 `_objectAliveInSlideWindow` 中彻底移除该对象。
3.  **发布帧过期事件**: 触发 `FrameExpiredEvent`。
4.  **释放资源**: 调用 `expiredFrame.Dispose()`。

### 4.4 查询功能
*   **GetFramesContainObjectId**: 根据对象 ID 返回包含该对象的所有帧列表。
*   **IsObjIdAlive**: 检查指定对象 ID 是否存在于当前滑动窗口中。

### 4.5 事件发布配置
*   提供 `SetPublisher` 方法，允许外部注入 `MessagePipe` 的 `IPublisher` 实例，用于后续的事件广播。

### 4.6 资源释放 (Dispose)
*   遍历当前队列中剩余的所有帧。
*   强制触发所有包含对象的 `ObjectExpiredEvent`。
*   强制触发所有帧的 `FrameExpiredEvent`。
