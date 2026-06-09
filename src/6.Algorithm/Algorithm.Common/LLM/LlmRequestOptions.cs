namespace Algorithm.Common.LLM;

public sealed record LlmRequestOptions
{
    public required LLMAnalysisScope Scope { get; init; }
    public required LLMQueuePolicy QueuePolicy { get; init; }
    public string? RequestId { get; init; }
    public string? CandidateEventId { get; init; }
    public DateTime? ExpireAtUtc { get; init; }
    public string? Prompt { get; init; }
    public byte[]? ImageJpeg { get; init; }
}
