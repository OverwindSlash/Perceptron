using Perceptron.Domain.Extensions;
using Serilog;
using System.Globalization;

namespace Perceptron.Domain.Setting;

public enum BestSnapshotBy
{
    Confidence,
    Area,
    Width,
    Height
}

public class SnapshotSettings : ComponentSettings
{
    public const string DefaultSnapshotsDir = "Snapshots";
    public const bool DefaultSaveBestSnapshot = false;
    public const BestSnapshotBy DefaultBestSnapshotBy = BestSnapshotBy.Confidence;
    public const float DefaultSnapshotExpansionRatio = 1.2f;
    public const int DefaultMaxSnapshots = 10;
    public const int DefaultMinSnapshotWidth = 40;
    public const int DefaultMinSnapshotHeight = 40;
    public const int DefaultVideoClipDurationSeconds = 10;
    public const double DefaultVideoFrameRate = 25.0;

    private static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;

    public string SnapshotsDir { get; private set; } = DefaultSnapshotsDir;
    public bool SaveBestSnapshot { get; private set; } = DefaultSaveBestSnapshot;
    public BestSnapshotBy BestSnapshotBy { get; private set; } = DefaultBestSnapshotBy;
    public float SnapshotExpansionRatio { get; private set; } = DefaultSnapshotExpansionRatio;
    public int MaxSnapshots { get; private set; } = DefaultMaxSnapshots;
    public int MinSnapshotWidth { get; private set; } = DefaultMinSnapshotWidth;
    public int MinSnapshotHeight { get; private set; } = DefaultMinSnapshotHeight;
    public int VideoClipDurationSeconds { get; private set; } = DefaultVideoClipDurationSeconds;
    public double VideoFrameRate { get; private set; } = DefaultVideoFrameRate;


    public override void ParsePreferences()
    {
        SnapshotsDir = ParseSnapshotDir(Preferences);
        SaveBestSnapshot = ParseSaveBestSnapshot(Preferences);
        BestSnapshotBy = ParseBestSnapshotBy(Preferences);
        SnapshotExpansionRatio = ParseSnapshotExpansionRatio(Preferences);
        MaxSnapshots = ParseMaxSnapshots(Preferences);
        MinSnapshotWidth = ParseMinSnapshotWidth(Preferences);
        MinSnapshotHeight = ParseMinSnapshotHeight(Preferences);
        VideoClipDurationSeconds = ParseVideoClipDurationSeconds(Preferences);
        VideoFrameRate = ParseVideoFrameRate(Preferences);

        Log.Information("SnapshotsDir: {dir}", SnapshotsDir);
        Log.Information("SaveBestSnapshot: {flag}, BestSnapshotBy: {by}, MaxSnapshots: {max}, MinSnapshotWidth: {minWidth}, MinSnapshotHeight: {minHeight}",
            SaveBestSnapshot, BestSnapshotBy, MaxSnapshots, MinSnapshotWidth, MinSnapshotHeight);
        Log.Information("VideoClipDurationSeconds: {duration}, VideoFrameRate: {frameRate}",
            VideoClipDurationSeconds, VideoFrameRate);
    }

    public static string ParseSnapshotDir(Dictionary<string, string> preferences)
    {
        var path = PreferenceParser.ParseStringValue(preferences, "SnapshotsDir", DefaultSnapshotsDir);

        path.EnsureDirExistence();

        return path;
    }

    public static bool ParseSaveBestSnapshot(Dictionary<string, string> preferences)
    {
        var flag = PreferenceParser.ParseBoolValue(preferences, "SaveBestSnapshot", DefaultSaveBestSnapshot);

        return flag;
    }

    public static BestSnapshotBy ParseBestSnapshotBy(Dictionary<string, string> preferences)
    {
        var bestSnapshotByString =
            PreferenceParser.ParseStringValue(preferences, "BestSnapshotBy", DefaultBestSnapshotBy.ToString()).Trim();

        bestSnapshotByString = TextInfo.ToTitleCase(bestSnapshotByString);
        var bestSnapshotBy = Enum.TryParse<BestSnapshotBy>(bestSnapshotByString, out var result) ? result : DefaultBestSnapshotBy;

        return bestSnapshotBy;
    }

    public static float ParseSnapshotExpansionRatio(Dictionary<string, string> preferences)
    {
        var ratio = PreferenceParser.ParseFloatValue(preferences, "SnapshotExpansionRatio",
            DefaultSnapshotExpansionRatio);

        if (ratio > 1.0f)
            return ratio;

        Log.Warning("SnapshotExpansionRatio must > 1.0, Reset to default: {SnapshotExpansionRatio}", DefaultSnapshotExpansionRatio);
        return DefaultSnapshotExpansionRatio;
    }

    public static int ParseMaxSnapshots(Dictionary<string, string> preferences)
    {
        var maxSnapshots = PreferenceParser.ParseIntValue(preferences, "MaxSnapshots", DefaultMaxSnapshots);

        if (maxSnapshots > 0) 
            return maxSnapshots;

        Log.Warning("MaxSnapshots must >= 0, Reset to default: {MaxSnapshots}", DefaultMaxSnapshots);
        return DefaultMaxSnapshots;
    }

    public static int ParseMinSnapshotWidth(Dictionary<string, string> preferences)
    {
        var minSnapshotWidth = PreferenceParser.ParseIntValue(preferences, "MinSnapshotWidth", DefaultMinSnapshotWidth);

        if (minSnapshotWidth > 0) 
            return minSnapshotWidth;

        Log.Warning("MinSnapshotWidth must >= 0, Reset to default: {MinSnapshotWidth}", DefaultMinSnapshotWidth);
        return DefaultMinSnapshotWidth;
    }

    public static int ParseMinSnapshotHeight(Dictionary<string, string> preferences)
    {
        var minSnapshotHeight =
            PreferenceParser.ParseIntValue(preferences, "MinSnapshotHeight", DefaultMinSnapshotHeight);

        if (minSnapshotHeight > 0) 
            return minSnapshotHeight;

        Log.Warning("MinSnapshotHeight must >= 0, Reset to default: {MinSnapshotHeight}", DefaultMinSnapshotHeight);
        return DefaultMinSnapshotHeight;
    }

    public static int ParseVideoClipDurationSeconds(Dictionary<string, string> preferences)
    {
        var videoClipDurationSeconds =
            PreferenceParser.ParseIntValue(preferences, "VideoClipDurationSeconds", DefaultVideoClipDurationSeconds);

        if (videoClipDurationSeconds > 0) 
            return videoClipDurationSeconds;

        Log.Warning("VideoClipDurationSeconds must > 0, Reset to default: {VideoClipDurationSeconds}", DefaultVideoClipDurationSeconds);
        return DefaultVideoClipDurationSeconds;
    }

    public static double ParseVideoFrameRate(Dictionary<string, string> preferences)
    {
        double videoFrameRate =
            PreferenceParser.ParseFloatValue(preferences, "VideoFrameRate", (float)DefaultVideoFrameRate);

        if (!(videoFrameRate <= 0)) 
            return videoFrameRate;

        Log.Warning($"VideoFrameRate must > 0, Reset to default: {DefaultVideoFrameRate}");
        return DefaultVideoFrameRate;
    }

    
}