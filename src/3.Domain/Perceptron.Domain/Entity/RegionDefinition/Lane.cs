using Perceptron.Domain.Entity.RegionDefinition.Geometric;

namespace Perceptron.Domain.Entity.RegionDefinition;

public class Lane : NormalizedPolygon
{
    public string Name { get; set; } = "Lane";
    public int Index { get; set; }

    public string Type { get; set; } = "";

    public HashSet<string> ForbiddenTypes { get; set; }

    public Lane()
    {
        ForbiddenTypes = new HashSet<string>();
    }
}