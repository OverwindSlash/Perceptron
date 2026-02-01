using Perceptron.Domain.Abstraction.AlgorithmModule;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Service.Pipeline;

namespace Algorithm.GenerateDebugAnnotations;

public class Executor : IAlgorithmModule, IDetectedObjectAnnotationGenerator, IRegionAnnotationGenerator, ILabelAnnotationGenerator
{
    public string AlgorithmName { get; }
    public string AlgorithmVersion { get; }
    public string AlgorithmDescription { get; }
    public bool IsInitialized { get; private set; }

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
    {
        AlgorithmName = "GenerateDebugAnnotations";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Generates debug annotations for detected objects, regions, and labels.";
        IsInitialized = false;
    }

    public bool Initialize()
    {
        IsInitialized = true;
        return IsInitialized;
    }

    public AnalysisResult Analyze(Frame frame)
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

        var rect = new Shape()
        {
            Id = "Obj_" + detectedObject.Id,
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
                StrokeColor = "#64EA6A",
            }
        };

        frame.Annotation.Shapes.Add(rect);

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

    public void Dispose()
    {
        
    }
}