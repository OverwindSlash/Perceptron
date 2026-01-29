using Perceptron.Domain.DataStructure;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.FrameBuffer;

public interface IVideoFrameBuffer : IConcurrentBoundedQueue<Frame>
{
    public string BufferName { get; }
    public int BufferSize { get; }

    /// <summary>
    /// Gets or sets the working mode of the buffer.
    /// </summary>
    FrameBufferMode Mode { get; }
    
    void PushFrame(Frame frame);
    Frame RetrieveFrame();

    void RegisterFrameDropHandler(Action<Frame>? frameDropHandler);
    void UnregisterFrameDropHandler(Action<Frame>? frameDropHandler);
}