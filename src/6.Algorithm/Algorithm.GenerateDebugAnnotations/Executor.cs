using Algorithm.Common;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;

namespace Algorithm.GenerateDebugAnnotations;

public class Executor : AlgorithmBase, IDetectedObjectAnnotationGenerator, IRegionAnnotationGenerator, ILabelAnnotationGenerator
{
    public string AlgorithmName { get; }
    public string AlgorithmVersion { get; }
    public string AlgorithmDescription { get; }

    public bool IsInitialized { get; private set; }

    private bool _willGenerateBBox;
    private string _bBoxStrokeColor;
    private int _bBoxStrokeWidth;

    private bool _willGenerateObjText;
    private string _objTextColor;
    private int _objTextFontSize;
    private bool _showLabel;
    private bool _showTrackingId;
    private bool _showConfidence;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "GenerateDebugAnnotations";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Generates debug annotations for detected objects, regions, and labels.";
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

        return base.Initialize();
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        foreach (var detectedObject in frame.DetectedObjects)
        {
            GenerateDetectedObjectAnnotation(frame, detectedObject);
        }

        return new AnalysisResult(true);
    }

    public VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject)
    {
        //if (!detectedObject.IsUnderAnalysis)
        //{
        //    return frame.Annotation;
        //}
        
        var bbox = detectedObject.Bbox;

        // bbox annotation
        if (_willGenerateBBox)
        {
            var rect = new Shape()
            {
                Id = "bbox_" + detectedObject.Id,
                Type = "rect",
                Origin = new Origin()
                {
                    X = bbox.X,
                    Y = bbox.Y
                },
                Size = new Size()
                {
                    Width = bbox.Width,
                    Height = bbox.Height
                },
                Style = new Style()
                {
                    StrokeColor = _bBoxStrokeColor,
                    StrokeWidth = _bBoxStrokeWidth
                }
            };

            frame.Annotation.Shapes.Add(rect);
        }

        if (_willGenerateObjText)
        {
            string content = string.Empty;
            if (_showLabel)
            {
                content += $"{detectedObject.Label}_";
            }

            if (_showTrackingId)
            {
                content += $"{detectedObject.TrackingId}_";
            }

            if (_showConfidence)
            {
                content += $"{detectedObject.Confidence:F2}_";
            }

            // text annotation
            var text = new Shape()
            {
                Id = "text_" + detectedObject.Id,
                Type = "text",
                Content = content.TrimEnd('_'),
                Position = new Position()
                {
                    X = bbox.X,
                    Y = bbox.Y - _objTextFontSize
                },
                Style = new Style()
                {
                    Color = _objTextColor,
                    FontSize = _objTextFontSize,
                }
            };

            frame.Annotation.Shapes.Add(text);
        }

        return frame.Annotation;
    }

    public VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        // TODO: Implement a debug annotation that highlights the region definition on the frame.

        return new VisualAnnotation(frame.SourceId, frame.UtcTimeStamp,
            frame.FrameId, frame.Scene.Width, frame.Scene.Height);
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