using Algorithm.Common.LLM;

namespace Algorithm.Common.Tests;

public class LLMJsonSanitizerTests
{
    [Test]
    public void StripMarkdownCodeFence_RemovesTripleBacktickJsonFence()
    {
        var content =
            """
            ```json
            {"status":"ok"}
            ```
            """;

        var result = LLMJsonSanitizer.StripMarkdownCodeFence(content);

        Assert.That(result, Is.EqualTo("""{"status":"ok"}"""));
    }

    [Test]
    public void StripMarkdownCodeFence_RemovesTripleQuoteJsonFence()
    {
        var content =
            """
            '''json
            {"status":"ok"}
            '''
            """;

        var result = LLMJsonSanitizer.StripMarkdownCodeFence(content);

        Assert.That(result, Is.EqualTo("""{"status":"ok"}"""));
    }

    [Test]
    public void StripMarkdownCodeFence_LeavesPlainJsonUnchanged()
    {
        const string content = """{"status":"ok"}""";

        var result = LLMJsonSanitizer.StripMarkdownCodeFence(content);

        Assert.That(result, Is.EqualTo(content));
    }
}
