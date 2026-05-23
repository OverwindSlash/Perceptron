using Algorithm.Common.LLM;

namespace Algorithm.Common.Tests;

public class LLMResultReconcilerTests
{
    [Test]
    public async Task ReconcileAsync_InvokesMatchingHandler()
    {
        var handler = new RecordingHandler("requester");
        var store = new CandidateEventStore();
        store.TryAdd(CreateState("candidate"));
        var evidence = new PendingEvidenceStore();
        evidence.TryAdd(CreateEvidence("request"));
        var reconciler = new LLMResultReconciler([handler], store, evidence);

        var reconciled = await reconciler.ReconcileAsync(CreateResult("request", "candidate"));

        Assert.That(reconciled, Is.True);
        Assert.That(handler.HandledCount, Is.EqualTo(1));
        Assert.That(evidence.TryGet("request", out _), Is.False);
    }

    [Test]
    public async Task ReconcileAsync_RejectsDuplicateResult()
    {
        var handler = new RecordingHandler("requester");
        var store = new CandidateEventStore();
        store.TryAdd(CreateState("candidate"));
        var reconciler = new LLMResultReconciler([handler], store, new PendingEvidenceStore());
        var result = CreateResult("request", "candidate");

        Assert.That(await reconciler.ReconcileAsync(result), Is.True);
        Assert.That(await reconciler.ReconcileAsync(result), Is.False);
        Assert.That(handler.HandledCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ReconcileAsync_RejectsExpiredResult()
    {
        var handler = new RecordingHandler("requester");
        var store = new CandidateEventStore();
        store.TryAdd(CreateState("candidate"));
        var reconciler = new LLMResultReconciler([handler], store, new PendingEvidenceStore());

        var reconciled = await reconciler.ReconcileAsync(CreateResult("request", "candidate", isExpired: true));

        Assert.That(reconciled, Is.False);
        Assert.That(handler.HandledCount, Is.EqualTo(0));
    }

    [Test]
    public async Task ReconcileAsync_RejectsUnknownCandidate()
    {
        var handler = new RecordingHandler("requester");
        var reconciler = new LLMResultReconciler([handler], new CandidateEventStore(), new PendingEvidenceStore());

        var reconciled = await reconciler.ReconcileAsync(CreateResult("request", "unknown"));

        Assert.That(reconciled, Is.False);
        Assert.That(handler.HandledCount, Is.EqualTo(0));
    }

    private sealed class RecordingHandler : ILLMResultHandler
    {
        public RecordingHandler(string requesterAlgorithmName)
        {
            RequesterAlgorithmName = requesterAlgorithmName;
        }

        public string RequesterAlgorithmName { get; }
        public int HandledCount { get; private set; }

        public bool CanHandle(LLMAnalysisResult result)
        {
            return result.RequesterAlgorithmName == RequesterAlgorithmName;
        }

        public Task HandleAsync(LLMAnalysisResult result, LLMReconcileContext context, CancellationToken cancellationToken)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }

    private static CandidateEventState CreateState(string id)
    {
        return new CandidateEventState
        {
            CandidateEventId = id,
            SourceId = "source",
            FrameId = 1,
            OffsetMilliSec = 0,
            UtcTimeStamp = DateTime.UtcNow,
            AlgorithmName = "requester",
            EventName = "event",
            Status = CandidateEventStatus.PendingLLM,
            PendingRequestId = "request",
            CreatedAtUtc = DateTime.UtcNow,
            DeadlineUtc = DateTime.UtcNow.AddMinutes(1)
        };
    }

    private static PendingLLMEvidence CreateEvidence(string requestId)
    {
        return new PendingLLMEvidence(
            requestId,
            "candidate",
            "source",
            1,
            0,
            DateTime.UtcNow,
            LLMAnalysisScope.Frame,
            [1],
            null,
            [],
            "prompt",
            DateTime.UtcNow.AddMinutes(1));
    }

    private static LLMAnalysisResult CreateResult(string requestId, string candidateEventId, bool isExpired = false)
    {
        return new LLMAnalysisResult(
            requestId,
            "requester",
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
            isExpired,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);
    }
}
