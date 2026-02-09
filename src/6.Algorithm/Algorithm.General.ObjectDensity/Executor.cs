using Algorithm.Common;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Service.Pipeline;

namespace Algorithm.General.ObjectDensity;

public class Executor : AlgorithmBase
{
    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences) 
        : base(pipeline, preferences)
    {
        _pipeline = pipeline;

        AlgorithmName = "Object Density";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects object density in video frames.";
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        throw new NotImplementedException();
    }
}