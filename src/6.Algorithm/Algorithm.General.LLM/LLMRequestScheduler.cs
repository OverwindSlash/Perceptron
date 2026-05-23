using Algorithm.Common.LLM;
using Serilog;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Algorithm.General.LLM;

public sealed class LLMRequestScheduler : IDisposable
{
    private readonly Channel<string> _workKeys;
    private readonly ConcurrentDictionary<string, LLMAnalysisRequest> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, byte> _queuedKeys = new();
    private readonly object _sync = new();
    private readonly int _capacity;
    private bool _isDisposed;
    private long _replacedRequestCount;
    private long _droppedRequestCount;
    private long _rejectedRequestCount;

    public LLMRequestScheduler(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _workKeys = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public int PendingCount => _pendingRequests.Count;
    public long ReplacedRequestCount => Interlocked.Read(ref _replacedRequestCount);
    public long DroppedRequestCount => Interlocked.Read(ref _droppedRequestCount);
    public long RejectedRequestCount => Interlocked.Read(ref _rejectedRequestCount);

    public bool TrySubmit(LLMAnalysisRequest request)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return false;
            }

            CleanupExpired(DateTime.UtcNow);

            if (request.QueuePolicy == LLMQueuePolicy.DropOldest && _pendingRequests.Count >= _capacity)
            {
                DropOldestRequest();
            }
            else if (_pendingRequests.Count >= _capacity && !_pendingRequests.ContainsKey(GetKey(request)))
            {
                Log.Warning("LLM scheduler is full. Reject request. RequestId: {RequestId}, Policy: {Policy}, SourceId: {SourceId}",
                    request.RequestId, request.QueuePolicy, request.SourceId);
                Interlocked.Increment(ref _rejectedRequestCount);
                return false;
            }

            var key = GetKey(request);
            if (_pendingRequests.TryGetValue(key, out var oldRequest))
            {
                if (!ShouldReplace(oldRequest, request))
                {
                    return true;
                }

                _pendingRequests[key] = request;
                Interlocked.Increment(ref _replacedRequestCount);
                Log.Debug("Replace pending LLM request. OldRequestId: {OldRequestId}, NewRequestId: {NewRequestId}, Policy: {Policy}",
                    oldRequest.RequestId, request.RequestId, request.QueuePolicy);
            }
            else
            {
                _pendingRequests[key] = request;
            }

            if (_queuedKeys.TryAdd(key, 0))
            {
                if (!_workKeys.Writer.TryWrite(key))
                {
                    _queuedKeys.TryRemove(key, out _);
                    _pendingRequests.TryRemove(key, out _);
                    return false;
                }
            }

            return true;
        }
    }

    public async ValueTask<LLMAnalysisRequest?> TakeAsync(CancellationToken cancellationToken)
    {
        while (await _workKeys.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_workKeys.Reader.TryRead(out var key))
            {
                _queuedKeys.TryRemove(key, out _);
                if (!_pendingRequests.TryRemove(key, out var request))
                {
                    continue;
                }

                if (request.ExpireAtUtc <= DateTime.UtcNow)
                {
                    Log.Warning("Drop expired LLM request before inference. RequestId: {RequestId}, Policy: {Policy}",
                        request.RequestId, request.QueuePolicy);
                    continue;
                }

                return request;
            }
        }

        return null;
    }

    public void Complete()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _workKeys.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        Complete();
        _pendingRequests.Clear();
        _queuedKeys.Clear();
    }

    private static string GetKey(LLMAnalysisRequest request)
    {
        return request.QueuePolicy switch
        {
            LLMQueuePolicy.LatestPerSource => $"source:{request.SourceId}",
            LLMQueuePolicy.LatestBestPerObject => $"object:{request.ObjectId ?? request.TrackKey ?? request.RequestId}",
            LLMQueuePolicy.EventAnchored => $"event:{request.RequestId}",
            LLMQueuePolicy.DropOldest => $"drop:{request.RequestId}",
            _ => request.RequestId
        };
    }

    private static bool ShouldReplace(LLMAnalysisRequest oldRequest, LLMAnalysisRequest newRequest)
    {
        return newRequest.QueuePolicy switch
        {
            LLMQueuePolicy.LatestPerSource => true,
            LLMQueuePolicy.LatestBestPerObject => GetQuality(newRequest) > GetQuality(oldRequest),
            LLMQueuePolicy.EventAnchored => false,
            LLMQueuePolicy.DropOldest => false,
            _ => true
        };
    }

    private static double GetQuality(LLMAnalysisRequest request)
    {
        return request.EvidenceQualityScore ?? request.DetectorConfidence ?? 0;
    }

    private void CleanupExpired(DateTime nowUtc)
    {
        foreach (var (key, request) in _pendingRequests.ToArray())
        {
            if (request.ExpireAtUtc > nowUtc)
            {
                continue;
            }

            _pendingRequests.TryRemove(key, out _);
            _queuedKeys.TryRemove(key, out _);
        }
    }

    private void DropOldestRequest()
    {
        var oldest = _pendingRequests
            .OrderBy(pair => pair.Value.CreatedAtUtc)
            .FirstOrDefault();

        if (oldest.Key == null)
        {
            return;
        }

        _pendingRequests.TryRemove(oldest.Key, out var dropped);
        _queuedKeys.TryRemove(oldest.Key, out _);
        if (dropped != null)
        {
            Interlocked.Increment(ref _droppedRequestCount);
            Log.Warning("Drop oldest LLM request. RequestId: {RequestId}, Policy: {Policy}",
                dropped.RequestId, dropped.QueuePolicy);
        }
    }
}
