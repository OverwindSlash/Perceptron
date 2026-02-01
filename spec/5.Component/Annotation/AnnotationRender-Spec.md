# 标注渲染组件规格说明书

## 1. 概述

**标注渲染组件 (Annotation Render Component)** 负责使用 OpenCV 库在图像上绘制可视化的标注。它实现了 `IAnnotationRender` 接口，并提供了一个灵活的样式系统，支持各种几何形状和文本。

## 2. 接口定义

### `IAnnotationRender`

位于 `Perceptron.Domain.Abstraction.Annotation`。

```csharp
public interface IAnnotationRender
{
    Mat DrawAnnotations(Mat image, VisualAnnotation annotation);
}
```

- **输入**:
  - `Mat image`: 源图像 (OpenCvSharp Matrix)。
  - `VisualAnnotation annotation`: 包含形状和样式信息的数据结构。
- **输出**:
  - `Mat`: 绘制了标注的图像。

## 3. 实现细节

**类**: `AnnotationRender.OpenCV.Render`
**继承**: `ComponentBase`, `IAnnotationRender`

### 3.1 初始化与配置

- **构造函数**: 接受 `Dictionary<string, string>? preferences`。
- **默认样式**:
  - 从 JSON 文件加载默认样式。
  - 文件路径由 `AnnotationRenderSettings.ParseDefaultStyleFile` 确定。
  - 存储在 `_defaultStyles` 字典中。

### 3.2 绘制流程 (`DrawAnnotations`)

1.  **排序**: 形状按其 `ZIndex` 属性（升序）排序，以确保正确的图层顺序。
2.  **遍历**: 遍历排序列表中的每个形状。
3.  **可见性检查**: 跳过 `Style.Visible` 为 false 的形状。
4.  **分发**: 根据 `shape.Type` 调用特定的绘制方法：
    - `circle` (圆)
    - `polyline` (折线)
    - `polygon` (多边形)
    - `rect` (矩形)
    - `text` (文本)
5.  **错误处理**: 捕获绘制过程中的异常并记录警告（不会导致应用程序崩溃）。

### 3.3 样式系统

该组件使用分层样式系统：
1.  **用户样式**: 在形状对象中定义。
2.  **默认样式**: 在特定形状类型的加载配置中定义。
3.  **合并逻辑**:
    - 用户样式属性优先于默认样式。
    - **属性**: `StrokeColor`, `FillColor`, `StrokeWidth`, `Opacity`, `Dash`, `ZIndex`, `Visible`, `Color` (文本), `FontSize`, `FontFamily`, `FontWeight`, `BackgroundColor`.

#### 颜色解析
- 支持 **十六进制** 代码 (例如 `#RRGGBB`)。
- 支持 **RGBA** 字符串 (例如 `rgba(255, 0, 0, 0.5)`)。
- 将颜色转换为 OpenCV `Scalar` (BGR 格式)。

#### 不透明度
- Alpha 值通过结合颜色的 Alpha 通道（如果是 RGBA）和显式的 `Opacity` 属性计算得出。

### 3.4 支持的形状

#### 圆形 (Circle)
- **数据**: `Center` (圆心), `Radius` (半径)。
- **渲染**:
  - 如果设置了 `FillColor`，则绘制填充圆。
  - 如果设置了 `StrokeColor`，则绘制轮廓。
  - 支持透明度混合。

#### 折线 (Polyline)
- **数据**: `Points` 数组。
- **渲染**:
  - 绘制连接的线段。
  - 通过 `Dash` 属性支持 **虚线**（自定义实现）。
  - 使用 `Cv2.Polylines`。

#### 多边形 (Polygon)
- **数据**: `Points` 数组。
- **渲染**:
  - **填充**: 如果存在 `FillColor`，则使用 `Cv2.FillPoly`。
  - **描边**: 如果存在 `StrokeColor`，则使用 `Cv2.Polylines`（或虚线逻辑）。
  - 支持透明度混合。

#### 矩形 (`rect`)
- **数据**: `Origin` (原点), `Size` (尺寸), `Rotation` (旋转)。
- **渲染**:
  - **旋转**: 如果 `Rotation > 0.1`，使用 `RotatedRect` 计算角点并作为多边形绘制。
  - **轴对齐**: 使用 `Cv2.Rectangle`。
  - 支持填充和描边。
  - 支持虚线边框。

#### 文本 (Text)
- **数据**: `Position` (位置), `Content` (内容), `Align` (对齐)。
- **渲染**:
  - 字体: `HersheyFonts.HersheySimplex`。
  - **对齐**:
    - **水平**: 左对齐（默认）、居中、右对齐。
    - **垂直**:
        - `top`: 文本绘制在锚点下方。
        - `center`/`middle`: 文本垂直居中。
        - `bottom`: 文本绘制在锚点上方。
  - **背景**: 如果设置了 `BackgroundColor`，则在文本后绘制填充矩形。
  - **安全**: 确保文本绘制在图像边界内。

### 3.5 实用功能

- **裁剪 (`ClipRectToImage`)**:
  - 自动将绘制区域 (ROI) 裁剪到图像边界，以防止在图像外部部分绘制时出现 OpenCV 错误。
- **虚线 (`DrawDashedLine`)**:
  - 通过计算沿向量的线段来绘制虚线的自定义算法。

## 4. 依赖项

- **OpenCvSharp**: 核心图像处理和绘图库。
- **System.Text.Json**: 用于样式配置的 JSON 序列化。
- **Serilog**: 日志记录。
