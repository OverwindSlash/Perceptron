# BoundingBox 规范（优化版 Spec）

* 依赖：`OpenCvSharp`（使用 `Rect` 与 `Rect2f`）
* 主要场景：目标检测框表达、几何运算（IoU/裁剪/缩放/合并）、与 YOLO/归一化标注互转
* 语义：采用左闭右开区间

  * X 方向：`[XMin, XMax)`
  * Y 方向：`[YMin, YMax)`
    其中 `XMax = X + Width`，`YMax = Y + Height`
* 设计：不可变值类型 `readonly struct`，所有操作返回新实例（无共享可变状态）

---

## 1. 坐标系与区间语义

### 1.1 坐标系约定

* `X, Y` 表示**左上角**像素坐标（image space）
* `Width, Height` 为像素尺寸
* 允许框在图像外（负坐标或超过图像边界）**仅在“非图像域”方法中**出现；图像域方法（如 `ClipTo` / `TryClipTo`）会输出落在 `[0, maxW]×[0, maxH]` 内的结果

> 说明：很多管线会在缩放/平移/ROI 映射过程中出现临时越界框。为避免“所有方法都强制非负”带来的不可用，本规范区分 **ValidBox** 与 **ImageBox**（见 2.2）。

### 1.2 左闭右开规则

* 点包含测试（pixel center 语义）：

  * `x >= XMin && x < XMax && y >= YMin && y < YMax`
* 面积：`Area = Width * Height`（当 `Width<=0` 或 `Height<=0` 时面积为 0）
* 交集宽度：`max(0, min(A.XMax, B.XMax) - max(A.XMin, B.XMin))`
* 交集高度：同理

---

## 2. 类型设计与有效性模型（关键优化）

### 2.1 默认值与 Empty 语义（修复“不可避免的 default(struct)”问题）

* `readonly struct` 必然存在 `default(BoundingBox)`，其值为 `(0,0,0,0)`
* 本规范显式定义：

  * `public static BoundingBox Empty { get; } = default;`
  * `public bool IsEmpty => Width <= 0 || Height <= 0;`
* **所有公开工厂方法**必须保证返回 **非 Empty 的有效框**（除非方法语义明确允许返回 `null` / `false` / `Empty`）

> 这样既保持“有效框宽高为正”的强约束，又让 `default` 有可解释语义，避免 `IsEmpty` 永远为 false 的自相矛盾。

### 2.2 有效性分层

* **ValidBox（几何有效）**：`Width > 0 && Height > 0`（允许 `X/Y` 为任意 int，用于几何变换中间态）
* **ImageBox（图像域有效）**：在 `ValidBox` 基础上进一步满足 `X>=0 && Y>=0 && XMax<=maxW && YMax<=maxH`（由裁剪类方法保证）

> 兼容现有“坐标非负”的需求：把它落实到**裁剪/图像域输出**上，而不是强行限制所有构造与几何变换。

---

## 3. 字段与属性

### 3.1 基础字段（只读）

* `int X { get; }`
* `int Y { get; }`
* `int Width { get; }`
* `int Height { get; }`

### 3.2 派生属性（建议类型明确）

* 边界：

  * `int XMin => X`
  * `int YMin => Y`
  * `int XMax => checked(X + Width)`（必须用 `checked` 或 `long` 中间值防溢出）
  * `int YMax => checked(Y + Height)`
  * 别名：`Left=XMin`, `Top=YMin`, `Right=XMax`, `Bottom=YMax`
* 中心点（避免整数截断，提升 IoU/NMS 等稳定性）：

  * `float CenterX => X + Width / 2f`
  * `float CenterY => Y + Height / 2f`
* 面积与周长：

  * `long Area => IsEmpty ? 0 : (long)Width * Height`（防 `int` 溢出）
  * `int Perimeter => IsEmpty ? 0 : checked(2 * (Width + Height))`
* 形状判断：

  * `bool IsSquare => !IsEmpty && Width == Height`
* OpenCV 兼容：

  * `Rect Rectangle => new Rect(X, Y, Width, Height)`

---

## 4. 不变式、参数校验与异常规则

### 4.1 工厂创建约束（对外强约束）

除明确允许产生空/无结果的方法外，**创建必须保证**：

* `Width > 0 && Height > 0`
* `X, Y` 允许为任意 `int`（见 2.2）

### 4.2 异常规范（统一、可预测）

* `ArgumentOutOfRangeException`

  * `width <= 0` 或 `height <= 0`
  * `imageWidth <= 0` / `imageHeight <= 0` / `maxW <= 0` / `maxH <= 0`
  * scale 因子 `<= 0`
* `ArgumentException`

  * 四点/两点构造形成非正面积（例如 `x1==x2` 或 `y1==y2`）
  * 归一化输入为 NaN/Infinity（必须显式拒绝）
* `OverflowException`

  * 任何 `checked` 溢出（如极端大坐标导致 `X+Width` 溢出）
* `InvalidOperationException`

  * `ClipTo` 强制裁剪后无面积（语义保持不变）

---

## 5. 创建与转换（优化：舍入规则统一 + 明确 clamp 策略）

### 5.1 基本构造（私有）

```csharp
private BoundingBox(int x, int y, int width, int height);
```

### 5.2 工厂方法

* `CreateFromRect(Rect rect)`

  * 要求 `rect.Width > 0 && rect.Height > 0`
* `CreateFromRect(int x, int y, int width, int height)`
* `CreateFromFourPoints(int x1, int y1, int x2, int y2)`

  * 自动排序：`xmin=min(x1,x2)`, `xmax=max(x1,x2)`，同理 y
  * `width = xmax - xmin`, `height = ymax - ymin`（必须 >0）
* `CreateFromYolo(int centerX, int centerY, int width, int height)`

  * 采用**明确的整数规则**：
    `x = centerX - width/2`，`y = centerY - height/2`（整数除法向零截断）
  * 适合“推理输出为 int”的场景
* `CreateFromYoloNormalized(float cx, float cy, float w, float h, int imageWidth, int imageHeight)`

  * 归一化转像素：
    `px = cx * imageWidth`，`py = cy * imageHeight`，`pw = w * imageWidth`，`ph = h * imageHeight`
  * 舍入规则统一：`MidpointRounding.AwayFromZero`
  * 输入允许超出 `[0,1]`（不自动 clamp），但必须是有限数
* `FromNormalized(float x, float y, float w, float h, int imageWidth, int imageHeight)`

  * 表示左上角 + 宽高的归一化
  * 舍入：`AwayFromZero`
  * 不自动 clamp（与上面保持一致）

> 可选增强（推荐提供重载而不破坏原语义）：
> `...Normalized(..., bool clamp01 = false)`：当 `clamp01=true` 时把输入先 clamp 到 `[0,1]`，更适合生成训练标注。

### 5.3 输出转换

* `Rect2f ToNormalized(int imageWidth, int imageHeight)`

  * 输出 `(x/imageWidth, y/imageHeight, w/imageWidth, h/imageHeight)`，float
* `ToYoloNormalized(int imageWidth, int imageHeight) -> (float cx, float cy, float w, float h)`

  * `cx = (X + Width/2f)/imageWidth`，同理 y
  * 宽高分别除以 imageWidth/Height

---

## 6. 几何运算（优化：定义一致、去重别名、边界条件明确）

### 6.1 相交、并集与指标

* `float IntersectionArea(BoundingBox other)`

  * 任一为空则返回 0
* `bool TryIntersection(BoundingBox other, out BoundingBox intersection)`

  * 有面积交集返回 true；否则返回 false 且 `intersection = BoundingBox.Empty`
* `BoundingBox? GetIntersection(BoundingBox other)`

  * 有则返回；无则 `null`
* `long UnionArea(BoundingBox other)`

  * `Area(A) + Area(B) - IntersectionArea(A,B)`
* `float IoU(BoundingBox other)`

  * `intersection / union`，若 union==0 返回 0
* `float IoF(BoundingBox other)`

  * **规范化定义**：`intersection / min(Area(A), Area(B))`，若 minArea==0 返回 0
* `float OverlapPercentage(BoundingBox other)`

  * **明确为 IoF 的别名**（建议标注 `[Obsolete]` 或在文档中说明等价，避免重复 API）

### 6.2 关系与包含

* `bool IntersectsWith(BoundingBox other)`

  * 仅当交集面积 > 0 返回 true（相切返回 false；若想把相切视作相交，另提供 `TouchesOrIntersectsWith`）
* `bool Contains(int x, int y)`（严格遵循左闭右开）
* `bool Contains(BoundingBox other)`

  * `other` 为空返回 false（避免 “空集合被包含” 的歧义；如需数学语义可另加参数）

### 6.3 距离

* `float CenterDistanceTo(BoundingBox other)`

  * 欧氏距离（基于 float center）
* `float MinDistance(BoundingBox other)`

  * 边缘最短距离；相交或相切返回 0
  * 计算使用 x/y 方向间隔：
    `dx = max(0, max(other.XMin - XMax, XMin - other.XMax))`（y 同理），`sqrt(dx*dx+dy*dy)`

---

## 7. 变换与裁剪（优化：明确 clamp 行为与结果保证）

### 7.1 合并包围

* `Merge(BoundingBox other) -> BoundingBox`

  * 返回能覆盖两者的最小外接矩形
  * 任一为空：返回另一方（都空返回 Empty）

### 7.2 按中心缩放

* `ScaleAboutCenter(float scaleX, float scaleY) -> BoundingBox`

  * `scaleX>0 && scaleY>0`
  * 新宽高：`Width*scaleX` / `Height*scaleY`，舍入 `AwayFromZero`
  * 中心保持不变：基于 float center 计算新 `X/Y`
  * **不自动 clamp 到非负**（将 clamp 职责交给 `ClipTo/TryClipTo`，更通用；如确需旧行为，提供 `ScaleAboutCenterClampedToNonNegative` 作为额外方法）

> 说明：原 spec 里“缩放时把左上角钳到非负”会悄悄改变中心位置，导致几何不一致，建议拆分为两种明确语义的方法。

### 7.3 平移

* `Translate(int dx, int dy) -> BoundingBox`

  * 结果 `X += dx`, `Y += dy`，允许负坐标（几何中间态）
  * 若业务强制只允许非负，可在业务层调用 `ClipTo` 或提供额外方法：`TranslateClamped(int dx, int dy)`

### 7.4 裁剪到图像范围

* `BoundingBox? TryClipTo(int maxW, int maxH)`

  * 将框与图像域 `[0,maxW)×[0,maxH)` 求交
  * 若无面积交集返回 `null`
* `BoundingBox ClipTo(int maxW, int maxH)`

  * 同上，但无面积时抛 `InvalidOperationException`
* 裁剪后保证 `ImageBox`（见 2.2）

---

## 8. 相等性、哈希与调试

* `ToString()`：建议格式 `"(X={X}, Y={Y}, W={Width}, H={Height})"`
* 值相等：基于 `X,Y,Width,Height`
* 实现：

  * `IEquatable<BoundingBox>`
  * `Equals(object?)`, `Equals(BoundingBox)`, `GetHashCode()`
  * `==`, `!=`
* 建议补充：

  * `void Deconstruct(out int x, out int y, out int w, out int h)`

---

## 9. 性能与实现细则（可落地约束）

* `readonly struct` + 纯函数式返回：线程安全、易于并行
* 所有边界计算使用 `checked` 或 `long` 中间变量，避免静默溢出
* 热路径建议 `MethodImplOptions.AggressiveInlining`（Intersection/IoU/Contains 等）
* 不在方法内部做不必要的 `Rect` 分配；`Rectangle` 属性按需构造即可

---

## 10. 兼容性与迁移建议（针对现有 spec 的差异）

1. **IsEmpty 与 “Width/Height 必须为正”冲突已修复**：

   * `Empty/default` 明确定义；公开创建仍禁止 `Width<=0 || Height<=0`
2. **坐标非负约束从“全局不变式”调整为“图像域裁剪后保证”**：

   * 更适配真实管线（缩放/平移/ROI 映射）
3. `OverlapPercentage` 明确为 `IoF` 别名：

   * 建议文档标注等价，或后续逐步废弃以减少 API 冗余
4. 缩放时不再隐式 `Math.Max(0, ...)`：

   * 如必须保留旧行为，新增 `ScaleAboutCenterClampedToNonNegative`，避免语义混淆

---

## 11. 示例

```csharp
using Baize.Domain.Entity.ObjectDetection;
using OpenCvSharp;

// 从 Rect 创建（必须 w/h > 0）
var box = BoundingBox.CreateFromRect(10, 20, 100, 50);

// YOLO 像素中心点创建（整数规则）
var yoloBox = BoundingBox.CreateFromYolo(60, 45, 100, 50);

// 相交与 IoU
if (box.TryIntersection(yoloBox, out var inter))
{
    float iou = box.IoU(yoloBox);
}

// 缩放（可能越界），再裁剪到图像域
var scaled = box.ScaleAboutCenter(1.2f, 1.2f);
var clipped = scaled.TryClipTo(1920, 1080);

// 归一化输出（YOLO）
var (cx, cy, w, h) = box.ToYoloNormalized(1920, 1080);
```

---

## 12. 必测用例清单（建议作为单元测试基线）

* `default(BoundingBox)`：`IsEmpty == true`；除 `Try*` 方法外不应参与几何结果
* `Contains`：验证右边界与下边界不包含（`x==XMax` 返回 false）
* `IntersectsWith`：相切（边界刚好贴合）返回 false
* `TryIntersection`：无交集返回 false 且 out 为 `Empty`
* `IoU`：两空 / union==0 时返回 0，不抛异常
* `ClipTo/TryClipTo`：完全在外返回 null/抛异常；部分越界裁剪后为 ImageBox
* 溢出防护：极大坐标 + 宽度触发 `OverflowException`（checked 生效）
* 归一化：NaN/Infinity 输入抛 `ArgumentException`

