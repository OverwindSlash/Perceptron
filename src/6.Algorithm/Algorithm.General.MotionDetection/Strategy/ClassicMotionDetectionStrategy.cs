using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;
using Serilog;

namespace Algorithm.General.MotionDetection.Strategy;

/// <summary>
/// 经典运动检测策略 使用简单的形态学处理和轮廓检测
/// </summary>
public class ClassicMotionDetectionStrategy : IMotionDetectionStrategy, IDisposable
{
    public string StrategyName => "Classic";

    // 配置设置
    private readonly MotionDetectionSettings _settings;

    private readonly int _motionHistoryDurationFrames;
    private readonly int _morphKernelSize;
    private readonly int _morphOpenIter;
    private readonly int _morphCloseIter;
    private readonly int _baseMotionDetectionMinArea;
    private readonly int _baseMotionDetectionMaxArea;
    private readonly int _maxContoursToProcess;
    private readonly double _boundingBoxMergeThreshold;
    private readonly double _aspectRatioThreshold;

    // 运动历史缓存相关
    private readonly Queue<MotionHistoryFrame> _motionHistory = new();
    private readonly object _motionHistoryLock = new object();

    // 形态学核
    private Mat? _morphKernel;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="settings">运动检测配置设置</param>
    public ClassicMotionDetectionStrategy(MotionDetectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _motionHistoryDurationFrames = _settings.MotionHistoryDurationFrames;
        _morphKernelSize = _settings.MorphKernelSize;
        _morphOpenIter = Math.Max(1, _settings.MorphOpenIter);
        _morphCloseIter = Math.Max(1, _settings.MorphCloseIter);
        _baseMotionDetectionMinArea = _settings.BaseMotionDetectionMinArea;
        _baseMotionDetectionMaxArea = _settings.BaseMotionDetectionMaxArea;
        _maxContoursToProcess = Math.Max(1, _settings.MaxContoursToProcess);
        _boundingBoxMergeThreshold = _settings.BoundingBoxMergeThreshold;
        _aspectRatioThreshold = _settings.AspectRatioThreshold;
    }

    public bool Initialize(Size frameSize)
    {
        try
        {
            int kernelSize = Math.Max(1, _morphKernelSize);
            if (kernelSize % 2 == 0) kernelSize++;
            _morphKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize));
            Log.Information($"Classic motion detection strategy initialized.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Classic motion detection strategy initialization failed: {ex.Message}");
            return false;
        }
    }

    public List<Rect> DetectMotionRegions(Frame frame, Mat foregroundMask, int frameNumber)
    {
        if (_morphKernel == null || foregroundMask == null || foregroundMask.Empty())
            return new List<Rect>();

        using var processedMask = new Mat();
        Cv2.MorphologyEx(foregroundMask, processedMask, MorphTypes.Open, _morphKernel);
        for (int i = 1; i < _morphOpenIter; i++)
        {
            Cv2.MorphologyEx(processedMask, processedMask, MorphTypes.Open, _morphKernel);
        }
        Cv2.MorphologyEx(processedMask, processedMask, MorphTypes.Close, _morphKernel);
        for (int i = 1; i < _morphCloseIter; i++)
        {
            Cv2.MorphologyEx(processedMask, processedMask, MorphTypes.Close, _morphKernel);
        }

        Cv2.FindContours(processedMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var motionRois = new List<Rect>();
        foreach (var contour in contours)
        {
            var boundingRect = Cv2.BoundingRect(contour);
            int area = boundingRect.Width * boundingRect.Height;
            if (area >= _baseMotionDetectionMinArea && area <= _baseMotionDetectionMaxArea)
            {
                int w = boundingRect.Width;
                int h = boundingRect.Height;
                int maxSide = Math.Max(w, h);
                int minSide = Math.Max(1, Math.Min(w, h));
                double ratio = maxSide / (double)minSide;
                if (ratio <= _aspectRatioThreshold)
                {
                    motionRois.Add(boundingRect);
                    if (motionRois.Count >= _maxContoursToProcess) break;
                }
            }
        }

        var mergedRois = MergeBoundingBoxes(motionRois, _boundingBoxMergeThreshold);
        UpdateMotionHistory(mergedRois, frameNumber);

        return mergedRois;
    }

    public List<Rect> GetHistoricalMotionRois()
    {
        var allMotionRois = new List<Rect>();
        
        lock (_motionHistoryLock)
        {
            foreach (var historyFrame in _motionHistory)
            {
                allMotionRois.AddRange(historyFrame.MotionRois);
            }
        }
        
        return allMotionRois;
    }

    public void UpdateMotionHistory(List<Rect> motionRois, long frameNumber)
    {
        lock (_motionHistoryLock)
        {
            // 添加当前帧的运动区域到历史记录
            _motionHistory.Enqueue(new MotionHistoryFrame
            {
                FrameNumber = frameNumber,
                MotionRois = new List<Rect>(motionRois)
            });

            // 移除超出时间窗口的历史记录
            while (_motionHistory.Count > 0 && 
                   frameNumber - _motionHistory.Peek().FrameNumber > _motionHistoryDurationFrames)
            {
                _motionHistory.Dequeue();
            }
        }
    }

    public void Dispose()
    {
        _morphKernel?.Dispose();
        
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

    private static List<Rect> MergeBoundingBoxes(List<Rect> boxes, double iouThreshold)
    {
        if (boxes == null || boxes.Count <= 1) return boxes ?? new List<Rect>();
        var result = new List<Rect>();
        foreach (var box in boxes)
        {
            bool merged = false;
            for (int i = 0; i < result.Count; i++)
            {
                var existing = result[i];
                double iou = ComputeIoU(existing, box);
                if (iou >= iouThreshold)
                {
                    var union = Union(existing, box);
                    result[i] = union;
                    merged = true;
                    break;
                }
            }
            if (!merged) result.Add(box);
        }
        return result;
    }

    private static Rect Union(Rect a, Rect b)
    {
        int x = Math.Min(a.X, b.X);
        int y = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X + a.Width, b.X + b.Width);
        int bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static double ComputeIoU(Rect a, Rect b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        int interW = Math.Max(0, x2 - x1);
        int interH = Math.Max(0, y2 - y1);
        double interArea = interW * (double)interH;
        double unionArea = a.Width * (double)a.Height + b.Width * (double)b.Height - interArea;
        if (unionArea <= 0) return 0.0;
        return interArea / unionArea;
    }
}
