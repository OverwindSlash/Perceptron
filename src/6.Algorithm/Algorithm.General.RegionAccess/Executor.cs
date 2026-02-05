using Algorithm.Common;
using Algorithm.General.RegionAccess.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Abstraction.EventHandler;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

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

    public bool Initialize()
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
            currentState = CalcCurrentState(isFullyInside, previousState, isPartiallyInside, isObjInArea);

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

        return new AnalysisResult(true);
    }

    private static ObjectRegionState CalcCurrentState(bool isFullyInside, ObjectRegionState previousState,
        bool isPartiallyInside, bool isObjInArea)
    {
        ObjectRegionState currentState;
        if (isFullyInside)
        {
            switch (previousState)
            {
                case ObjectRegionState.Outside:
                case ObjectRegionState.Leaving:
                    currentState = ObjectRegionState.Entering;
                    break;
                case ObjectRegionState.Entering:
                case ObjectRegionState.Inside:
                    currentState = ObjectRegionState.Inside;
                    break;
                default:
                    currentState = ObjectRegionState.Inside;
                    break;
            }
        }
        else if (isPartiallyInside)
        {
            if (isObjInArea)
            {
                switch (previousState)
                {
                    case ObjectRegionState.Outside:
                    case ObjectRegionState.Leaving:
                        currentState = ObjectRegionState.Entering;
                        break;
                    case ObjectRegionState.Entering:
                        currentState = ObjectRegionState.Entering;
                        break;
                    case ObjectRegionState.Inside:
                        currentState = ObjectRegionState.Inside;
                        break;
                    default:
                        currentState = ObjectRegionState.Entering;
                        break;
                }
            }
            else
            {
                switch (previousState)
                {
                    case ObjectRegionState.Inside:
                    case ObjectRegionState.Entering:
                        currentState = ObjectRegionState.Leaving;
                        break;
                    case ObjectRegionState.Leaving:
                        currentState = ObjectRegionState.Leaving;
                        break;
                    case ObjectRegionState.Outside:
                        currentState = ObjectRegionState.Outside;
                        break;
                    default:
                        currentState = ObjectRegionState.Leaving;
                        break;
                }
            }
        }
        else // isFullyOutside
        {
            switch (previousState)
            {
                case ObjectRegionState.Inside:
                case ObjectRegionState.Entering:
                    currentState = ObjectRegionState.Leaving;
                    break;
                case ObjectRegionState.Leaving:
                case ObjectRegionState.Outside:
                    currentState = ObjectRegionState.Outside;
                    break;
                default:
                    currentState = ObjectRegionState.Outside;
                    break;
            }
        }

        return currentState;
    }

    private void ProcessEnteringEvent(Frame frame, DetectedObject detectedObject, int stabilityCounter, string now,
        ISnapshotManager? snapshotManager, IEventRepository repository)
    {
        Log.Information("{DetectedObjectId} is ENTERING region: '{RegionName}' (stable for {StabilityCounter} frames)", detectedObject.Id, RegionName, stabilityCounter);
        
        // Create and publish event.
        var enterRegionEvent = new EnterRegionEvent(frame.SourceId, EnteringEventName, AlgorithmName, detectedObject.Id, RegionName);
        enterRegionEvent.ObjectGuid = _pipeline.QueryGuidByObjectId(detectedObject.Id);
        enterRegionEvent.Annotations = JsonSerializer.Serialize(frame.Annotation, _jsonOptions);
        _enterEventPublisher.Publish(enterRegionEvent);

        // Save snapshot and video clip asynchronously.
        Task.Run(async () =>
        {
            string savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"), detectedObject.Id);
            savePath.EnsureDirExistence();

            if (WillSaveEventSnapshot)
            {
                using var eventScene = frame.Scene.Clone();
                string imagePath = Path.Combine(savePath, $"entering_{now}.jpg");
                eventScene.SaveImage(imagePath);

                // save annotations to local file
                string annotationPath = Path.Combine(savePath, $"entering_{now}.json");
                File.WriteAllText(annotationPath, JsonSerializer.Serialize(frame.Annotation, _jsonOptions));

                enterRegionEvent.ImageLocalPath = imagePath;
            }

            if (WillSaveEventVideoClip)
            {
                string videoPath = Path.Combine(savePath, $"entering_{now}.mp4");
                snapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, frame.FrameId);
                enterRegionEvent.VideoLocalPath = videoPath;
            }

            await repository.SaveDomainEventAsync(enterRegionEvent);

            _messagePoster.PostDomainEventMessage(enterRegionEvent);
        });
    }

    private void ProcessInRegionEvent(Frame frame, DetectedObject detectedObject, int stabilityCounter, string now, 
        ISnapshotManager? snapshotManager, IEventRepository repository)
    {
        Log.Information("{DetectedObjectId} is IN region: '{RegionName}' (stable for {StabilityCounter} frames)", detectedObject.Id, RegionName, stabilityCounter);

        // Create and publish event.
        var inRegionEvent = new InRegionEvent(frame.SourceId, InEventName, AlgorithmName, detectedObject.Id, RegionName);
        inRegionEvent.ObjectGuid = _pipeline.QueryGuidByObjectId(detectedObject.Id);
        inRegionEvent.Annotations = JsonSerializer.Serialize(frame.Annotation, _jsonOptions);
        _inEventPublisher.Publish(inRegionEvent);

        // Save snapshot asynchronously.
        Task.Run(async () =>
        {
            string savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"), detectedObject.Id);
            savePath.EnsureDirExistence();

            if (WillSaveEventSnapshot)
            {
                var eventScene = frame.Scene.Clone();
                string imagePath = Path.Combine(savePath, $"inRegion_{now}.jpg");
                eventScene.SaveImage(imagePath);

                // save annotations to local file
                string annotationPath = Path.Combine(savePath, $"inRegion_{now}.json");
                File.WriteAllText(annotationPath, JsonSerializer.Serialize(frame.Annotation, _jsonOptions));

                inRegionEvent.ImageLocalPath = imagePath;
                inRegionEvent.ImageJsonLocalPath = annotationPath;
            }

            if (WillSaveEventVideoClip)
            {
                string videoPath = Path.Combine(savePath, $"inRegion_{now}.mp4");
                snapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, frame.FrameId);
                inRegionEvent.VideoLocalPath = videoPath;
            }

            await repository.SaveDomainEventAsync(inRegionEvent);

            _messagePoster.PostDomainEventMessage(inRegionEvent);
        });
    }

    private void ProcessLeavingEvent(Frame frame, DetectedObject detectedObject, int stabilityCounter, string now,
        ISnapshotManager? snapshotManager, IEventRepository repository)
    {
        Log.Information("{DetectedObjectId} is LEAVING region: '{RegionName}' (stable for {StabilityCounter} frames)", detectedObject.Id, RegionName, stabilityCounter);

        // Create and publish event.
        var leaveRegionEvent = new LeaveRegionEvent(frame.SourceId, LeavingEventName, AlgorithmName, detectedObject.Id, RegionName);
        leaveRegionEvent.ObjectGuid = _pipeline.QueryGuidByObjectId(detectedObject.Id);
        leaveRegionEvent.Annotations = JsonSerializer.Serialize(frame.Annotation, _jsonOptions);
        _leaveEventPublisher.Publish(leaveRegionEvent);

        // Save snapshot and video clip asynchronously.
        Task.Run(async () =>
        {
            string savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"), detectedObject.Id);
            savePath.EnsureDirExistence();

            if (WillSaveEventSnapshot)
            {
                var eventScene = frame.Scene.Clone();
                string imagePath = Path.Combine(savePath, $"leaving_{now}.jpg");
                eventScene.SaveImage(imagePath);
                leaveRegionEvent.ImageLocalPath = imagePath;

                // save annotations to local file
                string annotationPath = Path.Combine(savePath, $"leaving_{now}.json");
                File.WriteAllText(annotationPath, JsonSerializer.Serialize(frame.Annotation, _jsonOptions));
            }

            if (WillSaveEventVideoClip)
            {
                string videoPath = Path.Combine(savePath, $"leaving_{now}.mp4");
                snapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, frame.FrameId);
                leaveRegionEvent.VideoLocalPath = videoPath;
            }

            await repository.SaveDomainEventAsync(leaveRegionEvent);

            _messagePoster.PostDomainEventMessage(leaveRegionEvent);
        });
    }

    public VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        //if (_willGenerateAnalysisAreas)
        //{
        //    annotation.AddShapes(_regionAnnoGenerator.GenerateAnalysisAreas(regionDefinition, _analysisAreaStrokeColor));
        //}

        //if (_willGenerateExcludeAreas)
        //{
        //    annotation.AddShapes(_regionAnnoGenerator.GenerateExcludeAreas(regionDefinition, _excludeAreaStrokeColor));
        //}

        //if (_willGenerateLanes)
        //{
        //    annotation.AddShapes(_regionAnnoGenerator.GenerateLanes(regionDefinition, _lanesStrokeColor));
        //}

        //if (_willGenerateInterestAreas)
        //{
        //    annotation.AddShapes(_regionAnnoGenerator.GenerateInterestAreas(regionDefinition, _interestAreasStrokeColor));
        //}

        //if (_willGenerateCountLines)
        //{
        //    annotation.AddShapes(_regionAnnoGenerator.GenerateCountLines(regionDefinition, _enterLineStrokeColor, _leaveLineStrokeColor));
        //}

        return annotation;
    }

    public VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;

        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        //// bbox annotation
        //if (_willGenerateBBox)
        //{
        //    var rect = _objAnnoGenerator.GenerateBBox(detectedObject, _bBoxStrokeColor, _bBoxStrokeWidth);
        //    annotation.Shapes.Add(rect);
        //}

        //// object text annotation
        //if (_willGenerateObjText)
        //{
        //    var text = _objAnnoGenerator.GenerateObjectText(detectedObject, _objTextColor, _objTextFontSize,
        //        _showLabel, _showTrackingId, _showConfidence);
        //    annotation.Shapes.Add(text);
        //}

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
