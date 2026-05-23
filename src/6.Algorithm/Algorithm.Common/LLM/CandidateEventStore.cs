using System.Collections.Concurrent;

namespace Algorithm.Common.LLM;

public enum CandidateEventStatus
{
    PendingLLM,
    Confirmed,
    Rejected,
    TimedOut,
    Published,
    Cancelled
}

public sealed class CandidateEventState
{
    public string CandidateEventId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public long FrameId { get; init; }
    public long OffsetMilliSec { get; init; }
    public DateTime UtcTimeStamp { get; init; }
    public string AlgorithmName { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string? ObjectId { get; init; }
    public CandidateEventStatus Status { get; set; }
    public string? PendingRequestId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime DeadlineUtc { get; init; }
    public object? TraditionalPayload { get; init; }
    public object? LLMResultPayload { get; set; }
    public HashSet<string> ConsumedRequestIds { get; } = [];
}

public sealed class CandidateEventStore
{
    private readonly ConcurrentDictionary<string, CandidateEventState> _states = new();
    private readonly object _sync = new();

    public CandidateEventStore(int capacity = 1000)
    {
        Capacity = Math.Max(1, capacity);
    }

    public int Capacity { get; }
    public int Count => _states.Count;

    public bool TryAdd(CandidateEventState state)
    {
        lock (_sync)
        {
            CleanupCompleted(DateTime.UtcNow);
            if (_states.Count >= Capacity)
            {
                return false;
            }

            return _states.TryAdd(state.CandidateEventId, state);
        }
    }

    public bool TryGet(string candidateEventId, out CandidateEventState? state)
    {
        return _states.TryGetValue(candidateEventId, out state);
    }

    public bool TryConfirm(string candidateEventId, LLMAnalysisResult result)
    {
        lock (_sync)
        {
            if (!TryGetMutable(candidateEventId, out var state) || IsTerminal(state.Status))
            {
                return false;
            }

            if (!state.ConsumedRequestIds.Add(result.RequestId))
            {
                return false;
            }

            state.Status = CandidateEventStatus.Confirmed;
            state.LLMResultPayload = result;
            return true;
        }
    }

    public bool TryReject(string candidateEventId, LLMAnalysisResult result)
    {
        lock (_sync)
        {
            if (!TryGetMutable(candidateEventId, out var state) || IsTerminal(state.Status))
            {
                return false;
            }

            if (!state.ConsumedRequestIds.Add(result.RequestId))
            {
                return false;
            }

            state.Status = CandidateEventStatus.Rejected;
            state.LLMResultPayload = result;
            return true;
        }
    }

    public bool TryMarkTimedOut(string candidateEventId, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (!TryGetMutable(candidateEventId, out var state) || IsTerminal(state.Status))
            {
                return false;
            }

            if (state.DeadlineUtc > nowUtc)
            {
                return false;
            }

            state.Status = CandidateEventStatus.TimedOut;
            return true;
        }
    }

    public bool TryPublish(string candidateEventId)
    {
        lock (_sync)
        {
            if (!TryGetMutable(candidateEventId, out var state))
            {
                return false;
            }

            if (state.Status != CandidateEventStatus.Confirmed)
            {
                return false;
            }

            state.Status = CandidateEventStatus.Published;
            return true;
        }
    }

    public IReadOnlyList<CandidateEventState> ScanTimedOut(DateTime nowUtc)
    {
        return _states.Values
            .Where(state => state.Status == CandidateEventStatus.PendingLLM && state.DeadlineUtc <= nowUtc)
            .ToList();
    }

    public bool TryRemove(string candidateEventId, out CandidateEventState? state)
    {
        return _states.TryRemove(candidateEventId, out state);
    }

    public int CleanupCompleted(DateTime nowUtc)
    {
        var removed = 0;
        foreach (var (candidateEventId, state) in _states.ToArray())
        {
            var shouldRemove = state.Status is CandidateEventStatus.Published
                or CandidateEventStatus.Rejected
                or CandidateEventStatus.TimedOut
                or CandidateEventStatus.Cancelled;

            if (!shouldRemove && state.DeadlineUtc > nowUtc)
            {
                continue;
            }

            if (_states.TryRemove(candidateEventId, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private bool TryGetMutable(string candidateEventId, out CandidateEventState state)
    {
        if (_states.TryGetValue(candidateEventId, out var found))
        {
            state = found;
            return true;
        }

        state = null!;
        return false;
    }

    private static bool IsTerminal(CandidateEventStatus status)
    {
        return status is CandidateEventStatus.Rejected
            or CandidateEventStatus.TimedOut
            or CandidateEventStatus.Published
            or CandidateEventStatus.Cancelled;
    }
}
