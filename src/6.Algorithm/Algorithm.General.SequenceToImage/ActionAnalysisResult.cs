namespace Algorithm.General.SequenceToImage;

using System.Text.Json;
using System.Text.Json.Serialization;

public class ActionAnalysisResult
{
    public string Conclusion { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    [JsonConverter(typeof(FlexibleStringJsonConverter))]
    public string Evidence { get; set; } = string.Empty;
}

internal sealed class FlexibleStringJsonConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? string.Empty;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;

        return value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(GetText)),
            _ => GetText(value)
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

    private static string GetText(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
    }
}
