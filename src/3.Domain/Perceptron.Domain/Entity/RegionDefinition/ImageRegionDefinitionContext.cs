using System.Text.Json.Serialization;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;

namespace Perceptron.Domain.Entity.RegionDefinition;

[JsonSerializable(typeof(ImageRegionDefinition))]
[JsonSerializable(typeof(AnalysisArea))]
[JsonSerializable(typeof(ExcludedArea))]
[JsonSerializable(typeof(Lane))]
[JsonSerializable(typeof(InterestArea))]
[JsonSerializable(typeof(EnterLine))]
[JsonSerializable(typeof(LeaveLine))]
[JsonSerializable(typeof(Tuple<EnterLine, LeaveLine>))]
[JsonSerializable(typeof(List<NormalizedPoint>))]
internal partial class ImageRegionDefinitionContext : JsonSerializerContext
{
}
