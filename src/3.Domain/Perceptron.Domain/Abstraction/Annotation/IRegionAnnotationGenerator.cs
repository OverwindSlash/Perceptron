using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IRegionAnnotationGenerator
{
    VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition);
}