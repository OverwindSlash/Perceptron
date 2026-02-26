using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;
using Serilog;

namespace Algorithm.General.MotionDetection.Strategy;

/// <summary>
/// 高级运动检测策略 - 基于ExecutorV2的实现
/// 使用ConnectedComponents、热图和ROI优化
/// </summary>
public class AdvancedMotionDetectionStrategy : IMotionDetectionStrategy, IDisposable
{
    public string StrategyName => "Advanced";

    #region 参数配置
    private readonly int _morphKernelSize;
    private readonly int _morphOpenIter;
    private readonly int _morphCloseIter;
    private readonly byte _heatAdd;
    private readonly byte _heatDecay;
    private readonly byte _heatThreshold;
    private readonly int _motionHistoryDurationFrames;
    private readonly int _maxContoursToProcess;
    private readonly double _boundingBoxMergeThreshold;
    private readonly int _minRegionArea;
    private readonly int _maxRegionArea;
    private readonly double _aspectRatioThreshold;
    #endregion

    #region 状态变量
    private Size _frameSize = new Size(0, 0);
    private int _processedFrameCount = 0;

    // 配置设置
    private readonly MotionDetectionSettings _settings;

    // 并发控制
    private readonly object _motionHistoryLock = new object();

    // 运动历史
    private readonly Queue<MotionHistoryFrame> _motionHistory = new();
    private List<Rect> _cachedHistoricalRois = new List<Rect>();
    private int _lastMotionCacheFrame = -1;
    #endregion

    #region OpenCV 资源
    // 工作缓冲区
    private Mat? _processedMask;
    private Mat? _heatMap;
    private Mat? _tempMask;
    private Mat? _labels;
    private Mat? _stats;
    private Mat? _centroids;

    // 形态学核
    private Mat? _morphKernel;
    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="settings">运动检测配置设置</param>
    public AdvancedMotionDetectionStrategy(MotionDetectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        int kernelSize = Math.Max(1, _settings.MorphKernelSize);
        if (kernelSize % 2 == 0) kernelSize++;
        _morphKernelSize = kernelSize;
        _morphOpenIter = Math.Max(1, _settings.MorphOpenIter);
        _morphCloseIter = Math.Max(1, _settings.MorphCloseIter);
        _heatAdd = (byte)Math.Clamp(_settings.HeatAdd, 1, 255);
        _heatDecay = (byte)Math.Clamp(_settings.HeatDecay, 1, 255);
        _heatThreshold = (byte)Math.Clamp(_settings.HeatThreshold, 1, 255);
        _motionHistoryDurationFrames = Math.Max(1, _settings.MotionHistoryDurationFrames);
        _maxContoursToProcess = Math.Max(1, _settings.MaxContoursToProcess);
        _boundingBoxMergeThreshold = Math.Clamp(_settings.BoundingBoxMergeThreshold, 0.0, 1.0);
        _minRegionArea = Math.Max(1, _settings.BaseMotionDetectionMinArea);
        _maxRegionArea = Math.Max(_minRegionArea, _settings.BaseMotionDetectionMaxArea);
        _aspectRatioThreshold = Math.Max(1.0, _settings.AspectRatioThreshold);
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
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Advanced motion detection strategy initialization failed: {ex.Message}");
            return false;
        }
    }

    public List<Rect> DetectMotionRegions(Frame frame, Mat foregroundMask, int frameNumber)
    {
        if (_morphKernel == null || foregroundMask == null || foregroundMask.Empty())
            return new List<Rect>();
        if (!EnsureBuffers(foregroundMask.Size())) return new List<Rect>();
        _processedFrameCount++;

        // 应用形态学操作 - Open -> Close
        ApplyMorphologicalOperations(foregroundMask);

        // 使用ConnectedComponents生成运动ROI
        var motionRois = GenerateMotionRois();

        // 更新粘性热图
        UpdateStickyHeatmap(motionRois);

        // 更新运动历史
        UpdateMotionHistory(motionRois, frameNumber);

        return motionRois;
    }

    public List<Rect> GetHistoricalMotionRois()
    {
        lock (_motionHistoryLock)
        {
            if (_lastMotionCacheFrame == GetCurrentFrameNumber() && _cachedHistoricalRois.Count > 0)
                return new List<Rect>(_cachedHistoricalRois);

            var allRois = new List<Rect>();
            foreach (var historyFrame in _motionHistory)
                allRois.AddRange(historyFrame.MotionRois);

            if (_heatMap != null && _tempMask != null)
            {
                Cv2.Threshold(_heatMap, _tempMask, _heatThreshold, 255, ThresholdTypes.Binary);
                Cv2.FindContours(_tempMask, out var heatContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                
                foreach (var contour in heatContours)
                {
                    var boundingRect = Cv2.BoundingRect(contour);
                    if (boundingRect.Width * boundingRect.Height >= _minRegionArea)
                        allRois.Add(boundingRect);
                }
            }

            var mergedRois = MergeOverlappingRects(allRois);
            _cachedHistoricalRois = mergedRois;
            _lastMotionCacheFrame = GetCurrentFrameNumber();

            return mergedRois;
        }
    }

    public void UpdateMotionHistory(List<Rect> motionRois, long frameNumber)
    {
        lock (_motionHistoryLock)
        {
            _motionHistory.Enqueue(new MotionHistoryFrame
            {
                FrameNumber = frameNumber,
                MotionRois = new List<Rect>(motionRois)
            });

            while (_motionHistory.Count > 0 && 
                   frameNumber - _motionHistory.Peek().FrameNumber > _motionHistoryDurationFrames)
            {
                _motionHistory.Dequeue();
            }
        }
    }

    #region 私有方法
    private void ApplyMorphologicalOperations(Mat foregroundMask)
    {
        if (_morphKernel == null || _processedMask == null) return;

        // Open -> Close 操作
        Cv2.MorphologyEx(foregroundMask, _processedMask, MorphTypes.Open, _morphKernel, iterations: _morphOpenIter);
        Cv2.MorphologyEx(_processedMask, _processedMask, MorphTypes.Close, _morphKernel, iterations: _morphCloseIter);
    }

    private List<Rect> GenerateMotionRois()
    {
        if (_processedMask == null || _labels == null || _stats == null || _centroids == null) return new List<Rect>();

        var motionRois = new List<Rect>();

        int numLabels = Cv2.ConnectedComponentsWithStats(_processedMask, _labels, _stats, _centroids);

        // 遍历每个连通域（跳过背景标签0）
        for (int i = 1; i < numLabels && motionRois.Count < _maxContoursToProcess; i++)
        {
            // 获取连通域的统计信息
            int x = _stats.At<int>(i, 0);
            int y = _stats.At<int>(i, 1);
            int width = _stats.At<int>(i, 2);
            int height = _stats.At<int>(i, 3);
            int area = _stats.At<int>(i, 4);

            if (area < _minRegionArea || area > _maxRegionArea) continue;
            int maxSide = Math.Max(width, height);
            int minSide = Math.Max(1, Math.Min(width, height));
            double ratio = maxSide / (double)minSide;
            if (ratio > _aspectRatioThreshold) continue;
            motionRois.Add(new Rect(x, y, width, height));
        }

        return motionRois;
    }

    private void UpdateStickyHeatmap(List<Rect> currentMotionRois)
    {
        if (_heatMap == null) return;

        Cv2.Subtract(_heatMap, new Scalar(_heatDecay), _heatMap);

        // 在运动区域增加热度
        foreach (var roi in currentMotionRois)
        {
            var r = ClampRectToImage(roi, _frameSize);
            if (r.Width <= 0 || r.Height <= 0) continue;
            
            using var roiHeat = new Mat(_heatMap, r);
            Cv2.Add(roiHeat, Scalar.All(_heatAdd), roiHeat);
        }
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
        _heatMap?.Dispose();
        _tempMask?.Dispose();
        _labels?.Dispose();
        _stats?.Dispose();
        _centroids?.Dispose();

        _processedMask = new Mat(size, MatType.CV_8UC1);
        _heatMap = Mat.Zeros(size, MatType.CV_8UC1);
        _tempMask = new Mat(size, MatType.CV_8UC1);
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
    }

    // 运动历史帧数据结构
    private class MotionHistoryFrame
    {
        public long FrameNumber { get; set; }
        public List<Rect> MotionRois { get; set; } = new List<Rect>();
    }
}
