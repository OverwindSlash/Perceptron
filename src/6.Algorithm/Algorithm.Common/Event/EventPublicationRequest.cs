using OpenCvSharp;
using Perceptron.Domain.Event;

namespace Algorithm.Common;

public sealed record EventPublicationRequest<TEvent>
    where TEvent : DomainEvent
{
    public required TEvent Event { get; init; }
    public string AnnotationJson { get; init; } = string.Empty;
    public Func<Mat?>? CloneSnapshot { get; init; }
    public long? FrameId { get; init; }
    public string FilePrefix { get; init; } = "event";
    public string? RelativeDirectory { get; init; }
    public string? StableArtifactId { get; init; }
    public Action<TEvent>? PublishInProcess { get; init; }
    public bool SaveSnapshot { get; init; }
    public bool SaveVideoClip { get; init; }
}
