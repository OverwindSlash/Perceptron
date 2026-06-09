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
using Perceptron.Service.Pipeline;
using Serilog;
using System.Text.Json;
using Size = OpenCvSharp.Size;

namespace Algorithm.General.MotionDetection;

public class Executor : AlgorithmBase
{
    private MotionDetectionSettings _settings = null!;
    private IMotionDetectionStrategy _strategy = null!;
    private IFrameProcessor _frameProcessor = null!;
    private IResourceManager _resourceManager = null!;
    private IPerformanceMonitor _performanceMonitor = null!;
    private IPublisher<MotionDetectedEvent> _motionEventPublisher = null!;

    private int _frameCount;
    private Size _frameSize = new(0, 0);

    public Executor(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Motion Detection";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detect motion in video frames.";
    }

    protected override void InitializeCore()
    {
        _motionEventPublisher =
            Services.GetRequiredService<IPublisher<MotionDetectedEvent>>();
        _settings = new MotionDetectionSettings();
        _settings.ParsePreferences(Preferences);
        _strategy = CreateDefaultStrategy();
        _resourceManager = new DefaultResourceManager();
        _performanceMonitor = new DefaultPerformanceMonitor(_settings);
        _frameProcessor = new DefaultFrameProcessor(
            _resourceManager,
            _performanceMonitor,
            _strategy);
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

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        _frameCount++;

        if (_frameSize.Width == 0 ||
            _frameSize.Height == 0 ||
            _frameSize.Width != frame.Scene.Width ||
            _frameSize.Height != frame.Scene.Height)
        {
            InitializeFrameSize(frame.Scene.Size());
        }

        var processResult = _frameProcessor.ProcessFrame(frame, _frameCount);
        if (!processResult.Success)
        {
            Log.Warning(
                "Frame processing failed: {ErrorMessage}",
                processResult.ErrorMessage);
            return new AnalysisResult(false);
        }

        StoreMotionRegionsInFrame(frame, processResult.MotionRegions);
        GenerateMotionAnnotation(frame, processResult.MotionRegions);

        if (processResult.MotionRegions.Count != 0)
        {
            ProcessMotionDetectedEvent(frame, processResult.MotionRegions);
        }

        return new AnalysisResult(true);
    }

    private void InitializeFrameSize(Size frameSize)
    {
        _frameSize = frameSize;
        _resourceManager.Initialize(frameSize, _settings);
        _frameProcessor.Initialize(frameSize, _settings);
        _strategy.Initialize(frameSize);
        Log.Information(
            "Motion detection frame size initialized: {FrameSize}",
            _frameSize);
    }

    private void StoreMotionRegionsInFrame(Frame frame, List<Rect> motionRois)
    {
        try
        {
            frame.SetProperty("MotionRegions", motionRois);
            frame.SetProperty("MotionRegionCount", motionRois.Count);
            frame.SetProperty("MotionDetectionTimestamp", DateTime.UtcNow);
            frame.SetProperty("MotionDetectionStrategy", _strategy.StrategyName);

            var totalMotionArea = motionRois.Sum(roi => roi.Width * roi.Height);
            frame.SetProperty("TotalMotionArea", totalMotionArea);

            var motionCoverageRatio =
                (double)totalMotionArea /
                (_frameSize.Width * _frameSize.Height);
            frame.SetProperty("MotionCoverageRatio", motionCoverageRatio);

            Log.Debug(
                "Stored {MotionRegionCount} motion regions in frame, total area: {TotalMotionArea}, coverage: {MotionCoverageRatio:P2}",
                motionRois.Count,
                totalMotionArea,
                motionCoverageRatio);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store motion regions in frame.");
        }
    }

    private VisualAnnotation GenerateMotionAnnotation(
        Frame frame,
        List<Rect> motionRois)
    {
        var annotation = frame.Annotation;
        var id = 0;
        foreach (var roi in motionRois)
        {
            annotation.AddShape(new Shape
            {
                Id = $"motion_{id++}",
                Type = "rect",
                Origin = new Origin
                {
                    X = roi.X,
                    Y = roi.Y
                },
                Size = new Perceptron.Domain.Entity.Annotation.Size
                {
                    Width = roi.Width,
                    Height = roi.Height
                },
                Style = new Style
                {
                    StrokeColor = BBoxStrokeColor,
                    StrokeWidth = BBoxStrokeWidth
                }
            });
        }

        return annotation;
    }

    private void ProcessMotionDetectedEvent(
        Frame frame,
        List<Rect> motionRois)
    {
        Log.Information(
            "Motion detected for frame {FrameId} with {MotionRegionCount} motion regions.",
            frame.FrameId,
            motionRois.Count);

        var motionDetectedEvent = new MotionDetectedEvent(
            frame.SourceId,
            EventName,
            AlgorithmName,
            motionRois);
        var annotationJson = JsonSerializer.Serialize(
            frame.Annotation,
            Perceptron.Domain.Event.DomainEvent.JsonOptions);

        TryQueueThrottledEvent(new EventPublicationRequest<MotionDetectedEvent>
        {
            Event = motionDetectedEvent,
            AnnotationJson = annotationJson,
            CloneSnapshot = () => frame.Scene.Clone(),
            FrameId = frame.FrameId,
            FilePrefix = "motion",
            PublishInProcess = @event => _motionEventPublisher.Publish(@event),
            SaveSnapshot = WillSaveEventSnapshot,
            SaveVideoClip = WillSaveEventVideoClip
        });
    }

    protected override void DisposeCore()
    {
        _frameProcessor?.Dispose();
        _strategy?.Dispose();
        _resourceManager?.Dispose();
    }
}
