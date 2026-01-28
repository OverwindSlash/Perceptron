using Perceptron.Domain.Abstraction.FrameBuffer;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.MediaLoader;

public interface IVideoLoader : IDisposable
{
    public string SourceId { get; }
    public string VideoUri { get; }
    public VideoSpecs Specs { get; }
    public int VideoStride { get; }

    public VideoLoaderOptions Options { get; }
    public VideoLoaderState State { get; }
    
    void AttachBuffer(IVideoFrameBuffer buffer);
    
    bool Open(string uri);
    void Close();

    void Play(bool debugMode = false, int debugFrameCount = 0);
    void Pause();
    void Resume();
    void Stop();
    
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    
    bool Seek(long frameId);
    bool Seek(TimeSpan timestamp);

    void SetFrameCallback(Action<Frame>? frameHandler);
    void UnsetFrameCallback(Action<Frame>? frameHandler);
}