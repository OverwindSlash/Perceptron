using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;

namespace Perceptron.Domain.Annotation;

public class BasicObjectAnnotationGenerator : IDetectedObjectAnnotationGenerator
{
    public Shape GenerateBBox(DetectedObject detectedObject, string strokeColor = "#8fce00", int strokeWidth = 1)
    {
        var bbox = detectedObject.Bbox;

        var rect = new Shape()
        {
            Id = "bbox_" + detectedObject.Id,
            Type = "rect",
            Origin = new Origin()
            {
                X = bbox.X,
                Y = bbox.Y
            },
            Size = new Size()
            {
                Width = bbox.Width,
                Height = bbox.Height
            },
            Style = new Style()
            {
                StrokeColor = strokeColor,
                StrokeWidth = strokeWidth
            }
        };

        return rect;
    }

    public Shape GenerateObjectText(DetectedObject detectedObject, string textColor = "#ffff00", int fontSize = 20,
        bool showLabel = true, bool showTrackingId = true, bool showConfidence = false)
    {
        var bbox = detectedObject.Bbox;

        string content = string.Empty;
        if (showLabel)
        {
            content += $"{detectedObject.Label}_";
        }

        if (showTrackingId)
        {
            content += $"{detectedObject.TrackingId}_";
        }

        if (showConfidence)
        {
            content += $"{detectedObject.Confidence:F2}_";
        }

        // text annotation
        var text = new Shape()
        {
            Id = "text_" + detectedObject.Id,
            Type = "text",
            Content = content.TrimEnd('_'),
            Position = new Position()
            {
                X = bbox.X,
                Y = bbox.Y - fontSize
            },
            Style = new Style()
            {
                Color = textColor,
                FontSize = fontSize,
            }
        };

        return text;
    }
}