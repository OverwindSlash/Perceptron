using Algorithm.Common;
using Algorithm.General.MotionDetection.Core;
using Algorithm.General.MotionDetection.Event;
using Algorithm.General.MotionDetection.Strategy;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Extensions;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Text.Json;
using Size = OpenCvSharp.Size;

namespace Algorithm.General.MotionDetection;

public class Executor : AlgorithmBase
{
    private MotionDetectionSettings _settings = null!;
    private IMotionDetectionStrategy _strategy;
    private IFrameProcessor _frameProcessor;
    private IResourceManager _resourceManager;
    private IPerformanceMonitor _performanceMonitor;

    private IPublisher<MotionDetectedEvent> _motionEventPublisher;

    // 状态管理
    private int _frameCount = 0;
    private Size _frameSize = new Size(0, 0);

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences) 
        : base(pipeline, preferences)
    {
        AlgorithmName = "Motion Detection";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detect motion in video frames.";
    }

    public override bool Initialize()
    {
        var provider = Pipeline.Provider;
        _motionEventPublisher = provider.GetRequiredService<IPublisher<MotionDetectedEvent>>();

        // 从preferences解析配置
        _settings = new MotionDetectionSettings();
        _settings.ParsePreferences(Preferences);

        // 使用优化的策略作为默认选择
        _strategy = CreateDefaultStrategy();

        // 创建核心组件
        _resourceManager = new DefaultResourceManager();
        _performanceMonitor = new DefaultPerformanceMonitor(_settings);
        _frameProcessor = new DefaultFrameProcessor(_resourceManager, _performanceMonitor, _strategy);

        return base.Initialize();
    }

    private IMotionDetectionStrategy CreateDefaultStrategy()
    {
        return _settings.MotionDetectionStrategy?.ToLowerInvariant() switch
        {
            "classic" => new ClassicMotionDetectionStrategy(_settings),
            "advanced" => new AdvancedMotionDetectionStrategy(_settings),
            "adaptive" => new AdaptiveMotionStrategy(_settings),
            "optimized" or _ => new OptimizedAdvancedStrategy(_settings)
        };
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        _frameCount++;

        // 尺寸初始化
        if (_frameSize.Width == 0 || _frameSize.Height == 0 ||
            _frameSize.Width != frame.Scene.Width || _frameSize.Height != frame.Scene.Height)
        {
            InitializeFrameSize(frame.Scene.Size());
        }

        // 使用帧处理器处理帧
        var processResult = _frameProcessor.ProcessFrame(frame, _frameCount);

        if (!processResult.Success)
        {
            Log.Warning($"Frame processing failed: {processResult.ErrorMessage}");
            return new AnalysisResult(false);
        }

        // 将运动区域信息存储到Frame对象中
        StoreMotionRegionsInFrame(frame, processResult.MotionRegions);

        GenerateMotionAnnotation(frame, processResult.MotionRegions);

        if (processResult.MotionRegions.Count != 0)
        {
            ProcessMotionDetectedEvent(frame, processResult.MotionRegions);
        }

        frame.Dispose();

        return new AnalysisResult(true);
    }

    private void InitializeFrameSize(Size frameSize)
    {
        _frameSize = frameSize;

        // 初始化资源管理器
        _resourceManager.Initialize(frameSize, _settings);

        // 初始化帧处理器
        _frameProcessor.Initialize(frameSize, _settings);

        // 初始化运动检测策略
        _strategy.Initialize(frameSize);

        Log.Information($"Motion detection frame size initialized: {_frameSize}");
    }

    private void StoreMotionRegionsInFrame(Frame frame, List<Rect> motionRois)
    {
        try
        {
            // 将运动区域信息存储到Frame的Properties中
            frame.SetProperty("MotionRegions", motionRois);
            frame.SetProperty("MotionRegionCount", motionRois.Count);
            frame.SetProperty("MotionDetectionTimestamp", DateTime.UtcNow);
            frame.SetProperty("MotionDetectionStrategy", _strategy.StrategyName);

            // 计算运动区域的总面积
            int totalMotionArea = motionRois.Sum(roi => roi.Width * roi.Height);
            frame.SetProperty("TotalMotionArea", totalMotionArea);

            // 计算运动区域占总画面的比例
            double motionCoverageRatio = (double)totalMotionArea / (_frameSize.Width * _frameSize.Height);
            frame.SetProperty("MotionCoverageRatio", motionCoverageRatio);

            Log.Debug($"Stored {motionRois.Count} motion regions in frame, total area: {totalMotionArea}, coverage: {motionCoverageRatio:P2}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to store motion regions in frame: {ex.Message}");
        }
    }

    private VisualAnnotation GenerateMotionAnnotation(Frame frame, List<Rect> motionRois)
    {
        var annotation = frame.Annotation;

        int id = 0;
        foreach (var roi in motionRois)
        {
            var rect = new Shape()
            {
                Id = $"motion_{id++}",
                Type = "rect",
                Origin = new Origin()
                {
                    X = roi.X,
                    Y = roi.Y
                },
                Size = new Perceptron.Domain.Entity.Annotation.Size()
                {
                    Width = roi.Width,
                    Height = roi.Height
                },
                Style = new Style()
                {
                    StrokeColor = base.BBoxStrokeColor,
                    StrokeWidth = base.BBoxStrokeWidth
                }
            };

            annotation.AddShape(rect);
        }

        return annotation;
    }

    private void ProcessMotionDetectedEvent(Frame frame, List<Rect> motionRois)
    {
        if (!WillPublishEventMessage) return;

        if (CheckLocalEventInterval()) return;

        // 构建事件消息并发布
        Log.Information($"Motion detected for frame {frame.FrameId} with {motionRois.Count} motion regions.");

        // 1. Create event
        var motionDetectedEvent = new MotionDetectedEvent(
            sourceId: frame.SourceId,
            eventName: EventName,
            algorithmName: AlgorithmName,
            motionRects: motionRois);

        // 2. Serialize Annotations (Synchronously)
        var annotationJson = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        motionDetectedEvent.Annotations = annotationJson;

        // 3. Prepare Snapshot (Synchronously - critical for thread safety)
        Mat? snapshot = null;
        if (WillSaveEventSnapshot)
        {
            // Clone the scene because frame.Scene might be disposed/reused in the main loop
            snapshot = frame.Scene.Clone();
        }

        var frameId = frame.FrameId;

        // 4. Async Saving
        string now = DateTime.Now.ToString("yyyyMMddhhmmss");
        Task.Run(async () =>
        {
            try
            {
                using (snapshot) // Ensure disposal of the cloned snapshot
                {
                    string savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"));
                    savePath.EnsureDirExistence();

                    if (snapshot != null && !snapshot.IsDisposed)
                    {
                        string imagePath = Path.Combine(savePath, $"objectDensity_{now}.jpg");
                        snapshot.SaveImage(imagePath);

                        string annotationPath = Path.Combine(savePath, $"objectDensity_{now}.json");
                        await File.WriteAllTextAsync(annotationPath, annotationJson);

                        motionDetectedEvent.ImageLocalPath = imagePath;
                        motionDetectedEvent.ImageJsonLocalPath = annotationPath;
                    }

                    if (WillSaveEventVideoClip)
                    {
                        string videoPath = Path.Combine(savePath, $"objectDensity_{now}.mp4");
                        // Note: GenerateVideoClipAroundFrameAsync might be fire-and-forget or long running.
                        await SnapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, frameId);

                        motionDetectedEvent.VideoLocalPath = videoPath;
                    }

                    await EventRepository.SaveDomainEventAsync(motionDetectedEvent);
                    MessagePoster.PostDomainEventMessage(motionDetectedEvent);

                    _motionEventPublisher.Publish(motionDetectedEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing object density event {EventName}", EventName);
            }
        });
    }
}
