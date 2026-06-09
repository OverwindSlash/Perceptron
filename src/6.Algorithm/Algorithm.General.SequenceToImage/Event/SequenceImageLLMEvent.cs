using Algorithm.Common;
using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.General.SequenceToImage.Event;

public class SequenceImageLLMEvent : DomainEvent, IAnnotatedAlgorithmEvent
{
    public new const string EventType = "Sequence Image LLM Result Event";

    public string RequestId { get; }
    public string SequenceId { get; }
    public string Layout { get; }
    public int SequenceLength { get; }
    public long StartFrameId { get; }
    public long EndFrameId { get; }
    public long StartOffsetMilliSec { get; }
    public long EndOffsetMilliSec { get; }
    public List<long> FrameIds { get; }
    public List<SequenceImageFrameInfo> Frames { get; }
    public string ModelName { get; }
    public TimeSpan InferenceTime { get; }
    public bool IsSuccess { get; }
    public bool IsExpiredResult { get; }
    public string? ErrorCode { get; }
    public string LLMJsonResult { get; set; }
    public string Annotations { get; set; } = string.Empty;

    public SequenceImageLLMEvent(
        string sourceId,
        string eventName,
        string algorithmName,
        string requestId,
        string sequenceId,
        string layout,
        List<SequenceImageFrameInfo> frames,
        string modelName,
        TimeSpan inferenceTime,
        bool isSuccess,
        bool isExpiredResult,
        string? errorCode,
        string llmJsonResult)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        RequestId = requestId;
        SequenceId = sequenceId;
        Layout = layout;
        Frames = frames;
        SequenceLength = frames.Count;
        FrameIds = frames.Select(frame => frame.FrameId).ToList();
        StartFrameId = frames.FirstOrDefault()?.FrameId ?? 0;
        EndFrameId = frames.LastOrDefault()?.FrameId ?? 0;
        StartOffsetMilliSec = frames.FirstOrDefault()?.OffsetMilliSec ?? 0;
        EndOffsetMilliSec = frames.LastOrDefault()?.OffsetMilliSec ?? 0;
        ModelName = modelName;
        InferenceTime = inferenceTime;
        IsSuccess = isSuccess;
        IsExpiredResult = isExpiredResult;
        ErrorCode = errorCode;
        LLMJsonResult = llmJsonResult;
        Message = $"Sequence image {SequenceId} ({StartFrameId}-{EndFrameId}, {Layout}, {SequenceLength} frames) was analyzed by LLM '{ModelName}'.";
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
