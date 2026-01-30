using ComponentCommon;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Setting;
using Serilog;
using System.Globalization;

namespace AnnotationRender.OpenCV;

public class Render : ComponentBase, IAnnotationRender
{
    private string _defaultStyleFile;

    private Dictionary<string, Style> _defaultStyles = new();

    public Render(Dictionary<string, string>? preferences) 
        : base(preferences)
    {
        _defaultStyleFile = AnnotationRenderSettings.ParseDefaultStyleFile(preferences);

        LoadDefaultStyles(_defaultStyleFile);
    }

    protected override void LoadPreferences(Dictionary<string, string>? preferences)
    {
        throw new NotImplementedException();
    }

    private void LoadDefaultStyles(string defaultStyleFile)
    {
        try
        {
            if (File.Exists(defaultStyleFile))
            {
                var json = File.ReadAllText(defaultStyleFile);
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _defaultStyles = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Style>>(json, options) ?? new();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load default Styles: {ex.Message}");
        }
    }

    public Mat DrawAnnotations(Mat image, VisualAnnotation annotation)
    {
        try
        {
            var sortedShapes = annotation.Shapes.OrderBy(s => s.Style?.ZIndex ?? 0);

            foreach (var shape in sortedShapes)
            {
                if (shape.Style != null && !shape.Style.Visible) continue;

                switch (shape.Type)
                {
                    case "circle":
                        DrawCircle(image, shape);
                        break;
                    case "polyline":
                        DrawPolyline(image, shape);
                        break;
                    case "polygon":
                        DrawPolygon(image, shape);
                        break;
                    case "rect":
                        DrawRect(image, shape);
                        break;
                    case "text":
                        DrawText(image, shape);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Annotation render failed. Skip this annotation.");
        }

        return image.Clone();
    }

    private static Style MergeStyle(Style? user, Style? def)
    {
        if (user == null && def == null) return new Style();
        if (def == null) return user!;
        if (user == null) return def;

        return new Style
        {
            StrokeColor = user.StrokeColor ?? def.StrokeColor,
            FillColor = user.FillColor ?? def.FillColor,
            StrokeWidth = user.StrokeWidth > 0 ? user.StrokeWidth : def.StrokeWidth,
            Opacity = user.Opacity != 1.0f ? user.Opacity : def.Opacity,
            Dash = user.Dash ?? def.Dash,
            ZIndex = user.ZIndex != 0 ? user.ZIndex : def.ZIndex,
            Visible = user.Visible,
            Color = user.Color ?? def.Color,
            FontSize = user.FontSize > 0 ? user.FontSize : def.FontSize,
            FontFamily = user.FontFamily ?? def.FontFamily,
            FontWeight = user.FontWeight ?? def.FontWeight,
            BackgroundColor = user.BackgroundColor ?? def.BackgroundColor
        };
    }

    private Style GetMergedStyle(string key, Style? user)
    {
        if (_defaultStyles.TryGetValue(key, out var def))
        {
            return MergeStyle(user, def);
        }
        return user ?? new Style();
    }

    private static Scalar ParseColorToScalar(string ColorStr)
    {
        if (string.IsNullOrEmpty(ColorStr)) return new Scalar(0, 0, 0);

        byte r = 0, g = 0, b = 0;

        if (ColorStr.StartsWith("#"))
        {
            var hex = ColorStr.TrimStart('#');
            if (hex.Length == 6)
            {
                r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            }
        }
        else if (ColorStr.StartsWith("rgba", StringComparison.OrdinalIgnoreCase))
        {
            var Content = ColorStr.Substring(ColorStr.IndexOf('(') + 1, ColorStr.IndexOf(')') - ColorStr.IndexOf('(') - 1);
            var parts = Content.Split(',');
            if (parts.Length >= 3)
            {
                r = byte.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                g = byte.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                b = byte.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
            }
        }

        return new Scalar(b, g, r);
    }

    private static double ParseAlpha(string ColorStr, float Opacity)
    {
        double a = 1.0;
        if (ColorStr.StartsWith("rgba", StringComparison.OrdinalIgnoreCase))
        {
            var Content = ColorStr.Substring(ColorStr.IndexOf('(') + 1, ColorStr.IndexOf(')') - ColorStr.IndexOf('(') - 1);
            var parts = Content.Split(',');
            if (parts.Length == 4)
            {
                var aa = double.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);
                a = aa;
            }
        }

        var eff = a * Opacity;
        if (eff < 0) eff = 0;
        if (eff > 1) eff = 1;
        return eff;
    }

    private void DrawCircle(Mat image, Shape shape)
    {
        if (shape.Center == null) return;

        var Style = GetMergedStyle("circle", shape.Style);

        if (!string.IsNullOrEmpty(Style.FillColor))
        {
            var pad = 1;
            var roi = new OpenCvSharp.Rect(shape.Center.X - shape.Radius - pad, shape.Center.Y - shape.Radius - pad, shape.Radius * 2 + pad * 2, shape.Radius * 2 + pad * 2);
            roi = ClipRectToImage(image, roi);
            if (roi.Width > 0 && roi.Height > 0)
            {
                using var overlay = image[roi].Clone();
                var Color = ParseColorToScalar(Style.FillColor);
                var cx = shape.Center.X - roi.X;
                var cy = shape.Center.Y - roi.Y;
                Cv2.Circle(overlay, new OpenCvSharp.Point(cx, cy), shape.Radius, Color, -1, LineTypes.AntiAlias);
                var alpha = ParseAlpha(Style.FillColor, Style.Opacity);
                Cv2.AddWeighted(overlay, alpha, image[roi], 1.0 - alpha, 0, image[roi]);
            }
        }

        if (!string.IsNullOrEmpty(Style.StrokeColor))
        {
            var thickness = Style.StrokeWidth > 0 ? Style.StrokeWidth : 1;
            var pad = thickness + 1;
            var roi = new OpenCvSharp.Rect(shape.Center.X - shape.Radius - pad, shape.Center.Y - shape.Radius - pad, shape.Radius * 2 + pad * 2, shape.Radius * 2 + pad * 2);
            roi = ClipRectToImage(image, roi);
            if (roi.Width > 0 && roi.Height > 0)
            {
                using var overlay = image[roi].Clone();
                var Color = ParseColorToScalar(Style.StrokeColor);
                var cx = shape.Center.X - roi.X;
                var cy = shape.Center.Y - roi.Y;
                Cv2.Circle(overlay, new OpenCvSharp.Point(cx, cy), shape.Radius, Color, thickness, LineTypes.AntiAlias);
                var alpha = ParseAlpha(Style.StrokeColor, Style.Opacity);
                Cv2.AddWeighted(overlay, alpha, image[roi], 1.0 - alpha, 0, image[roi]);
            }
        }
    }

    private void DrawPolyline(Mat image, Shape shape)
    {
        if (shape.Points == null || shape.Points.Length < 2) return;

        var Style = GetMergedStyle("polyline", shape.Style);

        if (!string.IsNullOrEmpty(Style.StrokeColor))
        {
            var Color = ParseColorToScalar(Style.StrokeColor);
            var thickness = Style.StrokeWidth > 0 ? Style.StrokeWidth : 1;
            var pad = thickness + 1;
            var pts = shape.Points.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
            var minX = pts.Min(p => p.X) - pad;
            var minY = pts.Min(p => p.Y) - pad;
            var maxX = pts.Max(p => p.X) + pad;
            var maxY = pts.Max(p => p.Y) + pad;
            var roi = new OpenCvSharp.Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            roi = ClipRectToImage(image, roi);
            if (roi.Width <= 0 || roi.Height <= 0) return;
            using var overlay = image[roi].Clone();
            var oPts = pts.Select(p => new OpenCvSharp.Point(p.X - roi.X, p.Y - roi.Y)).ToArray();

            if (Style.Dash != null && Style.Dash.Length > 0)
            {
                for (int i = 0; i < oPts.Length - 1; i++)
                {
                    DrawDashedLine(overlay, oPts[i], oPts[i + 1], Color, thickness, Style.Dash);
                }
            }
            else
            {
                Cv2.Polylines(overlay, new[] { oPts }, false, Color, thickness, LineTypes.AntiAlias);
            }

            var alpha = ParseAlpha(Style.StrokeColor, Style.Opacity);
            Cv2.AddWeighted(overlay, alpha, image[roi], 1.0 - alpha, 0, image[roi]);
        }
    }

    private void DrawPolygon(Mat image, Shape shape)
    {
        if (shape.Points == null || shape.Points.Length < 3) return;

        var Style = GetMergedStyle("polygon", shape.Style);
        var pts = shape.Points.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
        var minX = pts.Min(p => p.X);
        var minY = pts.Min(p => p.Y);
        var maxX = pts.Max(p => p.X);
        var maxY = pts.Max(p => p.Y);
        var roiFill = ClipRectToImage(image, new OpenCvSharp.Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY)));

        if (!string.IsNullOrEmpty(Style.FillColor))
        {
            if (roiFill.Width > 0 && roiFill.Height > 0)
            {
                using var overlay = image[roiFill].Clone();
                var Color = ParseColorToScalar(Style.FillColor);
                var oPts = pts.Select(p => new OpenCvSharp.Point(p.X - roiFill.X, p.Y - roiFill.Y)).ToArray();
                Cv2.FillPoly(overlay, new[] { oPts }, Color, LineTypes.AntiAlias);
                var alpha = ParseAlpha(Style.FillColor, Style.Opacity);
                Cv2.AddWeighted(overlay, alpha, image[roiFill], 1.0 - alpha, 0, image[roiFill]);
            }
        }

        if (!string.IsNullOrEmpty(Style.StrokeColor))
        {
            var Color = ParseColorToScalar(Style.StrokeColor);
            var thickness = Style.StrokeWidth > 0 ? Style.StrokeWidth : 1;
            var pad = thickness + 1;
            var roiStroke = ClipRectToImage(image, new OpenCvSharp.Rect(minX - pad, minY - pad, Math.Max(1, (maxX - minX) + pad * 2), Math.Max(1, (maxY - minY) + pad * 2)));
            if (roiStroke.Width <= 0 || roiStroke.Height <= 0) return;
            using var overlay = image[roiStroke].Clone();
            var oPts = pts.Select(p => new OpenCvSharp.Point(p.X - roiStroke.X, p.Y - roiStroke.Y)).ToArray();
            if (Style.Dash != null && Style.Dash.Length > 0)
            {
                for (int i = 0; i < oPts.Length; i++)
                {
                    var a = oPts[i];
                    var b = oPts[(i + 1) % oPts.Length];
                    DrawDashedLine(overlay, a, b, Color, thickness, Style.Dash);
                }
            }
            else
            {
                Cv2.Polylines(overlay, new[] { oPts }, true, Color, thickness, LineTypes.AntiAlias);
            }
            var alpha = ParseAlpha(Style.StrokeColor, Style.Opacity);
            Cv2.AddWeighted(overlay, alpha, image[roiStroke], 1.0 - alpha, 0, image[roiStroke]);
        }
    }

    private void DrawRect(Mat image, Shape shape)
    {
        if (shape.Origin == null || shape.Size == null) return;

        var Style = GetMergedStyle("rect", shape.Style);
        var rect = new OpenCvSharp.Rect(shape.Origin.X, shape.Origin.Y, shape.Size.Width, shape.Size.Height);

        if (!string.IsNullOrEmpty(Style.FillColor))
        {
            var roi = ClipRectToImage(image, rect);
            if (roi.Width > 0 && roi.Height > 0)
            {
                using var overlay = image[roi].Clone();
                var Color = ParseColorToScalar(Style.FillColor);
                Cv2.Rectangle(overlay, new OpenCvSharp.Rect(0, 0, roi.Width, roi.Height), Color, -1, LineTypes.AntiAlias);
                var alpha = ParseAlpha(Style.FillColor, Style.Opacity);
                Cv2.AddWeighted(overlay, alpha, image[roi], 1.0 - alpha, 0, image[roi]);
            }
        }

        if (!string.IsNullOrEmpty(Style.StrokeColor))
        {
            var Color = ParseColorToScalar(Style.StrokeColor);
            var thickness = Style.StrokeWidth > 0 ? Style.StrokeWidth : 1;
            var pad = thickness + 1;
            var roi = ClipRectToImage(image, new OpenCvSharp.Rect(rect.X - pad, rect.Y - pad, rect.Width + pad * 2, rect.Height + pad * 2));
            if (roi.Width <= 0 || roi.Height <= 0) return;
            using var overlay = image[roi].Clone();
            var p1 = new OpenCvSharp.Point(0, 0);
            var p2 = new OpenCvSharp.Point(roi.Width, 0);
            var p3 = new OpenCvSharp.Point(roi.Width, roi.Height);
            var p4 = new OpenCvSharp.Point(0, roi.Height);
            if (Style.Dash != null && Style.Dash.Length > 0)
            {
                DrawDashedLine(overlay, p1, p2, Color, thickness, Style.Dash);
                DrawDashedLine(overlay, p2, p3, Color, thickness, Style.Dash);
                DrawDashedLine(overlay, p3, p4, Color, thickness, Style.Dash);
                DrawDashedLine(overlay, p4, p1, Color, thickness, Style.Dash);
            }
            else
            {
                Cv2.Rectangle(overlay, new OpenCvSharp.Rect(0, 0, roi.Width, roi.Height), Color, thickness, LineTypes.AntiAlias);
            }
            var alpha = ParseAlpha(Style.StrokeColor, Style.Opacity);
            Cv2.AddWeighted(overlay, alpha, image[roi], 1.0 - alpha, 0, image[roi]);
        }
    }

    private void DrawText(Mat image, Shape shape)
    {
        if (shape.Position == null || string.IsNullOrEmpty(shape.Content)) return;

        var Style = GetMergedStyle("text", shape.Style);
        var ColorStr = Style.Color ?? "#000000";
        var Color = ParseColorToScalar(ColorStr);
        var alpha = ParseAlpha(ColorStr, Style.Opacity);

        var fontFace = HersheyFonts.HersheySimplex;
        var fontScale = Style.FontSize > 0 ? Style.FontSize / 24.0 : 0.5;
        var thickness = Style.StrokeWidth > 0 ? Style.StrokeWidth : 1;

        var text = shape.Content;
        int baseline;
        var Size = Cv2.GetTextSize(text, fontFace, fontScale, thickness, out baseline);

        var x = shape.Position.X;
        var y = shape.Position.Y;
        var hAlign = shape.Align?.Horizontal?.ToLowerInvariant();
        if (hAlign == "Center") x -= Size.Width / 2;
        else if (hAlign == "right") x -= Size.Width;

        var roi = new OpenCvSharp.Rect(x, y - Size.Height, Math.Max(1, Size.Width), Math.Max(1, Size.Height + baseline));
        roi = ClipRectToImage(image, roi);
        if (roi.Width > 0 && roi.Height > 0)
        {
            using var overlay = image[roi].Clone();
            var ox = x - roi.X;
            var oy = y - roi.Y;
            Cv2.PutText(overlay, text, new OpenCvSharp.Point(ox, oy), fontFace, fontScale, Color, thickness, LineTypes.AntiAlias);
            Cv2.AddWeighted(overlay, alpha, image[roi], 1.0 - alpha, 0, image[roi]);
        }
    }

    private static void DrawDashedLine(Mat overlay, OpenCvSharp.Point p0, OpenCvSharp.Point p1, Scalar Color, int thickness, int[] Dash)
    {
        if (Dash == null || Dash.Length == 0)
        {
            Cv2.Line(overlay, p0, p1, Color, thickness, LineTypes.AntiAlias);
            return;
        }

        double dx = p1.X - p0.X;
        double dy = p1.Y - p0.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-3)
        {
            Cv2.Line(overlay, p0, p1, Color, thickness, LineTypes.AntiAlias);
            return;
        }

        double vx = dx / dist;
        double vy = dy / dist;
        double t = 0;
        int i = 0;
        bool draw = true;
        while (t < dist)
        {
            double seg = Dash[i % Dash.Length];
            double end = Math.Min(t + seg, dist);
            var s = new OpenCvSharp.Point((int)Math.Round(p0.X + vx * t), (int)Math.Round(p0.Y + vy * t));
            var e = new OpenCvSharp.Point((int)Math.Round(p0.X + vx * end), (int)Math.Round(p0.Y + vy * end));
            if (draw) Cv2.Line(overlay, s, e, Color, thickness, LineTypes.AntiAlias);
            t = end;
            i++;
            draw = !draw;
        }
    }

    private static Rect ClipRectToImage(Mat image, OpenCvSharp.Rect rect)
    {
        int x = Math.Max(0, rect.X);
        int y = Math.Max(0, rect.Y);
        int right = Math.Min(image.Width, rect.X + rect.Width);
        int bottom = Math.Min(image.Height, rect.Y + rect.Height);
        int w = Math.Max(0, right - x);
        int h = Math.Max(0, bottom - y);
        return new Rect(x, y, w, h);
    }
}
