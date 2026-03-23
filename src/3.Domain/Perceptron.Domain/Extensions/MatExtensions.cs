using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using System.Collections.Concurrent;
using SkiaSharp;

namespace Perceptron.Domain.Extensions;

public static class MatExtensions
{
    // 固定调色盘（BGR）
    private static readonly Scalar[] Palette =
    [
        new Scalar(143, 169, 194), // 柔蓝
        new Scalar(219, 172, 140), // 柔橙
        new Scalar(138, 173, 138), // 柔绿
        new Scalar(198, 144, 144), // 柔红
        new Scalar(179, 163, 194), // 柔紫
        new Scalar(168, 150, 144), // 柔棕
        new Scalar(216, 175, 207), // 柔粉
        new Scalar(176, 176, 176), // 柔灰
        new Scalar(203, 204, 157), // 柔黄绿
        new Scalar(150, 196, 200), // 柔青
    ];

    private static readonly ConcurrentDictionary<string, Scalar> ColorCache = new();

    private static Scalar GetColorByLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return Scalar.White;
        return ColorCache.GetOrAdd(label, key =>
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in key) hash = hash * 31 + c;
                if (hash < 0) hash = -hash;
                return Palette[hash % Palette.Length];
            }
        });
    }

    /// <summary>
    /// 绘制单个检测对象到Mat图像上
    /// </summary>
    /// <param name="mat">目标图像</param>
    /// <param name="detectedObject">要绘制的检测对象</param>
    /// <param name="labelFormatter">标签格式化函数</param>
    /// <param name="thicknessOverride">线条粗细覆盖值</param>
    /// <param name="fontScaleOverride">字体大小覆盖值</param>
    /// <param name="alpha">背景透明度</param>
    /// <param name="saturationScale">背景颜色饱和度缩放</param>
    public static void DrawDetection(
        this Mat mat,
        DetectedObject detectedObject,
        bool showLabel = false,
        bool showConfidence = false,
        Func<DetectedObject, string>? labelFormatter = null,
        int? thicknessOverride = null,
        double? fontScaleOverride = null,
        double alpha = 0.4,
        double saturationScale = 0.65
    )
    {
        if (mat == null || mat.Empty() || detectedObject == null)
            return;

        int minSide = Math.Min(mat.Width, mat.Height);
        int thickness = thicknessOverride ?? Math.Max(2, (int)Math.Round(minSide / 1200.0));
        double fontScale = fontScaleOverride ?? Math.Max(0.5, Math.Min(2.0, minSide / 1200.0));
        var fontFace = HersheyFonts.HersheySimplex;

        int x1 = Math.Clamp(detectedObject.X, 0, mat.Width - 1);
        int y1 = Math.Clamp(detectedObject.Y, 0, mat.Height - 1);
        int x2 = Math.Clamp(detectedObject.X + detectedObject.Width, 0, mat.Width - 1);
        int y2 = Math.Clamp(detectedObject.Y + detectedObject.Height, 0, mat.Height - 1);

        //var color = GetColorByLabel(detectedObject.Label);
        var color = Scalar.Crimson;

        // 画框
        Cv2.Rectangle(mat, new Point(x1, y1), new Point(x2, y2), color, thickness, LineTypes.AntiAlias);

        // 标签文本
        string text = string.Empty;
        if (showLabel && showConfidence)
        {
            text = $"{detectedObject.Label} {detectedObject.Confidence:0.00}";
        }
        else if (showLabel)
        {
            text = detectedObject.Label;
        }
        else if (showConfidence)
        {
            text = $"{detectedObject.Confidence:0.00}";
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            int baseline;
            var textSize = Cv2.GetTextSize(text, fontFace, fontScale, thickness, out baseline);
            int textW = textSize.Width;
            int textH = textSize.Height + baseline;

            // 标签背景位置
            int bgX1 = x1;
            int bgY1 = y1 - textH - 3;
            if (bgY1 < 0) bgY1 = y1 + 3;
            int bgX2 = Math.Min(bgX1 + textW + 6, mat.Width - 1);
            int bgY2 = Math.Min(bgY1 + textH + 2, mat.Height - 1);

            // 生成降饱和背景色
            Scalar bgColor = ReduceSaturation(color, saturationScale);

            // 透明混合绘制背景
            var roi = new Rect(bgX1, bgY1, bgX2 - bgX1, bgY2 - bgY1);
            var matRoi = new Mat(mat, roi);
            var overlay = matRoi.Clone();
            overlay.SetTo(bgColor);
            Cv2.AddWeighted(overlay, alpha, matRoi, 1 - alpha, 0, matRoi);

            // 绘制文字
            var textOrg = new Point(bgX1 + 3, bgY2 - baseline - 2);
            Cv2.PutText(mat, text, textOrg, fontFace, fontScale, Scalar.White, thickness, LineTypes.AntiAlias);
        }
    }

    public static void DrawDetections(
        this Mat mat,
        IReadOnlyList<DetectedObject> detectedObjects,
        Func<DetectedObject, string>? labelFormatter = null,
        int? thicknessOverride = null,
        double? fontScaleOverride = null,
        double alpha = 0.4, // 背景透明度
        double saturationScale = 0.65 // 背景颜色饱和度缩放
    )
    {
        if (mat == null || mat.Empty() || detectedObjects == null || detectedObjects.Count == 0)
            return;

        int minSide = Math.Min(mat.Width, mat.Height);
        int thickness = thicknessOverride ?? Math.Max(2, (int)Math.Round(minSide / 1200.0));
        double fontScale = fontScaleOverride ?? Math.Max(0.5, Math.Min(2.0, minSide / 1200.0));
        var fontFace = HersheyFonts.HersheySimplex;

        foreach (var obj in detectedObjects)
        {
            int x1 = Math.Clamp(obj.X, 0, mat.Width - 1);
            int y1 = Math.Clamp(obj.Y, 0, mat.Height - 1);
            int x2 = Math.Clamp(obj.X + obj.Width, 0, mat.Width - 1);
            int y2 = Math.Clamp(obj.Y + obj.Height, 0, mat.Height - 1);

            var color = GetColorByLabel(obj.Label);

            // 画框
            Cv2.Rectangle(mat, new Point(x1, y1), new Point(x2, y2), color, thickness, LineTypes.AntiAlias);

            // 标签文本
            string text = (labelFormatter != null)
                ? labelFormatter(obj)
                : $"{obj.Label} {obj.Confidence:0.00}";

            if (!string.IsNullOrWhiteSpace(text))
            {
                int baseline;
                var textSize = Cv2.GetTextSize(text, fontFace, fontScale, thickness, out baseline);
                int textW = textSize.Width;
                int textH = textSize.Height + baseline;

                // 标签背景位置
                int bgX1 = x1;
                int bgY1 = y1 - textH - 3;
                if (bgY1 < 0) bgY1 = y1 + 3;
                int bgX2 = Math.Min(bgX1 + textW + 6, mat.Width - 1);
                int bgY2 = Math.Min(bgY1 + textH + 2, mat.Height - 1);

                // 生成降饱和背景色
                Scalar bgColor = ReduceSaturation(color, saturationScale);

                // 透明混合绘制背景
                var roi = new Rect(bgX1, bgY1, bgX2 - bgX1, bgY2 - bgY1);
                var matRoi = new Mat(mat, roi);
                var overlay = matRoi.Clone();
                overlay.SetTo(bgColor);
                Cv2.AddWeighted(overlay, alpha, matRoi, 1 - alpha, 0, matRoi);

                // 绘制文字
                var textOrg = new Point(bgX1 + 3, bgY2 - baseline - 2);
                Cv2.PutText(mat, text, textOrg, fontFace, fontScale, Scalar.White, thickness, LineTypes.AntiAlias);
            }
        }
    }

    // 降低饱和度函数
    private static Scalar ReduceSaturation(Scalar bgr, double saturationScale)
    {
        // BGR -> HSV
        var bgrMat = new Mat(1, 1, MatType.CV_8UC3, bgr);
        Cv2.CvtColor(bgrMat, bgrMat, ColorConversionCodes.BGR2HSV);
        Vec3b hsv = bgrMat.Get<Vec3b>(0, 0);

        // 调整饱和度
        hsv.Item1 = (byte)Math.Clamp(hsv.Item1 * saturationScale, 0, 255);

        // HSV -> BGR
        bgrMat.Set(0, 0, hsv);
        Cv2.CvtColor(bgrMat, bgrMat, ColorConversionCodes.HSV2BGR);
        Vec3b newBgr = bgrMat.Get<Vec3b>(0, 0);

        return new Scalar(newBgr.Item0, newBgr.Item1, newBgr.Item2);
    }

    public static string ToBase64String(this Mat mat)
    {
        if (mat == null || mat.Empty()) throw new ArgumentNullException(nameof(mat));

        Cv2.ImEncode(".jpg", mat, out var bytes);
        return Convert.ToBase64String(bytes);
    }

    // 零拷贝：要求 mat.Channels() == 4 且数据为 BGRA 顺序（OpenCV 默认 BGRA）
    public static SKBitmap ToSKBitmap(this Mat mat)
    {
       if (mat == null || mat.Empty()) throw new ArgumentNullException(nameof(mat));

       if (mat.Channels() == 3)
       {
           // 创建一个 BGRA Mat（会分配新的内存并由 OpenCV 高效填充）
           var bgra = new Mat();
           Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
           // 此时 bgra.Data 指向 BGRA 数据，alpha 通常被设为 255

           var info = new SKImageInfo(bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
           var bmp = new SKBitmap();

           // 使用 ReleaseDelegate 确保 SKBitmap 释放时释放临时的 bgra Mat
           // 注意：这里只释放转换后的临时 Mat (bgra)，绝不会影响传入的原始 Mat (mat)
           SKBitmapReleaseDelegate releaseProc = (addr, context) =>
           {
               if (context is Mat tempMat)
               {
                   tempMat.Dispose();
               }
           };

           if (!bmp.InstallPixels(info, bgra.Data, (int)bgra.Step(), releaseProc, bgra))
           {
               bmp.Dispose();
               bgra.Dispose();
               throw new InvalidOperationException("InstallPixels 失败。");
           }

           // 返回包装，确保调用者 Dispose 时同时释放 bmp 与 bgra 内存
           return bmp;
       }
       else if (mat.Channels() == 4)
       {
           // 4通道 BGRA 直接包装 (Zero-Copy)
           var info = new SKImageInfo(mat.Width, mat.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
           var bmp = new SKBitmap();

           // 直接使用原始 Mat 的数据，不进行拷贝。
           // 注意：因为是直接引用 mat.Data，SKBitmap 不持有所有权，
           // 释放 SKBitmap 时 *不会* 释放 Mat。
           // 调用者必须确保 Mat 在 SKBitmap 使用期间保持有效。
           if (!bmp.InstallPixels(info, mat.Data, (int)mat.Step()))
           {
               bmp.Dispose();
               throw new InvalidOperationException("InstallPixels 失败。");
           }

           return bmp;
       }
       else
       {
           throw new ArgumentException("输入必须为 3 通道 (BGR) 或 4 通道 (BGRA) Mat。");
       }
    }

    // public static SKBitmap ToSKBitmap(this Mat mat)
    // {
    //     if (mat == null || mat.Empty()) throw new ArgumentNullException(nameof(mat));

    //     if (mat.Channels() != 3) throw new ArgumentException("输入必须为 3 通道 BGR Mat。");

    //     // 创建一个 BGRA Mat（会分配新的内存并由 OpenCV 高效填充）
    //     var bgra = new Mat();
    //     Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
    //     // 此时 bgra.Data 指向 BGRA 数据，alpha 通常被设为 255

    //     var info = new SKImageInfo(bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
    //     var bmp = new SKBitmap();
    //     if (!bmp.InstallPixels(info, bgra.Data, (int)bgra.Step()))
    //     {
    //         bmp.Dispose();
    //         bgra.Dispose();
    //         throw new InvalidOperationException("InstallPixels 失败。");
    //     }

    //     // 返回包装，确保调用者 Dispose 时同时释放 bmp 与 bgra 内存
    //     return bmp;
    // }
}
