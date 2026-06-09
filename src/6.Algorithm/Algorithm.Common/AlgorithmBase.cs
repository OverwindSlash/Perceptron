using MessagePipe;
using Perceptron.Domain.Abstraction.AlgorithmModule;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;

namespace Algorithm.Common;

public abstract class AlgorithmBase : IAlgorithmModule
{
    private readonly object _lifecycleSync = new();
    private readonly AlgorithmRuntimeDependencies? _providedDependencies;
    private AlgorithmSubscriptionRegistry _subscriptions = new();
    private AlgorithmEventDispatcher? _eventDispatcher;
    private int _initializationState;
    private int _isDisposed;

    public string AlgorithmName { get; protected set; } = string.Empty;
    public string AlgorithmVersion { get; protected set; } = string.Empty;
    public string AlgorithmDescription { get; protected set; } = string.Empty;
    public bool IsInitialized { get; protected set; }

    protected AnalysisPipeline Pipeline;
    protected IServiceProvider Services = null!;
    protected Dictionary<string, string> Preferences;

    protected IDetectedObjectAnnotationGenerator ObjAnnoGenerator;
    protected IRegionAnnotationGenerator RegionAnnoGenerator;

    protected bool WillGenerateBBox;
    protected string BBoxStrokeColor = string.Empty;
    protected int BBoxStrokeWidth;
    protected bool WillGenerateObjText;
    protected string ObjTextColor = string.Empty;
    protected int ObjTextFontSize;
    protected bool ShowLabel;
    protected bool ShowTrackingId;
    protected bool ShowConfidence;
    protected bool WillGenerateAnalysisAreas;
    protected string AnalysisAreaStrokeColor = string.Empty;
    protected bool WillGenerateExcludeAreas;
    protected string ExcludeAreaStrokeColor = string.Empty;
    protected bool WillGenerateLanes;
    protected string LanesStrokeColor = string.Empty;
    protected bool WillGenerateInterestAreas;
    protected string InterestAreasStrokeColor = string.Empty;
    protected bool WillGenerateCountLines;
    protected string EnterLineStrokeColor = string.Empty;
    protected int EnterLineStrokeWidth;
    protected string LeaveLineStrokeColor = string.Empty;
    protected int LeaveLineStrokeWidth;

    protected bool WillPublishEventMessage;
    protected bool WillSaveEventSnapshot;
    protected bool WillSaveEventVideoClip;
    protected int LocalEventIntervalSec;
    protected string EventSnapshotDir = string.Empty;
    protected string EventName = string.Empty;
    protected int EventTaskShutdownTimeoutSeconds;

    protected List<IRegionManager> RegionManagers = [];
    protected ISnapshotManager SnapshotManager = null!;
    protected IEventRepository EventRepository = null!;
    protected Perceptron.Domain.Abstraction.MessagePoster.IMessagePoster MessagePoster = null!;

    private DateTime _lastProcessTime = DateTime.MinValue;

    protected AlgorithmBase(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
    {
        Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        Preferences = preferences ?? new Dictionary<string, string>();
        ObjAnnoGenerator = new BasicObjectAnnotationGenerator();
        RegionAnnoGenerator = new BasicRegionAnnotationGenerator();
    }

    protected AlgorithmBase(
        AlgorithmRuntimeDependencies dependencies,
        Dictionary<string, string> preferences)
    {
        _providedDependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        Pipeline = null!;
        Preferences = preferences ?? new Dictionary<string, string>();
        ObjAnnoGenerator = new BasicObjectAnnotationGenerator();
        RegionAnnoGenerator = new BasicRegionAnnotationGenerator();
    }

    public bool Initialize()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _isDisposed) == 1,
                this);

            if (_initializationState == 2)
            {
                return true;
            }

            if (_initializationState == 1)
            {
                throw new InvalidOperationException(
                    $"Algorithm '{AlgorithmName}' initialization is already in progress.");
            }

            _initializationState = 1;
            try
            {
                ConfigureDefaultPreferences();
                ParseCommonPreferences();
                ResolveCommonServices();
                InitializeMode();
                InitializeCore();

                IsInitialized = true;
                _initializationState = 2;
                return true;
            }
            catch
            {
                IsInitialized = false;
                _initializationState = 0;
                try
                {
                    DisposeCore();
                }
                catch (Exception cleanupException)
                {
                    Log.Error(
                        cleanupException,
                        "Failed to roll back algorithm initialization. AlgorithmName: {AlgorithmName}",
                        AlgorithmName);
                }

                CleanupCommonResources();
                _subscriptions = new AlgorithmSubscriptionRegistry();
                throw;
            }
        }
    }

    protected virtual void ConfigureDefaultPreferences()
    {
    }

    protected virtual void InitializeMode()
    {
    }

    protected virtual void InitializeCore()
    {
    }

    private void ParseCommonPreferences()
    {
        ParseObjectAnnotationPreferences();
        ParseRegionAnnotationPreferences();
        ParseEventPreferences();
    }

    private void ParseObjectAnnotationPreferences()
    {
        WillGenerateBBox = PreferenceParser.ParseBoolValue(
            Preferences,
            "GenerateBBox",
            AlgorithmConstants.DefaultWillGenerateBBox);
        BBoxStrokeColor = PreferenceParser.ParseStringValue(
            Preferences,
            "BBoxStrokeColor",
            AlgorithmConstants.DefaultBBoxStrokeColor);
        BBoxStrokeWidth = PreferenceParser.ParseIntValue(
            Preferences,
            "BBoxStrokeWidth",
            AlgorithmConstants.DefaultBBoxStrokeWidth);

        WillGenerateObjText = PreferenceParser.ParseBoolValue(
            Preferences,
            "GenerateObjText",
            AlgorithmConstants.DefaultWillGenerateObjText);
        ObjTextColor = PreferenceParser.ParseStringValue(
            Preferences,
            "ObjTextColor",
            AlgorithmConstants.DefaultObjTextColor);
        ObjTextFontSize = PreferenceParser.ParseIntValue(
            Preferences,
            "ObjTextFontSize",
            AlgorithmConstants.DefaultObjTextFontSize);
        ShowLabel = PreferenceParser.ParseBoolValue(
            Preferences,
            "ObjTextShowLabel",
            AlgorithmConstants.DefaultShowLabel);
        ShowTrackingId = PreferenceParser.ParseBoolValue(
            Preferences,
            "ObjTextShowTrackingId",
            AlgorithmConstants.DefaultShowTrackingId);
        ShowConfidence = PreferenceParser.ParseBoolValue(
            Preferences,
            "ObjTextShowConfidence",
            AlgorithmConstants.DefaultShowConfidence);
    }

    private void ParseRegionAnnotationPreferences()
    {
        WillGenerateAnalysisAreas = PreferenceParser.ParseBoolValue(
            Preferences,
            "GenerateAnalysisAreas",
            AlgorithmConstants.DefaultWillGenerateAnalysisAreas);
        AnalysisAreaStrokeColor = PreferenceParser.ParseStringValue(
            Preferences,
            "AnalysisAreaStrokeColor",
            AlgorithmConstants.DefaultAnalysisAreaStrokeColor);
        WillGenerateExcludeAreas = PreferenceParser.ParseBoolValue(
            Preferences,
            "GenerateExcludeAreas",
            AlgorithmConstants.DefaultWillGenerateExcludeAreas);
        ExcludeAreaStrokeColor = PreferenceParser.ParseStringValue(
            Preferences,
            "ExcludeAreaStrokeColor",
            AlgorithmConstants.DefaultExcludeAreaStrokeColor);
        WillGenerateLanes = PreferenceParser.ParseBoolValue(
            Preferences,
            "GenerateLanes",
            AlgorithmConstants.DefaultWillGenerateLanes);
        LanesStrokeColor = PreferenceParser.ParseStringValue(
            Preferences,
            "LanesStrokeColor",
            AlgorithmConstants.DefaultLanesStrokeColor);
        WillGenerateInterestAreas = PreferenceParser.ParseBoolValue(
            Preferences,
            "GenerateInterestAreas",
            AlgorithmConstants.DefaultWillGenerateInterestAreas);
        InterestAreasStrokeColor = PreferenceParser.ParseStringValue(
            Preferences,
            "InterestAreasStrokeColor",
            AlgorithmConstants.DefaultInterestAreasStrokeColor);
        WillGenerateCountLines = PreferenceParser.ParseBoolValue(
            Preferences,
            "GenerateCountLines",
            AlgorithmConstants.DefaultWillGenerateCountLines);
        EnterLineStrokeColor = PreferenceParser.ParseStringValue(
            Preferences,
            "EnterLineStrokeColor",
            AlgorithmConstants.DefaultEnterLineStrokeColor);
        EnterLineStrokeWidth = PreferenceParser.ParseIntValue(
            Preferences,
            "EnterLineStrokeWidth",
            AlgorithmConstants.DefaultEnterLineWidth);
        LeaveLineStrokeColor = PreferenceParser.ParseStringValue(
            Preferences,
            "LeaveLineStrokeColor",
            AlgorithmConstants.DefaultLeaveLineStrokeColor);
        LeaveLineStrokeWidth = PreferenceParser.ParseIntValue(
            Preferences,
            "LeaveLineStrokeWidth",
            AlgorithmConstants.DefaultLeaveLineWidth);
    }

    private void ParseEventPreferences()
    {
        WillPublishEventMessage = PreferenceParser.ParseBoolValue(
            Preferences,
            "WillPublishEventMessage",
            AlgorithmConstants.DefaultWillPublishEventMessage);
        WillSaveEventSnapshot = PreferenceParser.ParseBoolValue(
            Preferences,
            "WillSaveEventSnapshot",
            AlgorithmConstants.DefaultWillSaveEventSnapshot);
        WillSaveEventVideoClip = PreferenceParser.ParseBoolValue(
            Preferences,
            "WillSaveEventVideoClip",
            AlgorithmConstants.DefaultWillSaveEventVideoClip);
        LocalEventIntervalSec = PreferenceParser.ParseIntValue(
            Preferences,
            "LocalEventIntervalSec",
            AlgorithmConstants.DefaultLocalEventIntervalSec);
        EventSnapshotDir = PreferenceParser.ParseStringValue(
            Preferences,
            "EventSnapshotDir",
            AlgorithmConstants.DefaultEventSnapshotDir);
        EventName = PreferenceParser.ParseStringValue(
            Preferences,
            "EventName",
            AlgorithmConstants.DefaultEventName);
        EventTaskShutdownTimeoutSeconds = PreferenceParser.ParseIntValue(
            Preferences,
            "EventTaskShutdownTimeoutSeconds",
            AlgorithmConstants.DefaultEventTaskShutdownTimeoutSeconds);
    }

    private void ResolveCommonServices()
    {
        var dependencies = _providedDependencies ??
            new AlgorithmRuntimeDependencies(
                Pipeline.Provider,
                Pipeline.RegionManagers,
                Pipeline.SnapshotManager,
                Pipeline.EventRepository,
                Pipeline.MessagePoster);

        Services = dependencies.Services;
        RegionManagers = [.. dependencies.RegionManagers];
        SnapshotManager = dependencies.SnapshotManager;
        EventRepository = dependencies.EventRepository;
        MessagePoster = dependencies.MessagePoster;
        _eventDispatcher = new AlgorithmEventDispatcher(
            EventRepository,
            MessagePoster,
            SnapshotManager,
            EventSnapshotDir,
            TimeSpan.FromSeconds(EventTaskShutdownTimeoutSeconds));
    }

    public AnalysisResult Analyze(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                $"Algorithm '{AlgorithmName}' has not been initialized.");
        }

        frame.Retain();
        try
        {
            return AnalyzeCore(frame);
        }
        finally
        {
            frame.Dispose();
        }
    }

    protected virtual AnalysisResult AnalyzeCore(Frame frame)
    {
        throw new NotSupportedException(
            $"Algorithm '{AlgorithmName}' must override AnalyzeCore().");
    }

    protected virtual VisualAnnotation GenerateDetectedObjectAnnotation(
        Frame frame,
        DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;
        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        if (WillGenerateBBox)
        {
            var rect = ObjAnnoGenerator.GenerateBBox(
                detectedObject,
                BBoxStrokeColor,
                BBoxStrokeWidth);
            annotation.Shapes.Add(rect);
        }

        if (WillGenerateObjText)
        {
            var text = ObjAnnoGenerator.GenerateObjectText(
                detectedObject,
                ObjTextColor,
                ObjTextFontSize,
                ShowLabel,
                ShowTrackingId,
                ShowConfidence);
            annotation.Shapes.Add(text);
        }

        return annotation;
    }

    protected virtual VisualAnnotation GenerateRegionAnnotation(
        Frame frame,
        ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;
        if (WillGenerateAnalysisAreas)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateAnalysisAreas(
                regionDefinition,
                AnalysisAreaStrokeColor));
        }

        if (WillGenerateExcludeAreas)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateExcludeAreas(
                regionDefinition,
                ExcludeAreaStrokeColor));
        }

        if (WillGenerateLanes)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateLanes(
                regionDefinition,
                LanesStrokeColor));
        }

        if (WillGenerateInterestAreas)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(
                regionDefinition,
                InterestAreasStrokeColor));
        }

        if (WillGenerateCountLines)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateCountLines(
                regionDefinition,
                EnterLineStrokeColor,
                LeaveLineStrokeColor));
        }

        return annotation;
    }

    protected virtual VisualAnnotation GenerateObjectLabelAnnotation(
        Frame frame,
        DetectedObject detectedObject)
    {
        return new VisualAnnotation(
            frame.SourceId,
            frame.UtcTimeStamp,
            frame.FrameId,
            frame.Scene.Width,
            frame.Scene.Height);
    }

    protected bool CheckLocalEventInterval() => ShouldSuppressLocalEvent();

    protected bool ShouldSuppressLocalEvent()
    {
        if ((DateTime.Now - _lastProcessTime).TotalSeconds < LocalEventIntervalSec)
        {
            return true;
        }

        _lastProcessTime = DateTime.Now;
        return false;
    }

    protected bool TryQueueEvent<TEvent>(EventPublicationRequest<TEvent> request)
        where TEvent : Perceptron.Domain.Event.DomainEvent
    {
        if (!WillPublishEventMessage)
        {
            return false;
        }

        return GetEventDispatcher().TryQueue(request);
    }

    protected bool TryQueueThrottledEvent<TEvent>(EventPublicationRequest<TEvent> request)
        where TEvent : Perceptron.Domain.Event.DomainEvent
    {
        if (!WillPublishEventMessage || ShouldSuppressLocalEvent())
        {
            return false;
        }

        return GetEventDispatcher().TryQueue(request);
    }

    protected IDisposable Subscribe<TMessage>(
        ISubscriber<TMessage> subscriber,
        Action<TMessage> handler) =>
        _subscriptions.Subscribe(subscriber, handler);

    protected void TrackSubscription(IDisposable subscription) =>
        _subscriptions.Add(subscription);

    private AlgorithmEventDispatcher GetEventDispatcher()
    {
        return _eventDispatcher ??
            throw new InvalidOperationException(
                $"Algorithm '{AlgorithmName}' event dispatcher is not initialized.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        try
        {
            DisposeCore();
        }
        finally
        {
            CleanupCommonResources();
        }
    }

    protected virtual void DisposeCore()
    {
    }

    private void CleanupCommonResources()
    {
        try
        {
            _subscriptions.Dispose();
        }
        finally
        {
            _eventDispatcher?.Dispose();
            _eventDispatcher = null;
        }
    }
}
