using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IDetectedObjectAnnotationGenerator
{
    VisualAnnotation GenerateDetectedObjectAnnotation(Frame frame, DetectedObject detectedObject);
}