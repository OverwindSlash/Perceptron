using Perceptron.Domain.Abstraction.FrameBuffer;
using Serilog;

namespace Perceptron.Domain.Setting;

public class FrameBufferSettings : ComponentSettings
{
    public const string DefaultBufferName = "video-buffer";
    public const int DefaultBufferSize = 300;
    public readonly FrameBufferMode DefaultFrameBufferModeMode  = FrameBufferMode.BlockingWait;

    public string BufferName { get; private set; } = DefaultBufferName;
    public int BufferSize { get; private set; } = DefaultBufferSize;
    public FrameBufferMode Mode { get; private set; } = FrameBufferMode.BlockingWait;

    public override void ParsePreferences()
    {
        BufferName = ParseBufferName(Preferences);
        BufferSize = ParseBufferSize(Preferences);
        Mode = ParseFrameBufferMode(Preferences);
    }

    public static string ParseBufferName(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseStringValue(preferences, "BufferName", DefaultBufferName);

        if (!string.IsNullOrWhiteSpace(value))
            return value;

        Log.Error("Video frame buffer name can not be empty");
        throw new ArgumentException("Video frame buffer name can not be empty.");
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