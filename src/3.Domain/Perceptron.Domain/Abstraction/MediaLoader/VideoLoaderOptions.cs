using OpenCvSharp;

namespace Perceptron.Domain.Abstraction.MediaLoader;

public class VideoLoaderOptions
{
    public VideoCaptureAPIs VideoCaptureApi { get; set; } = VideoCaptureAPIs.FFMPEG; // e.g., FFMPEG, GSTREAMER
    public VideoAccelerationType AccelerationType { get; set; } = VideoAccelerationType.None; // e.g., CUDA, D3D11
    public int VideoAccelerationDeviceId { get; set; } = 0;
    
    public int VideoStride { get; set; } = 1;
    public bool Loop { get; set; } = false;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
}
