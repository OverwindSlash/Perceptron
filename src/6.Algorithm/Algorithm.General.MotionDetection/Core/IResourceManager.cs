using OpenCvSharp;

namespace Algorithm.General.MotionDetection.Core;

/// <summary>
/// 资源管理接口 - 统一管理OpenCV资源和内存
/// </summary>
public interface IResourceManager : IDisposable
{
    /// <summary>
    /// 初始化资源
    /// </summary>
    /// <param name="frameSize">帧尺寸</param>
    /// <param name="settings">配置设置</param>
    /// <returns>是否初始化成功</returns>
    bool Initialize(Size frameSize, MotionDetectionSettings settings);

    /// <summary>
    /// 获取背景减法器
    /// </summary>
    BackgroundSubtractorMOG2? BackgroundSubtractor { get; }

    /// <summary>
    /// 获取前景掩码缓冲区
    /// </summary>
    Mat? ForegroundMask { get; }

    /// <summary>
    /// 获取工作帧缓冲区
    /// </summary>
    Mat? WorkingFrame { get; }

    /// <summary>
    /// 获取形态学核
    /// </summary>
    Mat? MorphKernel { get; }

    /// <summary>
    /// 重新初始化缓冲区（当帧尺寸改变时）
    /// </summary>
    /// <param name="newSize">新的帧尺寸</param>
    void ReinitializeBuffers(Size newSize);

    /// <summary>
    /// 获取资源使用统计
    /// </summary>
    ResourceUsageStats GetUsageStats();
}

/// <summary>
/// 资源使用统计
/// </summary>
public class ResourceUsageStats
{
    public long AllocatedMemoryBytes { get; set; }
    public int ActiveMatCount { get; set; }
    public DateTime LastAllocationTime { get; set; }
    public TimeSpan TotalUptime { get; set; }
}