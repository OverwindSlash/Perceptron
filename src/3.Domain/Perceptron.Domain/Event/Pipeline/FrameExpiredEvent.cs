using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Event.Pipeline;

public class FrameExpiredEvent : EventBase
{
    public long FrameId { get; }

    public FrameExpiredEvent(long frameId)
    {
        FrameId = frameId;
    }

    public FrameExpiredEvent(Frame frame)
    {
        FrameId = frame.FrameId;
    }
}