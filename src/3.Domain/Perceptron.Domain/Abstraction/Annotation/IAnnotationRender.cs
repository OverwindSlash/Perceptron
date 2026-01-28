using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IAnnotationRender
{
    Mat DrawAnnotations(Mat image, VisualAnnotation annotation);
}