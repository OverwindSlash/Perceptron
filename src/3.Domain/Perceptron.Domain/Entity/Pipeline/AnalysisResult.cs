namespace Perceptron.Domain.Entity.Pipeline;

public class AnalysisResult(bool success)
{
    public bool Success { get; private set; } = success;
    public string? ErrorMessage { get; private set; }
}