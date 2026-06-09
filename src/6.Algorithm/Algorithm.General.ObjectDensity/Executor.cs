using Algorithm.Common;
using Algorithm.General.ObjectDensity.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Text.Json;

namespace Algorithm.General.ObjectDensity;

public class Executor : AlgorithmBase
{
    public const string DefaultObjectToBeCount = "person";
    public const string DefaultCountRegionName = "Count Region";
    public const int DefaultMaxCountThreshold = 10;

    public string ObjectToBeCount { get; private set; } = string.Empty;
    public string CountRegionName { get; private set; } = string.Empty;
    public int MaxCountThreshold { get; private set; }

    private int _maxCount;
    private IPublisher<DensityExceedThresholdEvent> _densityEventPublisher = null!;

    public Executor(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Object Density";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects object density in video frames.";
    }

    protected override void InitializeCore()
    {
        _densityEventPublisher =
            Services.GetRequiredService<IPublisher<DensityExceedThresholdEvent>>();
        ObjectToBeCount = PreferenceParser.ParseStringValue(
            Preferences,
            "ObjectToBeCount",
            DefaultObjectToBeCount);
        CountRegionName = PreferenceParser.ParseStringValue(
            Preferences,
            "CountRegionName",
            DefaultCountRegionName);
        MaxCountThreshold = PreferenceParser.ParseIntValue(
            Preferences,
            "MaxCountThreshold",
            DefaultMaxCountThreshold);
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var regionManager =
            RegionManagers.First(manager => manager.SourceId == frame.SourceId);
        var definition = regionManager.RegionDefinition;
        var interestArea = definition.InterestAreas.FirstOrDefault(
            area => area.Name == CountRegionName);
        if (interestArea == null)
        {
            return new AnalysisResult(false);
        }

        var count = 0;
        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (!detectedObject.IsUnderAnalysis)
            {
                continue;
            }

            if (detectedObject.Label.ToLower() != ObjectToBeCount)
            {
                continue;
            }

            var objectCenter = new NormalizedPoint(
                frame.Scene.Width,
                frame.Scene.Height,
                (int)detectedObject.CenterX,
                (int)detectedObject.CenterY);
            if (!interestArea.IsPointInPolygon(objectCenter))
            {
                continue;
            }

            GenerateDetectedObjectAnnotation(frame, detectedObject);
            count++;
        }

        if (count > _maxCount)
        {
            _maxCount = count;
        }

        frame.SetProperty("ObjectCount", count);
        frame.SetProperty("MaxObjectCount", _maxCount);

        if (count > MaxCountThreshold)
        {
            frame.SetProperty("ObjectDensityExceed", true);
            ProcessDensityExceedEvent(frame, count);
        }

        GenerateRegionAnnotation(frame, definition);
        return new AnalysisResult(true);
    }

    private void ProcessDensityExceedEvent(Frame frame, int count)
    {
        Log.Warning(
            "{ObjectName} number: {ObjectCount} in detection region, exceed max thresh: {MaxCountThreshold}.",
            ObjectToBeCount,
            count,
            MaxCountThreshold);

        var densityEvent = new DensityExceedThresholdEvent(
            frame.SourceId,
            EventName,
            AlgorithmName,
            CountRegionName,
            ObjectToBeCount,
            count,
            MaxCountThreshold);
        var annotationJson = JsonSerializer.Serialize(
            frame.Annotation,
            Perceptron.Domain.Event.DomainEvent.JsonOptions);

        TryQueueThrottledEvent(
            new EventPublicationRequest<DensityExceedThresholdEvent>
            {
                Event = densityEvent,
                AnnotationJson = annotationJson,
                CloneSnapshot = () => frame.Scene.Clone(),
                FrameId = frame.FrameId,
                FilePrefix = "objectDensity",
                PublishInProcess = @event =>
                    _densityEventPublisher.Publish(@event),
                SaveSnapshot = WillSaveEventSnapshot,
                SaveVideoClip = WillSaveEventVideoClip
            });
    }

    protected override VisualAnnotation GenerateRegionAnnotation(
        Frame frame,
        ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;
        base.GenerateRegionAnnotation(frame, regionDefinition);

        var count = frame.GetProperty<int>("ObjectCount");
        annotation.AddShape(new Shape
        {
            Id = "text_realtimeCount",
            Type = "text",
            Content = $"Live {ObjectToBeCount} count:{count}",
            Position = new Position
            {
                X = frame.Scene.Width - 350,
                Y = 50
            },
            Style = new Style
            {
                Color = "#FFFF33",
                FontSize = 40
            }
        });

        annotation.AddShape(new Shape
        {
            Id = "text_maxCount",
            Type = "text",
            Content = $"Max {ObjectToBeCount} count:{_maxCount}",
            Position = new Position
            {
                X = frame.Scene.Width - 350,
                Y = 120
            },
            Style = new Style
            {
                Color = "#FFFF33",
                FontSize = 40
            }
        });

        return annotation;
    }
}
