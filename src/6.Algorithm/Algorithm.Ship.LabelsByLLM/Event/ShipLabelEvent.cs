using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.Ship.LabelsByLLM.Event;

public class ShipLabelEvent : DomainEvent
{
    public const string EventType = "Ship Label Event";

    public string ObjectId { get; }
    public string ObjectLocalId { get; }
    public float Confidence { get; }
    public ShipLabel Labels { get; }

    public string Annotations { get; set; }


    public ShipLabelEvent(string sourceId, string eventName, string algorithmName,
        string objectId, string objectLocalId, float confidence, ShipLabel labels)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        ObjectId = objectId;
        ObjectLocalId = objectLocalId;
        Confidence = confidence;
        Labels = labels;
        Message = $"{ObjectId} labels -> Type:{Labels.ShipType}, Colors:{string.Join(',', Labels.ShipColor)}, Draught:{Labels.ShipDraught}";
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