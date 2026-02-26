using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;

namespace Algorithm.General.MotionDetection.Core;

/// <summary>
/// 帧处理器接口 - 负责单帧的运动检测处理
/// </summary>
public interface IFrameProcessor : IDisposable
{
    /// <summary>
    /// 初始化处理器
    /// </summary>
    /// <param name="frameSize">帧尺寸</param>
    /// <param name="settings">配置设置</param>
    /// <returns>是否初始化成功</returns>
    bool Initialize(Size frameSize, MotionDetectionSettings settings);

    /// <summary>
    /// 处理单帧
    /// </summary>
    /// <param name="frame">输入帧</param>
    /// <param name="frameNumber">帧号</param>
    /// <returns>处理结果</returns>
    FrameProcessResult ProcessFrame(Frame frame, int frameNumber);

    /// <summary>
    /// 获取处理器状态
    /// </summary>
    ProcessorStatus GetStatus();
}

/// <summary>
/// 帧处理结果
/// </summary>
public class FrameProcessResult
{
    public bool Success { get; set; }
    public List<Rect> MotionRegions { get; set; } = new();
    public List<Rect> HistoricalRegions { get; set; } = new();
    public double ProcessingTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 运动统计信息
    /// </summary>
    public MotionStatistics Statistics { get; set; } = new();
}

/// <summary>
/// 运动统计信息
/// </summary>
public class MotionStatistics
{
    public int TotalMotionArea { get; set; }
    public double MotionCoverageRatio { get; set; }
    public int ActiveRegionCount { get; set; }
    public string StrategyUsed { get; set; } = string.Empty;
    public double AverageRegionSize { get; set; }
    public double MotionConsistency { get; set; }
}

/// <summary>
/// 处理器状态
/// </summary>
public class ProcessorStatus
{
    public bool IsInitialized { get; set; }
    public bool BaselineEstablished { get; set; }
    public int ProcessedFrameCount { get; set; }
    public double AverageProcessingTime { get; set; }
    public Size OriginalSize { get; set; }
    public Size ProcessSize { get; set; }
    public double ScaleFactor { get; set; }
}