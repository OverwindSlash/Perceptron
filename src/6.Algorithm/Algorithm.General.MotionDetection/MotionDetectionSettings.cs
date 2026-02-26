using Perceptron.Domain.Setting;
using Serilog;

namespace Algorithm.General.MotionDetection;

/// <summary>
/// Motion Detection 算法配置设置
/// </summary>
public class MotionDetectionSettings
{
    #region 默认值定义
    // MOG2 背景建模参数
    public const int DefaultMog2History = 500;
    public const double DefaultMog2VarThreshold = 50.0;
    public const bool DefaultMog2DetectShadows = true;

    // 学习率策略
    public const double DefaultMainLearningRate = 0.001;
    public const double DefaultFastLearningRate = 0.1;

    // 形态学参数
    public const int DefaultMorphKernelSize = 3;
    public const int DefaultMorphOpenIter = 1;
    public const int DefaultMorphCloseIter = 2;

    // 热力图参数
    public const int DefaultHeatAdd = 80;
    public const int DefaultHeatDecay = 4;
    public const int DefaultHeatThreshold = 60;

    // 基线和历史参数
    public const int DefaultBaselineFrameCount = 100;
    public const int DefaultBaselineExpiryFrames = 50;

    // 图像处理和缩放参数
    public const int DefaultMaxProcessWidth = 1280;
    public const int DefaultMaxProcessHeight = 720;

    public const int DefaultMotionHistoryDurationFrames = 150;
    public const int DefaultMaxContoursToProcess = 60;
    public const double DefaultBoundingBoxMergeThreshold = 0.25;
    public const int DefaultBaseMotionDetectionMinArea = 400;
    public const int DefaultBaseMotionDetectionMaxArea = 50000;
    public const double DefaultAspectRatioThreshold = 5.0;
    public const int DefaultBaseRoiExpansionPixels = 50;
    public const int DefaultHighResWidthThreshold = 1920;
    public const int DefaultHighResMaxActiveRois = 15;
    public const int DefaultNormalResMaxActiveRois = 20;

    // 性能自适应参数
    public const double DefaultPerformanceHighThresholdMs = 33.0;
    public const double DefaultPerformanceLowThresholdMs = 16.0;
    public const int DefaultMinActiveRois = 5;
    public const int DefaultMaxActiveRoisLimit = 30;

    // EMA 平滑系数
    public const double DefaultEmaAlpha = 0.1;

    // 运动检测策略
    public const string DefaultMotionDetectionStrategy = "optimized";

    // 全帧探测间隔
    public const int DefaultFullFrameProbeInterval = 5;

    // 运动区域显示
    public const bool DefaultShowMotionRegions = false;
    public const int DefaultMotionRoiLifetimeSec = 1;
    public const double DefaultMotionRoiMergeOverlapRatio = 0.6;
    #endregion

    #region 属性定义
    // MOG2 背景建模参数
    public int Mog2History { get; private set; }
    public double Mog2VarThreshold { get; private set; }
    public bool Mog2DetectShadows { get; private set; }

    // 学习率策略
    public double MainLearningRate { get; private set; }
    public double FastLearningRate { get; private set; }

    // 形态学参数
    public int MorphKernelSize { get; private set; }
    public int MorphOpenIter { get; private set; }
    public int MorphCloseIter { get; private set; }

    // 热力图参数
    public int HeatAdd { get; private set; }
    public int HeatDecay { get; private set; }
    public int HeatThreshold { get; private set; }

    // 基线和历史参数
    public int BaselineFrameCount { get; private set; }
    public int BaselineExpiryFrames { get; private set; }

    // 图像处理和缩放参数
    public int MaxProcessWidth { get; private set; }
    public int MaxProcessHeight { get; private set; }

    public int MotionHistoryDurationFrames { get; private set; }
    public int MaxContoursToProcess { get; private set; }
    public double BoundingBoxMergeThreshold { get; private set; }
    public int BaseMotionDetectionMinArea { get; private set; }
    public int BaseMotionDetectionMaxArea { get; private set; }
    public double AspectRatioThreshold { get; private set; }
    public int BaseRoiExpansionPixels { get; private set; }
    public int HighResWidthThreshold { get; private set; }
    public int HighResMaxActiveRois { get; private set; }
    public int NormalResMaxActiveRois { get; private set; }

    // 性能自适应参数
    public double PerformanceHighThresholdMs { get; private set; }
    public double PerformanceLowThresholdMs { get; private set; }
    public int MinActiveRois { get; private set; }
    public int MaxActiveRoisLimit { get; private set; }

    // EMA 平滑系数
    public double EmaAlpha { get; private set; }

    // 运动检测策略
    public string MotionDetectionStrategy { get; private set; }

    // 全帧探测间隔
    public int FullFrameProbeInterval { get; private set; }

    // 运动区域显示
    public bool ShowMotionRegions { get; private set; }
    public int MotionRoiLifetimeSec { get; private set; }
    public double MotionRoiMergeOverlapRatio { get; private set; }
    #endregion

    public MotionDetectionSettings()
    {
        // 设置默认值
        Mog2History = DefaultMog2History;
        Mog2VarThreshold = DefaultMog2VarThreshold;
        Mog2DetectShadows = DefaultMog2DetectShadows;
        MainLearningRate = DefaultMainLearningRate;
        FastLearningRate = DefaultFastLearningRate;
        MorphKernelSize = DefaultMorphKernelSize;
        MorphOpenIter = DefaultMorphOpenIter;
        MorphCloseIter = DefaultMorphCloseIter;
        HeatAdd = DefaultHeatAdd;
        HeatDecay = DefaultHeatDecay;
        HeatThreshold = DefaultHeatThreshold;
        BaselineFrameCount = DefaultBaselineFrameCount;
        BaselineExpiryFrames = DefaultBaselineExpiryFrames;
        MaxProcessWidth = DefaultMaxProcessWidth;
        MaxProcessHeight = DefaultMaxProcessHeight;
        MotionHistoryDurationFrames = DefaultMotionHistoryDurationFrames;
        BoundingBoxMergeThreshold = DefaultBoundingBoxMergeThreshold;
        MaxContoursToProcess = DefaultMaxContoursToProcess;
        BaseMotionDetectionMinArea = DefaultBaseMotionDetectionMinArea;
        BaseMotionDetectionMaxArea = DefaultBaseMotionDetectionMaxArea;
        AspectRatioThreshold = DefaultAspectRatioThreshold;
        BaseRoiExpansionPixels = DefaultBaseRoiExpansionPixels;
        HighResWidthThreshold = DefaultHighResWidthThreshold;
        HighResMaxActiveRois = DefaultHighResMaxActiveRois;
        NormalResMaxActiveRois = DefaultNormalResMaxActiveRois;
        PerformanceHighThresholdMs = DefaultPerformanceHighThresholdMs;
        PerformanceLowThresholdMs = DefaultPerformanceLowThresholdMs;
        MinActiveRois = DefaultMinActiveRois;
        MaxActiveRoisLimit = DefaultMaxActiveRoisLimit;
        EmaAlpha = DefaultEmaAlpha;
        MotionDetectionStrategy = DefaultMotionDetectionStrategy;
        FullFrameProbeInterval = DefaultFullFrameProbeInterval;
        ShowMotionRegions = DefaultShowMotionRegions;
        MotionRoiLifetimeSec = DefaultMotionRoiLifetimeSec;
        MotionRoiMergeOverlapRatio = DefaultMotionRoiMergeOverlapRatio;
    }

    public void ParsePreferences(Dictionary<string, string> preferences)
    {
        if (preferences == null) return;

        Mog2History = ParseMog2History(preferences);
        Mog2VarThreshold = ParseMog2VarThreshold(preferences);
        Mog2DetectShadows = ParseMog2DetectShadows(preferences);
        MainLearningRate = ParseMainLearningRate(preferences);
        FastLearningRate = ParseFastLearningRate(preferences);
        MorphKernelSize = ParseMorphKernelSize(preferences);
        MorphOpenIter = ParseMorphOpenIter(preferences);
        MorphCloseIter = ParseMorphCloseIter(preferences);
        HeatAdd = ParseHeatAdd(preferences);
        HeatDecay = ParseHeatDecay(preferences);
        HeatThreshold = ParseHeatThreshold(preferences);
        BaselineFrameCount = ParseBaselineFrameCount(preferences);
        BaselineExpiryFrames = ParseBaselineExpiryFrames(preferences);
        MaxProcessWidth = ParseMaxProcessWidth(preferences);
        MaxProcessHeight = ParseMaxProcessHeight(preferences);
        MotionHistoryDurationFrames = ParseMotionHistoryDurationFrames(preferences);
        MaxContoursToProcess = ParseMaxContoursToProcess(preferences);
        BoundingBoxMergeThreshold = ParseBoundingBoxMergeThreshold(preferences);
        BaseMotionDetectionMinArea = ParseBaseMotionDetectionMinArea(preferences);
        BaseMotionDetectionMaxArea = ParseBaseMotionDetectionMaxArea(preferences);
        AspectRatioThreshold = ParseAspectRatioThreshold(preferences);
        BaseRoiExpansionPixels = ParseBaseRoiExpansionPixels(preferences);
        HighResWidthThreshold = ParseHighResWidthThreshold(preferences);
        HighResMaxActiveRois = ParseHighResMaxActiveRois(preferences);
        NormalResMaxActiveRois = ParseNormalResMaxActiveRois(preferences);
        PerformanceHighThresholdMs = ParsePerformanceHighThresholdMs(preferences);
        PerformanceLowThresholdMs = ParsePerformanceLowThresholdMs(preferences);
        MinActiveRois = ParseMinActiveRois(preferences);
        MaxActiveRoisLimit = ParseMaxActiveRoisLimit(preferences);
        EmaAlpha = ParseEmaAlpha(preferences);
        MotionDetectionStrategy = ParseMotionDetectionStrategy(preferences);
        FullFrameProbeInterval = ParseFullFrameProbeInterval(preferences);
        ShowMotionRegions = ParseShowMotionRegions(preferences);
        MotionRoiLifetimeSec = ParseMotionRoiLifetimeSec(preferences);
        MotionRoiMergeOverlapRatio = ParseMotionRoiMergeOverlapRatio(preferences);
    }

    #region 解析方法
    private int ParseMog2History(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "Mog2History", DefaultMog2History);
        if (value > 0)
            return value;
        return DefaultMog2History;
    }

    private double ParseMog2VarThreshold(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "Mog2VarThreshold", DefaultMog2VarThreshold, double.Parse);
        if (value > 0)
            return value;
        return DefaultMog2VarThreshold;
    }

    private bool ParseMog2DetectShadows(Dictionary<string, string> preferences)
    {
        return PreferenceParser.ParseBoolValue(preferences, "Mog2DetectShadows", DefaultMog2DetectShadows);
    }

    private double ParseMainLearningRate(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "MainLearningRate", DefaultMainLearningRate, double.Parse);
        if (value > 0 && value <= 1)
            return value;
        return DefaultMainLearningRate;
    }

    private double ParseFastLearningRate(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "FastLearningRate", DefaultFastLearningRate, double.Parse);
        if (value > 0 && value <= 1)
            return value;
        return DefaultFastLearningRate;
    }

    private int ParseMorphKernelSize(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MorphKernelSize", DefaultMorphKernelSize);
        if (value > 0 && value % 2 == 1)
            return value;
        return DefaultMorphKernelSize;
    }

    private int ParseMorphOpenIter(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MorphOpenIter", DefaultMorphOpenIter);
        if (value >= 1)
            return value;
        return DefaultMorphOpenIter;
    }

    private int ParseMorphCloseIter(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MorphCloseIter", DefaultMorphCloseIter);
        if (value >= 1)
            return value;
        return DefaultMorphCloseIter;
    }

    private int ParseHeatAdd(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "HeatAdd", DefaultHeatAdd);
        if (value > 0)
            return value;
        return DefaultHeatAdd;
    }

    private int ParseHeatDecay(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "HeatDecay", DefaultHeatDecay);
        if (value > 0)
            return value;
        return DefaultHeatDecay;
    }

    private int ParseHeatThreshold(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "HeatThreshold", DefaultHeatThreshold);
        if (value > 0)
            return value;
        return DefaultHeatThreshold;
    }

    private int ParseBaselineFrameCount(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "BaselineFrameCount", DefaultBaselineFrameCount);
        if (value > 0)
            return value;
        return DefaultBaselineFrameCount;
    }

    private int ParseBaselineExpiryFrames(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "BaselineExpiryFrames", DefaultBaselineExpiryFrames);
        if (value > 0)
            return value;
        return DefaultBaselineExpiryFrames;
    }

    private int ParseMaxProcessWidth(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MaxProcessWidth", DefaultMaxProcessWidth);
        if (value > 0)
            return value;
        return DefaultMaxProcessWidth;
    }

    private int ParseMaxProcessHeight(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MaxProcessHeight", DefaultMaxProcessHeight);
        if (value > 0)
            return value;
        return DefaultMaxProcessHeight;
    }

    private int ParseMotionHistoryDurationFrames(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MotionHistoryDurationFrames", DefaultMotionHistoryDurationFrames);
        if (value > 0)
            return value;
        return DefaultMotionHistoryDurationFrames;
    }

    private double ParseBoundingBoxMergeThreshold(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "BoundingBoxMergeThreshold", DefaultBoundingBoxMergeThreshold, double.Parse);
        if (value > 0)
            return value;
        return DefaultBoundingBoxMergeThreshold;
    }

    private int ParseMaxContoursToProcess(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MaxContoursToProcess", DefaultMaxContoursToProcess);
        if (value > 0)
            return value;
        return DefaultMaxContoursToProcess;
    }

    private int ParseBaseMotionDetectionMinArea(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "BaseMotionDetectionMinArea", DefaultBaseMotionDetectionMinArea);
        if (value > 0)
            return value;
        return DefaultBaseMotionDetectionMinArea;
    }

    private int ParseBaseMotionDetectionMaxArea(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "BaseMotionDetectionMaxArea", DefaultBaseMotionDetectionMaxArea);
        if (value > 0)
            return value;
        return DefaultBaseMotionDetectionMaxArea;
    }

    public double ParseAspectRatioThreshold(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "AspectRatioThreshold", DefaultAspectRatioThreshold, double.Parse);
        if (value > 0)
            return value;
        return DefaultAspectRatioThreshold;
    }

    private int ParseBaseRoiExpansionPixels(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "BaseRoiExpansionPixels", DefaultBaseRoiExpansionPixels);
        if (value >= 0)
            return value;
        return DefaultBaseRoiExpansionPixels;
    }

    private int ParseHighResWidthThreshold(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "HighResWidthThreshold", DefaultHighResWidthThreshold);
        if (value > 0)
            return value;
        return DefaultHighResWidthThreshold;
    }

    private int ParseHighResMaxActiveRois(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "HighResMaxActiveRois", DefaultHighResMaxActiveRois);
        if (value > 0)
            return value;
        return DefaultHighResMaxActiveRois;
    }

    private int ParseNormalResMaxActiveRois(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "NormalResMaxActiveRois", DefaultNormalResMaxActiveRois);
        if (value > 0)
            return value;
        return DefaultNormalResMaxActiveRois;
    }

    private double ParsePerformanceHighThresholdMs(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "PerformanceHighThresholdMs", DefaultPerformanceHighThresholdMs, double.Parse);
        if (value > 0)
            return value;
        return DefaultPerformanceHighThresholdMs;
    }

    private double ParsePerformanceLowThresholdMs(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "PerformanceLowThresholdMs", DefaultPerformanceLowThresholdMs, double.Parse);
        if (value > 0)
            return value;
        return DefaultPerformanceLowThresholdMs;
    }

    private int ParseMinActiveRois(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MinActiveRois", DefaultMinActiveRois);
        if (value > 0)
            return value;
        return DefaultMinActiveRois;
    }

    private int ParseMaxActiveRoisLimit(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MaxActiveRoisLimit", DefaultMaxActiveRoisLimit);
        if (value > 0)
            return value;
        return DefaultMaxActiveRoisLimit;
    }

    private double ParseEmaAlpha(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "EmaAlpha", DefaultEmaAlpha, double.Parse);
        if (value > 0 && value <= 1)
            return value;
        return DefaultEmaAlpha;
    }

    private string ParseMotionDetectionStrategy(Dictionary<string, string> preferences)
    {
        string strategy = PreferenceParser.ParseStringValue(preferences, "MotionDetectionStrategy", DefaultMotionDetectionStrategy).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(strategy))
        {
            Log.Warning($"MotionDetectionStrategy must not be empty. Reset to default: {DefaultMotionDetectionStrategy}");
            strategy = DefaultMotionDetectionStrategy;
        }
        return strategy;
    }

    private int ParseFullFrameProbeInterval(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "FullFrameProbeInterval", DefaultFullFrameProbeInterval);
        if (value > 0)
            return value;
        return DefaultFullFrameProbeInterval;
    }

    private bool ParseShowMotionRegions(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseBoolValue(preferences, "ShowMotionRegions", DefaultShowMotionRegions);
        if (value)
            return value;
        return DefaultShowMotionRegions;
    }

    private int ParseMotionRoiLifetimeSec(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseIntValue(preferences, "MotionRoiLifetimeSec", DefaultMotionRoiLifetimeSec);
        if (value > 0)
            return value;
        return DefaultMotionRoiLifetimeSec;
    }

    private double ParseMotionRoiMergeOverlapRatio(Dictionary<string, string> preferences)
    {
        var value = PreferenceParser.ParseValue(preferences, "MotionRoiMergeOverlapRatio", DefaultMotionRoiMergeOverlapRatio, double.Parse);
        if (value > 0 && value <= 1)
            return value;
        return DefaultMotionRoiMergeOverlapRatio;
    }
    #endregion
}
