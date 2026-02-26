using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;

namespace Algorithm.General.MotionDetection.Strategy;

/// <summary>
/// 运动检测策略接口
/// 实现类应提供接受 MotionDetectionSettings 参数的构造函数
/// </summary>
public interface IMotionDetectionStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// 初始化策略
    /// </summary>
    /// <param name="frameSize">帧尺寸</param>
    /// <returns>是否初始化成功</returns>
    bool Initialize(Size frameSize);

    /// <summary>
    /// 检测运动区域
    /// </summary>
    /// <param name="frame">当前帧</param>
    /// <param name="foregroundMask">前景掩码</param>
    /// <param name="frameNumber">帧号</param>
    /// <returns>运动区域列表</returns>
    List<Rect> DetectMotionRegions(Frame frame, Mat foregroundMask, int frameNumber);

    /// <summary>
    /// 获取历史运动区域
    /// </summary>
    /// <returns>历史运动区域列表</returns>
    List<Rect> GetHistoricalMotionRois();

    /// <summary>
    /// 更新运动历史
    /// </summary>
    /// <param name="motionRois">当前运动区域</param>
    /// <param name="frameNumber">帧号</param>
    void UpdateMotionHistory(List<Rect> motionRois, long frameNumber);

    /// <summary>
    /// 清理资源
    /// </summary>
    void Dispose();
}