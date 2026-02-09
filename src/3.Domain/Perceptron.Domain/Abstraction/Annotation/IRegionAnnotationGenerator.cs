using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.RegionDefinition;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IRegionAnnotationGenerator
{
    List<Shape> GenerateAnalysisAreas(ImageRegionDefinition regionDefinition, string strokeColor = "#7DDA58");
    List<Shape> GenerateExcludeAreas(ImageRegionDefinition regionDefinition, string strokeColor = "#E36667");
    List<Shape> GenerateLanes(ImageRegionDefinition regionDefinition, string strokeColor = "#E8E8E8");
    List<Shape> GenerateInterestAreas(ImageRegionDefinition regionDefinition, string strokeColor = "#FFECA1");
    List<Shape> GenerateCountLines(ImageRegionDefinition regionDefinition, string enterStrokeColor = "#4E4E4E", string leaveStrokeColor = "#4E4E4E");
}