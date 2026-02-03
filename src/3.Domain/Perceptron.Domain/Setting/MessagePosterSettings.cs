using Serilog;
using System.Text.Json;

namespace Perceptron.Domain.Setting;

public class MessagePosterSettings : ComponentSettings
{
    public const bool DefaultWillPostMessage = true;
    public const string DefaultDestinationUrl = "http://127.0.0.1/perceptron-event";
    public const bool DefaultCheckDuplicateEvent = false;

    public bool WillPostMessage { get; private set; } = DefaultWillPostMessage;
    public string DestinationUrl { get; private set; } = DefaultDestinationUrl;
    public bool CheckDuplicateEvent { get; private set; } = DefaultCheckDuplicateEvent;
    public Dictionary<string, int> EventSuppressionIntervals { get; private set; } = new();

    public override void ParsePreferences()
    {
        WillPostMessage = ParseWillPostMessage(Preferences);
        DestinationUrl = ParseDestinationUrl(Preferences);
        CheckDuplicateEvent = ParseCheckDuplicateEvent(Preferences);
        EventSuppressionIntervals = ParseEventSuppressionIntervals(Preferences);

        Log.Information("WillPostMessage: {willPost}, DestinationUrl: {url}, CheckDuplicateEvent: {checkDup}", 
            WillPostMessage, DestinationUrl, CheckDuplicateEvent);
    }

    public static string ParseDestinationUrl(Dictionary<string, string>? preferences)
    {
        var url = PreferenceParser.ParseStringValue(preferences, "DestinationUrl", DefaultDestinationUrl);

        return url;
    }

    public static bool ParseWillPostMessage(Dictionary<string, string>? preferences)
    {
        var willPostMsg = PreferenceParser.ParseBoolValue(preferences, "WillPostMessage", DefaultWillPostMessage);

        return willPostMsg;
    }

    public static bool ParseCheckDuplicateEvent(Dictionary<string, string>? preferences)
    {
        return PreferenceParser.ParseBoolValue(preferences, "CheckDuplicateEvent", DefaultCheckDuplicateEvent);
    }

    public static Dictionary<string, int> ParseEventSuppressionIntervals(Dictionary<string, string>? preferences)
    {
        var json = PreferenceParser.ParseStringValue(preferences, "EventSuppressionIntervals", "{}");
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, int>();
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse EventSuppressionIntervals, using default empty dictionary.");
            return new Dictionary<string, int>();
        }
    }
}