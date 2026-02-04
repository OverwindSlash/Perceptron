using System.Text.RegularExpressions;
using Serilog;

namespace Perceptron.Domain.Setting;

public class EventRepositorySettings : ComponentSettings
{
    public const string DefaultRdbConnectionString = "server=localhost;port=3306;uid=root;pwd=root;database=baize";
    public const string DefaultStorageUrl = "localhost:9000";
    public const string DefaultStorageUsername = "admin";
    public const string DefaultStoragePassword = "admin";
    public const bool DefaultWillStoreSnapshot = true;
    public const bool DefaultWillStoreVideoClip = false;

    public string RdbConnectionString { get; private set; } = DefaultRdbConnectionString;

    public string StorageUrl { get; private set; } = DefaultStorageUrl;
    public string StorageUsername { get; private set; } = DefaultStorageUsername;
    public string StoragePassword { get; private set; } = DefaultStoragePassword;

    public bool WillStoreSnapshot { get; private set; } = DefaultWillStoreSnapshot;
    public bool WillStoreVideoClip { get; private set; } = DefaultWillStoreVideoClip;


    public override void ParsePreferences()
    {
        RdbConnectionString = ParseRdbConnectionString(Preferences);
        StorageUrl = ParseStorageUrl(Preferences);
        StorageUsername = ParseStorageUsername(Preferences);
        StoragePassword = ParseStoragePassword(Preferences);
        WillStoreSnapshot = ParseWillStoreSnapshot(Preferences);
        WillStoreVideoClip = ParseWillStoreVideoClip(Preferences);

        // 将RdbConnectionString中的密码打码后记录日志
        var maskedConnectionString = Regex.Replace(RdbConnectionString, "(?i)(pwd=)[^;]*", "$1******");
        Log.Information("RdbConnectionString: {ConnectionString}", maskedConnectionString);
        Log.Information("StorageUrl: {Uro}", StorageUrl);
        Log.Information("WillStoreSnapshot: {snapshotFlag}, WillStoreVideoClip: {videoFlag}", WillStoreSnapshot, WillStoreVideoClip);
    }

    public static string ParseRdbConnectionString(Dictionary<string, string> preferences)
    {
        var connectionString =
            PreferenceParser.ParseStringValue(preferences, "RdbConnectionString", DefaultRdbConnectionString);

        return connectionString;
    }

    public static string ParseStorageUrl(Dictionary<string, string> preferences)
    {
        var url = PreferenceParser.ParseStringValue(preferences, "StorageUrl", DefaultStorageUrl);

        return url;
    }

    public static string ParseStorageUsername(Dictionary<string, string> preferences)
    {
        var username = PreferenceParser.ParseStringValue(preferences, "StorageUsername", DefaultStorageUsername);

        return username;
    }

    public static string ParseStoragePassword(Dictionary<string, string> preferences)
    {
        var password = PreferenceParser.ParseStringValue(preferences, "StoragePassword", DefaultStoragePassword);

        return password;
    }

    public static bool ParseWillStoreSnapshot(Dictionary<string, string> preferences)
    {
        var willStoreSnapshot =
            PreferenceParser.ParseBoolValue(preferences, "WillStoreSnapshot", DefaultWillStoreSnapshot);

        return willStoreSnapshot;
    }

    public static bool ParseWillStoreVideoClip(Dictionary<string, string> preferences)
    {
        var willStoreVideoClip =
            PreferenceParser.ParseBoolValue(preferences, "WillStoreVideoClip", DefaultWillStoreVideoClip);

        return willStoreVideoClip;
    }
}
