# SnapshotManager 组件规格说明书

## 1. 概述
`SnapshotManager` 是 `ISnapshotManager` 接口的内存实现。它负责管理 Perceptron 系统中的视频帧（场景）和对象快照。其主要职责包括缓存场景、提取和管理对象快照、根据标准选择最佳快照、生成围绕特定事件的视频片段，以及处理帧和对象的生命周期事件。

## 2. 类设计

### 2.1 接口实现
`SnapshotManager` 类实现了：
- `ISnapshotManager`: 快照管理的核心接口。
- `FrameAndObjectExpiredSubscriber`: 用于处理 `ObjectExpiredEvent` 和 `FrameExpiredEvent` 的基类。

### 2.2 依赖项
- **OpenCvSharp**: 用于图像 (`Mat`) 操作和视频写入 (`VideoWriter`)。
- **MessagePipe**: 用于事件发布 (`IPublisher<ObjectBestSnapshotCreatedEvent>`)。
- **Serilog**: 用于日志记录。
- **Perceptron.Domain**: 提供领域实体 (`Frame`, `BoundingBox`, `DetectedObject`) 和设置。

## 3. 配置
该组件通过 `Dictionary<string, string>` (preferences) 进行配置，使用 `SnapshotSettings` 辅助类进行解析。

| 设置项 | 默认值 | 描述 |
| :--- | :--- | :--- |
| `SnapshotDir` | "Snapshots" | 保存快照的目录。 |
| `SaveBestSnapshot` | `false` | 对象过期时是否将最佳快照保存到磁盘。 |
| `BestSnapshotBy` | `Confidence` | 确定“最佳”快照的标准（Confidence/置信度, Area/面积, Width/宽度, Height/高度）。 |
| `SnapshotExpansionRatio` | `1.2f` | 拍摄快照时扩展边界框的比例。 |
| `MaxObjectSnapshots` | `10` | 每个对象在内存中保留的最大快照数量。 |
| `MinSnapshotWidth` | `40` | 快照被视为有效/可保存的最小宽度。 |
| `MinSnapshotHeight` | `40` | 快照被视为有效/可保存的最小高度。 |
| `VideoClipDurationSeconds` | `10` | 生成视频片段的默认时长（秒）。 |
| `VideoFrameRate` | `25.0` | 生成视频片段的默认帧率。 |

## 4. 核心功能

### 4.1 帧处理 (`ProcessSnapshots`)
当处理新的 `Frame` 时：
1.  **场景缓存**：帧的完整场景 (`Mat`) 以 `FrameId` 为键缓存在 `_scenesOfFrame` 中。
2.  **快照提取**：遍历帧中的 `DetectedObject`。
    - 跳过未标记为分析中 (`IsUnderAnalysis`) 的对象。
    - 使用对象的边界框（按 `SnapshotExpansionRatio` 缩放）提取子图像（快照）。
    - 将快照附加到 `DetectedObject` 实体。
    - 将快照添加到内部缓存 (`_snapshotsByScore`)。

### 4.2 快照管理
- **存储**：快照存储在 `_snapshotsByScore` (`ConcurrentDictionary<string, SortedList<float, Mat>>`) 中，以对象 ID 为键。内部的 `SortedList` 根据评分（由 `BestSnapshotBy` 决定）对快照进行排序。
- **容量控制**：如果某个对象的快照数量超过 `MaxObjectSnapshots`，得分最低的快照将被释放并移除。
- **最佳快照事件**：如果新添加的快照是该对象的最高分快照，则发布 `ObjectBestSnapshotCreatedEvent` 事件。

### 4.3 视频片段生成 (`GenerateVideoClipAroundFrameAsync`)
生成围绕特定 `centerFrameId` 的视频片段。
- **逻辑**：
    1.  根据 `durationSeconds` 和 `frameRate` 计算所需的帧范围。
    2.  检查 `centerFrameId` 和周围的帧是否存在于 `_scenesOfFrame` 中。
    3.  **等待机制**：如果可用帧数不足（例如未来的帧尚未到达），它会等待最多 **3秒**，每 100ms 检查一次。
    4.  **写入**：使用 `VideoWriter` (FFMPEG, H.264) 将范围内的可用帧写入指定的文件路径。
- **错误处理**：如果帧丢失或等待超时，抛出 `InvalidOperationException`。

### 4.4 事件处理

#### `ObjectExpiredEvent`
当对象跟踪生命周期结束时触发。
- **动作**：释放该对象 ID 的所有缓存快照。
- **持久化**：如果 `SaveBestSnapshot` 为 true，在释放前将最佳快照（最高分）保存到磁盘 (`SnapshotDir/Best`)。

#### `FrameExpiredEvent`
当管道不再需要某帧时触发。
- **动作**：从 `_scenesOfFrame` 中移除并释放与 `FrameId` 关联的场景 `Mat`。

## 5. 线程安全与资源管理
- **并发**：使用 `ConcurrentDictionary` 存储 `_scenesOfFrame` 和 `_snapshotsByScore`，以支持处理线程和事件处理程序的并发访问。
- **资源清理**：当 OpenCvSharp `Mat` 对象从缓存中移除或对象/帧过期时，显式调用 `Dispose()`，防止内存泄漏。

## 6. 代码引用
- 接口: [ISnapshotManager.cs](c:\workspace\formal\Perceptron\src\3.Domain\Perceptron.Domain\Abstraction\SnapshotManager\ISnapshotManager.cs)
- 实现: [SnapshotManager.cs](c:\workspace\formal\Perceptron\src\5.Component\SnapshotManager.InMemory\SnapshotManager.cs)
