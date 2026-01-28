using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using System.Text.Json.Serialization;

namespace Perceptron.Domain.Entity.RegionDefinition;

public class LeaveLine : NormalizedLine
{
    public string Name { get; set; }

    [JsonIgnore]
    public EnterLine EnterLine { get; set; }
}