using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.General.ObjectOccurrenceByLLM.Event;

public class ObjectOccurrenceLLMEvent : DomainEvent
{
    public const string EventType = "Object Occurrence LLM Confirmed Event";

    public string CandidateEventId { get; }
    public string RegionName { get; }
    public List<string> OccurredObjectNames { get; }
    public int DurationSec { get; }
    public string Annotations { get; set; } = string.Empty;
    public string LLMJsonResult { get; set; } = string.Empty;

    public ObjectOccurrenceLLMEvent(
        string sourceId,
        string eventName,
        string algorithmName,
        string candidateEventId,
        string regionName,
        List<string> occurredObjectNames,
        int durationSec)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        CandidateEventId = candidateEventId;
        RegionName = regionName;
        OccurredObjectNames = occurredObjectNames;
        DurationSec = durationSec;
        Message = $"Objects: {string.Join(", ", OccurredObjectNames)} have occurred in region:{regionName} for {DurationSec} seconds and were confirmed by LLM.";
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
