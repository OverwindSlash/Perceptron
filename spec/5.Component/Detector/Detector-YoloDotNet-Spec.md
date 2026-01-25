# YoloDetector 功能规格说明

## 1. 概述
`YoloDetector` 类位于 `Detector.YoloDotNet` 命名空间下，是基于 `YoloDotNet` 库实现的目标检测组件。该组件继承自 `ComponentBase` 并实现了 `IObjectDetector` 接口，旨在为系统提供高性能、可配置的 YOLO 系列模型推理能力。

## 2. 核心功能

### 2.1 模型推理环境
- **模型支持**: 支持加载 ONNX 格式的 YOLO 模型（默认为 `yolo11m.onnx`）。
- **执行后端**: 支持通过配置选择执行提供者（Execution Provider）：
  - `cpu`: 使用 CPU 进行推理。
  - `cuda`: 使用 NVIDIA CUDA 进行 GPU 加速推理（支持指定 Device ID）。
- **初始化与预热**: 组件初始化时会自动创建预测器并进行一次推理预热（Warmup），以确保后续检测的响应速度。

### 2.2 多种检测模式
组件提供三种主要的检测接口以适应不同场景：
1. **单帧检测 (`Detect`)**: 
   - 对输入的单帧图像进行推理。
   - 支持检测步长（Stride）配置，允许跳帧检测以节省资源。
   - 支持区域检测（ROI），仅对图像的特定区域进行推理。
2. **批量检测 (`DetectBatch`)**: 
   - 接受帧列表作为输入，进行批量推理。
   - 提高多帧处理时的吞吐量。
3. **分块检测 (`DetectByTile`)**: 
   - 针对高分辨率图像设计。
   - 将图像按行列分割成多个子图（Tile）分别检测。
   - 内置结果合并逻辑（`YoloPredictionMerger`），处理跨分块的边界框合并，支持配置重叠率和拼接缝隙阈值。

### 2.3 结果过滤与后处理
组件内置了丰富的后处理逻辑，可根据配置对原始检测结果进行清洗：
- **类别过滤**: 支持配置 `TargetTypes` 白名单，仅返回感兴趣的目标类别。
- **尺寸过滤**:
  - **小目标过滤**: 可配置最小宽/高阈值，剔除过小的误检或无关目标。
  - **大目标过滤**: 可配置最大宽/高阈值，剔除过大的异常目标。
- **类型映射**: 支持将源类别名称映射为目标类别名称（例如统一不同模型的标签命名）。
- **内部包含抑制**: 支持检测并过滤掉被同类别大框完全包含的小框（基于 IoU 重叠率），适用于消除重复检测或特定业务需求。

## 3. 配置参数详解
组件通过 `LoadPreferences` 方法加载配置字典，主要配置项如下：

| 配置类别 | 参数项 | 描述 |
| :--- | :--- | :--- |
| **基础配置** | `ModelPath` | ONNX 模型文件路径 |
| | `ExecutionProvider` | 推理后端 (`cpu` / `cuda`) |
| | `DeviceId` | GPU 设备 ID |
| | `DetectionStride` | 检测间隔帧数 (默认 1) |
| **检测范围** | `TargetTypes` | 逗号分隔的目标类别列表 |
| | `RegionDetectionEnabled` | 是否启用 ROI 区域检测 |
| | `DetectionRegion` | ROI 区域坐标 (X, Y, Width, Height) |
| **尺寸过滤** | `FilterSmallObject` | 是否启用小目标过滤 |
| | `MinBboxWidth/Height` | 小目标过滤阈值 |
| | `FilterLargeObject` | 是否启用大目标过滤 |
| | `MaxBboxWidth/Height` | 大目标过滤阈值 |
| **分块检测** | `TileDetectionEnabled` | 是否启用分块检测 |
| | `TileDetectionSize` | 分块行列数 (Rows, Cols) |
| | `MaxStitchGapPixel` | 分块合并最大像素缝隙 |
| | `MinVerticalOverlapRatio` | 分块合并最小垂直重叠率 |
| **高级处理** | `WillSuppressInnerSameObject` | 是否抑制内部同类对象 |
| | `InnerObjectOverlapRatio` | 内部包含判定 IoU 阈值 |
| | `WillMapObjectTypes` | 是否启用类型映射 |
| | `SourceObjectTypeNames` | 映射源类别名称 |
| | `DestinationObjectTypeName` | 映射目标类别名称 |

## 4. 代码结构
- **继承**: `ComponentBase`, `IObjectDetector`
- **依赖库**: 
  - `YoloDotNet` (核心推理)
  - `OpenCvSharp` (图像数据处理)
  - `SkiaSharp` (图像转换与预处理)
  - `Serilog` (日志记录)
