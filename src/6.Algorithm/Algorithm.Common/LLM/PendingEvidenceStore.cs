using System.Collections.Concurrent;

namespace Algorithm.Common.LLM;

public sealed class PendingEvidenceStore
{
    private sealed class Entry
    {
        public Entry(PendingLLMEvidence evidence, long sizeBytes)
        {
            Evidence = evidence;
            SizeBytes = sizeBytes;
        }

        public PendingLLMEvidence Evidence { get; }
        public long SizeBytes { get; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, int> _sourceCounts = new();
    private readonly object _sync = new();
    private long _totalBytes;

    public PendingEvidenceStore(int maxPendingPerSource = 30, long maxTotalBytes = 128L * 1024 * 1024)
    {
        MaxPendingPerSource = Math.Max(1, maxPendingPerSource);
        MaxTotalBytes = Math.Max(1, maxTotalBytes);
    }

    public int MaxPendingPerSource { get; }
    public long MaxTotalBytes { get; }
    public int Count => _entries.Count;
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    public bool TryAdd(PendingLLMEvidence evidence)
    {
        var sizeBytes = GetSizeBytes(evidence);

        lock (_sync)
        {
            CleanupExpired(DateTime.UtcNow);

            var sourceCount = _sourceCounts.GetValueOrDefault(evidence.SourceId);
            if (sourceCount >= MaxPendingPerSource)
            {
                return false;
            }

            if (_totalBytes + sizeBytes > MaxTotalBytes)
            {
                return false;
            }

            if (!_entries.TryAdd(evidence.RequestId, new Entry(evidence, sizeBytes)))
            {
                return false;
            }

            _sourceCounts.AddOrUpdate(evidence.SourceId, 1, (_, count) => count + 1);
            _totalBytes += sizeBytes;
            return true;
        }
    }

    public bool TryGet(string requestId, out PendingLLMEvidence? evidence)
    {
        if (_entries.TryGetValue(requestId, out var entry))
        {
            evidence = entry.Evidence;
            return true;
        }

        evidence = null;
        return false;
    }

    public bool TryRemove(string requestId, out PendingLLMEvidence? evidence)
    {
        lock (_sync)
        {
            if (!_entries.TryRemove(requestId, out var entry))
            {
                evidence = null;
                return false;
            }

            DecrementSource(entry.Evidence.SourceId);
            _totalBytes -= entry.SizeBytes;
            evidence = entry.Evidence;
            return true;
        }
    }

    public int CleanupExpired(DateTime nowUtc)
    {
        var removed = 0;

        foreach (var (requestId, entry) in _entries.ToArray())
        {
            if (entry.Evidence.ExpireAtUtc > nowUtc)
            {
                continue;
            }

            if (TryRemove(requestId, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private static long GetSizeBytes(PendingLLMEvidence evidence)
    {
        return (evidence.FrameJpeg?.LongLength ?? 0) + (evidence.ObjectCropJpeg?.LongLength ?? 0);
    }

    private void DecrementSource(string sourceId)
    {
        if (!_sourceCounts.TryGetValue(sourceId, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            _sourceCounts.TryRemove(sourceId, out _);
        }
        else
        {
            _sourceCounts[sourceId] = count - 1;
        }
    }
}
