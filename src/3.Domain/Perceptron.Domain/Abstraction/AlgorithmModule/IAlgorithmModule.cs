using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.AlgorithmModule;

public interface IAlgorithmModule : IDisposable
{
    public string AlgorithmName { get; }
    public string AlgorithmVersion { get; }
    public string AlgorithmDescription { get; }

    public bool IsInitialized { get; }

    bool Initialize();

    AnalysisResult Analyze(Frame frame);
}