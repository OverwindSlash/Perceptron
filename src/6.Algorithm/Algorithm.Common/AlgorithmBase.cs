using Perceptron.Domain.Abstraction.AlgorithmModule;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Service.Pipeline;

namespace Algorithm.Common;

public abstract class AlgorithmBase : IAlgorithmModule
{
    public string AlgorithmName { get; protected set; }
    public string AlgorithmVersion { get; protected set; }
    public string AlgorithmDescription { get; protected set; }

    public bool IsInitialized { get; protected set; }

    protected AnalysisPipeline _pipeline;
    protected Dictionary<string, string> _preferences;

    protected AlgorithmBase(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _preferences = preferences ?? new Dictionary<string, string>();
    }

    public virtual bool Initialize()
    {
        IsInitialized = true;
        return IsInitialized;
    }
    
    public abstract AnalysisResult Analyze(Frame frame);

    public virtual void Dispose()
    {
        // Do nothing by default
    }
}