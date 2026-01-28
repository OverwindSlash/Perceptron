using Perceptron.Domain.Entity.RegionDefinition.Geometric;

namespace Perceptron.Domain.Entity.RegionDefinition;

public class ExcludedArea : NormalizedPolygon
{
    public string Name { get; set; }
}