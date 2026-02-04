using Perceptron.Domain.Entity.Annotation;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IAnnotationSender
{
    Task<int> SendAsync(VisualAnnotation annotation);
}