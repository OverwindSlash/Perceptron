using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perceptron.Domain.Event;

public abstract class DomainEvent : EventBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };
    public string SourceId { get; }
    public string EventType { get; }
    public string EventName { get; }
    public string AlgorithmName { get; }
    public string Message { get; protected set; }
    public string BucketName { get; set; }
    public string ImageId { get; set; }
    public string VideoId { get; set; }

    [JsonIgnore]
    public string ImageLocalPath { get; set; }
    [JsonIgnore]
    public string ImageJsonLocalPath { get; set; }
    [JsonIgnore]
    public string VideoLocalPath { get; set; }
    [JsonIgnore]
    public string VideoJsonLocalPath { get; set; }

    private readonly ConcurrentDictionary<string, object?> _customProperties = new();

    // PropertyBag
    public void SetProperty(string key, object? value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key must not be empty.", nameof(key));
        _customProperties[key] = value;
    }

    public bool TryGetProperty<T>(string key, out T? value)
    {
        if (_customProperties.TryGetValue(key, out var obj) && obj is T v)
        {
            value = v;
            return true;
        }
        value = default;
        return false;
    }

    public T? GetPropertyOrDefault<T>(string key, T? defaultValue = default)
        => TryGetProperty<T>(key, out var v) ? v : defaultValue;

    public bool RemoveProperty(string key) => _customProperties.TryRemove(key, out _);

    public bool ContainsProperty(string key) => _customProperties.ContainsKey(key);

    public bool TryRemove(string key, out object? value) => _customProperties.TryRemove(key, out value);

    public IReadOnlyDictionary<string, object?> GetAllProperties() => _customProperties;

    protected DomainEvent(string sourceId, string eventType, string eventName, string algorithmName)
    {
        SourceId = sourceId;
        EventType = eventType;
        EventName = eventName;
        AlgorithmName = algorithmName;
        Message = string.Empty;
    }

    public virtual string GetEventKey()
    {
        return $"{SourceId}_{EventName}";
    }

    public abstract string GenerateJsonContent();

    public abstract string GenerateLogContent();
}
