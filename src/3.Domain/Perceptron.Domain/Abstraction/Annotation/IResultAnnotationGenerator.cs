using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IResultAnnotationGenerator
{
    VisualAnnotation GenerateResultAnnotation(Frame frame);
}