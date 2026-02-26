using Algorithm.General.MotionDetection.Strategy;
using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;
using Serilog;
using System.Diagnostics;

namespace Algorithm.General.MotionDetection.Core;

/// <summary>
/// 默认帧处理器实现
/// </summary>
public class DefaultFrameProcessor : IFrameProcessor
{
    #region 私有字段
    private readonly IResourceManager _resourceManager;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly IMotionDetectionStrategy _strategy;
    
    private MotionDetectionSettings? _settings;
    private Size _originalSize = new Size(0, 0);
    private Size _processSize = new Size(0, 0);
    private double _scaleFactor = 1.0;
    
    private int _currentFrameCount = 0;
    private bool _baselineEstablished = false;
    private readonly Stopwatch _frameStopwatch = new();
    
    // 并发控制
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    #endregion

    public DefaultFrameProcessor(
        IResourceManager resourceManager,
        IPerformanceMonitor performanceMonitor,
        IMotionDetectionStrategy strategy)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    }

    public bool Initialize(Size frameSize, MotionDetectionSettings settings)
    {
        try
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            // 初始化资源管理器
            if (!_resourceManager.Initialize(frameSize, settings))
            {
                Log.Error("Failed to initialize resource manager");
                return false;
            }

            // 计算处理尺寸和缩放因子
            InitializeScaleAndBuffers(frameSize);

            // 初始化运动检测策略
            if (!_strategy.Initialize(_processSize))
            {
                Log.Error("Failed to initialize motion detection strategy");
                return false;
            }

            Log.Information($"Frame processor initialized - Original: {_originalSize}, Process: {_processSize}, Scale: {_scaleFactor:F3}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Frame processor initialization failed: {ex.Message}");
            return false;
        }
    }

    public FrameProcessResult ProcessFrame(Frame frame, int frameNumber)
    {
        var result = new FrameProcessResult();
        
        if (_settings == null || _resourceManager.BackgroundSubtractor == null)
        {
            result.ErrorMessage = "Processor not initialized";
            return result;
        }

        // 并发防抖：仅占用时丢弃当前帧
        if (!_processingLock.Wait(0))
        {
            result.ErrorMessage = "Frame dropped due to concurrent processing";
            return result;
        }

        try
        {
            _frameStopwatch.Restart();
            _currentFrameCount++;

            // 检查尺寸变化
            if (frame.Scene.Size() != _originalSize)
            {
                InitializeScaleAndBuffers(frame.Scene.Size());
                _resourceManager.ReinitializeBuffers(_processSize);
            }

            // 基线期处理
            if (!_baselineEstablished)
            {
                result = ProcessBaselinePeriod(frame);
            }
            else
            {
                // 正常期运动检测处理
                result = ProcessMotionDetection(frame, frameNumber);
            }

            // 性能统计更新
            _frameStopwatch.Stop();
            result.ProcessingTimeMs = _frameStopwatch.Elapsed.TotalMilliseconds;
            _performanceMonitor.UpdateMetrics(result.ProcessingTimeMs);

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            Log.Error($"Frame processing failed: {ex.Message}");
            result.ErrorMessage = ex.Message;
            return result;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public ProcessorStatus GetStatus()
    {
        var metrics = _performanceMonitor.GetMetrics();
        
        return new ProcessorStatus
        {
            IsInitialized = _settings != null,
            BaselineEstablished = _baselineEstablished,
            ProcessedFrameCount = _currentFrameCount,
            AverageProcessingTime = metrics.AverageProcessingTime,
            OriginalSize = _originalSize,
            ProcessSize = _processSize,
            ScaleFactor = _scaleFactor
        };
    }

    #region 私有方法
    private void InitializeScaleAndBuffers(Size frameSize)
    {
        if (_settings == null) return;
        
        _originalSize = frameSize;

        // 计算处理尺寸和缩放因子
        double widthScale = (double)_settings.MaxProcessWidth / frameSize.Width;
        double heightScale = (double)_settings.MaxProcessHeight / frameSize.Height;
        _scaleFactor = Math.Min(Math.Min(widthScale, heightScale), 1.0);

        _processSize = new Size(
            (int)(frameSize.Width * _scaleFactor),
            (int)(frameSize.Height * _scaleFactor)
        );
    }

    private FrameProcessResult ProcessBaselinePeriod(Frame frame)
    {
        var result = new FrameProcessResult();
        
        if (_settings == null || _resourceManager.BackgroundSubtractor == null || _resourceManager.ForegroundMask == null)
        {
            result.ErrorMessage = "Resources not available";
            return result;
        }

        var workingFrame = GetWorkingFrame(frame.Scene);
        _resourceManager.BackgroundSubtractor.Apply(workingFrame, _resourceManager.ForegroundMask, _settings.FastLearningRate);

        if (_currentFrameCount >= _settings.BaselineFrameCount)
        {
            _baselineEstablished = true;
            Log.Information($"Motion detection baseline established after {_currentFrameCount} frames");
        }

        result.Success = true;
        return result;
    }

    private FrameProcessResult ProcessMotionDetection(Frame frame, int frameNumber)
    {
        var result = new FrameProcessResult();
        
        if (_settings == null || _resourceManager.BackgroundSubtractor == null || _resourceManager.ForegroundMask == null)
        {
            result.ErrorMessage = "Resources not available";
            return result;
        }

        var workingFrame = GetWorkingFrame(frame.Scene);

        // 应用背景减法
        _resourceManager.BackgroundSubtractor.Apply(workingFrame, _resourceManager.ForegroundMask, _settings.MainLearningRate);

        // 使用策略模式进行运动检测
        var motionRoisProcess = _strategy.DetectMotionRegions(frame, _resourceManager.ForegroundMask, frameNumber);
        
        // 将处理尺度的运动区域转换为原始尺度
        result.MotionRegions = ScaleRectsToOriginal(motionRoisProcess);

        // 获取历史运动区域
        var historicalRoisProcess = _strategy.GetHistoricalMotionRois();
        result.HistoricalRegions = ScaleRectsToOriginal(historicalRoisProcess);

        // 计算统计信息
        result.Statistics = CalculateStatistics(result.MotionRegions, result.HistoricalRegions);

        result.Success = true;
        return result;
    }

    private Mat GetWorkingFrame(Mat originalFrame)
    {
        if (Math.Abs(_scaleFactor - 1.0) < 0.001)
            return originalFrame;

        if (_resourceManager.WorkingFrame == null)
            throw new InvalidOperationException("Working frame buffer not available");

        Cv2.Resize(originalFrame, _resourceManager.WorkingFrame, _processSize);
        return _resourceManager.WorkingFrame;
    }

    private List<Rect> ScaleRectsToOriginal(List<Rect> processRects)
    {
        if (Math.Abs(_scaleFactor - 1.0) < 0.001)
            return processRects;

        return processRects.Select(rect => new Rect(
            (int)(rect.X / _scaleFactor),
            (int)(rect.Y / _scaleFactor),
            (int)(rect.Width / _scaleFactor),
            (int)(rect.Height / _scaleFactor)
        )).ToList();
    }

    private MotionStatistics CalculateStatistics(List<Rect> currentRegions, List<Rect> historicalRegions)
    {
        var allRegions = new List<Rect>(currentRegions);
        allRegions.AddRange(historicalRegions);
        
        // 合并重叠区域
        var mergedRegions = MergeOverlappingRects(allRegions);
        
        int totalMotionArea = mergedRegions.Sum(roi => roi.Width * roi.Height);
        double motionCoverageRatio = (double)totalMotionArea / (_originalSize.Width * _originalSize.Height);

        return new MotionStatistics
        {
            TotalMotionArea = totalMotionArea,
            MotionCoverageRatio = motionCoverageRatio,
            ActiveRegionCount = mergedRegions.Count,
            StrategyUsed = _strategy.StrategyName
        };
    }

    private List<Rect> MergeOverlappingRects(List<Rect> rects)
    {
        if (rects.Count <= 1) return rects;

        var merged = new List<Rect>();
        var used = new bool[rects.Count];

        for (int i = 0; i < rects.Count; i++)
        {
            if (used[i]) continue;

            var current = rects[i];
            used[i] = true;

            for (int j = i + 1; j < rects.Count; j++)
            {
                if (used[j]) continue;

                if (CalculateIoU(current, rects[j]) > 0.3) // 30% IoU阈值
                {
                    current = UnionRects(current, rects[j]);
                    used[j] = true;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    private double CalculateIoU(Rect rect1, Rect rect2)
    {
        var intersection = rect1 & rect2;
        if (intersection.Width <= 0 || intersection.Height <= 0) return 0.0;

        double intersectionArea = intersection.Width * intersection.Height;
        double unionArea = rect1.Width * rect1.Height + rect2.Width * rect2.Height - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0.0;
    }

    private Rect UnionRects(Rect rect1, Rect rect2)
    {
        int x1 = Math.Min(rect1.X, rect2.X);
        int y1 = Math.Min(rect1.Y, rect2.Y);
        int x2 = Math.Max(rect1.X + rect1.Width, rect2.X + rect2.Width);
        int y2 = Math.Max(rect1.Y + rect1.Height, rect2.Y + rect2.Height);

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }
    #endregion

    public void Dispose()
    {
        _processingLock.Dispose();
    }
}