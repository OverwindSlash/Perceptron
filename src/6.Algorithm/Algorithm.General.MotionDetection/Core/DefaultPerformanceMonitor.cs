using OpenCvSharp;
using Serilog;

namespace Algorithm.General.MotionDetection.Core;

/// <summary>
/// 默认性能监控器实现
/// </summary>
public class DefaultPerformanceMonitor : IPerformanceMonitor
{
    #region 私有字段
    private readonly MotionDetectionSettings _settings;
    private readonly object _lockObject = new();
    
    private double _emaProcessingTime = 0.0;
    private double _minProcessingTime = double.MaxValue;
    private double _maxProcessingTime = 0.0;
    private int _totalFramesProcessed = 0;
    private DateTime _startTime = DateTime.UtcNow;
    private DateTime _lastUpdateTime = DateTime.UtcNow;
    
    // 性能历史记录（用于更精确的FPS计算）
    private readonly Queue<DateTime> _frameTimestamps = new();
    private readonly int _maxHistorySize = 100;
    #endregion

    public DefaultPerformanceMonitor(MotionDetectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void UpdateMetrics(double processingTimeMs)
    {
        lock (_lockObject)
        {
            _totalFramesProcessed++;
            _lastUpdateTime = DateTime.UtcNow;

            // 更新EMA处理时间
            if (_emaProcessingTime == 0.0)
            {
                _emaProcessingTime = processingTimeMs;
            }
            else
            {
                _emaProcessingTime = _emaProcessingTime * (1 - _settings.EmaAlpha) + 
                                   processingTimeMs * _settings.EmaAlpha;
            }

            // 更新最小/最大处理时间
            _minProcessingTime = Math.Min(_minProcessingTime, processingTimeMs);
            _maxProcessingTime = Math.Max(_maxProcessingTime, processingTimeMs);

            // 更新帧时间戳历史
            _frameTimestamps.Enqueue(_lastUpdateTime);
            while (_frameTimestamps.Count > _maxHistorySize)
            {
                _frameTimestamps.Dequeue();
            }

            // 性能警告检查
            CheckPerformanceWarnings(processingTimeMs);
        }
    }

    public PerformanceStats GetPerformanceStats()
    {
        lock (_lockObject)
        {
            return new PerformanceStats
            {
                AverageProcessingTime = _emaProcessingTime,
                ScaleFactor = 1.0, // 默认缩放因子
                ProcessSize = new Size(1920, 1080), // 默认处理尺寸
                FrameCount = _totalFramesProcessed,
                BaselineEstablished = _totalFramesProcessed > 30 // 假设30帧后基线建立
            };
        }
    }

    public PerformanceMetrics GetMetrics()
    {
        lock (_lockObject)
        {
            return new PerformanceMetrics
            {
                AverageProcessingTime = _emaProcessingTime,
                MinProcessingTime = _minProcessingTime == double.MaxValue ? 0 : _minProcessingTime,
                MaxProcessingTime = _maxProcessingTime,
                TotalFramesProcessed = _totalFramesProcessed,
                Fps = CalculateFps(),
                LastUpdateTime = _lastUpdateTime
            };
        }
    }

    public PerformanceAdjustment CheckPerformance()
    {
        lock (_lockObject)
        {
            var adjustment = new PerformanceAdjustment();

            // 检查处理时间是否超过阈值
            if (_emaProcessingTime > _settings.PerformanceHighThresholdMs)
            {
                adjustment.NeedsAdjustment = true;
                adjustment.Type = AdjustmentType.ReduceComplexity;
                adjustment.Reason = $"Processing time ({_emaProcessingTime:F2}ms) exceeds high threshold ({_settings.PerformanceHighThresholdMs}ms)";
                
                // 建议降低分辨率
                adjustment.Parameters["SuggestedScaleFactor"] = 0.8;
                adjustment.Parameters["SuggestedMaxWidth"] = (int)(_settings.MaxProcessWidth * 0.8);
                adjustment.Parameters["SuggestedMaxHeight"] = (int)(_settings.MaxProcessHeight * 0.8);
            }
            else if (_emaProcessingTime < _settings.PerformanceLowThresholdMs && 
                     _settings.MaxProcessWidth < 1920) // 还有提升空间
            {
                adjustment.NeedsAdjustment = true;
                adjustment.Type = AdjustmentType.IncreaseComplexity;
                adjustment.Reason = $"Processing time ({_emaProcessingTime:F2}ms) is below low threshold ({_settings.PerformanceLowThresholdMs}ms)";
                
                // 建议提高分辨率
                adjustment.Parameters["SuggestedScaleFactor"] = 1.2;
                adjustment.Parameters["SuggestedMaxWidth"] = Math.Min(1920, (int)(_settings.MaxProcessWidth * 1.2));
                adjustment.Parameters["SuggestedMaxHeight"] = Math.Min(1080, (int)(_settings.MaxProcessHeight * 1.2));
            }

            return adjustment;
        }
    }

    public void Reset()
    {
        lock (_lockObject)
        {
            _emaProcessingTime = 0.0;
            _minProcessingTime = double.MaxValue;
            _maxProcessingTime = 0.0;
            _totalFramesProcessed = 0;
            _startTime = DateTime.UtcNow;
            _lastUpdateTime = DateTime.UtcNow;
            _frameTimestamps.Clear();
            
            Log.Information("Performance monitor reset");
        }
    }

    #region 私有方法
    private double CalculateFps()
    {
        if (_frameTimestamps.Count < 2) return 0.0;

        var timeSpan = _frameTimestamps.Last() - _frameTimestamps.First();
        if (timeSpan.TotalSeconds <= 0) return 0.0;

        return (_frameTimestamps.Count - 1) / timeSpan.TotalSeconds;
    }

    private void CheckPerformanceWarnings(double processingTimeMs)
    {
        // 单帧处理时间过长警告
        if (processingTimeMs > _settings.PerformanceHighThresholdMs * 2)
        {
            Log.Warning($"Single frame processing time very high: {processingTimeMs:F2}ms");
        }

        // 平均处理时间趋势警告
        if (_emaProcessingTime > _settings.PerformanceHighThresholdMs)
        {
            Log.Warning($"Average processing time degraded: {_emaProcessingTime:F2}ms > {_settings.PerformanceHighThresholdMs}ms");
        }

        // FPS过低警告
        var currentFps = CalculateFps();
        if (currentFps > 0 && currentFps < 15) // 低于15fps
        {
            Log.Warning($"Low FPS detected: {currentFps:F1} fps");
        }
    }
    #endregion
}