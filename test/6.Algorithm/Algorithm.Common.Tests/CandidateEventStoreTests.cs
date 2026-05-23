using Algorithm.Common.LLM;

namespace Algorithm.Common.Tests;

public class CandidateEventStoreTests
{
    [Test]
    public void TryConfirm_IsIdempotentForSameRequest()
    {
        var store = new CandidateEventStore();
        store.TryAdd(CreateState("candidate"));
        var result = CreateResult("request", "candidate");

        Assert.That(store.TryConfirm("candidate", result), Is.True);
        Assert.That(store.TryConfirm("candidate", result), Is.False);
    }

    [Test]
    public void TryReject_MovesPendingCandidateToRejected()
    {
        var store = new CandidateEventStore();
        store.TryAdd(CreateState("candidate"));

        Assert.That(store.TryReject("candidate", CreateResult("request", "candidate")), Is.True);
        Assert.That(store.TryGet("candidate", out var state), Is.True);
        Assert.That(state?.Status, Is.EqualTo(CandidateEventStatus.Rejected));
    }

    [Test]
    public void TryMarkTimedOut_OnlyTimesOutAfterDeadline()
    {
        var store = new CandidateEventStore();
        store.TryAdd(CreateState("candidate", DateTime.UtcNow.AddSeconds(10)));

        Assert.That(store.TryMarkTimedOut("candidate", DateTime.UtcNow), Is.False);
        Assert.That(store.TryMarkTimedOut("candidate", DateTime.UtcNow.AddSeconds(20)), Is.True);
    }

    [Test]
    public void CleanupCompleted_RemovesTerminalAndExpiredStates()
    {
        var store = new CandidateEventStore(capacity: 10);
        store.TryAdd(CreateState("done"));
        store.TryAdd(CreateState("expired", DateTime.UtcNow.AddSeconds(-1)));
        store.TryConfirm("done", CreateResult("request", "done"));
        store.TryPublish("done");

        var removed = store.CleanupCompleted(DateTime.UtcNow);

        Assert.That(removed, Is.EqualTo(2));
        Assert.That(store.Count, Is.EqualTo(0));
    }

    [Test]
    public void CapacityLimit_RejectsAdditionalStates()
    {
        var store = new CandidateEventStore(capacity: 1);

        Assert.That(store.TryAdd(CreateState("one")), Is.True);
        Assert.That(store.TryAdd(CreateState("two")), Is.False);
    }

    private static CandidateEventState CreateState(string id, DateTime? deadlineUtc = null)
    {
        return new CandidateEventState
        {
            CandidateEventId = id,
            SourceId = "source",
            FrameId = 1,
            OffsetMilliSec = 0,
            UtcTimeStamp = DateTime.UtcNow,
            AlgorithmName = "test",
            EventName = "event",
            Status = CandidateEventStatus.PendingLLM,
            PendingRequestId = "request",
            CreatedAtUtc = DateTime.UtcNow,
            DeadlineUtc = deadlineUtc ?? DateTime.UtcNow.AddMinutes(1)
        };
    }

    private static LLMAnalysisResult CreateResult(string requestId, string candidateEventId)
    {
        return new LLMAnalysisResult(
            requestId,
            "test",
            candidateEventId,
            "source",
            1,
            0,
            DateTime.UtcNow,
            null,
            LLMAnalysisScope.Frame,
            "model",
            TimeSpan.FromMilliseconds(1),
            "{}",
            true,
            false,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);
    }
}
