using Serilog;

namespace Perceptron.Domain.Setting;

public class TrackerSettings : ComponentSettings
{
    public const float DefaultIouThreshold = 0.3f;
    public const int DefaultMaxMisses = 3;
    public const float DefaultAppearanceWeight = 0.5f;
    public const int DefaultFramesToAppearanceSmooth = 100;
    public const float DefaultSmoothAppearanceWeight = 0.9f;
    public const int DefaultMinStreak = 3;
    
    public float IouThreshold { get; private set; }
    public int MaxMisses { get; private set; }
    public float AppearanceWeight { get; private set; }
    public int FramesToAppearanceSmooth { get; private set; }
    public float SmoothAppearanceWeight { get; private set; }
    public int MinStreak { get; private set; }

    public override void ParsePreferences()
    {
        IouThreshold = ParseIouThreshold(Preferences);
        MaxMisses = ParseMaxMisses(Preferences);
        AppearanceWeight = ParseAppearanceWeight(Preferences);
        FramesToAppearanceSmooth = ParseFramesToAppearanceSmooth(Preferences);
        SmoothAppearanceWeight = ParseSmoothAppearanceWeight(Preferences);
        MinStreak = ParseMinStreak(Preferences);

        Log.Information("IouThreshold: {iou}, MaxMisses: {misses}", IouThreshold, MaxMisses);
        // Log.Information("AppearanceWeight: {appWeight}, FramesToAppearanceSmooth: {frames}, SmoothAppearanceWeight: {smoothWeight}, MinStreak: {streak}",
        //     AppearanceWeight, FramesToAppearanceSmooth, SmoothAppearanceWeight, MinStreak);
    }

    public static float ParseIouThreshold(Dictionary<string, string> preferences)
    {
        var iouThreshold = PreferenceParser.ParseFloatValue(preferences, "IouThreshold", DefaultIouThreshold);

        if (iouThreshold is >= 0 and <= 1) 
            return iouThreshold;

        Log.Warning($"IoU threshold must be in range [0, 1], reset to default: {DefaultIouThreshold}");
        return DefaultIouThreshold;
    }

    public static int ParseMaxMisses(Dictionary<string, string> preferences)
    {
        var maxMisses = PreferenceParser.ParseIntValue(preferences, "MaxMisses", DefaultMaxMisses);

        if (maxMisses >= 0) 
            return maxMisses;

        Log.Warning("Max misses must >= 0, reset to default: {MaxMisses}", DefaultMaxMisses);
        return DefaultMaxMisses;
    }

    public static float ParseAppearanceWeight(Dictionary<string, string> preferences)
    {
        var appearanceWeight =
            PreferenceParser.ParseFloatValue(preferences, "AppearanceWeight", DefaultAppearanceWeight);

        if (!(appearanceWeight < 0) && !(appearanceWeight > 1)) 
            return appearanceWeight;

        Log.Warning("Appearance weight must be in range [0, 1], reset to default: {AppearanceWeight}", DefaultAppearanceWeight);
        return DefaultAppearanceWeight;
    }

    public static int ParseFramesToAppearanceSmooth(Dictionary<string, string> preferences)
    {
        var framesToAppearanceSmooth =
            PreferenceParser.ParseIntValue(preferences, "FramesToAppearanceSmooth", DefaultFramesToAppearanceSmooth);

        if (framesToAppearanceSmooth > 0) 
            return framesToAppearanceSmooth;

        Log.Warning("Frames to appearance smooth must > 0, reset to default: {FramesToAppearanceSmooth}", DefaultFramesToAppearanceSmooth);
        return DefaultFramesToAppearanceSmooth;
    }

    public static float ParseSmoothAppearanceWeight(Dictionary<string, string> preferences)
    {
        var smoothAppearanceWeight =
            PreferenceParser.ParseFloatValue(preferences, "SmoothAppearanceWeight", DefaultSmoothAppearanceWeight);

        if (!(smoothAppearanceWeight < 0) && !(smoothAppearanceWeight > 1)) 
            return smoothAppearanceWeight;

        Log.Warning("Smooth appearance weight must be in range [0, 1], reset to default: {SmoothAppearanceWeight}", DefaultSmoothAppearanceWeight);
        return DefaultSmoothAppearanceWeight;
    }

    public static int ParseMinStreak(Dictionary<string, string> preferences)
    {
        var minStreak = PreferenceParser.ParseIntValue(preferences, "MinStreak", DefaultMinStreak);

        if (minStreak >= 0) 
            return minStreak;

        Log.Warning("Min streak must >= 0, reset to default: {MinStreak}", DefaultMinStreak);
        return DefaultMinStreak;
    }
}