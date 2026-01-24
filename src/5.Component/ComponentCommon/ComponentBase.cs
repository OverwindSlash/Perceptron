namespace ComponentCommon;

public abstract class ComponentBase
{
    protected Dictionary<string, string>? _preferences;

    protected ComponentBase(Dictionary<string, string>? preferences)
    {
        _preferences = preferences ?? new Dictionary<string, string>();
    }
}