using Perceptron.Domain.Entity.Common;
using Perceptron.Domain.Entity.ObjectDetection;

namespace Algorithm.CoastGuard.SmugglingDetection;

internal sealed class SmugglingObjectGroup : PropertiesBag
{
    public IReadOnlyList<DetectedObject> GroupObjects { get; }
    public BoundingBox Bbox { get; }
    public string Label { get; }
    public int TrackingId { get; }

    public SmugglingObjectGroup(IReadOnlyList<DetectedObject> groupObjects, string label, int trackingId)
    {
        if (groupObjects.Count == 0)
        {
            throw new ArgumentException("Group must contain at least one object.", nameof(groupObjects));
        }

        GroupObjects = groupObjects;
        Label = label;
        TrackingId = trackingId;
        Bbox = CreateGroupBoundingBox(groupObjects);
    }

    private static BoundingBox CreateGroupBoundingBox(IReadOnlyList<DetectedObject> detectedObjects)
    {
        var minX = detectedObjects.Min(o => o.Bbox.XMin);
        var minY = detectedObjects.Min(o => o.Bbox.YMin);
        var maxX = detectedObjects.Max(o => o.Bbox.XMax);
        var maxY = detectedObjects.Max(o => o.Bbox.YMax);

        return BoundingBox.CreateFromFourPoints(minX, minY, maxX, maxY);
    }
}
