using Algorithm.Common.LLM;

namespace Algorithm.Common.Tests;

public class PendingEvidenceStoreTests
{
    [Test]
    public void TryAdd_RejectsWhenSourceLimitIsReached()
    {
        var store = new PendingEvidenceStore(maxPendingPerSource: 1, maxTotalBytes: 1000);

        Assert.That(store.TryAdd(CreateEvidence("one", "source", [1])), Is.True);
        Assert.That(store.TryAdd(CreateEvidence("two", "source", [2])), Is.False);
    }

    [Test]
    public void TryAdd_RejectsWhenTotalBytesLimitIsReached()
    {
        var store = new PendingEvidenceStore(maxPendingPerSource: 10, maxTotalBytes: 2);

        Assert.That(store.TryAdd(CreateEvidence("one", "source", [1, 2, 3])), Is.False);
    }

    [Test]
    public void CleanupExpired_RemovesExpiredEvidence()
    {
        var store = new PendingEvidenceStore();
        var now = DateTime.UtcNow;
        store.TryAdd(CreateEvidence("expired", "source", [1], now.AddMinutes(1)));
        store.TryAdd(CreateEvidence("fresh", "source", [2], now.AddMinutes(5)));

        var removed = store.CleanupExpired(now.AddMinutes(2));

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.TryGet("fresh", out _), Is.True);
    }

    private static PendingLLMEvidence CreateEvidence(
        string requestId,
        string sourceId,
        byte[] bytes,
        DateTime? expireAtUtc = null)
    {
        return new PendingLLMEvidence(
            requestId,
            null,
            sourceId,
            1,
            0,
            DateTime.UtcNow,
            LLMAnalysisScope.Frame,
            bytes,
            null,
            [],
            "prompt",
            expireAtUtc ?? DateTime.UtcNow.AddMinutes(1));
    }
}
