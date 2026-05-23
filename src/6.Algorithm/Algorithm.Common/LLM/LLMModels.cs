using Perceptron.Domain.Entity.ObjectDetection;

namespace Algorithm.Common.LLM;

public enum LLMAnalysisScope
{
    Frame,
    Object
}

public enum LLMQueuePolicy
{
    LatestPerSource,
    LatestBestPerObject,
    EventAnchored,
    DropOldest
}

public sealed record DetectedObjectEvidence(
    string ObjectId,
    string LocalId,
    string Label,
    int LabelId,
    int TrackingId,
    float Confidence,
    int X,
    int Y,
    int Width,
    int Height)
{
    public static DetectedObjectEvidence FromDetectedObject(DetectedObject detectedObject)
    {
        return new DetectedObjectEvidence(
            detectedObject.Id,
            detectedObject.LocalId,
            detectedObject.Label,
            detectedObject.LabelId,
            detectedObject.TrackingId,
            detectedObject.Confidence,
            detectedObject.X,
            detectedObject.Y,
            detectedObject.Width,
            detectedObject.Height);
    }
}

public sealed record PendingLLMEvidence(
    string RequestId,
    string? CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    LLMAnalysisScope Scope,
    byte[]? FrameJpeg,
    byte[]? ObjectCropJpeg,
    IReadOnlyList<DetectedObjectEvidence> Objects,
    string Prompt,
    DateTime ExpireAtUtc);

public sealed record FrameEvidence(
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    byte[] FrameJpeg,
    IReadOnlyList<DetectedObjectEvidence> Objects,
    string? AnnotationJson);

public sealed record LLMAnalysisRequest(
    string RequestId,
    string RequesterAlgorithmName,
    string? CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    string? ObjectId,
    string? ObjectLocalId,
    string? TrackKey,
    LLMAnalysisScope Scope,
    LLMQueuePolicy QueuePolicy,
    string Prompt,
    byte[] ImageJpeg,
    float? DetectorConfidence,
    double? EvidenceQualityScore,
    DateTime CreatedAtUtc,
    DateTime ExpireAtUtc);

public sealed record LLMAnalysisResult(
    string RequestId,
    string RequesterAlgorithmName,
    string? CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    string? ObjectId,
    LLMAnalysisScope Scope,
    string ModelName,
    TimeSpan InferenceTime,
    string JsonResult,
    bool IsSuccess,
    bool IsExpiredResult,
    string? ErrorCode,
    DateTime RequestedAtUtc,
    DateTime CompletedAtUtc);
