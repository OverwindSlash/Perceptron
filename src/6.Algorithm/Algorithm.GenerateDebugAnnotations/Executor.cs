using Algorithm.Common;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Service.Pipeline;

namespace Algorithm.GenerateDebugAnnotations;

public class Executor : AlgorithmBase
{
    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "GenerateDebugAnnotations";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Generates debug annotations for detected objects, regions, and labels.";
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var regionManager = RegionManagers.First(rm => rm.SourceId == frame.SourceId);
        GenerateRegionAnnotation(frame, regionManager.RegionDefinition);

        foreach (var detectedObject in frame.DetectedObjects)
        {
            GenerateDetectedObjectAnnotation(frame, detectedObject);
        }

        return new AnalysisResult(true);
    }
}
