using Perceptron.Domain.Event;
using System.Text.Json;

using Algorithm.Common;

namespace Algorithm.General.RegionAccess.Event;

public class InRegionEvent : DomainEvent, IAnnotatedAlgorithmEvent
{
    public const string EventType = "In Region Event";

    public string ObjectId { get; }
    public string RegionName { get; }
    public string Annotations { get; set; } = string.Empty;

    public InRegionEvent(string sourceId, string eventName, string algorithmName, string objectId, string regionName)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        ObjectId = objectId;
        RegionName = regionName;
        Message = $"'{objectId}' in region '{RegionName}' at '{Timestamp.ToLocalTime()}'.";
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
