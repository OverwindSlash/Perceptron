using Perceptron.Domain.Abstraction.AlgorithmModule;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Abstraction.MessagePoster;
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

namespace Algorithm.Common;

public abstract class AlgorithmBase : IAlgorithmModule
{
    public string AlgorithmName { get; protected set; }
    public string AlgorithmVersion { get; protected set; }
    public string AlgorithmDescription { get; protected set; }

    public bool IsInitialized { get; protected set; }

    protected AnalysisPipeline Pipeline;
    protected Dictionary<string, string> Preferences;

    // common functions
    protected IDetectedObjectAnnotationGenerator ObjAnnoGenerator;
    protected IRegionAnnotationGenerator RegionAnnoGenerator;

    // annotation generation preferences
    protected bool WillGenerateBBox;
    protected string BBoxStrokeColor;
    protected int BBoxStrokeWidth;

    protected bool WillGenerateObjText;
    protected string ObjTextColor;
    protected int ObjTextFontSize;
    protected bool ShowLabel;
    protected bool ShowTrackingId;
    protected bool ShowConfidence;

    protected bool WillGenerateAnalysisAreas;
    protected string AnalysisAreaStrokeColor;

    protected bool WillGenerateExcludeAreas;
    protected string ExcludeAreaStrokeColor;

    protected bool WillGenerateLanes;
    protected string LanesStrokeColor;

    protected bool WillGenerateInterestAreas;
    protected string InterestAreasStrokeColor;

    protected bool WillGenerateCountLines;
    protected string EnterLineStrokeColor;
    protected int EnterLineStrokeWidth;
    protected string LeaveLineStrokeColor;
    protected int LeaveLineStrokeWidth;

    // event generation preferences
    protected bool WillPublishEventMessage;
    protected bool WillSaveEventSnapshot;
    protected bool WillSaveEventVideoClip;
    protected int LocalEventIntervalSec;
    protected string EventSnapshotDir;
    protected string EventName;

    protected List<IRegionManager> RegionManagers;
    protected ISnapshotManager SnapshotManager;
    protected IEventRepository EventRepository;
    protected IMessagePoster MessagePoster;

    // for local event interval check
    private DateTime _lastProcessTime = DateTime.MinValue;

    protected AlgorithmBase(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
    {
        Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        Preferences = preferences ?? new Dictionary<string, string>();

        ObjAnnoGenerator = new BasicObjectAnnotationGenerator();
        RegionAnnoGenerator = new BasicRegionAnnotationGenerator();
    }

    public virtual bool Initialize()
    {
        WillGenerateBBox = PreferenceParser.ParseBoolValue(Preferences, "GenerateBBox", AlgorithmConstants.DefaultWillGenerateBBox);
        BBoxStrokeColor = PreferenceParser.ParseStringValue(Preferences, "BBoxStrokeColor", AlgorithmConstants.DefaultBBoxStrokeColor);
        BBoxStrokeWidth = PreferenceParser.ParseIntValue(Preferences, "BBoxStrokeWidth", AlgorithmConstants.DefaultBBoxStrokeWidth);

        WillGenerateObjText = PreferenceParser.ParseBoolValue(Preferences, "GenerateObjText", AlgorithmConstants.DefaultWillGenerateObjText);
        ObjTextColor = PreferenceParser.ParseStringValue(Preferences, "ObjTextColor", AlgorithmConstants.DefaultObjTextColor);
        ObjTextFontSize = PreferenceParser.ParseIntValue(Preferences, "ObjTextFontSize", AlgorithmConstants.DefaultObjTextFontSize);
        ShowLabel = PreferenceParser.ParseBoolValue(Preferences, "ObjTextShowLabel", AlgorithmConstants.DefaultShowLabel);
        ShowTrackingId = PreferenceParser.ParseBoolValue(Preferences, "ObjTextShowTrackingId", AlgorithmConstants.DefaultShowTrackingId);
        ShowConfidence = PreferenceParser.ParseBoolValue(Preferences, "ObjTextShowConfidence", AlgorithmConstants.DefaultShowConfidence);

        WillGenerateAnalysisAreas = PreferenceParser.ParseBoolValue(Preferences, "GenerateAnalysisAreas", AlgorithmConstants.DefaultWillGenerateAnalysisAreas);
        AnalysisAreaStrokeColor = PreferenceParser.ParseStringValue(Preferences, "AnalysisAreaStrokeColor", AlgorithmConstants.DefaultAnalysisAreaStrokeColor);

        WillGenerateExcludeAreas = PreferenceParser.ParseBoolValue(Preferences, "GenerateExcludeAreas", AlgorithmConstants.DefaultWillGenerateExcludeAreas);
        ExcludeAreaStrokeColor = PreferenceParser.ParseStringValue(Preferences, "ExcludeAreaStrokeColor", AlgorithmConstants.DefaultExcludeAreaStrokeColor);

        WillGenerateLanes = PreferenceParser.ParseBoolValue(Preferences, "GenerateLanes", AlgorithmConstants.DefaultWillGenerateLanes);
        LanesStrokeColor = PreferenceParser.ParseStringValue(Preferences, "LanesStrokeColor", AlgorithmConstants.DefaultLanesStrokeColor);

        WillGenerateInterestAreas = PreferenceParser.ParseBoolValue(Preferences, "GenerateInterestAreas", AlgorithmConstants.DefaultWillGenerateInterestAreas);
        InterestAreasStrokeColor = PreferenceParser.ParseStringValue(Preferences, "InterestAreasStrokeColor", AlgorithmConstants.DefaultInterestAreasStrokeColor);

        WillGenerateCountLines = PreferenceParser.ParseBoolValue(Preferences, "GenerateCountLines", AlgorithmConstants.DefaultWillGenerateCountLines);
        EnterLineStrokeColor = PreferenceParser.ParseStringValue(Preferences, "EnterLineStrokeColor", AlgorithmConstants.DefaultEnterLineStrokeColor);
        EnterLineStrokeWidth = PreferenceParser.ParseIntValue(Preferences, "EnterLineStrokeWidth", AlgorithmConstants.DefaultEnterLineWidth);
        LeaveLineStrokeColor = PreferenceParser.ParseStringValue(Preferences, "LeaveLineStrokeColor", AlgorithmConstants.DefaultLeaveLineStrokeColor);
        LeaveLineStrokeWidth = PreferenceParser.ParseIntValue(Preferences, "LeaveLineStrokeWidth", AlgorithmConstants.DefaultLeaveLineWidth);

        WillPublishEventMessage = PreferenceParser.ParseBoolValue(Preferences, "WillPublishEventMessage", AlgorithmConstants.DefaultWillPublishEventMessage);
        WillSaveEventSnapshot = PreferenceParser.ParseBoolValue(Preferences, "WillSaveEventSnapshot", AlgorithmConstants.DefaultWillSaveEventSnapshot);
        WillSaveEventVideoClip = PreferenceParser.ParseBoolValue(Preferences, "WillSaveEventVideoClip", AlgorithmConstants.DefaultWillSaveEventVideoClip);
        LocalEventIntervalSec = PreferenceParser.ParseIntValue(Preferences, "LocalEventIntervalSec", AlgorithmConstants.DefaultLocalEventIntervalSec);
        EventSnapshotDir = PreferenceParser.ParseStringValue(Preferences, "EventSnapshotDir", AlgorithmConstants.DefaultEventSnapshotDir);
        EventName = PreferenceParser.ParseStringValue(Preferences, "EventName", AlgorithmConstants.DefaultEventName);

        RegionManagers = Pipeline.RegionManagers;
        SnapshotManager = Pipeline.SnapshotManager;
        EventRepository = Pipeline.EventRepository;
        MessagePoster = Pipeline.MessagePoster;

        IsInitialized = true;
        return IsInitialized;
    }
    
    public abstract AnalysisResult Analyze(Frame frame);

    protected virtual VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;

        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        // bbox annotation
        if (WillGenerateBBox)
        {
            var rect = ObjAnnoGenerator.GenerateBBox(detectedObject, BBoxStrokeColor, BBoxStrokeWidth);
            annotation.Shapes.Add(rect);
        }

        // object text annotation
        if (WillGenerateObjText)
        {
            var text = ObjAnnoGenerator.GenerateObjectText(detectedObject, ObjTextColor, ObjTextFontSize,
                ShowLabel, ShowTrackingId, ShowConfidence);
            annotation.Shapes.Add(text);
        }

        return annotation;
    }

    protected virtual VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        if (WillGenerateAnalysisAreas)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateAnalysisAreas(regionDefinition, AnalysisAreaStrokeColor));
        }

        if (WillGenerateExcludeAreas)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateExcludeAreas(regionDefinition, ExcludeAreaStrokeColor));
        }

        if (WillGenerateLanes)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateLanes(regionDefinition, LanesStrokeColor));
        }

        if (WillGenerateInterestAreas)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition, InterestAreasStrokeColor));
        }

        if (WillGenerateCountLines)
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateCountLines(regionDefinition, EnterLineStrokeColor, LeaveLineStrokeColor));
        }

        return annotation;
    }

    protected virtual VisualAnnotation GenerateObjectLabelAnnotation(Frame frame, DetectedObject detectedObject)
    {
        // TODO: Implement a debug annotation that adds a label to the frame.

        return new VisualAnnotation(frame.SourceId, frame.UtcTimeStamp,
            frame.FrameId, frame.Scene.Width, frame.Scene.Height);
    }

    protected bool CheckLocalEventInterval()
    {
        if ((DateTime.Now - _lastProcessTime).TotalSeconds < LocalEventIntervalSec)
        {
            return true;
        }

        _lastProcessTime = DateTime.Now;
        return false;
    }

    public virtual void Dispose()
    {
        // Do nothing by default
    }
}