using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.General.RegionAccess.Event;

public class EnterRegionEvent : DomainEvent
{
    public const string EventType = "Enter Region Event";

    public string ObjectId { get; }
    public string RegionName { get; }
    public string ObjectGuid { get; set; }
    public string Annotations { get; set; }

    public EnterRegionEvent(string sourceId, string eventName, string algorithmName, string objectId, string regionName)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        ObjectId = objectId;
        RegionName = regionName;
        Message = $"'{objectId}' entering region '{RegionName}' at '{Timestamp.ToLocalTime()}'.";
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
