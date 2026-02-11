using OpenCvSharp;
using Serilog;

namespace Perceptron.Domain.Setting;

public static class PreferenceParser
{
    public static string ParseStringValue(Dictionary<string, string>? preferences, string key, string defaultValue)
    {
        return ParseValue(preferences, key, defaultValue, s => s);
    }

    public static List<string> ParseStringListValue(Dictionary<string, string>? preferences, string key, List<string> defaultValue)
    {
        return ParseValue(preferences, key, defaultValue, s =>
        {
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(p => p.Trim().ToLower()).ToList();
        });
    }

    public static int ParseIntValue(Dictionary<string, string>? preferences, string key, int defaultValue)
    {
        return ParseValue(preferences, key, defaultValue, int.Parse);
    }

    public static float ParseFloatValue(Dictionary<string, string>? preferences, string key, float defaultValue)
    {
        return ParseValue(preferences, key, defaultValue, float.Parse);
    }
    public static TimeSpan ParseTimeSpanValue(Dictionary<string, string>? preferences, string v, TimeSpan defaultTimeout)
    {
        // 实现从字符串解析TimeSpan值
        return ParseValue(preferences, v, defaultTimeout, TimeSpan.Parse);
    }
    
    public static bool ParseBoolValue(Dictionary<string, string>? preferences, string key, bool defaultValue)
    {
        return ParseValue(preferences, key, defaultValue, bool.Parse);
    }

    public static Rect ParseRectValue(Dictionary<string, string>? preferences, string key, Rect defaultValue)
    {
        return ParseValue(preferences, key, defaultValue, s =>
        {
            var parts = s.Split(',');
            if (parts.Length != 4) throw new FormatException("Rect must have 4 components");
            
            return new Rect(
                int.Parse(parts[0]),
                int.Parse(parts[1]),
                int.Parse(parts[2]),
                int.Parse(parts[3])
            );
        });
    }

    public static Tuple<int, int> ParseIntTupleValue(Dictionary<string, string>? preferences, string key, Tuple<int, int> defaultValue)
    {
        return ParseValue(preferences, key, defaultValue, s =>
        {
            var parts = s.Split(',');
            if (parts.Length != 2) throw new FormatException("Tuple must have 2 components");

            return new Tuple<int, int>(
                int.Parse(parts[0]),
                int.Parse(parts[1])
            );
        });
    }

    public static Dictionary<string, string> ParserDictionary(Dictionary<string, string>? preferences, string key, Dictionary<string, string> defaultValue)
    {
        // 字典会以逗号分隔，键值对之间用冒号连接
        // 例如："key1:value1,key2:value2,key3:value3"
        return ParseValue(preferences, key, defaultValue, s =>
        {
            var dict = new Dictionary<string, string>();
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var part in parts)
            {
                var kv = part.Split(':', 2);
                if (kv.Length != 2) throw new FormatException("Dictionary entry must be in 'key:value' format");
                dict[kv[0].Trim()] = kv[1].Trim();
            }

            return dict;
        });
    }

    public static T ParseValue<T>(Dictionary<string, string>? preferences, string key, T defaultValue, Func<string, T> parser)
    {
        if (preferences == null)
        {
            return defaultValue;
        }

        var valueStr = preferences.GetValueOrDefault(key);
        if (string.IsNullOrEmpty(valueStr))
        {
            // For strings, empty might be valid, but here we seem to treat it as "use default" based on original code.
            // Original code for string: if (!string.IsNullOrEmpty(setting)) return setting;
            // Original code for int: default.ToString() used if missing.
            
            // However, the helper logic below tries to handle the "empty means default" logic.
            // If the key is present but value is empty string, original code returned default for string, int, float.
            return defaultValue;
        }

        try
        {
            return parser(valueStr);
        }
        catch (Exception)
        {
            Log.Warning("Invalid {Key} format. Reset to default: {DefaultValue}", key, defaultValue);
            return defaultValue;
        }
    }    
}