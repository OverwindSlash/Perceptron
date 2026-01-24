using Perceptron.Domain.Abstraction.FrameBuffer;
using Serilog;

namespace Perceptron.Domain.Setting;

public class FrameBufferSettings : ComponentSettings
{
    public const int DefaultBufferSize = 300;
    public readonly FrameBufferMode DefaultFrameBufferModeMode  = FrameBufferMode.BlockingWait;

    public int BufferSize { get; private set; } = DefaultBufferSize;
    public FrameBufferMode Mode { get; private set; } = FrameBufferMode.BlockingWait;

    public override void ParsePreferences()
    {
        BufferSize = ParseBufferSize(Preferences);
        Mode = ParseFrameBufferMode(Preferences);
    }

    public static int ParseBufferSize(Dictionary<string, string>? preferences)
    {
        var size = PreferenceParser.ParseIntValue(preferences, "BufferSize", DefaultBufferSize);

        if (size >= 0)
            return size;

        Log.Warning("Video frame buffer size must >= 0, reset to default: {BufferSize}", DefaultBufferSize);
        return DefaultBufferSize;
    }

    public static FrameBufferMode ParseFrameBufferMode(Dictionary<string, string>? preferences)
    {
        var modeStr = PreferenceParser.ParseStringValue(preferences, "Mode", "BlockingWait");

        if (Enum.TryParse(modeStr, true, out FrameBufferMode mode))
        {
            Log.Information("Using Frame Buffer Mode: {FrameBufferMode}.", mode);
            return mode;
        }
        else
        {
            Log.Warning("Invalid Frame Buffer Mode: {Mode}, reset to default: {DefaultFrameBufferMode}", modeStr, FrameBufferMode.BlockingWait);
            return FrameBufferMode.BlockingWait;
        }
    }
}