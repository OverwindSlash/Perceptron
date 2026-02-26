using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;
using Serilog;

namespace Algorithm.General.MotionDetection.Strategy;

/// <summary>
/// 优化的高级运动检测策略
/// 改进了内存使用、算法效率和检测精度
/// </summary>
public class OptimizedAdvancedStrategy : IMotionDetectionStrategy, IDisposable
{
    public string StrategyName => "OptimizedAdvanced";

    #region 参数配置
    // 形态学参数
    private readonly int _morphKernelSize;
    private readonly int _morphOpenIter;
    private readonly int _morphCloseIter;       // 增加闭运算迭代次数

    // 优化的热图参数
    private readonly int _heatAdd;              // 增加热度增量
    private readonly int _heatDecay;            // 减少衰减速度
    private readonly int _heatThreshold;        // 提高阈值

    // 历史参数
    private readonly int _motionHistoryDurationFrames;          // 减少到6秒
    private readonly int _maxContoursToProcess;                 // 减少处理数量
    private readonly double _boundingBoxMergeThreshold;         // 降低合并阈值

    // 新增：自适应参数
    private readonly int _minRegionArea;                        // 最小区域面积
    private readonly int _maxRegionArea;                        // 最大区域面积
    private readonly double _aspectRatioThreshold;              // 长宽比阈值
    #endregion

    #region 状态变量
    private Size _frameSize = new Size(0, 0);
    private int _motionDetectionMinArea = 400;

    // 配置设置
    private readonly MotionDetectionSettings _settings;

    // 并发控制
    private readonly object _motionHistoryLock = new object();

    // 运动历史 - 使用更高效的数据结构
    private readonly CircularBuffer<MotionHistoryFrame> _motionHistory;
    private List<Rect> _cachedHistoricalRois = new List<Rect>();
    private int _lastMotionCacheFrame = -1;

    // 性能统计
    private int _processedFrameCount = 0;
    private double _averageRegionCount = 0.0;
    #endregion

    #region OpenCV 资源
    // 工作缓冲区 - 复用以减少内存分配
    private Mat? _processedMask;
    private Mat? _heatMap;
    private Mat? _tempMask;
    private Mat? _morphKernel;

    // 连通域分析缓冲区
    private Mat? _labels;
    private Mat? _stats;
    private Mat? _centroids;
    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="settings">运动检测配置设置</param>
    public OptimizedAdvancedStrategy(MotionDetectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        int kernelSize = Math.Max(1, _settings.MorphKernelSize);
        if (kernelSize % 2 == 0) kernelSize++;
        _morphKernelSize = kernelSize;
        _morphOpenIter = Math.Max(1, _settings.MorphOpenIter);
        _morphCloseIter = Math.Max(1, _settings.MorphCloseIter);

        _heatAdd = Math.Max(1, _settings.HeatAdd);
        _heatDecay = Math.Max(1, _settings.HeatDecay);
        _heatThreshold = Math.Max(1, _settings.HeatThreshold);

        _motionHistoryDurationFrames = Math.Max(1, _settings.MotionHistoryDurationFrames);
        _maxContoursToProcess = Math.Max(1, _settings.MaxContoursToProcess);
        _boundingBoxMergeThreshold = Math.Clamp(_settings.BoundingBoxMergeThreshold, 0.0, 1.0);

        _minRegionArea = Math.Max(1, _settings.BaseMotionDetectionMinArea);
        _maxRegionArea = Math.Max(_minRegionArea, _settings.BaseMotionDetectionMaxArea);
        _aspectRatioThreshold = Math.Max(1.0, _settings.AspectRatioThreshold);
        _motionDetectionMinArea = _minRegionArea;

        _motionHistory = new CircularBuffer<MotionHistoryFrame>(_motionHistoryDurationFrames);
    }

    public bool Initialize(Size frameSize)
    {
        try
        {
            if (frameSize.Width <= 0 || frameSize.Height <= 0) return false;
            _frameSize = frameSize;
            
            _morphKernel = Cv2.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(_morphKernelSize, _morphKernelSize)
            );

            if (!EnsureBuffers(frameSize)) return false;

            Log.Information($"Optimized advanced strategy initialized for size {frameSize}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Optimized advanced strategy initialization failed: {ex.Message}");
            return false;
        }
    }

    public List<Rect> DetectMotionRegions(Frame frame, Mat foregroundMask, int frameNumber)
    {
        if (_morphKernel == null || foregroundMask == null || foregroundMask.Empty())
            return new List<Rect>();

        if (!EnsureBuffers(foregroundMask.Size())) return new List<Rect>();

        _processedFrameCount++;

        // 应用优化的形态学操作
        ApplyOptimizedMorphologicalOperations(foregroundMask);

        // 使用优化的连通域分析生成运动ROI
        var motionRois = GenerateOptimizedMotionRois();

        // 应用智能过滤
        motionRois = ApplyIntelligentFiltering(motionRois);

        // 更新自适应热图
        UpdateAdaptiveHeatmap(motionRois);

        // 更新运动历史
        UpdateMotionHistory(motionRois, frameNumber);

        // 更新统计信息
        UpdateStatistics(motionRois);

        return motionRois;
    }

    public List<Rect> GetHistoricalMotionRois()
    {
        lock (_motionHistoryLock)
        {
            if (_lastMotionCacheFrame == GetCurrentFrameNumber() && _cachedHistoricalRois.Count > 0)
                return new List<Rect>(_cachedHistoricalRois);

            var allRois = new List<Rect>();
            
            // 从历史记录中收集ROI
            foreach (var historyFrame in _motionHistory.GetItems())
            {
                allRois.AddRange(historyFrame.MotionRois);
            }

            // 从热图中提取额外的运动区域
            var heatRois = ExtractHeatmapRegions();
            allRois.AddRange(heatRois);

            // 优化的合并算法
            var mergedRois = OptimizedMergeOverlappingRects(allRois);
            _cachedHistoricalRois = mergedRois;
            _lastMotionCacheFrame = GetCurrentFrameNumber();

            return mergedRois;
        }
    }

    public void UpdateMotionHistory(List<Rect> motionRois, long frameNumber)
    {
        lock (_motionHistoryLock)
        {
            var historyFrame = new MotionHistoryFrame
            {
                FrameNumber = frameNumber,
                MotionRois = new List<Rect>(motionRois),
                Timestamp = DateTime.UtcNow
            };

            _motionHistory.Add(historyFrame);
        }
    }

    #region 优化的私有方法
    private void ApplyOptimizedMorphologicalOperations(Mat foregroundMask)
    {
        if (_morphKernel == null || _processedMask == null || _tempMask == null) return;
        if (foregroundMask == null || foregroundMask.Empty()) return;
        if (_processedMask.Size() != foregroundMask.Size()) return;

        Cv2.MorphologyEx(foregroundMask, _tempMask, MorphTypes.Open, _morphKernel, iterations: _morphOpenIter);
        Cv2.MorphologyEx(_tempMask, _processedMask, MorphTypes.Close, _morphKernel, iterations: _morphCloseIter);

        if (_processedFrameCount % 5 == 0) // 每5帧应用一次
        {
            Cv2.GaussianBlur(_processedMask, _processedMask, new Size(3, 3), 0);
        }
    }

    private List<Rect> GenerateOptimizedMotionRois()
    {
        if (_processedMask == null || _labels == null || _stats == null || _centroids == null) 
            return new List<Rect>();

        var motionRois = new List<Rect>();

        try
        {
            // 使用预分配的缓冲区进行连通域分析
            int numLabels = Cv2.ConnectedComponentsWithStats(_processedMask, _labels, _stats, _centroids);

            // 遍历每个连通域（跳过背景标签0）
            for (int i = 1; i < numLabels && motionRois.Count < _maxContoursToProcess; i++)
            {
                // 获取连通域的统计信息
                int x = _stats.At<int>(i, 0);      // CC_STAT_LEFT
                int y = _stats.At<int>(i, 1);      // CC_STAT_TOP
                int width = _stats.At<int>(i, 2);  // CC_STAT_WIDTH
                int height = _stats.At<int>(i, 3); // CC_STAT_HEIGHT
                int area = _stats.At<int>(i, 4);   // CC_STAT_AREA

                // 应用面积过滤
                if (area >= _motionDetectionMinArea && area <= _maxRegionArea)
                {
                    motionRois.Add(new Rect(x, y, width, height));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Connected components analysis failed: {ex.Message}");
        }

        return motionRois;
    }

    private List<Rect> ApplyIntelligentFiltering(List<Rect> rois)
    {
        var filtered = new List<Rect>();

        foreach (var roi in rois)
        {
            if (roi.Width <= 0 || roi.Height <= 0) continue;
            int area = roi.Width * roi.Height;
            if (area < _minRegionArea || area > _maxRegionArea)
                continue;

            int maxSide = Math.Max(roi.Width, roi.Height);
            int minSide = Math.Max(1, Math.Min(roi.Width, roi.Height));
            double aspectRatio = maxSide / (double)minSide;
            if (aspectRatio > _aspectRatioThreshold)
                continue;

            if (roi.X < 0 || roi.Y < 0 || 
                roi.X + roi.Width > _frameSize.Width || 
                roi.Y + roi.Height > _frameSize.Height)
                continue;

            filtered.Add(roi);
        }

        return filtered;
    }

    private void UpdateAdaptiveHeatmap(List<Rect> currentMotionRois)
    {
        if (_heatMap == null) return;

        byte decayValue = (byte)Math.Max(2, _heatDecay - (_averageRegionCount > 5 ? 1 : 0));
        Cv2.Subtract(_heatMap, new Scalar(decayValue), _heatMap);

        foreach (var roi in currentMotionRois)
        {
            var clampedRoi = ClampRectToImage(roi, _frameSize);
            if (clampedRoi.Width <= 0 || clampedRoi.Height <= 0) continue;
            
            try
            {
                using var roiHeat = new Mat(_heatMap, clampedRoi);
                Cv2.Add(roiHeat, Scalar.All(_heatAdd), roiHeat);
            }
            catch (Exception ex)
            {
                Log.Debug($"Heatmap update failed for ROI {roi}: {ex.Message}");
            }
        }
    }

    private List<Rect> ExtractHeatmapRegions()
    {
        if (_heatMap == null || _tempMask == null) return new List<Rect>();

        var heatRois = new List<Rect>();

        try
        {
            Cv2.Threshold(_heatMap, _tempMask, _heatThreshold, 255, ThresholdTypes.Binary);
            Cv2.FindContours(_tempMask, out var heatContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            
            foreach (var contour in heatContours.Take(20)) // 限制数量
            {
                var boundingRect = Cv2.BoundingRect(contour);
                if (boundingRect.Width * boundingRect.Height >= _motionDetectionMinArea)
                    heatRois.Add(boundingRect);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Heatmap region extraction failed: {ex.Message}");
        }

        return heatRois;
    }

    private List<Rect> OptimizedMergeOverlappingRects(List<Rect> rects)
    {
        if (rects == null || rects.Count <= 1) return rects ?? new List<Rect>();

        var merged = new List<Rect>();
        var used = new bool[rects.Count];

        var sortedIndices = Enumerable.Range(0, rects.Count)
            .OrderByDescending(i => rects[i].Width * rects[i].Height)
            .ToArray();

        foreach (int i in sortedIndices)
        {
            if (used[i]) continue;

            var current = rects[i];
            used[i] = true;

            for (int j = 0; j < rects.Count; j++)
            {
                if (used[j] || i == j) continue;

                if (CalculateIoU(current, rects[j]) > _boundingBoxMergeThreshold)
                {
                    current = UnionRects(current, rects[j]);
                    used[j] = true;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    private void UpdateStatistics(List<Rect> motionRois)
    {
        const double alpha = 0.1;
        _averageRegionCount = _averageRegionCount * (1 - alpha) + motionRois.Count * alpha;
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

    private Rect ClampRectToImage(Rect rect, Size imageSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return new Rect(0, 0, 0, 0);
        int x = Math.Max(0, Math.Min(rect.X, imageSize.Width - 1));
        int y = Math.Max(0, Math.Min(rect.Y, imageSize.Height - 1));
        int width = Math.Max(0, Math.Min(rect.Width, imageSize.Width - x));
        int height = Math.Max(0, Math.Min(rect.Height, imageSize.Height - y));

        return new Rect(x, y, width, height);
    }

    private int GetCurrentFrameNumber()
    {
        return _processedFrameCount;
    }

    private bool EnsureBuffers(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return false;
        if (_processedMask != null && _heatMap != null && _tempMask != null &&
            _labels != null && _stats != null && _centroids != null &&
            _processedMask.Size() == size && _heatMap.Size() == size)
        {
            _frameSize = size;
            return true;
        }

        _processedMask?.Dispose();
        _tempMask?.Dispose();
        _heatMap?.Dispose();
        _labels?.Dispose();
        _stats?.Dispose();
        _centroids?.Dispose();

        _processedMask = new Mat(size, MatType.CV_8UC1);
        _tempMask = new Mat(size, MatType.CV_8UC1);
        _heatMap = Mat.Zeros(size, MatType.CV_8UC1);
        _labels = new Mat(size, MatType.CV_32SC1);
        _stats = new Mat();
        _centroids = new Mat();

        _frameSize = size;
        _cachedHistoricalRois = new List<Rect>();
        _lastMotionCacheFrame = -1;
        return true;
    }
    #endregion

    public void Dispose()
    {
        _morphKernel?.Dispose();
        _processedMask?.Dispose();
        _heatMap?.Dispose();
        _tempMask?.Dispose();
        _labels?.Dispose();
        _stats?.Dispose();
        _centroids?.Dispose();

        lock (_motionHistoryLock)
        {
            _motionHistory.Clear();
        }

        Log.Information("Optimized advanced strategy disposed");
    }

    // 运动历史帧数据结构
    private class MotionHistoryFrame
    {
        public long FrameNumber { get; set; }
        public List<Rect> MotionRois { get; set; } = new List<Rect>();
        public DateTime Timestamp { get; set; }
    }
}

/// <summary>
/// 高效的循环缓冲区实现
/// </summary>
public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _head = 0;
    private int _count = 0;
    private readonly int _capacity;

    public CircularBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _buffer = new T[_capacity];
    }

    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _capacity;
        
        if (_count < _capacity)
            _count++;
    }

    public IEnumerable<T> GetItems()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = (_head - _count + i + _capacity) % _capacity;
            yield return _buffer[index];
        }
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
        Array.Clear(_buffer, 0, _capacity);
    }

    public int Count => _count;
}
