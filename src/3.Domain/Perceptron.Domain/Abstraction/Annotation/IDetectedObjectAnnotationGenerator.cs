using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;

namespace Perceptron.Domain.Abstraction.Annotation;

public interface IDetectedObjectAnnotationGenerator
{
    Shape GenerateBBox(DetectedObject detectedObject, string strokeColor = "#8fce00", int strokeWidth = 1);

    Shape GenerateObjectText(DetectedObject detectedObject, string textColor = "#ffff00", int fontSize = 20,
        bool showLabel = true, bool showTrackingId = true, bool showConfidence = false);
}