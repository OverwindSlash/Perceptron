using Serilog;

namespace Perceptron.Domain.Setting;

public class RegionManagerSettings : ComponentSettings
{
    public const string DefaultRegionDefinitionFile = "default-region.json";
    public const string DefaultCameraServiceUrl = "http://localhost:44311/api/services/app/";
    public const string DefaultCameraId = "CameraId001";

    public string RegionDefinitionFile { get; private set; } = DefaultRegionDefinitionFile;
    public string CameraServiceUrl { get; private set; } = DefaultCameraServiceUrl;
    public string CameraId { get; private set; } = DefaultCameraId;

    public override void ParsePreferences()
    {
        RegionDefinitionFile = ParseRegionDefinitionFile(Preferences);
        CameraServiceUrl = ParseCameraServiceUrl(Preferences);
        CameraId = ParseCameraId(Preferences);

        Log.Information("RegionDefinitionFile: {file}", RegionDefinitionFile);
        Log.Information("CameraServiceUrl: {url}", CameraServiceUrl);
        Log.Information("CameraId: {id}", CameraId);
    }

    public static string ParseRegionDefinitionFile(Dictionary<string, string> preferences)
    {
        var path = PreferenceParser.ParseStringValue(preferences, "RegionDefinitionFile", DefaultRegionDefinitionFile);
        
        if (File.Exists(path)) 
            return path;

        Log.Warning($"Region definition file not found or invalid, using default: {DefaultRegionDefinitionFile}");
        return DefaultRegionDefinitionFile;
    }

    public static string ParseCameraServiceUrl(Dictionary<string, string> preferences)
    {
        var url = PreferenceParser.ParseStringValue(preferences, "CameraServiceUrl", DefaultCameraServiceUrl);

        return url;
    }

    public static string ParseCameraId(Dictionary<string, string> preferences)
    {
        var cameraId = PreferenceParser.ParseStringValue(preferences, "CameraId", DefaultCameraId);

        return cameraId;
    }
}