using Serilog;

namespace Perceptron.Domain.Setting;

public class AnnotationRenderSettings : ComponentSettings
{
    public const string BaseStyleFile = "default-style.json";

    public string DefaultStyleFile { get; private set; } = BaseStyleFile;

    public override void ParsePreferences()
    {
        DefaultStyleFile = ParseDefaultStyleFile(Preferences);
    }

    public static string ParseDefaultStyleFile(Dictionary<string, string> preferences)
    {
        var styleFile = PreferenceParser.ParseStringValue(preferences, "DefaultStyleFile", BaseStyleFile);

        if (styleFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return styleFile;

        Log.Warning($"DefaultStyleFile should be a JSON file. Reset to default: {BaseStyleFile}");
        return BaseStyleFile;
    }
}