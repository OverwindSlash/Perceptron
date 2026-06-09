using Algorithm.Common;
using Algorithm.General.RegionAccess.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Algorithm.General.RegionAccess;

public class Executor : AlgorithmBase
{
    private const string DefaultRegionName = "RestrictedArea";
    private static readonly List<string> DefaultRelativeTypes = [];
    private const int DefaultStateStabilityThreshold = 3;
    private const int DefaultFontSize = 16;
    private const string DefaultEnteringEventName = "EnterRegion";
    private const string DefaultEnteringAnnotationColor = "#FF0000";
    private const string DefaultInEventName = "InRegion";
    private const string DefaultInAnnotationColor = "#00FF00";
    private const string DefaultLeavingEventName = "LeaveRegion";
    private const string DefaultLeavingAnnotationColor = "#0000FF";

    public string RegionName { get; private set; } = string.Empty;
    public List<string> RegionRelativeTypes { get; private set; } = [];

    // 状态变化阈值：对象状态需要稳定多少帧后才发布事件（默认3帧）
    public int StateStabilityThreshold { get; private set; }

    public int FontSize { get; private set; }
    public string EnteringEventName { get; private set; } = string.Empty;
    public string EnteringAnnotationColor { get; private set; } = string.Empty;
    public string InEventName { get; private set; } = string.Empty;
    public string InAnnotationColor { get; private set; } = string.Empty;
    public string LeavingEventName { get; private set; } = string.Empty;
    public string LeavingAnnotationColor { get; private set; } = string.Empty;


    private IPublisher<EnterRegionEvent> _enterEventPublisher = null!;
    private IPublisher<InRegionEvent> _inEventPublisher = null!;
    private IPublisher<LeaveRegionEvent> _leaveEventPublisher = null!;

    private readonly ConcurrentDictionary<string, ObjectRegionState> _objRegionStates = new();

    // 状态稳定性跟踪：记录每个对象在当前状态下的持续帧数
    private readonly ConcurrentDictionary<string, int> _objStateStabilityCounter = new();

    private readonly ConcurrentDictionary<string, bool> _objLastInRegionStatus = new();


    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Region Access";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects access to specific regions in the video stream.";

    }

    protected override void InitializeCore()
    {
        _enterEventPublisher =
            Services.GetRequiredService<IPublisher<EnterRegionEvent>>();
        _inEventPublisher =
            Services.GetRequiredService<IPublisher<InRegionEvent>>();
        _leaveEventPublisher =
            Services.GetRequiredService<IPublisher<LeaveRegionEvent>>();
        Subscribe(
            Services.GetRequiredService<ISubscriber<ObjectExpiredEvent>>(),
            ProcessEvent);

        // settings
        RegionName = PreferenceParser.ParseStringValue(Preferences, "RegionName", DefaultRegionName);

        RegionRelativeTypes =
            PreferenceParser.ParseStringListValue(Preferences, "RegionRelativeTypes", DefaultRelativeTypes);

        StateStabilityThreshold =
            PreferenceParser.ParseIntValue(Preferences, "StateStabilityThreshold", DefaultStateStabilityThreshold);
        StateStabilityThreshold = Math.Max(1, StateStabilityThreshold); // 确保阈值至少为1

        FontSize = PreferenceParser.ParseIntValue(Preferences, "FontSize", DefaultFontSize);

        EnteringEventName =
            PreferenceParser.ParseStringValue(Preferences, "EnteringEventName", DefaultEnteringEventName);
        EnteringAnnotationColor = PreferenceParser.ParseStringValue(Preferences, "EnteringAnnotationColor", DefaultEnteringAnnotationColor);

        InEventName = PreferenceParser.ParseStringValue(Preferences, "InEventName", DefaultInEventName);
        InAnnotationColor = PreferenceParser.ParseStringValue(Preferences, "InAnnotationColor", DefaultInAnnotationColor);

        LeavingEventName =
            PreferenceParser.ParseStringValue(Preferences, "LeavingEventName", DefaultLeavingEventName);
        LeavingAnnotationColor = PreferenceParser.ParseStringValue(Preferences, "LeavingAnnotationColor", DefaultLeavingAnnotationColor);
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var regionManager = RegionManagers.First(rm => rm.SourceId == frame.SourceId);
        var definition = regionManager.RegionDefinition;

        var interestArea = definition.InterestAreas.FirstOrDefault(ia => ia.Name == RegionName);
        if (interestArea == null)
        {
            // 处理未找到兴趣区域的情况
            return new AnalysisResult(false);
        }

        // 绘制区域与分析结果标注
        GenerateRegionAnnotation(frame, definition);

        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (!detectedObject.IsUnderAnalysis)
            {
                continue;
            }

            if (!RegionRelativeTypes.Contains(detectedObject.Label.ToLower()))
            {
                continue;
            }

            // 创建对象的四个角点及中心点
            var topLeft = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height,
                detectedObject.X, detectedObject.Y);

            var topRight = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height,
                detectedObject.X + detectedObject.Width, detectedObject.Y);

            var bottomRight = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height,
                detectedObject.X + detectedObject.Width, detectedObject.Y + detectedObject.Height);

            var bottomLeft = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height,
                detectedObject.X, detectedObject.Y + detectedObject.Height);

            var objectCenter = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height,
                (int)detectedObject.CenterX, (int)detectedObject.CenterY);

            // 判断对象是否完全在区域内
            bool isFullyInside = interestArea.IsPointInPolygon(topLeft) &&
                                 interestArea.IsPointInPolygon(topRight) &&
                                 interestArea.IsPointInPolygon(bottomRight) &&
                                 interestArea.IsPointInPolygon(bottomLeft);

            // 判断对象是否完全在区域外
            bool isFullyOutside = !interestArea.IsPointInPolygon(topLeft) &&
                                  !interestArea.IsPointInPolygon(topRight) &&
                                  !interestArea.IsPointInPolygon(bottomRight) &&
                                  !interestArea.IsPointInPolygon(bottomLeft);

            // 部分在区域内
            bool isPartiallyInside = !isFullyInside && !isFullyOutside;
            
            // 确定对象中心是否在区域内
            bool isObjInArea = interestArea.IsPointInPolygon(objectCenter);

            // 获取对象的前一状态
            _objRegionStates.TryGetValue(detectedObject.Id, out var previousState);

            // 获取对象的状态稳定性计数器
            _objStateStabilityCounter.TryGetValue(detectedObject.Id, out var stabilityCounter);

            ObjectRegionState currentState = previousState;

            // 状态转换逻辑
            currentState = CalcCurrentState(isFullyInside, isPartiallyInside, isObjInArea, previousState);

            // 更新状态稳定性计数器
            if (currentState == previousState)
            {
                stabilityCounter++;
            }
            else
            {
                // 状态发生变化，重置稳定性计数器
                stabilityCounter = 1;
            }

            // 更新稳定性计数器
            _objStateStabilityCounter[detectedObject.Id] = stabilityCounter;

            // 只有当状态稳定达到阈值时才发布事件（首次达到阈值时发布）
            bool reachStableThresh = stabilityCounter == StateStabilityThreshold;
            
            // 根据当前状态设置属性
            switch (currentState)
            {
                case ObjectRegionState.Entering:
                    detectedObject.SetProperty("EnterRegion", true);
                    detectedObject.SetProperty("InRegion", false);
                    detectedObject.SetProperty("LeaveRegion", false);

                    GenerateDetectedObjectAnnotation(frame, detectedObject);

                    if (reachStableThresh)
                    {
                        ProcessEnteringEvent(frame, detectedObject, stabilityCounter);
                    }

                    break;

                case ObjectRegionState.Inside:
                    detectedObject.SetProperty("EnterRegion", false);
                    detectedObject.SetProperty("InRegion", true);
                    detectedObject.SetProperty("LeaveRegion", false);

                    GenerateDetectedObjectAnnotation(frame, detectedObject);

                    if (reachStableThresh)
                    {
                        ProcessInRegionEvent(frame, detectedObject, stabilityCounter);
                    }

                    break;

                case ObjectRegionState.Leaving:
                    detectedObject.SetProperty("EnterRegion", false);
                    detectedObject.SetProperty("InRegion", false);
                    detectedObject.SetProperty("LeaveRegion", true);

                    GenerateDetectedObjectAnnotation(frame, detectedObject);

                    if (reachStableThresh)
                    {
                        ProcessLeavingEvent(frame, detectedObject, stabilityCounter);
                    }

                    break;

                case ObjectRegionState.Outside:
                    detectedObject.SetProperty("EnterRegion", false);
                    detectedObject.SetProperty("InRegion", false);
                    detectedObject.SetProperty("LeaveRegion", false);

                    GenerateDetectedObjectAnnotation(frame, detectedObject);

                    break;
            }
            
            // 更新对象的当前状态
            _objRegionStates[detectedObject.Id] = currentState;
        }

        return new AnalysisResult(true);
    }

    private static ObjectRegionState CalcCurrentState(bool isFullyInside, bool isPartiallyInside, bool isObjInArea, ObjectRegionState previousState)
    {
        if (isFullyInside)
        {
            // 如果完全在区域内，且之前是 Outside/Leaving，则视为 Entering 过程的一部分（或者直接转为 Inside）
            // 根据原有逻辑：
            // Outside/Leaving -> Entering
            // Entering/Inside -> Inside
            // Default -> Inside
            if (previousState == ObjectRegionState.Outside || previousState == ObjectRegionState.Leaving)
                return ObjectRegionState.Entering;
            
            return ObjectRegionState.Inside;
        }
        
        if (isPartiallyInside)
        {
            if (isObjInArea)
            {
                // 中心在区域内
                // Outside/Leaving -> Entering
                // Entering -> Entering
                // Inside -> Inside
                if (previousState == ObjectRegionState.Inside)
                    return ObjectRegionState.Inside;

                return ObjectRegionState.Entering;
            }
            else
            {
                // 中心在区域外
                // Inside/Entering -> Leaving
                // Leaving -> Leaving
                // Outside -> Outside
                if (previousState == ObjectRegionState.Outside)
                    return ObjectRegionState.Outside;

                return ObjectRegionState.Leaving;
            }
        }
        
        // isFullyOutside
        // Inside/Entering -> Leaving
        // Leaving/Outside -> Outside
        if (previousState == ObjectRegionState.Inside || previousState == ObjectRegionState.Entering)
            return ObjectRegionState.Leaving;
            
        return ObjectRegionState.Outside;
    }

    private void ProcessEnteringEvent(
        Frame frame,
        DetectedObject detectedObject,
        int stabilityCounter)
    {
        ProcessRegionEventCommon(
            frame,
            detectedObject,
            stabilityCounter,
            EnteringEventName,
            _enterEventPublisher,
            (sourceId, evtName, algoName, objId, regionName) => new EnterRegionEvent(sourceId, evtName, algoName, objId, regionName),
            "entering");
    }

    private void ProcessInRegionEvent(
        Frame frame,
        DetectedObject detectedObject,
        int stabilityCounter)
    {
        ProcessRegionEventCommon(
            frame,
            detectedObject,
            stabilityCounter,
            InEventName,
            _inEventPublisher,
            (sourceId, evtName, algoName, objId, regionName) => new InRegionEvent(sourceId, evtName, algoName, objId, regionName),
            "inRegion");
    }

    private void ProcessLeavingEvent(
        Frame frame,
        DetectedObject detectedObject,
        int stabilityCounter)
    {
        ProcessRegionEventCommon(
            frame,
            detectedObject,
            stabilityCounter,
            LeavingEventName,
            _leaveEventPublisher,
            (sourceId, evtName, algoName, objId, regionName) => new LeaveRegionEvent(sourceId, evtName, algoName, objId, regionName),
            "leaving");
    }

    private void ProcessRegionEventCommon<TEvent>(
        Frame frame, 
        DetectedObject detectedObject, 
        int stabilityCounter,
        string eventName,
        IPublisher<TEvent> publisher,
        Func<string, string, string, string, string, TEvent> eventFactory,
        string filePrefix)
        where TEvent : DomainEvent, IAnnotatedAlgorithmEvent
    {
        Log.Information("{DetectedObjectId} is {EventName} region: '{RegionName}' (stable for {StabilityCounter} frames)", detectedObject.Id, eventName, RegionName, stabilityCounter);
        
        // 1. Create Event
        var domainEvent = eventFactory(frame.SourceId, eventName, AlgorithmName, detectedObject.Id, RegionName);
        domainEvent.ObjectGuid = Pipeline.QueryGuidByObjectId(detectedObject.Id);
        
        // 2. Serialize Annotations (Synchronously)
        var annotationJson = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        
        domainEvent.Annotations = annotationJson;

        TryQueueEvent(
            new EventPublicationRequest<TEvent>
            {
                Event = domainEvent,
                AnnotationJson = annotationJson,
                CloneSnapshot = () => frame.Scene.Clone(),
                FrameId = frame.FrameId,
                FilePrefix = filePrefix,
                RelativeDirectory = Path.Combine(
                    DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    detectedObject.Id),
                PublishInProcess = @event => publisher.Publish(@event),
                SaveSnapshot = WillSaveEventSnapshot,
                SaveVideoClip = WillSaveEventVideoClip
            });
    }

    protected override VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition));

        return annotation;
    }

    protected override VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;

        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        var rect = ObjAnnoGenerator.GenerateBBox(detectedObject);
        annotation.Shapes.Add(rect);

        var text = ObjAnnoGenerator.GenerateObjectText(detectedObject, fontSize:FontSize);
        annotation.Shapes.Add(text);

        if (detectedObject.HasProperty("EnterRegion") && detectedObject.GetProperty<bool>("EnterRegion"))
        {
            text.Content = $"{detectedObject.LocalId} Entering";
            text.Style ??= new Style();
            rect.Style ??= new Style();
            text.Style.Color = EnteringAnnotationColor;
            rect.Style.StrokeColor = EnteringAnnotationColor;
        }

        if (detectedObject.HasProperty("InRegion") && detectedObject.GetProperty<bool>("InRegion"))
        {
            text.Content = $"{detectedObject.LocalId} In Region";
            text.Style ??= new Style();
            rect.Style ??= new Style();
            text.Style.Color = InAnnotationColor;
            rect.Style.StrokeColor = InAnnotationColor;
        }

        if (detectedObject.HasProperty("LeaveRegion") && detectedObject.GetProperty<bool>("LeaveRegion"))
        {
            text.Content = $"{detectedObject.LocalId} Leaving";
            text.Style ??= new Style();
            rect.Style ??= new Style();
            text.Style.Color = LeavingAnnotationColor;
            rect.Style.StrokeColor = LeavingAnnotationColor;
        }

        return annotation;
    }

    public void ProcessEvent(ObjectExpiredEvent @event)
    {
        if (_objLastInRegionStatus.ContainsKey(@event.Id))
        {
            _objLastInRegionStatus.TryRemove(@event.Id, out _);
        }

        // 清理对象的状态和稳定性计数器
        if (_objRegionStates.ContainsKey(@event.Id))
        {
            _objRegionStates.TryRemove(@event.Id, out _);
        }

        if (_objStateStabilityCounter.ContainsKey(@event.Id))
        {
            _objStateStabilityCounter.TryRemove(@event.Id, out _);
        }
    }

    protected override void DisposeCore()
    {
        _objLastInRegionStatus.Clear();
        _objRegionStates.Clear();
        _objStateStabilityCounter.Clear();
    }
}
