namespace Perceptron.Domain.Setting;

public abstract class ComponentSettings
{
    public string AssemblyFile { get; set; } = string.Empty;
    public string FullQualifiedClassName { get; set; } = string.Empty;
    public Dictionary<string, string> Preferences { get; set; } = new();

    // 由于父类的属性赋值在父子类的构造函数之后执行。所以必须要有一个专门用于解析的函数
    // 此函数在父类的属性赋值之后调用
    public abstract void ParsePreferences();

    public T? GetSetting<T>(string name)
    {
        if (Preferences.TryGetValue(name, out var result))
        {
            try
            {
                return (T)Convert.ChangeType(result, typeof(T));
            }
            catch
            {
                // Ignore conversion errors
                return default;
            }
        }

        return default;
    }
}