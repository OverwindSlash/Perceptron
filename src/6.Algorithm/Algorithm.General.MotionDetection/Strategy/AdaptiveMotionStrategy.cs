using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;
using Serilog;

namespace Algorithm.General.MotionDetection.Strategy;

/// <summary>
/// 自适应运动检测策略
/// 根据场景特征动态选择和调整检测算法
/// </summary>
public class AdaptiveMotionStrategy : IMotionDetectionStrategy, IDisposable
{
    public string StrategyName => "Adaptive";

    #region 策略组合
    private readonly OptimizedAdvancedStrategy _advancedStrategy;
    private readonly ClassicMotionDetectionStrategy _classicStrategy;
    private IMotionDetectionStrategy _currentStrategy;
    #endregion

    #region 配置设置
    private readonly MotionDetectionSettings _settings;
    #endregion

    #region 自适应参数
    private const int ADAPTATION_WINDOW = 30;        // 自适应窗口大小
    private const double NOISE_THRESHOLD = 0.15;     // 噪声阈值
    private const double COMPLEXITY_THRESHOLD = 0.7; // 复杂度阈值
    private const int MIN_STABLE_FRAMES = 10;        // 最小稳定帧数
    
    // 场景分析参数
    private readonly CircularBuffer<SceneMetrics> _sceneHistory;
    private SceneType _currentSceneType = SceneType.Unknown;
    private int _stableFrameCount = 0;
    private DateTime _lastAdaptation = DateTime.UtcNow;
    #endregion

    #region 性能监控
    private readonly CircularBuffer<double> _processingTimes;
    private readonly CircularBuffer<int> _regionCounts;
    private double _averageProcessingTime = 0.0;
    private double _averageRegionCount = 0.0;
    #endregion

    #region 场景分析资源
    private Mat? _grayFrame;
    private Mat? _prevGrayFrame;
    private Mat? _flowMask;
    private Mat? _tempMat;
    private Size _frameSize = new Size(0, 0);
    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="settings">运动检测配置设置</param>
    public AdaptiveMotionStrategy(MotionDetectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        
        _advancedStrategy = new OptimizedAdvancedStrategy(settings);
        _classicStrategy = new ClassicMotionDetectionStrategy(settings);
        _currentStrategy = _advancedStrategy; // 默认使用高级策略

        _sceneHistory = new CircularBuffer<SceneMetrics>(ADAPTATION_WINDOW);
        _processingTimes = new CircularBuffer<double>(ADAPTATION_WINDOW);
        _regionCounts = new CircularBuffer<int>(ADAPTATION_WINDOW);

        Log.Information("Adaptive motion detection strategy initialized");
    }

    public bool Initialize(Size frameSize)
    {
        try
        {
            _frameSize = frameSize;

            // 初始化场景分析资源
            _grayFrame = new Mat(frameSize, MatType.CV_8UC1);
            _prevGrayFrame = new Mat(frameSize, MatType.CV_8UC1);
            _flowMask = new Mat(frameSize, MatType.CV_8UC1);
            _tempMat = new Mat(frameSize, MatType.CV_8UC1);

            // 初始化所有策略
            bool advancedInit = _advancedStrategy.Initialize(frameSize);
            bool classicInit = _classicStrategy.Initialize(frameSize);

            if (!advancedInit || !classicInit)
            {
                Log.Error("Failed to initialize one or more motion detection strategies");
                return false;
            }

            Log.Information($"Adaptive strategy initialized for frame size: {frameSize}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Adaptive strategy initialization failed: {ex.Message}");
            return false;
        }
    }

    public List<Rect> DetectMotionRegions(Frame frame, Mat foregroundMask, int frameNumber)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // 分析当前场景特征
            var sceneMetrics = AnalyzeScene(frame, foregroundMask);
            _sceneHistory.Add(sceneMetrics);

            // 检查是否需要策略适应
            if (ShouldAdaptStrategy())
            {
                AdaptStrategy();
            }

            // 使用当前策略检测运动
            var motionRegions = _currentStrategy.DetectMotionRegions(frame, foregroundMask, frameNumber);

            // 应用自适应后处理
            motionRegions = ApplyAdaptivePostProcessing(motionRegions, sceneMetrics);

            // 更新性能统计
            var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            UpdatePerformanceMetrics(processingTime, motionRegions.Count);

            return motionRegions;
        }
        catch (Exception ex)
        {
            Log.Error($"Adaptive motion detection failed: {ex.Message}");
            return new List<Rect>();
        }
    }

    public List<Rect> GetHistoricalMotionRois()
    {
        return _currentStrategy.GetHistoricalMotionRois();
    }

    public void UpdateMotionHistory(List<Rect> motionRois, long frameNumber)
    {
        _currentStrategy.UpdateMotionHistory(motionRois, frameNumber);
    }

    #region 场景分析
    private SceneMetrics AnalyzeScene(Frame frame, Mat foregroundMask)
    {
        if (_grayFrame == null || _prevGrayFrame == null || _flowMask == null || _tempMat == null)
            return new SceneMetrics();

        try
        {
            // 转换为灰度图
            using var frameMat = frame.Scene.Clone();
            Cv2.CvtColor(frameMat, _grayFrame, ColorConversionCodes.BGR2GRAY);

            var metrics = new SceneMetrics
            {
                FrameNumber = frame.FrameId,
                Timestamp = DateTime.UtcNow
            };

            // 计算图像复杂度（基于梯度）
            metrics.ImageComplexity = CalculateImageComplexity(_grayFrame);

            // 计算噪声水平
            metrics.NoiseLevel = CalculateNoiseLevel(foregroundMask);

            // 计算运动强度（如果有前一帧）
            if (!_prevGrayFrame.Empty())
            {
                metrics.MotionIntensity = CalculateMotionIntensity(_prevGrayFrame, _grayFrame);
            }

            // 计算前景密度
            metrics.ForegroundDensity = CalculateForegroundDensity(foregroundMask);

            // 分析场景类型
            metrics.SceneType = ClassifyScene(metrics);

            // 保存当前帧作为下一次的前一帧
            _grayFrame.CopyTo(_prevGrayFrame);

            return metrics;
        }
        catch (Exception ex)
        {
            Log.Debug($"Scene analysis failed: {ex.Message}");
            return new SceneMetrics();
        }
    }

    private double CalculateImageComplexity(Mat grayFrame)
    {
        try
        {
            // 使用Sobel算子计算梯度
            using var gradX = new Mat();
            using var gradY = new Mat();
            using var grad = new Mat();

            Cv2.Sobel(grayFrame, gradX, MatType.CV_16S, 1, 0, 3);
            Cv2.Sobel(grayFrame, gradY, MatType.CV_16S, 0, 1, 3);
            
            Cv2.ConvertScaleAbs(gradX, gradX);
            Cv2.ConvertScaleAbs(gradY, gradY);
            Cv2.AddWeighted(gradX, 0.5, gradY, 0.5, 0, grad);

            // 计算梯度的平均值作为复杂度指标
            var mean = Cv2.Mean(grad);
            return mean.Val0 / 255.0; // 归一化到[0,1]
        }
        catch
        {
            return 0.5; // 默认中等复杂度
        }
    }

    private double CalculateNoiseLevel(Mat foregroundMask)
    {
        try
        {
            // 计算小区域的数量来估计噪声
            Cv2.FindContours(foregroundMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            
            int smallRegions = contours.Count(c => Cv2.ContourArea(c) < 100);
            int totalRegions = contours.Length;
            
            return totalRegions > 0 ? (double)smallRegions / totalRegions : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    private double CalculateMotionIntensity(Mat prevFrame, Mat currentFrame)
    {
        try
        {
            if (_tempMat == null) return 0.0;

            // 计算帧差
            Cv2.Absdiff(prevFrame, currentFrame, _tempMat);
            
            // 计算平均差值
            var mean = Cv2.Mean(_tempMat);
            return mean.Val0 / 255.0; // 归一化
        }
        catch
        {
            return 0.0;
        }
    }

    private double CalculateForegroundDensity(Mat foregroundMask)
    {
        try
        {
            int foregroundPixels = Cv2.CountNonZero(foregroundMask);
            int totalPixels = foregroundMask.Width * foregroundMask.Height;
            
            return (double)foregroundPixels / totalPixels;
        }
        catch
        {
            return 0.0;
        }
    }

    private SceneType ClassifyScene(SceneMetrics metrics)
    {
        // 基于多个指标分类场景
        if (metrics.NoiseLevel > NOISE_THRESHOLD)
            return SceneType.Noisy;
        
        if (metrics.ImageComplexity > COMPLEXITY_THRESHOLD)
            return SceneType.Complex;
        
        if (metrics.MotionIntensity > 0.3)
            return SceneType.HighMotion;
        
        if (metrics.ForegroundDensity > 0.1)
            return SceneType.CrowdedScene;
        
        return SceneType.Simple;
    }
    #endregion

    #region 策略适应
    private bool ShouldAdaptStrategy()
    {
        // 检查是否有足够的历史数据
        if (_sceneHistory.Count < MIN_STABLE_FRAMES)
            return false;

        // 检查时间间隔
        if (DateTime.UtcNow - _lastAdaptation < TimeSpan.FromSeconds(2))
            return false;

        // 检查场景稳定性
        var recentMetrics = _sceneHistory.GetItems().TakeLast(MIN_STABLE_FRAMES).ToList();
        var dominantSceneType = recentMetrics
            .GroupBy(m => m.SceneType)
            .OrderByDescending(g => g.Count())
            .First().Key;

        // 如果场景类型发生变化，需要适应
        return dominantSceneType != _currentSceneType;
    }

    private void AdaptStrategy()
    {
        try
        {
            var recentMetrics = _sceneHistory.GetItems().TakeLast(MIN_STABLE_FRAMES).ToList();
            var avgComplexity = recentMetrics.Average(m => m.ImageComplexity);
            var avgNoise = recentMetrics.Average(m => m.NoiseLevel);
            var avgMotion = recentMetrics.Average(m => m.MotionIntensity);

            // 根据场景特征选择最适合的策略
            IMotionDetectionStrategy newStrategy;
            SceneType newSceneType;

            if (avgNoise > NOISE_THRESHOLD || avgComplexity > COMPLEXITY_THRESHOLD)
            {
                // 复杂或噪声场景使用高级策略
                newStrategy = _advancedStrategy;
                newSceneType = avgNoise > NOISE_THRESHOLD ? SceneType.Noisy : SceneType.Complex;
            }
            else if (avgMotion < 0.1 && _averageProcessingTime > 20.0)
            {
                // 简单场景且性能压力大时使用经典策略
                newStrategy = _classicStrategy;
                newSceneType = SceneType.Simple;
            }
            else
            {
                // 默认使用高级策略
                newStrategy = _advancedStrategy;
                newSceneType = SceneType.Simple;
            }

            if (newStrategy != _currentStrategy)
            {
                _currentStrategy = newStrategy;
                _currentSceneType = newSceneType;
                _stableFrameCount = 0;
                _lastAdaptation = DateTime.UtcNow;

                Log.Information($"Adapted to strategy: {_currentStrategy.StrategyName} for scene type: {_currentSceneType}");
            }
            else
            {
                _stableFrameCount++;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Strategy adaptation failed: {ex.Message}");
        }
    }
    #endregion

    #region 自适应后处理
    private List<Rect> ApplyAdaptivePostProcessing(List<Rect> regions, SceneMetrics metrics)
    {
        if (regions.Count == 0) return regions;

        try
        {
            var processed = new List<Rect>(regions);

            // 根据场景类型应用不同的后处理
            switch (metrics.SceneType)
            {
                case SceneType.Noisy:
                    processed = FilterNoiseRegions(processed);
                    break;

                case SceneType.Complex:
                    processed = MergeComplexRegions(processed);
                    break;

                case SceneType.CrowdedScene:
                    processed = OptimizeCrowdedRegions(processed);
                    break;

                case SceneType.HighMotion:
                    processed = ExpandMotionRegions(processed);
                    break;
            }

            return processed;
        }
        catch (Exception ex)
        {
            Log.Debug($"Adaptive post-processing failed: {ex.Message}");
            return regions;
        }
    }

    private List<Rect> FilterNoiseRegions(List<Rect> regions)
    {
        // 过滤掉过小的区域
        return regions.Where(r => r.Width * r.Height >= 300).ToList();
    }

    private List<Rect> MergeComplexRegions(List<Rect> regions)
    {
        // 在复杂场景中更积极地合并相近区域
        return MergeNearbyRegions(regions, 0.15); // 降低合并阈值
    }

    private List<Rect> OptimizeCrowdedRegions(List<Rect> regions)
    {
        // 在拥挤场景中限制区域数量
        return regions.OrderByDescending(r => r.Width * r.Height).Take(20).ToList();
    }

    private List<Rect> ExpandMotionRegions(List<Rect> regions)
    {
        // 在高运动场景中适当扩展区域
        return regions.Select(r => new Rect(
            Math.Max(0, r.X - 5),
            Math.Max(0, r.Y - 5),
            Math.Min(_frameSize.Width - r.X, r.Width + 10),
            Math.Min(_frameSize.Height - r.Y, r.Height + 10)
        )).ToList();
    }

    private List<Rect> MergeNearbyRegions(List<Rect> regions, double threshold)
    {
        if (regions.Count <= 1) return regions;

        var merged = new List<Rect>();
        var used = new bool[regions.Count];

        for (int i = 0; i < regions.Count; i++)
        {
            if (used[i]) continue;

            var current = regions[i];
            used[i] = true;

            for (int j = i + 1; j < regions.Count; j++)
            {
                if (used[j]) continue;

                if (CalculateIoU(current, regions[j]) > threshold)
                {
                    current = UnionRects(current, regions[j]);
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

    #region 性能监控
    private void UpdatePerformanceMetrics(double processingTime, int regionCount)
    {
        _processingTimes.Add(processingTime);
        _regionCounts.Add(regionCount);

        if (_processingTimes.Count > 0)
        {
            _averageProcessingTime = _processingTimes.GetItems().Average();
        }

        if (_regionCounts.Count > 0)
        {
            _averageRegionCount = _regionCounts.GetItems().Average();
        }
    }

    public AdaptiveStrategyStats GetAdaptiveStats()
    {
        return new AdaptiveStrategyStats
        {
            CurrentStrategy = _currentStrategy.StrategyName,
            CurrentSceneType = _currentSceneType,
            AverageProcessingTime = _averageProcessingTime,
            AverageRegionCount = _averageRegionCount,
            StableFrameCount = _stableFrameCount,
            LastAdaptation = _lastAdaptation
        };
    }
    #endregion

    public void Dispose()
    {
        _advancedStrategy?.Dispose();
        _classicStrategy?.Dispose();
        _grayFrame?.Dispose();
        _prevGrayFrame?.Dispose();
        _flowMask?.Dispose();
        _tempMat?.Dispose();

        Log.Information("Adaptive motion detection strategy disposed");
    }
}

#region 支持类和枚举
/// <summary>
/// 场景类型枚举
/// </summary>
public enum SceneType
{
    Unknown,
    Simple,
    Complex,
    Noisy,
    HighMotion,
    CrowdedScene
}

/// <summary>
/// 场景指标
/// </summary>
public class SceneMetrics
{
    public long FrameNumber { get; set; }
    public DateTime Timestamp { get; set; }
    public double ImageComplexity { get; set; }
    public double NoiseLevel { get; set; }
    public double MotionIntensity { get; set; }
    public double ForegroundDensity { get; set; }
    public SceneType SceneType { get; set; }
}

/// <summary>
/// 自适应策略统计信息
/// </summary>
public class AdaptiveStrategyStats
{
    public string CurrentStrategy { get; set; } = "";
    public SceneType CurrentSceneType { get; set; }
    public double AverageProcessingTime { get; set; }
    public double AverageRegionCount { get; set; }
    public int StableFrameCount { get; set; }
    public DateTime LastAdaptation { get; set; }
}
#endregion