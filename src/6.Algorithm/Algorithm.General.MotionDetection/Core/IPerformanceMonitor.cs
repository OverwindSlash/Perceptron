using OpenCvSharp;

namespace Algorithm.General.MotionDetection.Core;

/// <summary>
/// 性能监控接口
/// </summary>
public interface IPerformanceMonitor
{
    /// <summary>
    /// 更新性能指标
    /// </summary>
    /// <param name="processingTimeMs">处理时间（毫秒）</param>
    void UpdateMetrics(double processingTimeMs);

    /// <summary>
    /// 获取性能统计信息
    /// </summary>
    /// <returns>性能统计信息</returns>
    PerformanceStats GetPerformanceStats();

    /// <summary>
    /// 获取当前性能统计
    /// </summary>
    /// <returns>性能统计信息</returns>
    PerformanceMetrics GetMetrics();

    /// <summary>
    /// 检查是否需要性能调整
    /// </summary>
    /// <returns>性能调整建议</returns>
    PerformanceAdjustment CheckPerformance();

    /// <summary>
    /// 重置统计信息
    /// </summary>
    void Reset();
}

/// <summary>
/// 性能统计信息
/// </summary>
public class PerformanceStats
{
    public double AverageProcessingTime { get; set; }
    public double ScaleFactor { get; set; }
    public Size ProcessSize { get; set; }
    public int FrameCount { get; set; }
    public bool BaselineEstablished { get; set; }
}

/// <summary>
/// 性能指标
/// </summary>
public class PerformanceMetrics
{
    public double AverageProcessingTime { get; set; }
    public double MinProcessingTime { get; set; }
    public double MaxProcessingTime { get; set; }
    public int TotalFramesProcessed { get; set; }
    public double Fps { get; set; }
    public DateTime LastUpdateTime { get; set; }
}

/// <summary>
/// 性能调整建议
/// </summary>
public class PerformanceAdjustment
{
    public bool NeedsAdjustment { get; set; }
    public AdjustmentType Type { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public double ScaleFactor { get; set; } = 1.0;
}

/// <summary>
/// 调整类型
/// </summary>
public enum AdjustmentType
{
    None,
    ReduceResolution,
    IncreaseResolution,
    ReduceComplexity,
    IncreaseComplexity,
    OptimizeMemory,
    OptimizeParameters
}