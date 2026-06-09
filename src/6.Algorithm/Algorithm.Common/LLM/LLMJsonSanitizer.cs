namespace Algorithm.Common.LLM;

public static class LLMJsonSanitizer
{
    public static string StripMarkdownCodeFence(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var sanitized = content.Trim();

        sanitized = StripPrefix(sanitized);
        sanitized = sanitized.Trim();

        sanitized = StripSuffix(sanitized);
        return sanitized.Trim();
    }

    private static string StripPrefix(string content)
    {
        if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            return content["```json".Length..];
        }

        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            return content["```".Length..];
        }

        if (content.StartsWith("'''json", StringComparison.OrdinalIgnoreCase))
        {
            return content["'''json".Length..];
        }

        if (content.StartsWith("'''", StringComparison.Ordinal))
        {
            return content["'''".Length..];
        }

        return content;
    }

    private static string StripSuffix(string content)
    {
        if (content.EndsWith("```", StringComparison.Ordinal))
        {
            return content[..^3];
        }

        if (content.EndsWith("'''", StringComparison.Ordinal))
        {
            return content[..^3];
        }

        return content;
    }
}
