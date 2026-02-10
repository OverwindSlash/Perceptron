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
        var dict = PreferenceParser.ParserDictionary(preferences, "EventSuppressionIntervals", new Dictionary<string, string>());

        var result = new Dictionary<string, int>();
        foreach (var kvp in dict)
        {
            if (int.TryParse(kvp.Value, out int interval))
            {
                result[kvp.Key] = interval;
            }
            else
            {
                Log.Warning("Invalid suppression interval for event {event}: {value}", kvp.Key, kvp.Value);
            }
        }
        return result;
    }
}