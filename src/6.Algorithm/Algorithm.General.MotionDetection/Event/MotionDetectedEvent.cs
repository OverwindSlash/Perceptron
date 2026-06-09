using OpenCvSharp;
using Perceptron.Domain.Event;
using System.Text.Json;

using Algorithm.Common;

namespace Algorithm.General.MotionDetection.Event;

public class MotionDetectedEvent : DomainEvent, IAnnotatedAlgorithmEvent
{
    public const string EventType = "Motion Detected Event";

    public List<Rect> MotionRects { get; }

    public string Annotations { get; set; } = string.Empty;

    public MotionDetectedEvent(string sourceId, string eventName, string algorithmName, List<Rect> motionRects) 
        : base(sourceId, EventType, eventName, algorithmName)
    {
        MotionRects = motionRects; 
        Message = $"Motion detected with {MotionRects.Count} boxes.";
    }

    public override string GenerateJsonContent()
    {
        var jsonContent = JsonSerializer.Serialize(this, JsonOptions);

        return jsonContent;
    }

    public override string GenerateLogContent()
    {
        return Message;
    }
}
