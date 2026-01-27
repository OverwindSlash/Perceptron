using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Event;

namespace Perceptron.Domain.Pipeline;

public class ObjectExpiredEvent : EventBase
{
    public string Id { get; }

    public int LabelId { get; }

    public string Label { get; }

    public int TrackingId { get; }

    public ObjectExpiredEvent(string id, int labelId, string label, int trackingId)
    {
        Id = id;
        LabelId = labelId;
        Label = label;
        TrackingId = trackingId;
    }

    public ObjectExpiredEvent(DetectedObject detectedObject)
    {
        Id = detectedObject.Id;
        LabelId = detectedObject.LabelId;
        Label = detectedObject.Label;
        TrackingId = detectedObject.TrackingId;
    }
}