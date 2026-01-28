using Perceptron.Domain.Entity.RegionDefinition.Geometric;

namespace Perceptron.Domain.Entity.RegionDefinition;

public class Lane : NormalizedPolygon
{
    private string _type;

    public string Name { get; set; }
    public int Index { get; set; }

    public string Type
    {
        get => _type;
        set
        {
            _type = value;
        }
    }

    public HashSet<string> ForbiddenTypes { get; set; }

    public Lane()
    {
        ForbiddenTypes = new HashSet<string>();
    }
}