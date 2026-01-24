using System.Collections.Concurrent;

namespace Perceptron.Domain.Entity.Common;

public class PropertiesBag : IPropertiesBag
{
    private readonly ConcurrentDictionary<string, object?> _properties = new();

    public virtual void SetProperty(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key)) 
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));

        _properties[key] = value;
    }

    public virtual T? GetProperty<T>(string key, T? defaultValue = default)
    {
        if (string.IsNullOrWhiteSpace(key)) 
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));

        if (!_properties.TryGetValue(key, out var value)) 
            return defaultValue;

        if (value is T tValue) 
            return tValue;

        // 类型不匹配，返回默认值
        return defaultValue;
    }

    public virtual T GetRequiredProperty<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) 
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));

        if (!_properties.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Property '{key}' not found.");

        if (value is T tValue) 
            return tValue;

        throw new InvalidCastException($"Property '{key}' is not of type {typeof(T).Name}.");
    }

    public virtual void RemoveProperty(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) 
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));

        _properties.TryRemove(key, out _);
    }

    public virtual IReadOnlyDictionary<string, object?> GetAllProperties()
    {
        return new Dictionary<string, object?>(_properties);
    }
}