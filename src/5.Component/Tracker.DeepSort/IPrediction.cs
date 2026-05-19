using System.Drawing;

namespace Tracker.DeepSort;

public interface IPrediction
{
    public int DetectionObjectType { get; }
    public Rectangle CurrentBoundingBox { get; }
    public float Confidence { get; }
}