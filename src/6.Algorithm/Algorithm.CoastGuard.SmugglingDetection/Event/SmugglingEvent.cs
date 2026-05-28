using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.CoastGuard.SmugglingDetection.Event;

public class SmugglingEvent : DomainEvent
{
    public new const string EventType = "Smuggling Event";

    public string IncidentId { get; }
    public long FrameId { get; }
    public DateTime FrameUtcTimeStamp { get; }
    public int GatheringCount { get; }
    public int PersonCountInGatherings { get; }
    public bool PeopleGathering { get; }
    public bool BoatExistence { get; }
    public bool PeopleAwayFromBoat { get; }

    public string Annotations { get; set; }

    public SmugglingEvent(
        string sourceId,
        string eventName,
        string algorithmName,
        string incidentId,
        long frameId,
        DateTime frameUtcTimeStamp,
        int gatheringCount,
        int personCountInGatherings,
        bool peopleGathering,
        bool boatExistence,
        bool peopleAwayFromBoat)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        IncidentId = incidentId;
        FrameId = frameId;
        FrameUtcTimeStamp = frameUtcTimeStamp;
        GatheringCount = gatheringCount;
        PersonCountInGatherings = personCountInGatherings;
        PeopleGathering = peopleGathering;
        BoatExistence = boatExistence;
        PeopleAwayFromBoat = peopleAwayFromBoat;
        Annotations = string.Empty;

        Message = $"Smuggling detected. Gatherings:{GatheringCount}, people:{PersonCountInGatherings}, frame:{FrameId}.";
    }

    public override string GenerateJsonContent()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public override string GenerateLogContent()
    {
        return Message;
    }
}
