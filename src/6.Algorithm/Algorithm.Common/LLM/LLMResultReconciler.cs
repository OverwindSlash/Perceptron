namespace Algorithm.Common.LLM;

public sealed class LLMReconcileContext
{
    public LLMReconcileContext(
        CandidateEventStore candidateEvents,
        PendingEvidenceStore pendingEvidence)
    {
        CandidateEvents = candidateEvents;
        PendingEvidence = pendingEvidence;
    }

    public CandidateEventStore CandidateEvents { get; }
    public PendingEvidenceStore PendingEvidence { get; }
}

public interface ILLMResultHandler
{
    string RequesterAlgorithmName { get; }
    bool CanHandle(LLMAnalysisResult result);
    Task HandleAsync(LLMAnalysisResult result, LLMReconcileContext context, CancellationToken cancellationToken);
}

public sealed class LLMResultReconciler
{
    private readonly IReadOnlyList<ILLMResultHandler> _handlers;
    private readonly LLMReconcileContext _context;
    private readonly HashSet<string> _consumedRequestIds = [];
    private readonly object _sync = new();

    public LLMResultReconciler(
        IEnumerable<ILLMResultHandler> handlers,
        CandidateEventStore candidateEvents,
        PendingEvidenceStore pendingEvidence)
    {
        _handlers = handlers.ToList();
        _context = new LLMReconcileContext(candidateEvents, pendingEvidence);
    }

    public async Task<bool> ReconcileAsync(LLMAnalysisResult result, CancellationToken cancellationToken = default)
    {
        if (result.IsExpiredResult)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(result.CandidateEventId) &&
            (!_context.CandidateEvents.TryGet(result.CandidateEventId, out var candidate) ||
             candidate.Status is CandidateEventStatus.Published
                 or CandidateEventStatus.Rejected
                 or CandidateEventStatus.TimedOut
                 or CandidateEventStatus.Cancelled ||
             candidate.DeadlineUtc <= result.CompletedAtUtc))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_consumedRequestIds.Add(result.RequestId))
            {
                return false;
            }
        }

        var handler = _handlers.FirstOrDefault(handler =>
            handler.RequesterAlgorithmName == result.RequesterAlgorithmName &&
            handler.CanHandle(result));

        if (handler == null)
        {
            lock (_sync)
            {
                _consumedRequestIds.Remove(result.RequestId);
            }

            return false;
        }

        await handler.HandleAsync(result, _context, cancellationToken);
        _context.PendingEvidence.TryRemove(result.RequestId, out _);
        return true;
    }
}
