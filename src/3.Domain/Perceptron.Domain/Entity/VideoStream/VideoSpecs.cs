namespace Perceptron.Domain.Entity.VideoStream;

public class VideoSpecs
{
    public int Width { get; }
    public int Height { get; }
    public double Fps { get; private set; }
    public int FrameCount { get; private set; }
    public string Codec { get; private set; }
    public string PixelFormat { get; private set; }
    public int Rotation { get; private set; }

    public VideoSpecs(int width, int height, double fps, int frameCount, string codec = "unknown", string pixelFormat = "unknown", int rotation = 0)
    {
        Width = width;
        Height = height;
        Fps = fps;
        FrameCount = frameCount;
        Codec = codec;
        PixelFormat = pixelFormat;
        Rotation = rotation;
    }

    // 缺省的VideoSpecs对象
    public static VideoSpecs Default => new VideoSpecs(0, 0, 0, 0);
}