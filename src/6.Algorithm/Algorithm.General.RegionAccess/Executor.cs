using Algorithm.Common;
using Algorithm.General.RegionAccess.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Abstraction.EventHandler;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Algorithm.General.RegionAccess;

public class Executor : AlgorithmBase, IEventSubscriber<ObjectExpiredEvent>
{
    private const string DefaultEventSnapshotDir = "Events/RegionAccess";
    private const string DefaultRegionName = "RestrictedArea";
    private static readonly List<string> DefaultRelativeTypes = [];
    private const int DefaultStateStabilityThreshold = 3;
    private const string DefaultEnteringEventName = "EnterRegion";
    private const string DefaultInEventName = "InRegion";
    private const string DefaultLeavingEventName = "LeaveRegion";
    private const int DefaultFontSize = 16;
    private const string DefaultEnteringAnnotationColor = "#FF0000";
    private const string DefaultInAnnotationColor = "#00FF00";
    private const string DefaultLeavingAnnotationColor = "#0000FF";
    private const bool DefaultWillSaveEventSnapshot = true;
    private const bool DefaultWillSaveEventVideoClip = false;

    public string EventSnapshotDir { get; }
    public string RegionName { get; }
    public List<string> RegionRelativeTypes { get; }

    public string EnteringEventName { get; }
    public string InEventName { get; }
    public string LeavingEventName { get; }

    public int FontSize { get; }
    public string EnteringAnnotationColor { get; }
    public string InAnnotationColor { get; }
    public string LeavingAnnotationColor { get; }

    public bool WillSaveEventSnapshot { get; }
    public bool WillSaveEventVideoClip { get; }

    private IPublisher<EnterRegionEvent> _enterEventPublisher;
    private IPublisher<InRegionEvent> _inEventPublisher;
    private IPublisher<LeaveRegionEvent> _leaveEventPublisher;

    private IMessagePoster _messagePoster;

    private ISubscriber<ObjectExpiredEvent> _oeSubscriber;
    private IDisposable _disposableOeSubscriber;

    private ConcurrentDictionary<string, ObjectRegionState> _objRegionStates = new();

    // 状态稳定性跟踪：记录每个对象在当前状态下的持续帧数
    private ConcurrentDictionary<string, int> _objStateStabilityCounter = new();

    // 状态变化阈值：对象状态需要稳定多少帧后才发布事件（默认3帧）
    private readonly int _stateStabilityThreshold;

    private ConcurrentDictionary<string, bool> _objLastInRegionStatus;

    private readonly AnalysisPipeline _pipeline;

    private IDetectedObjectAnnotationGenerator _objAnnoGenerator;
    private IRegionAnnotationGenerator _regionAnnoGenerator;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        _pipeline = pipeline;

        AlgorithmName = "Region Access";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects access to specific regions in the video stream.";

        EventSnapshotDir = PreferenceParser.ParseStringValue(preferences, "EventSnapshotDir", DefaultEventSnapshotDir);
        EventSnapshotDir.EnsureDirExistence();

        RegionName = PreferenceParser.ParseStringValue(preferences, "RegionName", DefaultRegionName);

        RegionRelativeTypes =
            PreferenceParser.ParseStringListValue(preferences, "RegionRelativeTypes", DefaultRelativeTypes);

        EnteringEventName =
            PreferenceParser.ParseStringValue(preferences, "EnteringEventName", DefaultEnteringEventName);

        InEventName = PreferenceParser.ParseStringValue(preferences, "InEventName", DefaultInEventName);

        LeavingEventName =
            PreferenceParser.ParseStringValue(preferences, "LeavingEventName", DefaultLeavingEventName);

        FontSize = PreferenceParser.ParseIntValue(preferences, "FontSize", DefaultFontSize);
        EnteringAnnotationColor = PreferenceParser.ParseStringValue(preferences, "EnteringAnnotationColor", DefaultEnteringAnnotationColor);
        InAnnotationColor = PreferenceParser.ParseStringValue(preferences, "InAnnotationColor", DefaultInAnnotationColor);
        LeavingAnnotationColor = PreferenceParser.ParseStringValue(preferences, "LeavingAnnotationColor", DefaultLeavingAnnotationColor);

        WillSaveEventSnapshot =
            PreferenceParser.ParseBoolValue(preferences, "WillSaveEventSnapshot", DefaultWillSaveEventSnapshot);

        WillSaveEventVideoClip =
            PreferenceParser.ParseBoolValue(preferences, "WillSaveEventVideoClip", DefaultWillSaveEventVideoClip);

        _stateStabilityThreshold =
            PreferenceParser.ParseIntValue(preferences, "StateStabilityThreshold", DefaultStateStabilityThreshold);
        _stateStabilityThreshold = Math.Max(1, _stateStabilityThreshold); // 确保阈值至少为1

        _objLastInRegionStatus = new ConcurrentDictionary<string, bool>();

        _objAnnoGenerator = new BasicObjectAnnotationGenerator();
        _regionAnnoGenerator = new BasicRegionAnnotationGenerator();
    }

    public override bool Initialize()
    {
        var provider = _pipeline.Provider;

        _enterEventPublisher = provider.GetRequiredService<IPublisher<EnterRegionEvent>>();
        _inEventPublisher = provider.GetRequiredService<IPublisher<InRegionEvent>>();
        _leaveEventPublisher = provider.GetRequiredService<IPublisher<LeaveRegionEvent>>();

        var subscriber = provider.GetService<ISubscriber<ObjectExpiredEvent>>();
        this.SetSubscriber(subscriber);

        _messagePoster = provider.GetRequiredService<IMessagePoster>();

        IsInitialized = true;

        return true;
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        var regionManager = _pipeline.RegionManagers.First(rm => rm.SourceId == frame.SourceId);
        var definition = regionManager.RegionDefinition;

        var snapshotManager = _pipeline.SnapshotManager;
        var repository = _pipeline.EventRepository;

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
            bool reachStableThresh = stabilityCounter == _stateStabilityThreshold;
            
            // 根据当前状态设置属性
            string now = DateTime.Now.ToString("yyyyMMddhhmmss");
            switch (currentState)
            {
                case ObjectRegionState.Entering:
                    detectedObject.SetProperty("EnterRegion", true);
                    detectedObject.SetProperty("InRegion", false);
                    detectedObject.SetProperty("LeaveRegion", false);

                    GenerateDetectedObjectAnnotation(frame, detectedObject);

                    if (reachStableThresh)
                    {
                        ProcessEnteringEvent(frame, detectedObject, stabilityCounter, now, snapshotManager, repository);
                    }

                    break;

                case ObjectRegionState.Inside:
                    detectedObject.SetProperty("EnterRegion", false);
                    detectedObject.SetProperty("InRegion", true);
                    detectedObject.SetProperty("LeaveRegion", false);

                    GenerateDetectedObjectAnnotation(frame, detectedObject);

                    if (reachStableThresh)
                    {
                        ProcessInRegionEvent(frame, detectedObject, stabilityCounter, now, snapshotManager, repository);
                    }

                    break;

                case ObjectRegionState.Leaving:
                    detectedObject.SetProperty("EnterRegion", false);
                    detectedObject.SetProperty("InRegion", false);
                    detectedObject.SetProperty("LeaveRegion", true);

                    GenerateDetectedObjectAnnotation(frame, detectedObject);

                    if (reachStableThresh)
                    {
                        ProcessLeavingEvent(frame, detectedObject, stabilityCounter, now, snapshotManager, repository);
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

        frame.Dispose();

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

    private void ProcessEnteringEvent(Frame frame, DetectedObject detectedObject, int stabilityCounter, string now,
        ISnapshotManager? snapshotManager, IEventRepository repository)
    {
        ProcessRegionEventCommon(
            frame, 
            detectedObject, 
            stabilityCounter, 
            now, 
            EnteringEventName,
            _enterEventPublisher,
            snapshotManager,
            repository,
            (sourceId, evtName, algoName, objId, regionName) => new EnterRegionEvent(sourceId, evtName, algoName, objId, regionName),
            "entering",
            (evt, imgPath, jsonPath) =>
            {
                if (evt is EnterRegionEvent enEvt)
                {
                    enEvt.ImageJsonLocalPath = jsonPath;
                }
            }
        );
    }

    private void ProcessInRegionEvent(Frame frame, DetectedObject detectedObject, int stabilityCounter, string now, 
        ISnapshotManager? snapshotManager, IEventRepository repository)
    {
        ProcessRegionEventCommon(
            frame, 
            detectedObject, 
            stabilityCounter, 
            now, 
            InEventName,
            _inEventPublisher,
            snapshotManager,
            repository,
            (sourceId, evtName, algoName, objId, regionName) => new InRegionEvent(sourceId, evtName, algoName, objId, regionName),
            "inRegion",
            (evt, imgPath, jsonPath) => 
            {
                if (evt is InRegionEvent inEvt)
                {
                    inEvt.ImageJsonLocalPath = jsonPath;
                }
            }
        );
    }

    private void ProcessLeavingEvent(Frame frame, DetectedObject detectedObject, int stabilityCounter, string now,
        ISnapshotManager? snapshotManager, IEventRepository repository)
    {
        ProcessRegionEventCommon(
            frame, 
            detectedObject, 
            stabilityCounter, 
            now, 
            LeavingEventName,
            _leaveEventPublisher,
            snapshotManager,
            repository,
            (sourceId, evtName, algoName, objId, regionName) => new LeaveRegionEvent(sourceId, evtName, algoName, objId, regionName),
            "leaving",
            (evt, imgPath, jsonPath) =>
            {
                if (evt is LeaveRegionEvent lvEvt)
                {
                    lvEvt.ImageJsonLocalPath = jsonPath;
                }
            }
        );
    }

    private void ProcessRegionEventCommon<TEvent>(
        Frame frame, 
        DetectedObject detectedObject, 
        int stabilityCounter, 
        string now, 
        string eventName,
        IPublisher<TEvent> publisher,
        ISnapshotManager? snapshotManager, 
        IEventRepository repository,
        Func<string, string, string, string, string, TEvent> eventFactory,
        string filePrefix,
        Action<TEvent, string, string>? extraPathSetter = null) 
        where TEvent : DomainEvent
    {
        Log.Information("{DetectedObjectId} is {EventName} region: '{RegionName}' (stable for {StabilityCounter} frames)", detectedObject.Id, eventName, RegionName, stabilityCounter);
        
        // 1. Create Event
        var domainEvent = eventFactory(frame.SourceId, eventName, AlgorithmName, detectedObject.Id, RegionName);
        domainEvent.ObjectGuid = _pipeline.QueryGuidByObjectId(detectedObject.Id);
        
        // 2. Serialize Annotations (Synchronously)
        var annotationJson = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        
        // Set Annotations property dynamically if it exists
        // (EnterRegionEvent, InRegionEvent, LeaveRegionEvent all have 'Annotations' property)
        if (domainEvent is EnterRegionEvent enterEvt) enterEvt.Annotations = annotationJson;
        else if (domainEvent is InRegionEvent inEvt) inEvt.Annotations = annotationJson;
        else if (domainEvent is LeaveRegionEvent leaveEvt) leaveEvt.Annotations = annotationJson;

        publisher.Publish(domainEvent);

        // 3. Prepare Snapshot (Synchronously - critical for thread safety)
        Mat? snapshot = null;
        if (WillSaveEventSnapshot)
        {
            // Clone the scene because frame.Scene might be disposed/reused in the main loop
            snapshot = frame.Scene.Clone();
        }
        
        var frameId = frame.FrameId;
        var objId = detectedObject.Id;

        // 4. Async Saving
        Task.Run(async () =>
        {
            try 
            {
                using (snapshot) // Ensure disposal of the cloned snapshot
                {
                    string savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"), objId);
                    savePath.EnsureDirExistence();

                    if (snapshot != null && !snapshot.IsDisposed)
                    {
                        string imagePath = Path.Combine(savePath, $"{filePrefix}_{now}.jpg");
                        snapshot.SaveImage(imagePath);

                        string annotationPath = Path.Combine(savePath, $"{filePrefix}_{now}.json");
                        File.WriteAllText(annotationPath, annotationJson);

                        // Set ImageLocalPath (Available on DomainEvent or specific events)
                        // Assuming DomainEvent or specific events have ImageLocalPath. 
                        // Using dynamic to avoid casting if base class support is unsure, 
                        // but strictly we should cast.
                        if (domainEvent is EnterRegionEvent e) e.ImageLocalPath = imagePath;
                        else if (domainEvent is InRegionEvent i) i.ImageLocalPath = imagePath;
                        else if (domainEvent is LeaveRegionEvent l) l.ImageLocalPath = imagePath;

                        extraPathSetter?.Invoke(domainEvent, imagePath, annotationPath);
                    }

                    if (WillSaveEventVideoClip && snapshotManager != null)
                    {
                        string videoPath = Path.Combine(savePath, $"{filePrefix}_{now}.mp4");
                        // Note: GenerateVideoClipAroundFrameAsync might be fire-and-forget or long running.
                        snapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, frameId);
                        
                        if (domainEvent is EnterRegionEvent e) e.VideoLocalPath = videoPath;
                        else if (domainEvent is InRegionEvent i) i.VideoLocalPath = videoPath;
                        else if (domainEvent is LeaveRegionEvent l) l.VideoLocalPath = videoPath;
                    }

                    await repository.SaveDomainEventAsync(domainEvent);

                    _messagePoster.PostDomainEventMessage(domainEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing region event {EventName} for object {ObjectId}", eventName, objId);
            }
        });
    }

    public VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        annotation.AddShapes(_regionAnnoGenerator.GenerateInterestAreas(regionDefinition));

        return annotation;
    }

    public VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;

        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        var rect = _objAnnoGenerator.GenerateBBox(detectedObject);
        annotation.Shapes.Add(rect);

        var text = _objAnnoGenerator.GenerateObjectText(detectedObject, fontSize:30);
        annotation.Shapes.Add(text);

        if (detectedObject.HasProperty("EnterRegion") && detectedObject.GetProperty<bool>("EnterRegion"))
        {
            text.Content = $"{detectedObject.LocalId} Entering";
            text.Style.Color = EnteringAnnotationColor;
            rect.Style.StrokeColor = EnteringAnnotationColor;
        }

        if (detectedObject.HasProperty("InRegion") && detectedObject.GetProperty<bool>("InRegion"))
        {
            text.Content = $"{detectedObject.LocalId} In Region";
            text.Style.Color = InAnnotationColor;
            rect.Style.StrokeColor = InAnnotationColor;
        }

        if (detectedObject.HasProperty("LeaveRegion") && detectedObject.GetProperty<bool>("LeaveRegion"))
        {
            text.Content = $"{detectedObject.LocalId} Leaving";
            text.Style.Color = LeavingAnnotationColor;
            rect.Style.StrokeColor = LeavingAnnotationColor;
        }

        return annotation;
    }

    public void SetSubscriber(ISubscriber<ObjectExpiredEvent> subscriber)
    {
        _oeSubscriber = subscriber;
        _disposableOeSubscriber = _oeSubscriber.Subscribe(ProcessEvent);
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

    public void Dispose()
    {
        _disposableOeSubscriber.Dispose();
    }
}
