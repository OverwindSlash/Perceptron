using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.RegionDefinition;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IRegionAnnotationGenerator
{
    List<Shape> GenerateAnalysisAreas(ImageRegionDefinition regionDefinition, string strokeColor);
    List<Shape> GenerateExcludeAreas(ImageRegionDefinition regionDefinition, string strokeColor);
    List<Shape> GenerateLanes(ImageRegionDefinition regionDefinition, string strokeColor);
    List<Shape> GenerateInterestAreas(ImageRegionDefinition regionDefinition, string strokeColor);
    List<Shape> GenerateCountLines(ImageRegionDefinition regionDefinition, string enterStrokeColor, string leaveStrokeColor);
}