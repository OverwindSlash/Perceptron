using Perceptron.Domain.Entity.ObjectDetection;

namespace Perceptron.Domain.Event.Pipeline;

public class ObjectExpiredEvent : EventBase
{
    public string SourceId { get; }

    public string Id { get; }

    public string LocalId { get; }

    public int LabelId { get; }

    public string Label { get; }

    public int TrackingId { get; }
    

    public ObjectExpiredEvent(string sourceId, string id, string localId, int labelId, string label, int trackingId)
    {
        SourceId = sourceId;
        Id = id;
        LocalId = localId;
        LabelId = labelId;
        Label = label;
        TrackingId = trackingId;
    }

    public ObjectExpiredEvent(DetectedObject detectedObject)
    {
        SourceId = detectedObject.SourceId;
        Id = detectedObject.Id;
        LocalId = detectedObject.LocalId;
        LabelId = detectedObject.LabelId;
        Label = detectedObject.Label;
        TrackingId = detectedObject.TrackingId;
    }
}