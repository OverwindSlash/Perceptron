using Algorithm.Common;
using Algorithm.General.ObjectOccurrence.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Text.Json;

namespace Algorithm.General.ObjectOccurrence;

public class Executor : AlgorithmBase
{
    public const string DefaultOccurrenceCheckRegionName = "Occurrence Region";
    public const string DefaultTargetObjectNames = "person";
    public const string DefaultOccurrenceCondition = "or";
    public const bool DefaultEnableProximityCheck = false;
    public const float DefaultProximityThresholdRatio = 1.5f;
    public const string DefaultProximityReferenceObject = "person";
    public const string DefaultProximityReferenceDimension = "width"; // width or height
    public const int DefaultMinDurationSec = 3;

    public string OccurrenceCheckRegionName { get; private set; } = string.Empty;
    public HashSet<string> TargetObjectNames { get; private set; } = [];
    public string OccurrenceCondition { get; private set; } = string.Empty;
    public bool EnableProximityCheck { get; private set; }
    public string ProximityObject1Label { get; private set; } = string.Empty;
    public string ProximityObject2Label { get; private set; } = string.Empty;
    public float ProximityThresholdRatio { get; private set; }
    public string ProximityReferenceObject { get; private set; } = string.Empty;
    public string ProximityReferenceDimension { get; private set; } = string.Empty;
    public int MinDurationSec { get; private set; }

    private DateTime? _firstOccurrenceTime;
    private IPublisher<ObjectOccurrenceEvent> _occurrenceEventPublisher = null!;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Object Occurrence";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects object occurrence in video frames.";
    }

    protected override void InitializeCore()
    {
        _occurrenceEventPublisher =
            Services.GetRequiredService<IPublisher<ObjectOccurrenceEvent>>();

        OccurrenceCheckRegionName = PreferenceParser.ParseStringValue(Preferences, "OccurrenceCheckRegionName", DefaultOccurrenceCheckRegionName);
        
        var objectNames = PreferenceParser.ParseStringValue(Preferences, "TargetObjectNames", DefaultTargetObjectNames);
        TargetObjectNames = new HashSet<string>(objectNames.Split(',').Select(name => name.Trim().ToLower()));

        OccurrenceCondition = PreferenceParser.ParseStringValue(Preferences, "OccurrenceCondition", DefaultOccurrenceCondition);

        EnableProximityCheck = PreferenceParser.ParseBoolValue(Preferences, "EnableProximityCheck", DefaultEnableProximityCheck);
        if (EnableProximityCheck)
        {
            ProximityObject1Label = PreferenceParser.ParseStringValue(Preferences, "ProximityObject1Label", "").ToLower();
            ProximityObject2Label = PreferenceParser.ParseStringValue(Preferences, "ProximityObject2Label", "").ToLower();
            ProximityThresholdRatio = PreferenceParser.ParseFloatValue(Preferences, "ProximityThresholdRatio", DefaultProximityThresholdRatio);
            ProximityReferenceObject = PreferenceParser.ParseStringValue(Preferences, "ProximityReferenceObject", DefaultProximityReferenceObject).ToLower();
            ProximityReferenceDimension = PreferenceParser.ParseStringValue(Preferences, "ProximityReferenceDimension", DefaultProximityReferenceDimension).ToLower();
        }

        MinDurationSec = PreferenceParser.ParseIntValue(Preferences, "MinDurationSec", DefaultMinDurationSec);
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var regionManager = RegionManagers.First(rm => rm.SourceId == frame.SourceId);
        var definition = regionManager.RegionDefinition;
        
        var interestArea = definition.InterestAreas.FirstOrDefault(ia => ia.Name == OccurrenceCheckRegionName);
        if (interestArea == null)
        {
            // 处理未找到兴趣区域的情况
            return new AnalysisResult(false);
        }

        // 筛选出目标对象
        var targetObjects = frame.DetectedObjects
            .Where(o => o.IsUnderAnalysis && TargetObjectNames.Contains(o.Label.ToLower()))
            .ToList();

        // 检查目标对象是否在兴趣区域内
        targetObjects.Where(o =>
            {
                var objectCenter = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height, (int)o.CenterX, (int)o.CenterY);
                return interestArea.IsPointInPolygon(objectCenter);
            })
            .ToList()
            .ForEach(o =>
            {
                GenerateDetectedObjectAnnotation(frame, o);
                o.SetProperty("Occurrence", true);
            });

        // 检查是否发生了目标对象出现
        bool isOccurrence = false;
        if (OccurrenceCondition == "or")
        {
            isOccurrence = targetObjects.Any(o => o.GetProperty<bool>("Occurrence"));
        }
        else if (OccurrenceCondition == "and")
        {
            isOccurrence = targetObjects.Any() && targetObjects.All(o => o.GetProperty<bool>("Occurrence"));
        }

        // 检查对象间距离
        if (isOccurrence && EnableProximityCheck)
        {
            var occurrenceObjects = targetObjects
                .Where(o => o.GetProperty<bool>("Occurrence"))
                .ToList();

            var group1 = occurrenceObjects
                .Where(o => o.Label.ToLower() == ProximityObject1Label)
                .ToList();
            var group2 = occurrenceObjects
                .Where(o => o.Label.ToLower() == ProximityObject2Label)
                .ToList();

            bool proximityMet = false;

            // 遍历所有可能的配对
            foreach (var obj1 in group1)
            {
                foreach (var obj2 in group2)
                {
                    if (obj1 == obj2) continue; // 避免自引用（如果类型相同）

                    var distance = Math.Sqrt(Math.Pow(obj1.CenterX - obj2.CenterX, 2) + Math.Pow(obj1.CenterY - obj2.CenterY, 2));
                    
                    var referenceObj = ProximityReferenceObject == obj2.Label.ToLower() ? obj2 : obj1;
                    var referenceDimension = ProximityReferenceDimension == "height" ? referenceObj.Height : referenceObj.Width;
                    var threshold = referenceDimension * ProximityThresholdRatio;

                    if (distance < threshold)
                    {
                        proximityMet = true;
                        // 标记满足条件的对象
                        obj1.SetProperty("ProximityMet", obj2.Id);
                        obj2.SetProperty("ProximityMet", obj1.Id);
                    }
                }
            }

            if (!proximityMet)
            {
                isOccurrence = false;
            }
        }

        // 根据持续时间判断是否最终认定为对象出现
        if (isOccurrence)
        {
            frame.SetProperty("ObjectOccurrence", true);

            if (MinDurationSec > 0)
            {
                if (_firstOccurrenceTime == null)
                {
                    _firstOccurrenceTime = frame.UtcTimeStamp;
                }
                
                var duration = (frame.UtcTimeStamp - _firstOccurrenceTime.Value).TotalSeconds;
                if (duration >= MinDurationSec)
                {
                    ProcessObjectOccurrenceEvent(frame, duration);
                }
            }
        }
        else
        {
            _firstOccurrenceTime = null;
        }   

        // 绘制区域与分析结果标注
        GenerateRegionAnnotation(frame, definition);

        return new AnalysisResult(true);
    }

    private void ProcessObjectOccurrenceEvent(Frame frame, double duration)
    {
        var occurredObjectNames = frame.DetectedObjects
            .Where(o => o.HasProperty("Occurrence") && o.GetProperty<bool>("Occurrence"))
            .Select(o => o.Label.ToLower())
            .Distinct()
            .ToList();

        if (occurredObjectNames.Count == 0)
        {
            occurredObjectNames = TargetObjectNames.ToList();
        }

        Log.Warning("Objects: {ObjectNames} have occurred in region:{RegionName} for {Duration} seconds.",
            string.Join(", ", occurredObjectNames), OccurrenceCheckRegionName, (int)Math.Round(duration));

        var occurrenceEvent = new ObjectOccurrenceEvent(
            sourceId: frame.SourceId,
            eventType: ObjectOccurrenceEvent.EventType,
            eventName: EventName,
            algorithmName: AlgorithmName,
            regionName: OccurrenceCheckRegionName,
            occurredObjectNames: occurredObjectNames,
            durationSec: (int)Math.Round(duration));

        var annotationJson = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        occurrenceEvent.Annotations = annotationJson;

        TryQueueThrottledEvent(
            new EventPublicationRequest<ObjectOccurrenceEvent>
            {
                Event = occurrenceEvent,
                AnnotationJson = annotationJson,
                CloneSnapshot = () => frame.Scene.Clone(),
                FrameId = frame.FrameId,
                FilePrefix = "objectOccurrence",
                PublishInProcess = @event =>
                    _occurrenceEventPublisher.Publish(@event),
                SaveSnapshot = WillSaveEventSnapshot,
                SaveVideoClip = WillSaveEventVideoClip
            });
    }

    protected override void DisposeCore()
    {
        _firstOccurrenceTime = null;
    }

    protected override VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        if (frame.HasProperty("ObjectOccurrence") && frame.GetProperty<bool>("ObjectOccurrence"))
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition, "#F44336"));
        }
        else
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition));
        }

        return annotation;
    }
}
