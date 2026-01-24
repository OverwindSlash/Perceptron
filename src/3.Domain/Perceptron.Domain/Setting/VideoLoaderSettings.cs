using OpenCvSharp;
using Serilog;

namespace Perceptron.Domain.Setting;

public class VideoLoaderSettings : ComponentSettings
{
    public const string DefaultSourceId = "Test-Camera";
    public const VideoCaptureAPIs DefaultVideoCaptureApi = VideoCaptureAPIs.FFMPEG;
    public const VideoAccelerationType DefaultVideoAccelerationType = OpenCvSharp.VideoAccelerationType.None;
    public const int DefaultVideoAccelerationDeviceId = 0;
    public const int DefaultVideoStride = 1;
    public const int DefaultMaxRetries = 3;
    public const int DefaultRetryDelayMs = 1000;
    public const bool DefaultLoop = false;

    
    public string SourceId { get; private set; } = DefaultSourceId;
    public VideoCaptureAPIs VideoCaptureApi { get; private set; } = DefaultVideoCaptureApi;
    public VideoAccelerationType VideoAccelerationType { get; private set; } = DefaultVideoAccelerationType;
    public int VideoAccelerationDeviceId { get; private set; } = DefaultVideoAccelerationDeviceId;
    public int VideoStride { get; private set; } =  DefaultVideoStride;
    public int MaxRetries { get; private set; } = DefaultMaxRetries;
    public int RetryDelayMs { get; private set; } = DefaultRetryDelayMs;
    public bool Loop { get; private set; } = DefaultLoop;
    
    public override void ParsePreferences()
    {
        SourceId = ParseSourceId(Preferences);
        VideoCaptureApi = ParseVideoCaptureApi(Preferences);
        VideoAccelerationType = ParseVideoAccelerationType(Preferences);
        VideoAccelerationDeviceId = ParseVideoAccelerationDeviceId(Preferences);
        VideoStride = ParseVideoStride(Preferences);
        MaxRetries = ParseMaxRetries(Preferences);
        RetryDelayMs = ParseRetryDelayMs(Preferences);        
        Loop = ParseLoop(Preferences);
    }

    public static string ParseSourceId(Dictionary<string, string>? preferences)
    {
        var value = PreferenceParser.ParseStringValue(preferences, "SourceId", DefaultSourceId);

        if (!string.IsNullOrEmpty(value))
            return value;
        
        Log.Warning("Source Id is empty, reset to default: {SourceId}", DefaultSourceId);
        return DefaultSourceId;
    }

    public static VideoCaptureAPIs ParseVideoCaptureApi(Dictionary<string, string>? preferences)
    {
        var value = PreferenceParser.ParseStringValue(preferences, "VideoCaptureAPI", "FFMPEG");

        VideoCaptureAPIs videoCaptureApi;
        if (Enum.TryParse(value, out videoCaptureApi))
        {
            Log.Information("Using VideoCapture API: {VideoCaptureApIs}.", videoCaptureApi);
            return videoCaptureApi;
        }
        else
        {
            Log.Warning("Failed to parse VideoCapture API from preferences. Using default API: FFMPEG.");
            return DefaultVideoCaptureApi;
        }
    }

    public static VideoAccelerationType ParseVideoAccelerationType(Dictionary<string, string>? preferences)
    {
        var value =  PreferenceParser.ParseStringValue(preferences, "VideoAccelerationType", "None");
        
        VideoAccelerationType videoAccelerationType;
        if (Enum.TryParse(value, out videoAccelerationType))
        {
            Log.Information("Using Video Acceleration Type: {VideoAccelerationType}", videoAccelerationType);
            return videoAccelerationType;
        }
        else
        {
            Log.Warning("Failed to parse Video Acceleration Type from preferences. Using default: None.");
            return DefaultVideoAccelerationType;
        }
    }

    public static int ParseVideoAccelerationDeviceId(Dictionary<string, string>? preferences)
    {
        var id = PreferenceParser.ParseIntValue(preferences, "VideoAccelerationDeviceId", DefaultVideoAccelerationDeviceId);

        if (id >= 0)
            return id;

        Log.Warning("Video acceleration device ID must >= 0, reset to default: {VideoAccelerationDeviceId}", DefaultVideoAccelerationDeviceId);
        return DefaultVideoAccelerationDeviceId;
    }
    
    public static int ParseVideoStride(Dictionary<string, string>? preferences)
    {
        var stride = PreferenceParser.ParseIntValue(preferences, "VideoStride", DefaultVideoStride);

        if (stride > 0) 
            return stride;

        Log.Warning("Video stride must >= 0, reset to default: {VideoStride}", DefaultVideoStride);
        return DefaultVideoStride;
    }

    public static int ParseMaxRetries(Dictionary<string, string>? preferences)
    {
        var maxRetries = PreferenceParser.ParseIntValue(preferences, "MaxRetries", DefaultMaxRetries);

        if (maxRetries > 0)
            return maxRetries;

        Log.Warning("Max retries must >= 0, reset to default: {MaxRetries}", DefaultMaxRetries);
        return DefaultMaxRetries;
    }

    public static int ParseRetryDelayMs(Dictionary<string, string>? preferences)
    {
        var retryDelayMs = PreferenceParser.ParseIntValue(preferences, "RetryDelayMs", DefaultRetryDelayMs);

        if (retryDelayMs > 0)
            return retryDelayMs;

        Log.Warning("Retry delay ms must >= 0, reset to default: {RetryDelayMs}", DefaultRetryDelayMs);
        return DefaultRetryDelayMs;
    }

    public static bool ParseLoop(Dictionary<string, string>? preferences)
    {
        return PreferenceParser.ParseBoolValue(preferences, "Loop", DefaultLoop);
    }
}