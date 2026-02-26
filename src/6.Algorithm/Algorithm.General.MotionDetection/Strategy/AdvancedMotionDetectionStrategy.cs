using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;

namespace Algorithm.General.MotionDetection.Strategy;

/// <summary>
/// 高级运动检测策略 - 基于ExecutorV2的实现
/// 使用ConnectedComponents、热图和ROI优化
/// </summary>
public class AdvancedMotionDetectionStrategy : IMotionDetectionStrategy, IDisposable
{
    public string StrategyName => "Advanced";

    #region 参数配置
    // 形态学参数
    private const int MORPH_KERNEL_SIZE = 3;
    private const int MORPH_OPEN_ITER = 1;
    private const int MORPH_CLOSE_ITER = 1;

    // 热图参数
    private const byte HEAT_ADD = 64;
    private const byte HEAT_DECAY = 6;
    private const byte HEAT_THRESHOLD = 48;

    // 历史参数
    private const int MOTION_HISTORY_DURATION_FRAMES = 240; // 8秒 * 30fps
    private const int MAX_CONTOURS_TO_PROCESS = 80;
    private const double BOUNDING_BOX_MERGE_THRESHOLD = 0.28;
    #endregion

    #region 状态变量
    private Size _frameSize = new Size(0, 0);
    private int _motionDetectionMinArea = 400;

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
    }

    public bool Initialize(Size frameSize)
    {
        try
        {
            _frameSize = frameSize;
            
            // 创建形态学核
            _morphKernel = Cv2.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(MORPH_KERNEL_SIZE, MORPH_KERNEL_SIZE)
            );

            // 初始化工作缓冲区
            _processedMask = new Mat(frameSize, MatType.CV_8UC1);
            _heatMap = Mat.Zeros(frameSize, MatType.CV_8UC1);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Advanced motion detection strategy initialization failed: {ex.Message}");
            return false;
        }
    }

    public List<Rect> DetectMotionRegions(Frame frame, Mat foregroundMask, int frameNumber)
    {
        if (_morphKernel == null || _processedMask == null || _heatMap == null)
            return new List<Rect>();

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
            
            // 添加历史运动区域（这些是process-scale的）
            foreach (var historyFrame in _motionHistory)
                allRois.AddRange(historyFrame.MotionRois);

            // 从热图中提取额外的运动区域（这些也是process-scale的）
            if (_heatMap != null)
            {
                using var heatBinary = new Mat();
                Cv2.Threshold(_heatMap, heatBinary, HEAT_THRESHOLD, 255, ThresholdTypes.Binary);

                Cv2.FindContours(heatBinary, out var heatContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                
                foreach (var contour in heatContours)
                {
                    var boundingRect = Cv2.BoundingRect(contour); // process-scale
                    if (boundingRect.Width * boundingRect.Height >= _motionDetectionMinArea)
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
                   frameNumber - _motionHistory.Peek().FrameNumber > MOTION_HISTORY_DURATION_FRAMES)
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
        Cv2.MorphologyEx(foregroundMask, _processedMask, MorphTypes.Open, _morphKernel, iterations: MORPH_OPEN_ITER);
        Cv2.MorphologyEx(_processedMask, _processedMask, MorphTypes.Close, _morphKernel, iterations: MORPH_CLOSE_ITER);
    }

    private List<Rect> GenerateMotionRois()
    {
        if (_processedMask == null) return new List<Rect>();

        var motionRois = new List<Rect>();

        // 使用ConnectedComponents进行连通域分析
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();

        int numLabels = Cv2.ConnectedComponentsWithStats(_processedMask, labels, stats, centroids);

        // 遍历每个连通域（跳过背景标签0）
        for (int i = 1; i < numLabels && motionRois.Count < MAX_CONTOURS_TO_PROCESS; i++)
        {
            // 获取连通域的统计信息
            int x = stats.At<int>(i, 0);      // CC_STAT_LEFT
            int y = stats.At<int>(i, 1);      // CC_STAT_TOP
            int width = stats.At<int>(i, 2);  // CC_STAT_WIDTH
            int height = stats.At<int>(i, 3); // CC_STAT_HEIGHT
            int area = stats.At<int>(i, 4);   // CC_STAT_AREA

            // 过滤面积太小的区域
            if (area >= _motionDetectionMinArea)
            {
                motionRois.Add(new Rect(x, y, width, height));
            }
        }

        return motionRois;
    }

    private void UpdateStickyHeatmap(List<Rect> currentMotionRois)
    {
        if (_heatMap == null) return;

        // 热图衰减
        Cv2.Subtract(_heatMap, new Scalar(HEAT_DECAY), _heatMap);

        // 在运动区域增加热度
        foreach (var roi in currentMotionRois)
        {
            var r = ClampRectToImage(roi, _frameSize);
            if (r.Width <= 0 || r.Height <= 0) continue;
            
            using var roiHeat = new Mat(_heatMap, r);
            Cv2.Add(roiHeat, Scalar.All(HEAT_ADD), roiHeat);
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

                if (CalculateIoU(current, rects[j]) > BOUNDING_BOX_MERGE_THRESHOLD)
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
        // 简化实现，实际应该从外部传入
        return Environment.TickCount / 33; // 假设30fps
    }
    #endregion

    public void Dispose()
    {
        _morphKernel?.Dispose();
        _processedMask?.Dispose();
        _heatMap?.Dispose();

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