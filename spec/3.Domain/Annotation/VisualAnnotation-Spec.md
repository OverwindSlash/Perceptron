# 视觉标注规范 (Visual Annotation Specification)

## 概述
`VisualAnnotation` 领域实体旨在捕获和管理媒体源的视觉元数据。它将一组图形形状与视频或图像源中的特定时间戳及帧关联起来。

## 类结构

### VisualAnnotation (根类)
表示单个帧的完整标注集。

| 属性 | 类型 | 描述 |
| :--- | :--- | :--- |
| `Version` | `string` | 格式版本 (默认为 "1.0")。 |
| `SourceId` | `string` | 媒体源的标识符。 |
| `Timestamp` | `DateTimeOffset` | 帧的时间偏移量。 |
| `FrameId` | `long` | 顺序帧号。 |
| `CoordinateSpace` | `CoordinateSpace` | 定义空间上下文 (分辨率)。 |
| `Shapes` | `List<Shape>` | 视觉元素的集合。 |

**方法:**
- `VisualAnnotation(string sourceId, DateTimeOffset timestamp, long frameId, int width, int height)`: 构造函数。
- `FromJson(string json)`: 用于 JSON 反序列化的静态工厂方法。
- `SetCoordinateSpace(int width, int height, string type)`: 更新尺寸上下文。
- `AddShape(Shape? shape)`: 添加单个图形。
- `AddShapes(IEnumerable<Shape>? shapes)`: 添加多个图形。

### CoordinateSpace (坐标空间)
定义图形使用的坐标系。

| 属性 | 类型 | 默认值 | 描述 |
| :--- | :--- | :--- | :--- |
| `Type` | `string` | "pixel" | 测量单位。 |
| `Width` | `int` | - | 画布宽度。 |
| `Height` | `int` | - | 画布高度。 |

### Shape (图形)
表示单个图形元素或文本标注。根据 `Type` 的不同，使用的属性也有所不同。

**通用属性:**

| 属性 | 类型 | 默认值 | 描述 |
| :--- | :--- | :--- | :--- |
| `Id` | `string` | Empty | 图形的唯一标识符。 |
| `Type` | `string` | "rect" | 图形类型。支持: `rect`, `circle`, `polyline`, `polygon`, `text`。 |
| `Style` | `Style` | Default | 视觉样式属性。 |

**类型特定属性:**

| 图形类型 (Type) | 关键属性 | 描述 |
| :--- | :--- | :--- |
| **rect** (矩形) | `Origin`, `Size`, `Rotation` | 使用 `Origin` 定义起始点 (左上角)，`Size` 定义宽高。 |
| **circle** (圆形) | `Center`, `Radius` | 使用 `Center` 定义圆心，`Radius` 定义半径。 |
| **polyline** (折线) | `Points` | 一系列连接点。 |
| **polygon** (多边形)| `Points` | 一系列连接点 (首尾相连)。 |
| **text** (文本) | `Position`, `Content`, `Align` | 使用 `Position` 定义位置，`Content` 为文本内容。 |

**属性详情:**

| 属性 | 类型 | 描述 |
| :--- | :--- | :--- |
| `Content` | `string` | 文本内容。 |
| `Position` | `Position` | 文本的定位点 (X, Y)。 |
| `Center` | `Center` | 圆心坐标 (X, Y)。 |
| `Origin` | `Origin` | 矩形原点坐标 (X, Y)。 |
| `Size` | `Size` | 尺寸 (Width, Height)。 |
| `Radius` | `int` | 半径。 |
| `Points` | `Point[]` | 坐标点数组。 |
| `Rotation` | `int` | 旋转角度 (度)。 |
| `Align` | `Align` | 文本对齐方式。 |

### Style (样式)
用于渲染图形的视觉属性。

| 属性 | 类型 | 默认值 | 描述 |
| :--- | :--- | :--- | :--- |
| `StrokeColor` | `string` | "#000000" | 边框颜色 (Hex 或 RGBA)。 |
| `FillColor` | `string` | Empty | 填充颜色 (Hex 或 RGBA)。 |
| `StrokeWidth` | `int` | 0 | 边框粗细。 |
| `Opacity` | `float` | 1.0 | 透明度 (0.0 - 1.0)。 |
| `Dash` | `int[]` | [] | 虚线模式。 |
| `Visible` | `bool` | true | 可见性标志。 |
| `ZIndex` | `int` | 0 | 堆叠顺序。 |
| `Color` | `string` | "#000000" | 文本颜色。 |
| `FontSize` | `int` | 0 | 字体大小。 |
| `FontFamily` | `string` | "Microsoft YaHei" | 字体系列。 |
| `FontWeight` | `string` | "normal" | 字体粗细。 |
| `BackgroundColor`| `object` | Empty | 文本/容器的背景颜色。 |

### 辅助类
- **Position / Center / Origin / Point**: 包含 `X`, `Y` (int) 坐标。
- **Size**: 包含 `Width`, `Height` (int) 尺寸。
- **Align**: 包含 `Horizontal` (水平), `Vertical` (垂直) 对齐方式字符串。

## 使用示例 (JSON)
参考 `sample.json`:

```json
{
  "version": "1.0",
  "sourceId": "Cam-POC.Profile_101",
  "timestamp": "2025-11-26T12:00:00Z",
  "frameId": 12345,
  "coordinateSpace": {
    "type": "pixel",
    "width": 1920,
    "height": 1080
  },
  "shapes": [
    {
      "id": "c1",
      "type": "circle",
      "center": { "x": 200, "y": 150 },
      "radius": 5,
      "style": { "strokeColor": "#FFFF00", "strokeWidth": 1 }
    },
    {
      "id": "l1",
      "type": "polyline",
      "points": [
        { "x": 20, "y": 20 },
        { "x": 100, "y": 100 }
      ],
      "style": { "strokeColor": "#FFFF00", "strokeWidth": 2 }
    },
    {
      "id": "p1",
      "type": "polygon",
      "points": [
        { "x": 100, "y": 100 },
        { "x": 150, "y": 150 },
        { "x": 200, "y": 200 }
      ],
      "style": {
        "strokeColor": "#FFFF00",
        "fillColor": "rgba(255,255,0,0.2)",
        "zIndex": 1
      }
    },
    {
      "id": "r1",
      "type": "rect",
      "origin": { "x": 300, "y": 250 },
      "size": { "width": 80, "height": 60 },
      "rotation": 0,
      "style": { "strokeColor": "#00FF00" }
    },
    {
      "id": "t1",
      "type": "text",
      "position": { "x": 200, "y": 150 },
      "content": "告警",
      "style": {
        "color": "#FFFF00",
        "fontSize": 24,
        "fontFamily": "sans-serif"
      },
      "align": { "horizontal": "left", "vertical": "top" }
    }
  ]
}
```
