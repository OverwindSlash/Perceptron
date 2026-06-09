using OpenCvSharp;
using Algorithm.Common.LLM;
using Perceptron.Domain.Event;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Algorithm.Common.Event;

public class LLMInferenceResultEvent : DomainEvent
{
    public new const string EventType = "LLM Inference Result Event";

    public string ModelName { get; }
    public TimeSpan InferenceTime { get; }

    public string DetectedObjectId { get; set; }
    public float Confidence { get; set; }
    public long FrameId { get; set; }
    public long OffsetMilliSec { get; set; }
    public DateTime UtcTimeStamp { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string RequesterAlgorithmName { get; set; } = string.Empty;
    public string? CandidateEventId { get; set; }
    public string? TrackKey { get; set; }
    public LLMAnalysisScope Scope { get; set; } = LLMAnalysisScope.Frame;
    public LLMQueuePolicy QueuePolicy { get; set; } = LLMQueuePolicy.LatestPerSource;
    public bool IsSuccess { get; set; } = true;
    public bool IsExpiredResult { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public DateTime ExpireAtUtc { get; set; }

    public string JsonResult { get; set; }

    [JsonIgnore]
    public Mat? Snapshot { get; set; }

    public LLMInferenceResultEvent(string sourceId, string eventType, string eventName, string algorithmName,
        string detectedObjectId, float confidence, string modelName, TimeSpan inferenceTime, string jsonResult)
        : base(sourceId, eventType, eventName, algorithmName)
    {
        DetectedObjectId = detectedObjectId;
        Confidence = confidence;
        ModelName = modelName;
        InferenceTime = inferenceTime;
        JsonResult = jsonResult;
        Message = $"LLM '{modelName}', Elapse: {inferenceTime}, Result: {jsonResult}.";
    }

    public static LLMInferenceResultEvent FromAnalysisResult(LLMAnalysisResult result, string eventName, float confidence = 0)
    {
        var inferenceEvent = new LLMInferenceResultEvent(
            sourceId: result.SourceId,
            eventType: EventType,
            eventName: eventName,
            algorithmName: result.RequesterAlgorithmName,
            detectedObjectId: result.ObjectId ?? string.Empty,
            confidence: confidence,
            modelName: result.ModelName,
            inferenceTime: result.InferenceTime,
            jsonResult: result.JsonResult)
        {
            RequestId = result.RequestId,
            RequesterAlgorithmName = result.RequesterAlgorithmName,
            CandidateEventId = result.CandidateEventId,
            FrameId = result.FrameId,
            OffsetMilliSec = result.OffsetMilliSec,
            UtcTimeStamp = result.UtcTimeStamp,
            Scope = result.Scope,
            IsSuccess = result.IsSuccess,
            IsExpiredResult = result.IsExpiredResult,
            ErrorCode = result.ErrorCode,
            RequestedAtUtc = result.RequestedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc
        };

        return inferenceEvent;
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
