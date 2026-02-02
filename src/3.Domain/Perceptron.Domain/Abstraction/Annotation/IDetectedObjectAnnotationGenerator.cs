using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IDetectedObjectAnnotationGenerator
{
    Shape GenerateBBox(DetectedObject detectedObject, string strokeColor, int strokeWidth);

    Shape GenerateObjectText(DetectedObject detectedObject, string textColor, int fontSize,
        bool showLabel, bool showTrackingId, bool showConfidence);
}