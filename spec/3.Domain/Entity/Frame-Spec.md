# 视频流处理核心实体功能规范 (Functional Spec)

本文档基于 `Insight.Domain` 域中的 `VideoSpecs` 和 `Frame` 实体代码生成，描述了视频流处理中的核心数据结构及其功能。

## 1. VideoSpecs (视频规格)

### 1.1 概述
`VideoSpecs` 类用于定义视频源的基本技术参数。它是一个不可变（Immutable-like）的数据结构，主要用于在系统传递视频流的元数据信息。

### 1.2 数据模型
| 属性名 | 类型 | 描述 | 访问权限 |
| :--- | :--- | :--- | :--- |
| **Width** | `int` | 视频帧宽度 (像素) | 只读 |
| **Height** | `int` | 视频帧高度 (像素) | 只读 |
| **Fps** | `double` | 帧率 (Frames Per Second) | 私有设置 (仅构造时赋值) |
| **FrameCount** | `int` | 视频总帧数 | 私有设置 (仅构造时赋值) |

### 1.3 功能特性
- **初始化约束**: 所有属性必须通过构造函数初始化，确保了对象创建后的数据完整性。
- **只读性**: 对外暴露的属性均为只读，防止外部逻辑意外修改视频规格参数。

---

## 2. Frame (视频帧)

### 2.1 概述
`Frame` 类是视频流处理中的核心单元，代表视频中的单帧图像及其相关上下文信息。它继承自 `PropertiesBag` 并实现了 `IDisposable` 接口，负责管理非托管资源（如图像内存）。

### 2.2 数据模型

#### 2.2.1 基础元数据
| 属性名 | 类型 | 描述 | 约束 |
| :--- | :--- | :--- | :--- |
| **SourceId** | `string` | 视频源标识符 | 非空，非空白字符 |
| **FrameId** | `long` | 帧序号 | >= 0 |
| **OffsetMilliSec** | `long` | 时间偏移量 (毫秒) | >= 0 |
| **UtcTimeStamp** | `DateTimeOffset` | 帧创建的 UTC 时间戳 | 构造时自动生成 |

#### 2.2.2 图像与分析数据
| 属性名 | 类型 | 描述 | 生命周期管理 |
| :--- | :--- | :--- | :--- |
| **Scene** | `Mat` (OpenCvSharp) | 原始图像数据 | 由 Frame 负责 Dispose |
| **DetectedObjects** | `IReadOnlyList<DetectedObject>` | 目标检测结果列表 | 包含的对象需被 Dispose |
| **Annotation** | `VisualAnnotation` | 可视化标注信息 | - |

### 2.3 核心功能

#### 2.3.1 构造与验证
- 构造函数会对关键参数进行严格校验：
  - `SourceId` 不能为空。
  - `FrameId` 和 `OffsetMilliSec` 必须非负。
  - `Scene` 对象不能为空且不能为 Empty。
- 初始化时会自动创建空的 `DetectedObjects` 列表和默认的 `VisualAnnotation`。

#### 2.3.2 资源管理 (IDisposable)
- **Dispose 模式**: 实现了标准的 Dispose 模式。
- **资源清理**:
  - 释放 `Scene` (OpenCV Mat 对象)。
  - 遍历并释放 `DetectedObjects` 列表中的所有检测对象。
- **安全检查**: 提供 `ThrowIfDisposed()` 方法，防止在对象销毁后被访问。

#### 2.3.3 克隆 (Cloning)
- 提供 `Clone()` 方法用于创建当前帧的深拷贝（Deep Copy）。
- 复制内容包括：基础元数据 (`SourceId`, `FrameId`, `OffsetMilliSec`) 和图像数据 (`Scene.Clone()`)。
- **注意**: 调用 Clone 前会检查对象是否已销毁。

#### 2.3.4 高级内存管理
- **引用计数 (Reference Counting)**:
  - 提供 `Retain()` 方法增加引用计数，用于多处共享 Frame 实例的场景（如缓冲区）。
  - `Dispose()` 方法会减少引用计数。只有当引用计数降为 0 时，才会真正释放底层资源。
  - 引用计数操作是线程安全的。
- **对象池回收 (Recycling)**:
  - 构造函数支持传入 `Action<Mat>? recycler` 委托。
  - 当资源被真正释放时（引用计数为 0），如果存在 recycler，系统会将 `Scene` (Mat) 传递给 recycler 进行回收复用，而不是直接销毁。这有助于降低高频创建/销毁图像对象的内存压力。
