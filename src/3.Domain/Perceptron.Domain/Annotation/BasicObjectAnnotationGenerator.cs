using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Annotation;

public class BasicObjectAnnotationGenerator : IDetectedObjectAnnotationGenerator
{
    public VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject)
    {
        throw new NotImplementedException();
    }
}