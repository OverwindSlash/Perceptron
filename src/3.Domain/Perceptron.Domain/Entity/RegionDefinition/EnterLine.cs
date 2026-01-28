using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using System.Text.Json.Serialization;

namespace Perceptron.Domain.Entity.RegionDefinition;

public class EnterLine : NormalizedLine
{
    public string Name { get; set; }

    [JsonIgnore]
    public LeaveLine LeaveLine { get; set; }
}