using Algorithm.Common;
using Algorithm.General.ObjectDensity.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Extensions;
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

    public string ObjectToBeCount { get; private set; }
    public string CountRegionName { get; private set; }
    public int MaxCountThreshold { get; private set; }

    private int _maxCount = 0;

    private IPublisher<DensityExceedThresholdEvent> _densityEventPublisher;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences) 
        : base(pipeline, preferences)
    {
        AlgorithmName = "Object Density";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects object density in video frames.";
    }

    public override bool Initialize()
    {
        var provider = Pipeline.Provider;
        _densityEventPublisher = provider.GetRequiredService<IPublisher<DensityExceedThresholdEvent>>();

        ObjectToBeCount = PreferenceParser.ParseStringValue(Preferences, "ObjectToBeCount", DefaultObjectToBeCount);
        CountRegionName = PreferenceParser.ParseStringValue(Preferences, "CountRegionName", DefaultCountRegionName);
        MaxCountThreshold = PreferenceParser.ParseIntValue(Preferences, "MaxCountThreshold", DefaultMaxCountThreshold);

        return base.Initialize(); ;
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        var regionManager = Pipeline.RegionManagers.First(rm => rm.SourceId == frame.SourceId);
        var definition = regionManager.RegionDefinition;

        var interestArea = definition.InterestAreas.FirstOrDefault(ia => ia.Name == CountRegionName);
        if (interestArea == null)
        {
            // 处理未找到兴趣区域的情况
            return new AnalysisResult(false);
        }

        int count = 0;
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

            var objectCenter = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height,
                (int)detectedObject.CenterX, (int)detectedObject.CenterY);

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

        // 绘制区域与分析结果标注
        GenerateRegionAnnotation(frame, definition);

        frame.Dispose();

        return new AnalysisResult(true);
    }

    private void ProcessDensityExceedEvent(Frame frame, int count)
    {
        if (CheckLocalEventInterval()) return;

        Log.Warning($"{ObjectToBeCount} number: {count} in detection region, exceed max thresh: {MaxCountThreshold}.");

        // 1. Create Event
        var densityEvent = new DensityExceedThresholdEvent(
            sourceId: frame.SourceId,
            eventName: EventName,
            algorithmName: AlgorithmName,
            regionName: CountRegionName,
            objectToBeCount: ObjectToBeCount,
            objectCount: count,
            maxCountThresh: MaxCountThreshold
        );

        // 2. Serialize Annotations (Synchronously)
        var annotationJson = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        densityEvent.Annotations = annotationJson;

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

                        densityEvent.ImageLocalPath = imagePath;
                        densityEvent.ImageJsonLocalPath = annotationPath;
                    }

                    if (WillSaveEventVideoClip)
                    {
                        string videoPath = Path.Combine(savePath, $"objectDensity_{now}.mp4");
                        // Note: GenerateVideoClipAroundFrameAsync might be fire-and-forget or long running.
                        await SnapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, frameId);

                        densityEvent.VideoLocalPath = videoPath;
                    }

                    await EventRepository.SaveDomainEventAsync(densityEvent);
                    MessagePoster.PostDomainEventMessage(densityEvent);

                    _densityEventPublisher.Publish(densityEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing object density event {EventName}", EventName);
            }
        });
    }

    protected override VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        // if (frame.HasProperty("ObjectDensityExceed") && frame.GetProperty<bool>("ObjectDensityExceed"))
        // {
        //     annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition, "#F44336"));
        // }
        // else
        // {
        //     annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition));
        // }

        base.GenerateRegionAnnotation(frame, regionDefinition);

        // text annotation
        var countArea = regionDefinition.InterestAreas.FirstOrDefault(ia => ia.Name == CountRegionName);
        var objectNames = string.Join(',', countArea.RelativeTypes);

        int count = frame.GetProperty<int>("ObjectCount");
        
        var realtimeText = new Shape()
        {
            Id = "text_realtimeCount",
            Type = "text",
            Content = $"Live {ObjectToBeCount} count:{count}",
            Position = new Position()
            {
                X = frame.Scene.Width - 350,
                Y = 50
            },
            Style = new Style()
            {
                Color = "#FFFF33",
                FontSize = 40,
            }
        };
        annotation.AddShape(realtimeText);

        var maxText = new Shape()
        {
            Id = "text_maxCount",
            Type = "text",
            Content = $"Max {ObjectToBeCount} count:{_maxCount}",
            Position = new Position()
            {
                X = frame.Scene.Width - 350,
                Y = 120
            },
            Style = new Style()
            {
                Color = "#FFFF33",
                FontSize = 40,
            }
        };
        annotation.AddShape(maxText);

        return annotation;
    }
}