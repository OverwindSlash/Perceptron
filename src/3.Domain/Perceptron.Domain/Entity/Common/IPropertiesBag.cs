namespace Perceptron.Domain.Entity.Common;

public interface IPropertiesBag
{
    void SetProperty(string key, object? value);
    T? GetProperty<T>(string key, T? defaultValue = default);
    T GetRequiredProperty<T>(string key);
    IReadOnlyDictionary<string, object?> GetAllProperties();
}