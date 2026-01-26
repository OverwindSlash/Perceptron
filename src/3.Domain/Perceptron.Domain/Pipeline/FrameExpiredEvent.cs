using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;

namespace Perceptron.Domain.Pipeline;

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