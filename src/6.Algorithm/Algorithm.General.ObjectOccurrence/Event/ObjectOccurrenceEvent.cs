using Perceptron.Domain.Event;
using System.Text.Json;

using Algorithm.Common;

namespace Algorithm.General.ObjectOccurrence.Event;

public class ObjectOccurrenceEvent : DomainEvent, IAnnotatedAlgorithmEvent
{
    public const string EventType = "Object Occurrence Event";

    public string RegionName { get; }
    public List<string> OccurredObjectNames { get; }
    public int DurationSec { get; }

    public string Annotations { get; set; } = string.Empty;

    public ObjectOccurrenceEvent(string sourceId, string eventType, string eventName, string algorithmName,
        string regionName, List<string> occurredObjectNames, int durationSec)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        RegionName = regionName;
        OccurredObjectNames = occurredObjectNames;
        DurationSec = durationSec;
        Message = $"Objects: {string.Join(", ", OccurredObjectNames)} have occurred in region:{regionName} for {DurationSec} seconds.";
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
