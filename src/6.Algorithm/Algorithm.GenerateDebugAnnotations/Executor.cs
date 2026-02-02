using Algorithm.Common;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;

namespace Algorithm.GenerateDebugAnnotations;

public class Executor : AlgorithmBase
{
    private IDetectedObjectAnnotationGenerator _objAnnoGenerator;
    private IRegionAnnotationGenerator _regionAnnoGenerator;

    private bool _willGenerateBBox;
    private string _bBoxStrokeColor;
    private int _bBoxStrokeWidth;

    private bool _willGenerateObjText;
    private string _objTextColor;
    private int _objTextFontSize;
    private bool _showLabel;
    private bool _showTrackingId;
    private bool _showConfidence;

    private bool _willGenerateAnalysisAreas;
    private string _analysisAreaStrokeColor;

    private bool _willGenerateExcludeAreas;
    private string _excludeAreaStrokeColor;

    private bool _willGenerateLanes;
    private string _lanesStrokeColor;

    private bool _willGenerateInterestAreas;
    private string _interestAreasStrokeColor;

    private bool _willGenerateCountLines;
    private string _enterLineStrokeColor;
    private string _leaveLineStrokeColor;

    private List<IRegionManager?> _regionManagers;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "GenerateDebugAnnotations";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Generates debug annotations for detected objects, regions, and labels.";

        _objAnnoGenerator = new BasicObjectAnnotationGenerator();
        _regionAnnoGenerator = new BasicRegionAnnotationGenerator();
    }

    public override bool Initialize()
    {
        _willGenerateBBox = PreferenceParser.ParseBoolValue(_preferences, "GenerateBBox", true);
        _bBoxStrokeColor = PreferenceParser.ParseStringValue(_preferences, "BBoxStrokeColor", "#8fce00");
        _bBoxStrokeWidth = PreferenceParser.ParseIntValue(_preferences, "BBoxStrokeWidth", 1);

        _willGenerateObjText = PreferenceParser.ParseBoolValue(_preferences, "GenerateObjText", true);
        _objTextColor = PreferenceParser.ParseStringValue(_preferences, "ObjTextColor", "#ffea00");
        _objTextFontSize = PreferenceParser.ParseIntValue(_preferences, "ObjTextFontSize", 20);
        _showLabel = PreferenceParser.ParseBoolValue(_preferences, "ObjTextShowLabel", true);
        _showTrackingId = PreferenceParser.ParseBoolValue(_preferences, "ObjTextShowTrackingId", true);
        _showConfidence = PreferenceParser.ParseBoolValue(_preferences, "ObjTextShowConfidence", false);

        _willGenerateAnalysisAreas = PreferenceParser.ParseBoolValue(_preferences, "GenerateAnalysisAreas", true);
        _analysisAreaStrokeColor = PreferenceParser.ParseStringValue(_preferences, "AnalysisAreaStrokeColor", "#7dda58");

        _willGenerateExcludeAreas = PreferenceParser.ParseBoolValue(_preferences, "GenerateExcludeAreas", true);
        _excludeAreaStrokeColor = PreferenceParser.ParseStringValue(_preferences, "ExcludeAreaStrokeColor", "#e36667");

        _willGenerateLanes = PreferenceParser.ParseBoolValue(_preferences, "GenerateLanes", true);
        _lanesStrokeColor = PreferenceParser.ParseStringValue(_preferences, "LanesStrokeColor", "#e8e8e8");

        _willGenerateInterestAreas = PreferenceParser.ParseBoolValue(_preferences, "GenerateInterestAreas", true);
        _interestAreasStrokeColor = PreferenceParser.ParseStringValue(_preferences, "InterestAreasStrokeColor", "#ffeca1");

        _willGenerateCountLines = PreferenceParser.ParseBoolValue(_preferences, "GenerateCountLines", true);
        _enterLineStrokeColor = PreferenceParser.ParseStringValue(_preferences, "EnterLineStrokeColor", "#4e4e4e");
        _leaveLineStrokeColor = PreferenceParser.ParseStringValue(_preferences, "LeaveLineStrokeColor", "#4e4e4e");

        _regionManagers = _pipeline.RegionManagers;

        return base.Initialize();
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        var regionManager = _regionManagers.First(rm => rm.SourceId == frame.SourceId);
        GenerateRegionAnnotation(frame, regionManager.RegionDefinition);

        foreach (var detectedObject in frame.DetectedObjects)
        {
            GenerateDetectedObjectAnnotation(frame, detectedObject);
        }

        return new AnalysisResult(true);
    }

    public VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;

        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        // bbox annotation
        if (_willGenerateBBox)
        {
            var rect = _objAnnoGenerator.GenerateBBox(detectedObject, _bBoxStrokeColor, _bBoxStrokeWidth);
            annotation.Shapes.Add(rect);
        }

        // object text annotation
        if (_willGenerateObjText)
        {
            var text = _objAnnoGenerator.GenerateObjectText(detectedObject, _objTextColor, _objTextFontSize,
                _showLabel, _showTrackingId, _showConfidence);
            annotation.Shapes.Add(text);
        }

        return annotation;
    }

    public VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        if (_willGenerateAnalysisAreas)
        {
            annotation.AddShapes(_regionAnnoGenerator.GenerateAnalysisAreas(regionDefinition, _analysisAreaStrokeColor));
        }

        if (_willGenerateExcludeAreas)
        {
            annotation.AddShapes(_regionAnnoGenerator.GenerateExcludeAreas(regionDefinition, _excludeAreaStrokeColor));
        }

        if (_willGenerateLanes)
        {
            annotation.AddShapes(_regionAnnoGenerator.GenerateLanes(regionDefinition, _lanesStrokeColor));
        }

        if (_willGenerateInterestAreas)
        {
            annotation.AddShapes(_regionAnnoGenerator.GenerateInterestAreas(regionDefinition, _interestAreasStrokeColor));
        }

        if (_willGenerateCountLines)
        {
            annotation.AddShapes(_regionAnnoGenerator.GenerateCountLines(regionDefinition, _enterLineStrokeColor, _leaveLineStrokeColor));
        }

        return annotation;
    }

    public VisualAnnotation GenerateLabelAnnotation(Frame frame)
    {
        // TODO: Implement a debug annotation that adds a label to the frame.

        return new VisualAnnotation(frame.SourceId, frame.UtcTimeStamp,
            frame.FrameId, frame.Scene.Width, frame.Scene.Height);
    }

    public override void Dispose()
    {
        
    }
}