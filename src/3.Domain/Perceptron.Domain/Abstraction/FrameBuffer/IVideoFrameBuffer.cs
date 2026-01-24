using Perceptron.Domain.DataStructure;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.FrameBuffer;

public interface IVideoFrameBuffer : IConcurrentBoundedQueue<Frame>
{
    public int BufferSize { get; }
    
    void PushFrame(Frame frame);
    Frame RetrieveFrame();

    void RegisterFrameDropHandler(Action<Frame>? frameDropHandler);
    void UnregisterFrameDropHandler(Action<Frame>? frameDropHandler);
}