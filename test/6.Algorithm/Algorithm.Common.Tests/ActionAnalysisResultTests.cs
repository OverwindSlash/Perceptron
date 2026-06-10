using System.Text.Json;
using Algorithm.General.SequenceToImage;

namespace Algorithm.Common.Tests;

public class ActionAnalysisResultTests
{
    [Test]
    public void Deserialize_AcceptsStringEvidence()
    {
        const string json =
            """
            {
              "Conclusion": "异常",
              "Action": "发生肢体冲突",
              "Evidence": "有人挥拳"
            }
            """;

        var result = JsonSerializer.Deserialize<ActionAnalysisResult>(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Evidence, Is.EqualTo("有人挥拳"));
    }

    [Test]
    public void Deserialize_JoinsArrayEvidence()
    {
        const string json =
            """
            {
              "Conclusion": "异常",
              "Action": "发生肢体冲突",
              "Evidence": ["有人挥拳", "多人肢体交叠"]
            }
            """;

        var result = JsonSerializer.Deserialize<ActionAnalysisResult>(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Evidence, Is.EqualTo("有人挥拳; 多人肢体交叠"));
    }

    [Test]
    public void Deserialize_ConvertsNullEvidenceToEmptyString()
    {
        const string json =
            """
            {
              "Conclusion": "正常",
              "Action": "正常行走",
              "Evidence": null
            }
            """;

        var result = JsonSerializer.Deserialize<ActionAnalysisResult>(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Evidence, Is.Empty);
    }
}
