using OpenCvSharp;

namespace Perceptron.Domain.Entity.ObjectDetection;

/// <summary>
/// Represents a bounding box in an image.
/// Coordinate system: X, Y is Top-Left.
/// Semantics: [XMin, XMax), [YMin, YMax).
/// Immutable value type.
/// </summary>
public readonly struct BoundingBox : IEquatable<BoundingBox>
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public static BoundingBox Empty { get; } = default;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int XMin => X;
    public int YMin => Y;
    public int XMax => checked(X + Width);
    public int YMax => checked(Y + Height);
    public int Left => XMin;
    public int Top => YMin;
    public int Right => XMax;
    public int Bottom => YMax;

    public float CenterX => X + Width / 2f;
    public float CenterY => Y + Height / 2f;

    public long Area => IsEmpty ? 0 : (long)Width * Height;
    public int Perimeter => IsEmpty ? 0 : checked(2 * (Width + Height));
    public bool IsSquare => !IsEmpty && Width == Height;

    public Rect Rectangle => new Rect(X, Y, Width, Height);

    private BoundingBox(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    #region Factory Methods

    public static BoundingBox CreateFromRect(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(rect), "Width and Height must be positive.");

        return new BoundingBox(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static BoundingBox CreateFromRect(int x, int y, int width, int height)
    {
        //if (x < 0 || y < 0)
        //    throw new ArgumentException("X and Y must be non-negative.");

        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and Height must be positive.");

        return new BoundingBox(x, y, width, height);
    }

    public static BoundingBox CreateFromFourPoints(int x1, int y1, int x2, int y2)
    {
        int xmin = Math.Min(x1, x2);
        int xmax = Math.Max(x1, x2);
        int ymin = Math.Min(y1, y2);
        int ymax = Math.Max(y1, y2);

        int width = xmax - xmin;
        int height = ymax - ymin;

        //if (xmin < 0 || ymin < 0)
        //    throw new ArgumentException("Points must have non-negative coordinates.");

        if (width <= 0 || height <= 0)
            throw new ArgumentException("Points must form a box with positive area.");

        return new BoundingBox(xmin, ymin, width, height);
    }

    public static BoundingBox CreateFromYolo(int centerX, int centerY, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and Height must be positive.");

        int x = centerX - width / 2;
        int y = centerY - height / 2;

        return new BoundingBox(x, y, width, height);
    }

    public static BoundingBox CreateFromYoloNormalized(float cx, float cy, float w, float h, int imageWidth, int imageHeight)
    {
        if (float.IsNaN(cx) || float.IsInfinity(cx) ||
            float.IsNaN(cy) || float.IsInfinity(cy) ||
            float.IsNaN(w) || float.IsInfinity(w) ||
            float.IsNaN(h) || float.IsInfinity(h))
            throw new ArgumentException("Normalized coordinates must be finite numbers.");

        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");

        if (w <= 0 || h <= 0)
            throw new ArgumentOutOfRangeException(nameof(w), "Normalized width and height must be positive.");

        int pw = (int)Math.Round(w * imageWidth, MidpointRounding.AwayFromZero);
        int ph = (int)Math.Round(h * imageHeight, MidpointRounding.AwayFromZero);

        if (pw <= 0 || ph <= 0)
            throw new ArgumentOutOfRangeException(nameof(pw), "Resulting pixel width and height must be positive.");

        int centerPx = (int)Math.Round(cx * imageWidth, MidpointRounding.AwayFromZero);
        int centerPy = (int)Math.Round(cy * imageHeight, MidpointRounding.AwayFromZero);

        int x = centerPx - pw / 2;
        int y = centerPy - ph / 2;

        return new BoundingBox(x, y, pw, ph);
    }

    public static BoundingBox FromNormalized(float x, float y, float w, float h, int imageWidth, int imageHeight)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) ||
            float.IsNaN(y) || float.IsInfinity(y) ||
            float.IsNaN(w) || float.IsInfinity(w) ||
            float.IsNaN(h) || float.IsInfinity(h))
            throw new ArgumentException("Normalized coordinates must be finite numbers.");

        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");

        if (w <= 0 || h <= 0)
            throw new ArgumentOutOfRangeException(nameof(w), "Normalized width and height must be positive.");

        int pw = (int)Math.Round(w * imageWidth, MidpointRounding.AwayFromZero);
        int ph = (int)Math.Round(h * imageHeight, MidpointRounding.AwayFromZero);

        if (pw <= 0 || ph <= 0)
            throw new ArgumentOutOfRangeException(nameof(pw), "Resulting pixel width and height must be positive.");

        int px = (int)Math.Round(x * imageWidth, MidpointRounding.AwayFromZero);
        int py = (int)Math.Round(y * imageHeight, MidpointRounding.AwayFromZero);

        return new BoundingBox(px, py, pw, ph);
    }

    #endregion

    #region Conversions

    public Rect2f ToNormalized(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");
        return new Rect2f((float)X / imageWidth, (float)Y / imageHeight, (float)Width / imageWidth, (float)Height / imageHeight);
    }

    public (float cx, float cy, float w, float h) ToYoloNormalized(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");

        float w = (float)Width / imageWidth;
        float h = (float)Height / imageHeight;
        float cx = (X + Width / 2f) / imageWidth;
        float cy = (Y + Height / 2f) / imageHeight;

        return (cx, cy, w, h);
    }

    #endregion

    #region Geometry

    public float IntersectionArea(BoundingBox other)
    {
        if (IsEmpty || other.IsEmpty) return 0;

        int intersectW = Math.Max(0, Math.Min(XMax, other.XMax) - Math.Max(XMin, other.XMin));
        int intersectH = Math.Max(0, Math.Min(YMax, other.YMax) - Math.Max(YMin, other.YMin));

        return (float)intersectW * intersectH;
    }

    public bool TryIntersection(BoundingBox other, out BoundingBox intersection)
    {
        if (IsEmpty || other.IsEmpty)
        {
            intersection = Empty;
            return false;
        }

        int xmin = Math.Max(XMin, other.XMin);
        int ymin = Math.Max(YMin, other.YMin);
        int xmax = Math.Min(XMax, other.XMax);
        int ymax = Math.Min(YMax, other.YMax);

        int w = xmax - xmin;
        int h = ymax - ymin;

        if (w > 0 && h > 0)
        {
            intersection = new BoundingBox(xmin, ymin, w, h);
            return true;
        }

        intersection = Empty;
        return false;
    }

    public BoundingBox? GetIntersection(BoundingBox other)
    {
        if (TryIntersection(other, out var result)) 
            return result;

        return null;
    }

    public long UnionArea(BoundingBox other)
    {
        return Area + other.Area - (long)IntersectionArea(other);
    }

    public float IoU(BoundingBox other)
    {
        float union = UnionArea(other);

        if (union == 0) 
            return 0;

        return IntersectionArea(other) / union;
    }

    public float IoF(BoundingBox other)
    {
        long minArea = Math.Min(Area, other.Area);

        if (minArea == 0) 
            return 0;

        return IntersectionArea(other) / minArea;
    }

    [Obsolete("Use IoF instead.")]
    public float OverlapPercentage(BoundingBox other) => IoF(other);

    public bool IntersectsWith(BoundingBox other)
    {
        return IntersectionArea(other) > 0;
    }

    public bool Contains(int x, int y)
    {
        return x >= XMin && x < XMax && y >= YMin && y < YMax;
    }

    public bool Contains(BoundingBox other)
    {
        if (other.IsEmpty) return false;

        return XMin <= other.XMin && YMin <= other.YMin && XMax >= other.XMax && YMax >= other.YMax;
    }

    public float CenterDistanceTo(BoundingBox other)
    {
        float dx = CenterX - other.CenterX;
        float dy = CenterY - other.CenterY;

        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public float MinDistance(BoundingBox other)
    {
        // dx = max(0, max(other.XMin - XMax, XMin - other.XMax))
        int dx = Math.Max(0, Math.Max(other.XMin - XMax, XMin - other.XMax));
        int dy = Math.Max(0, Math.Max(other.YMin - YMax, YMin - other.YMax));

        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    #endregion

    #region Transformations

    public BoundingBox Merge(BoundingBox other)
    {
        if (IsEmpty) return other.IsEmpty ? Empty : other;
        if (other.IsEmpty) return this;

        int xmin = Math.Min(XMin, other.XMin);
        int ymin = Math.Min(YMin, other.YMin);
        int xmax = Math.Max(XMax, other.XMax);
        int ymax = Math.Max(YMax, other.YMax);

        return new BoundingBox(xmin, ymin, xmax - xmin, ymax - ymin);
    }

    public BoundingBox ScaleAboutCenter(float scaleX, float scaleY)
    {
        if (scaleX <= 0 || scaleY <= 0) throw new ArgumentOutOfRangeException(nameof(scaleX), "Scale must be positive.");

        int newW = (int)Math.Round(Width * scaleX, MidpointRounding.AwayFromZero);
        int newH = (int)Math.Round(Height * scaleY, MidpointRounding.AwayFromZero);

        if (newW <= 0) newW = 1;
        if (newH <= 0) newH = 1;

        float cx = CenterX;
        float cy = CenterY;

        int newX = (int)(cx - newW / 2f);
        int newY = (int)(cy - newH / 2f);

        return new BoundingBox(newX, newY, newW, newH);
    }

    public BoundingBox Translate(int dx, int dy)
    {
        return new BoundingBox(X + dx, Y + dy, Width, Height);
    }

    public BoundingBox? TryClipTo(int maxW, int maxH)
    {
        if (maxW <= 0 || maxH <= 0) throw new ArgumentOutOfRangeException(nameof(maxW), "Max dimensions must be positive.");

        int xmin = Math.Max(0, XMin);
        int ymin = Math.Max(0, YMin);
        int xmax = Math.Min(maxW, XMax);
        int ymax = Math.Min(maxH, YMax);

        int w = xmax - xmin;
        int h = ymax - ymin;

        if (w > 0 && h > 0)
        {
            return new BoundingBox(xmin, ymin, w, h);
        }

        return null;
    }

    public BoundingBox ClipTo(int maxW, int maxH)
    {
        var result = TryClipTo(maxW, maxH);

        if (result == null) throw new InvalidOperationException("Clip resulted in empty box.");

        return result.Value;
    }

    #endregion

    #region Equality & Standard Overrides

    public override bool Equals(object? obj) => obj is BoundingBox other && Equals(other);
    public bool Equals(BoundingBox other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(BoundingBox left, BoundingBox right) => left.Equals(right);
    public static bool operator !=(BoundingBox left, BoundingBox right) => !left.Equals(right);

    public override string ToString() => $"(X={X}, Y={Y}, W={Width}, H={Height})";

    public void Deconstruct(out int x, out int y, out int w, out int h)
    {
        x = X;
        y = Y;
        w = Width;
        h = Height;
    }

    #endregion
}