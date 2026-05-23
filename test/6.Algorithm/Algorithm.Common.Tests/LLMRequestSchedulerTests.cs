using Algorithm.Common.LLM;
using Algorithm.General.LLM;

namespace Algorithm.Common.Tests;

public class LLMRequestSchedulerTests
{
    [Test]
    public async Task LatestPerSource_ReplacesOlderRequest()
    {
        using var scheduler = new LLMRequestScheduler(capacity: 10);
        var oldRequest = CreateRequest("old", LLMQueuePolicy.LatestPerSource, sourceId: "cam-1", quality: 0.1);
        var newRequest = CreateRequest("new", LLMQueuePolicy.LatestPerSource, sourceId: "cam-1", quality: 0.2);

        Assert.That(scheduler.TrySubmit(oldRequest), Is.True);
        Assert.That(scheduler.TrySubmit(newRequest), Is.True);

        var taken = await scheduler.TakeAsync(CancellationToken.None);

        Assert.That(taken?.RequestId, Is.EqualTo("new"));
        Assert.That(scheduler.ReplacedRequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task LatestBestPerObject_KeepsBetterQualityRequest()
    {
        using var scheduler = new LLMRequestScheduler(capacity: 10);
        var best = CreateRequest("best", LLMQueuePolicy.LatestBestPerObject, objectId: "obj-1", quality: 0.9);
        var worse = CreateRequest("worse", LLMQueuePolicy.LatestBestPerObject, objectId: "obj-1", quality: 0.1);

        Assert.That(scheduler.TrySubmit(best), Is.True);
        Assert.That(scheduler.TrySubmit(worse), Is.True);

        var taken = await scheduler.TakeAsync(CancellationToken.None);

        Assert.That(taken?.RequestId, Is.EqualTo("best"));
    }

    [Test]
    public async Task EventAnchored_DoesNotReplaceRequest()
    {
        using var scheduler = new LLMRequestScheduler(capacity: 10);
        var first = CreateRequest("event-1", LLMQueuePolicy.EventAnchored, sourceId: "cam-1");
        var second = CreateRequest("event-2", LLMQueuePolicy.EventAnchored, sourceId: "cam-1");

        Assert.That(scheduler.TrySubmit(first), Is.True);
        Assert.That(scheduler.TrySubmit(second), Is.True);

        var firstTaken = await scheduler.TakeAsync(CancellationToken.None);
        var secondTaken = await scheduler.TakeAsync(CancellationToken.None);

        Assert.That(new[] { firstTaken?.RequestId, secondTaken?.RequestId }, Is.EquivalentTo(new[] { "event-1", "event-2" }));
    }

    [Test]
    public async Task TakeAsync_DropsExpiredRequest()
    {
        using var scheduler = new LLMRequestScheduler(capacity: 10);
        var expired = CreateRequest("expired", LLMQueuePolicy.EventAnchored, expireAtUtc: DateTime.UtcNow.AddSeconds(-1));
        var fresh = CreateRequest("fresh", LLMQueuePolicy.EventAnchored);

        scheduler.TrySubmit(expired);
        scheduler.TrySubmit(fresh);

        var taken = await scheduler.TakeAsync(CancellationToken.None);

        Assert.That(taken?.RequestId, Is.EqualTo("fresh"));
    }

    private static LLMAnalysisRequest CreateRequest(
        string requestId,
        LLMQueuePolicy policy,
        string sourceId = "source",
        string? objectId = null,
        double quality = 0,
        DateTime? expireAtUtc = null)
    {
        return new LLMAnalysisRequest(
            requestId,
            "test",
            policy == LLMQueuePolicy.EventAnchored ? $"candidate-{requestId}" : null,
            sourceId,
            1,
            0,
            DateTime.UtcNow,
            objectId,
            objectId,
            objectId == null ? null : $"source|{objectId}",
            objectId == null ? LLMAnalysisScope.Frame : LLMAnalysisScope.Object,
            policy,
            "prompt",
            [1, 2, 3],
            null,
            quality,
            DateTime.UtcNow,
            expireAtUtc ?? DateTime.UtcNow.AddMinutes(1));
    }
}
