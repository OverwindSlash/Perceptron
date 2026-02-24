using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.General.Classify.Event;

public class ObjectClassifiedEvent : DomainEvent
{
    public const string EventType = "Object Classified Event";

    public string ObjectId { get; }
    public string Label { get; }
    public double Confidence { get; }

    public string Annotations { get; set; }

    public ObjectClassifiedEvent(string sourceId, string eventName, string algorithmName,
        string objectId, string label, double conf) 
        : base(sourceId, eventName, EventType, algorithmName)
    {
        ObjectId = objectId;
        Label = label;
        Confidence = conf;

        Message = $"Object:'{ObjectId}' classified as {Label}, with conf:{Confidence:F2}";
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