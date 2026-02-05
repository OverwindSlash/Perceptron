using Perceptron.Domain.Entity.ObjectDetection;

namespace Perceptron.Domain.Event.Pipeline;

public class ObjectExpiredEvent : EventBase
{
    public string SourceId { get; }

    public string Id { get; }

    public int LabelId { get; }

    public string Label { get; }

    public int TrackingId { get; }
    

    public ObjectExpiredEvent(string sourceId, string id, int labelId, string label, int trackingId)
    {
        SourceId = sourceId;
        Id = id;
        LabelId = labelId;
        Label = label;
        TrackingId = trackingId;
    }

    public ObjectExpiredEvent(DetectedObject detectedObject)
    {
        SourceId = detectedObject.SourceId;
        Id = detectedObject.Id;
        LabelId = detectedObject.LabelId;
        Label = detectedObject.Label;
        TrackingId = detectedObject.TrackingId;
    }
}