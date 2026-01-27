using OpenCvSharp;
using Serilog;

namespace Perceptron.Domain.Setting;

public class DetectorSettings : ComponentSettings
{
    public const string DefaultModelPath = "yolo11m.onnx";
    public const string DefaultModelConfig = "yolov11.json";
    public const int DefaultClassNum = 80;
    public const string DefaultExecutionProvider = "cpu";
    public const int DefaultDeviceId = 0;
    public const float DefaultConfThresh = 0.25f;
    public static readonly List<string> DefaultTargetTypes = [];
    public const int DefaultDetectionStride = 1;
    public const bool DefaultFilterSmallObject = false;
    public const int DefaultMinBboxWidth = 40;
    public const int DefaultMinBboxHeight = 40;
    public const bool DefaultFilterLargeObject = false;
    public const int DefaultMaxBboxWidth = 300;
    public const int DefaultMaxBboxHeight = 300;
    public const bool DefaultRegionDetectionEnabled = false;
    public static readonly Rect DefaultDetectionRegion = new (0, 0, 1, 1);
    public const bool DefaultTileDetectionEnabled = false;
    public static readonly Tuple<int, int> DefaultTileSize = new (1, 1);
    public const int DefaultMaxStitchGapPixel = 2;
    public const float DefaultMinVerticalOverlapRatio = 0.9f;
    public const bool DefaultWillSuppressInnerSameObject = false;
    public const float DefaultInnerObjectOverlapRatio = 0.8f;
    public const bool DefaultWillMapObjectTypes = false;
    public static readonly List<string> DefaultSourceObjectTypeNames = [];
    public const string DefaultTargetObjectTypeName = "";
    public static readonly List<string> DefaultNames = [];

    public string ModelPath { get; private set; } = DefaultModelPath;
    public string ModelConfig { get; private set; } = DefaultModelConfig;
    public string ExecutionProvider { get; private set; } = DefaultExecutionProvider;
    public int DeviceId { get; private set; }
    public int ClassNum { get; private set; }
    public float ConfThresh { get; private set; }
    public List<string> TargetTypes { get; private set; } = DefaultTargetTypes;
    public int DetectionStride { get; private set; }
    public bool FilterSmallObject { get; private set; }
    public int MinBboxWidth { get; private set; }
    public int MinBboxHeight { get; private set; }
    public bool FilterLargeObject { get; private set; }
    public int MaxBboxWidth { get; private set; }
    public int MaxBboxHeight { get; private set; }
    public bool RegionDetectionEnabled { get; private set; }
    public Rect DetectionRegion { get; private set; }
    public bool TileDetectionEnabled { get; private set; }
    public Tuple<int, int> TileDetectionSize { get; private set; } = DefaultTileSize;
    public int MaxStitchGapPixel { get; private set; }
    public float MinVerticalOverlapRatio { get; private set; }
    public bool WillSuppressInnerSameObject { get; private set; }
    public float InnerObjectOverlapRatio { get; private set; }
    public bool WillMapObjectTypes { get; private set; }
    public List<string> SourceObjectTypeNames { get; private set; } = DefaultSourceObjectTypeNames;
    public string TargetObjectTypeName { get; private set; } = DefaultTargetObjectTypeName;
    public List<string> Names { get; private set; } = DefaultNames;


    public override void ParsePreferences()
    {
        ModelPath = ParseModelPath(Preferences);
        ModelConfig = ParseModelConfig(Preferences);
        ClassNum = ParseClassNum(Preferences);
        ExecutionProvider = ParseExecutionProvider(Preferences);
        DeviceId = ParseDeviceId(Preferences);
        ConfThresh = ParseConfThresh(Preferences);
        TargetTypes = ParseTargetTypes(Preferences);
        DetectionStride = ParseDetectionStride(Preferences);
        FilterSmallObject = ParseFilterSmallObject(Preferences);
        MinBboxWidth = ParseMinBboxWidth(Preferences);
        MinBboxHeight = ParseMinBboxHeight(Preferences);
        FilterLargeObject = ParseFilterLargeObject(Preferences);
        MaxBboxWidth = ParseMaxBboxWidth(Preferences);
        MaxBboxHeight = ParseMaxBboxHeight(Preferences);
        RegionDetectionEnabled = ParseRegionDetectionEnabled(Preferences);
        DetectionRegion = ParseDetectionRegion(Preferences);
        TileDetectionEnabled = ParseTileDetectionEnabled(Preferences);
        TileDetectionSize = ParseTileDetectionSize(Preferences);
        MaxStitchGapPixel = ParseMaxStitchGapPixel(Preferences);
        MinVerticalOverlapRatio = ParseMinVerticalOverlapRatio(Preferences);
        WillSuppressInnerSameObject = ParseWillSuppressInnerSameObject(Preferences);
        InnerObjectOverlapRatio = ParseInnerObjectOverlapRatio(Preferences);
        WillMapObjectTypes = ParseWillMapObjectTypes(Preferences);
        SourceObjectTypeNames = ParseSourceObjectTypeNames(Preferences);
        TargetObjectTypeName = ParseDestinationObjectTypeName(Preferences);
        Names = ParseNames(Preferences);
    }

    public static string ParseModelPath(Dictionary<string, string> preferences)
    {
        var modelPath = PreferenceParser.ParseStringValue(preferences,"ModelPath", DefaultModelPath);

        if (modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
            modelPath.EndsWith(".om", StringComparison.OrdinalIgnoreCase)) 
            return modelPath;
        
        Log.Warning($"ModelPath should be an ONNX or OM file. Reset to default: {DefaultModelPath}");
        return DefaultModelPath;
    }

    public static string ParseModelConfig(Dictionary<string, string> preferences)
    {
        var modelConfig = PreferenceParser.ParseStringValue(preferences, "ModelConfig", DefaultModelConfig);

        if (modelConfig.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) 
            return modelConfig;

        Log.Warning($"ModelConfig should be a JSON file. Reset to default: {DefaultModelConfig}");
        return DefaultModelConfig;
    }


    public static int ParseClassNum(Dictionary<string, string> preferences)
    {
        var classNum = PreferenceParser.ParseIntValue(preferences, "ClassNum", DefaultClassNum);

        if (classNum > 0) 
            return classNum;

        Log.Warning("ClassNum must > 0. Reset to default: {ClassNum}", DefaultClassNum);
        return DefaultClassNum;
    }

    public static string ParseExecutionProvider(Dictionary<string, string> preferences)
    {
        var provider = PreferenceParser.ParseStringValue(preferences, "ExecutionProvider", DefaultExecutionProvider);

        return provider;
    }

    public static int ParseDeviceId(Dictionary<string, string> preferences)
    {
        var deviceId = PreferenceParser.ParseIntValue(preferences, "DeviceId", DefaultDeviceId);

        if (deviceId >= 0) 
            return deviceId;

        Log.Warning("DeviceId must >= 0. Reset to default: {DeviceId}", DefaultDeviceId);
        return DefaultDeviceId;
    }

    public static float ParseConfThresh(Dictionary<string, string> preferences)
    {
        var thresh = PreferenceParser.ParseFloatValue(preferences, "ConfThresh", DefaultConfThresh);

        if (thresh is >= 0 and <= 1) 
            return thresh;

        Log.Warning("ConfThresh must between 0 and 1. Reset to default:{ConfThresh}", DefaultConfThresh);
        return DefaultConfThresh;
    }

    public static List<string> ParseTargetTypes(Dictionary<string, string> preferences)
    {
        var types = PreferenceParser.ParseStringListValue(preferences, "TargetTypes", DefaultTargetTypes);

        return types;
    }

    public static int ParseDetectionStride(Dictionary<string, string> preferences)
    {
        var stride = PreferenceParser.ParseIntValue(preferences, "DetectionStride", DefaultDetectionStride);

        if (stride > 0) 
            return stride;

        Log.Warning("Detection stride must > 0. Reset to default: {DetectionStride}", DefaultDetectionStride);
        return DefaultDetectionStride;
    }

    public static bool ParseFilterSmallObject(Dictionary<string, string> preferences)
    {
        var filter = PreferenceParser.ParseBoolValue(preferences, "FilterSmallObject", DefaultFilterSmallObject);

        return filter;
    }
    
    public static int ParseMinBboxWidth(Dictionary<string, string> preferences)
    {
        var width = PreferenceParser.ParseIntValue(preferences, "MinBboxWidth", DefaultMinBboxWidth);

        if (width >= 0) 
            return width;

        Log.Warning("MinBboxWidth must >= 0. Reset to default: {MinBboxWidth}", DefaultMinBboxWidth);
        return DefaultMinBboxWidth;
    }

    public static int ParseMinBboxHeight(Dictionary<string, string> preferences)
    {
        var height = PreferenceParser.ParseIntValue(preferences, "MinBboxHeight", DefaultMinBboxHeight);

        if (height >= 0) 
            return height;

        Log.Warning("MinBboxHeight must >= 0. Reset to default: {MinBboxHeight}", DefaultMinBboxHeight);
        return DefaultMinBboxHeight;
    }

    public static bool ParseFilterLargeObject(Dictionary<string, string> preferences)
    {
        var filter = PreferenceParser.ParseBoolValue(preferences, "FilterLargeObject", DefaultFilterLargeObject);

        return filter;
    }

    public static int ParseMaxBboxWidth(Dictionary<string, string> preferences)
    {
        var width = PreferenceParser.ParseIntValue(preferences, "MaxBboxWidth", DefaultMaxBboxWidth);

        if (width >= 0) 
            return width;

        Log.Warning("MaxBboxWidth must >= 0. Reset to default: {MaxBboxWidth}", DefaultMaxBboxWidth);
        return DefaultMaxBboxWidth;
    }

    public static int ParseMaxBboxHeight(Dictionary<string, string> preferences)
    {
        var height = PreferenceParser.ParseIntValue(preferences, "MaxBboxHeight", DefaultMaxBboxHeight);

        if (height >= 0) 
            return height;

        Log.Warning("MaxBboxHeight must >= 0. Reset to default: {MaxBboxHeight}", DefaultMaxBboxHeight);
        return DefaultMaxBboxHeight;
    }

    public static bool ParseRegionDetectionEnabled(Dictionary<string, string> preferences)
    {
        var enabled =
            PreferenceParser.ParseBoolValue(preferences, "RegionDetectionEnabled", DefaultRegionDetectionEnabled);

        return enabled;
    }

    public static Rect ParseDetectionRegion(Dictionary<string, string> preferences)
    {
        var region = PreferenceParser.ParseRectValue(preferences, "DetectionRegion", DefaultDetectionRegion);

        return region;
    }

    public static bool ParseTileDetectionEnabled(Dictionary<string, string> preferences)
    {
        var enabled =
            PreferenceParser.ParseBoolValue(preferences, "TileDetectionEnabled", DefaultTileDetectionEnabled);

        return enabled;
    }

    public static Tuple<int, int> ParseTileDetectionSize(Dictionary<string, string> preferences)
    {
        var tileSize =
            PreferenceParser.ParseIntTupleValue(preferences, "TileDetectionSize", DefaultTileSize);
        
        return tileSize;
    }

    public static int ParseMaxStitchGapPixel(Dictionary<string, string> preferences)
    {
        var gap = PreferenceParser.ParseIntValue(preferences, "MaxStitchGapPixel", DefaultMaxStitchGapPixel);

        if (gap >= 0) 
            return gap;

        Log.Warning("MaxStitchGapPixel must >= 0. Reset to default:{MaxStitchGapPixel}", DefaultMaxStitchGapPixel);
        return DefaultMaxStitchGapPixel;
    }

    public static float ParseMinVerticalOverlapRatio(Dictionary<string, string> preferences)
    {
        var ratio =
            PreferenceParser.ParseFloatValue(preferences, "MinVerticalOverlapRatio", DefaultMinVerticalOverlapRatio);

        if (ratio is >= 0 and <= 1) 
            return ratio;

        Log.Warning("MinVerticalOverlapRatio must between 0 and 1. Reset to default:{MinVerticalOverlapRatio}", DefaultMinVerticalOverlapRatio);
        return DefaultMinVerticalOverlapRatio;
    }

    public static bool ParseWillSuppressInnerSameObject(Dictionary<string, string> preferences)
    {
        var willSuppress = PreferenceParser.ParseBoolValue(preferences, "WillSuppressInnerSameObject", DefaultWillSuppressInnerSameObject);

        return willSuppress;
    }

    public static float ParseInnerObjectOverlapRatio(Dictionary<string, string> preferences)
    {
        var ratio = PreferenceParser.ParseFloatValue(preferences, "InnerObjectOverlapRatio", DefaultInnerObjectOverlapRatio);

        if (ratio is >= 0 and <= 1) 
            return ratio;

        Log.Warning("InnerObjectOverlapRatio must between 0 and 1. Reset to default: {InnerObjectOverlapRatio}", DefaultInnerObjectOverlapRatio);
        return DefaultInnerObjectOverlapRatio;
    }

    public static bool ParseWillMapObjectTypes(Dictionary<string, string> preferences)
    {
        var willMap = PreferenceParser.ParseBoolValue(preferences, "WillMapObjectTypes", DefaultWillMapObjectTypes);

        return willMap;
    }

    public static List<string> ParseSourceObjectTypeNames(Dictionary<string, string> preferences)
    {
        var types =
            PreferenceParser.ParseStringListValue(preferences, "SourceObjectTypeNames", DefaultSourceObjectTypeNames);

        return types;
    }

    public static string ParseDestinationObjectTypeName(Dictionary<string, string> preferences)
    {
        var targetTypeName =
            PreferenceParser.ParseStringValue(preferences, "DestinationObjectTypeName", DefaultTargetObjectTypeName);

        return targetTypeName;
    }

    public static List<string> ParseNames(Dictionary<string, string> preferences)
    {
        var names = PreferenceParser.ParseStringListValue(preferences, "Names", DefaultNames);

        return names;
    }
}