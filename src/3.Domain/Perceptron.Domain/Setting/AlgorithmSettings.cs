namespace Perceptron.Domain.Setting;

public class AlgorithmSettings
{
    public string AssemblyFile { get; set; }
    public string FullQualifiedClassName { get; set; }

    public Dictionary<string, string> Preferences { get; set; }

    public AlgorithmSettings()
    {
        AssemblyFile = string.Empty;
        FullQualifiedClassName = string.Empty;
        Preferences = new Dictionary<string, string>();
    }
}