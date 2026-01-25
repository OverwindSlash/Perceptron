using OpenCvSharp;

namespace Detector.Common;

public class YoloPrediction
{
    public int TypeId { get; set; }
    public string Type { get; set; }
    public float Confidence { get; set; }

    public Rect BBox { get; set; }

    public int X => BBox.X;
    public int Y => BBox.Y;
    public int Width => BBox.Width;
    public int Height => BBox.Height;

    public int TrackingId { get; set; }

    public Point TopLeft => new Point(X, Y);
    public Point TopRight => new Point(X + Width, Y);
    public Point BottomLeft => new Point(X, Y + Height);
    public Point BottomRight => new Point(X + Width, Y + Height);
    public Point Center => new Point(X + Width / 2, Y + Height / 2);

    public List<Point> CornerPoints => new List<Point>() { TopLeft, TopRight, BottomLeft, BottomRight };
}