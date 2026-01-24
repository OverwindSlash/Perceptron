namespace Perceptron.Domain.Abstraction.FrameBuffer;

public enum FrameBufferMode
{
    /// <summary>
    /// Blocks and waits for a frame when the buffer is empty.
    /// </summary>
    BlockingWait,

    /// <summary>
    /// Returns a default/blank frame immediately when the buffer is empty.
    /// </summary>
    ReturnBlankFrame
}
