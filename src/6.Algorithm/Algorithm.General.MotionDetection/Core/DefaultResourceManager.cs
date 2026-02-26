using OpenCvSharp;
using Serilog;

namespace Algorithm.General.MotionDetection.Core;

/// <summary>
/// 默认资源管理器实现
/// </summary>
public class DefaultResourceManager : IResourceManager
{
    #region 私有字段
    private MotionDetectionSettings? _settings;
    private Size _currentSize = new Size(0, 0);
    private DateTime _initializationTime;
    private int _activeMatCount = 0;
    private long _allocatedMemoryBytes = 0;
    #endregion

    #region OpenCV 资源
    public BackgroundSubtractorMOG2? BackgroundSubtractor { get; private set; }
    public Mat? ForegroundMask { get; private set; }
    public Mat? WorkingFrame { get; private set; }
    public Mat? MorphKernel { get; private set; }
    #endregion

    public bool Initialize(Size frameSize, MotionDetectionSettings settings)
    {
        try
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _currentSize = frameSize;
            _initializationTime = DateTime.UtcNow;

            // 创建背景减法器
            BackgroundSubtractor = BackgroundSubtractorMOG2.Create(
                history: settings.Mog2History,
                varThreshold: settings.Mog2VarThreshold,
                detectShadows: settings.Mog2DetectShadows
            );

            // 创建形态学核
            MorphKernel = Cv2.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(settings.MorphKernelSize, settings.MorphKernelSize)
            );
            _activeMatCount++;

            // 初始化缓冲区
            InitializeBuffers(frameSize);

            Log.Information($"Resource manager initialized for size {frameSize}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Resource manager initialization failed: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public void ReinitializeBuffers(Size newSize)
    {
        if (newSize == _currentSize) return;

        try
        {
            // 释放旧缓冲区
            DisposeBuffers();

            // 创建新缓冲区
            InitializeBuffers(newSize);
            _currentSize = newSize;

            Log.Information($"Buffers reinitialized for new size {newSize}");
        }
        catch (Exception ex)
        {
            Log.Error($"Buffer reinitialization failed: {ex.Message}");
        }
    }

    public ResourceUsageStats GetUsageStats()
    {
        return new ResourceUsageStats
        {
            AllocatedMemoryBytes = _allocatedMemoryBytes,
            ActiveMatCount = _activeMatCount,
            LastAllocationTime = _initializationTime,
            TotalUptime = DateTime.UtcNow - _initializationTime
        };
    }

    #region 私有方法
    private void InitializeBuffers(Size frameSize)
    {
        _allocatedMemoryBytes = 0;
        // 创建前景掩码缓冲区
        ForegroundMask = new Mat(frameSize, MatType.CV_8UC1);
        _activeMatCount++;
        _allocatedMemoryBytes += frameSize.Width * frameSize.Height; // 单通道

        // 创建工作帧缓冲区
        WorkingFrame = new Mat(frameSize, MatType.CV_8UC3);
        _activeMatCount++;
        _allocatedMemoryBytes += frameSize.Width * frameSize.Height * 3; // 三通道

        Log.Debug($"Initialized buffers: {_activeMatCount} Mats, ~{_allocatedMemoryBytes / 1024 / 1024:F1}MB");
    }

    private void DisposeBuffers()
    {
        if (ForegroundMask != null)
        {
            ForegroundMask.Dispose();
            ForegroundMask = null;
            _activeMatCount--;
        }

        if (WorkingFrame != null)
        {
            WorkingFrame.Dispose();
            WorkingFrame = null;
            _activeMatCount--;
        }

        _allocatedMemoryBytes = 0;
    }
    #endregion

    public void Dispose()
    {
        try
        {
            BackgroundSubtractor?.Dispose();
            BackgroundSubtractor = null;

            DisposeBuffers();

            if (MorphKernel != null)
            {
                MorphKernel.Dispose();
                MorphKernel = null;
                _activeMatCount--;
            }

            _allocatedMemoryBytes = 0;
            Log.Information("Resource manager disposed");
        }
        catch (Exception ex)
        {
            Log.Error($"Error during resource manager disposal: {ex.Message}");
        }
    }
}
