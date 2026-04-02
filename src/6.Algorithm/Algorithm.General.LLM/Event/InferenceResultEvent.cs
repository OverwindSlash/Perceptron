using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.General.LLM.Event;

public class InferenceResultEvent : DomainEvent
{
    public const string EventType = "LLM Inference Result Event";

    public string ModelName { get; }
    public TimeSpan InferenceTime { get; }
    public string JsonResult { get; set; }

    public InferenceResultEvent(string sourceId, string eventType, string eventName, string algorithmName, 
        string modelName, TimeSpan inferenceTime, string jsonResult) 
        : base(sourceId, EventType, eventName, algorithmName)
    {
        ModelName = modelName;
        InferenceTime = inferenceTime;
        JsonResult = jsonResult;
        Message = $"LLM '{modelName}', Elapse: {inferenceTime}, Result：{jsonResult} .";
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