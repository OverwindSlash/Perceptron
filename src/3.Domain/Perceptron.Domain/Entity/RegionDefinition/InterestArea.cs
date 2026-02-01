using Perceptron.Domain.Entity.RegionDefinition.Geometric;

namespace Perceptron.Domain.Entity.RegionDefinition
{
    public class InterestArea : NormalizedPolygon
    {
        public string Name { get; set; } = "Interest Area";
        public string Type { get; set; } = "";
        public List<string> RelativeTypes { get; set; } = [];
    }
}
