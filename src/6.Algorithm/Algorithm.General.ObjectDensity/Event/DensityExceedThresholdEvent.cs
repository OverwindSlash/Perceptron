using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.General.ObjectDensity.Event;

public class DensityExceedThresholdEvent : DomainEvent
{
    public const string EventType = "Object Density Exceed Event";

    public string RegionName { get; }
    public string ObjectToBeCount { get; }
    public int ObjectCount { get; }
    public int MaxCountThresh { get; }

    public string Annotations { get; set; }

    public DensityExceedThresholdEvent(string sourceId, string eventName, string algorithmName, 
        string regionName, string objectToBeCount, int objectCount, int maxCountThresh)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        RegionName = regionName;
        ObjectToBeCount = objectToBeCount;
        ObjectCount = objectCount;
        MaxCountThresh = maxCountThresh;
        Message = $"{ObjectToBeCount} number: {objectCount} in detection region:{regionName}, exceed max thresh: {MaxCountThresh}.";
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