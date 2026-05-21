using OpenCvSharp;
using Perceptron.Domain.Event;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Algorithm.Common.Event;

public class LLMInferenceResultEvent : DomainEvent
{
    public const string EventType = "LLM Inference Result Event";

    public string ModelName { get; }
    public TimeSpan InferenceTime { get; }

    public string DetectedObjectId { get; set; }
    public float Confidence { get; set; }
    public long FrameId { get; set; }
    public DateTime UtcTimeStamp { get; set; }

    public string JsonResult { get; set; }

    [JsonIgnore]
    public Mat? Snapshot { get; set; }

    public LLMInferenceResultEvent(string sourceId, string eventType, string eventName, string algorithmName,
        string detectedObjectId, float confidence, string modelName, TimeSpan inferenceTime, string jsonResult)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        DetectedObjectId = detectedObjectId;
        Confidence = confidence;
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
