using System.Collections.Concurrent;

namespace Algorithm.Common.LLM;

public sealed class LLMRuntimeMetrics
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, long> _gauges = new();

    public void Increment(string name, string? sourceId = null, LLMQueuePolicy? queuePolicy = null, string? requesterAlgorithmName = null)
    {
        _counters.AddOrUpdate(BuildKey(name, sourceId, queuePolicy, requesterAlgorithmName), 1, (_, value) => value + 1);
    }

    public void SetGauge(string name, long value, string? sourceId = null, LLMQueuePolicy? queuePolicy = null, string? requesterAlgorithmName = null)
    {
        _gauges[BuildKey(name, sourceId, queuePolicy, requesterAlgorithmName)] = value;
    }

    public IReadOnlyDictionary<string, long> SnapshotCounters()
    {
        return new Dictionary<string, long>(_counters);
    }

    public IReadOnlyDictionary<string, long> SnapshotGauges()
    {
        return new Dictionary<string, long>(_gauges);
    }

    private static string BuildKey(string name, string? sourceId, LLMQueuePolicy? queuePolicy, string? requesterAlgorithmName)
    {
        return string.Join('|',
            name,
            $"source={sourceId ?? "*"}",
            $"policy={queuePolicy?.ToString() ?? "*"}",
            $"requester={requesterAlgorithmName ?? "*"}");
    }
}
