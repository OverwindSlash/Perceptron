using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface ILabelAnnotationGenerator
{
    VisualAnnotation GenerateLabelAnnotation(Frame frame);
}