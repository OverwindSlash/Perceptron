
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Annotation;

public class BasicRegionAnnotationGenerator : IRegionAnnotationGenerator
{
    public VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        annotation.AddShapes(GenerateAnalysisAreas(regionDefinition));
        annotation.AddShapes(GenerateExcludeAreas(regionDefinition));
        annotation.AddShapes(GenerateLanes(regionDefinition));
        annotation.AddShapes(GenerateInterestAreas(regionDefinition));
        annotation.AddShapes(GenerateCountLines(regionDefinition));

        return annotation;
    }

    public static List<Shape> GenerateAnalysisAreas(ImageRegionDefinition regionDefinition, string strokeColor = "#7DDA58")
    {
        List<Shape> analysisAreas = new List<Shape>();

        int areaIndex = 1;
        foreach (var analysisArea in regionDefinition.AnalysisAreas)
        {
            var area = CreatePolygon(analysisArea, "AnalysisArea", areaIndex++);
            area.Style.StrokeColor = strokeColor;

            analysisAreas.Add(area);
        }

        return analysisAreas;
    }

    private static Shape CreatePolygon(NormalizedPolygon polygon, string polygonName, int areaIndex)
    {
        var areaPoints = polygon.Points.Select(
                p => new Point() { X = p.OriginalX, Y = p.OriginalY })
            .ToArray();

        var area = new Shape
        {
            Id = $"{polygonName}_{areaIndex}",
            Type = "polygon",
            Points = areaPoints,
            Style = new Style()
        };

        return area;
    }


    public static List<Shape> GenerateExcludeAreas(ImageRegionDefinition regionDefinition, string strokeColor = "#E36667")
    {
        List<Shape> excludeAreas = new List<Shape>();

        int areaIndex = 1;
        foreach (var excludedArea in regionDefinition.ExcludedAreas)
        {
            var area = CreatePolygon(excludedArea, "ExcludeArea", areaIndex++);
            area.Style.StrokeColor = strokeColor;

            excludeAreas.Add(area);
        }

        return excludeAreas;
    }

    public static List<Shape> GenerateLanes(ImageRegionDefinition regionDefinition, string strokeColor = "#E8E8E8")
    {
        List<Shape> lanes = new List<Shape>();

        int areaIndex = 1;
        foreach (var lane in regionDefinition.Lanes)
        {
            var area = CreatePolygon(lane, "Lane", areaIndex++);
            area.Style.StrokeColor = strokeColor;

            lanes.Add(area);
        }

        return lanes;
    }

    public static List<Shape> GenerateInterestAreas(ImageRegionDefinition regionDefinition, string strokeColor = "#FFECA1")
    {
        List<Shape> rois = new List<Shape>();

        int areaIndex = 1;
        foreach (var interestArea in regionDefinition.InterestAreas)
        {
            var area = CreatePolygon(interestArea, "ROI", areaIndex++);
            area.Style.StrokeColor = strokeColor;

            rois.Add(area);
        }

        return rois;
    }

    public static List<Shape> GenerateCountLines(ImageRegionDefinition regionDefinition, string enterStrokeColor = "#4E4E4E", string leaveStrokeColor = "#4E4E4E")
    {
        List<Shape> lines = new List<Shape>();

        int areaIndex = 1;
        foreach (var countLine in regionDefinition.CountLines)
        {
            // Enter line
            var enterLine = CreateLine(countLine.Item1, "EnterLine", areaIndex++);
            enterLine.Style.StrokeColor = enterStrokeColor;

            lines.Add(enterLine);

            // leave line
            var leaveLine = CreateLine(countLine.Item2, "LeaveLine", areaIndex++);
            leaveLine.Style.StrokeColor = leaveStrokeColor;

            lines.Add(leaveLine);
        }

        return lines;
    }

    private static Shape CreateLine(NormalizedLine line, string lineName, int lineIndex)
    {
        var linePoints = new List<Point>();

        linePoints.Add(new Point()
        {
            X = line.Start.OriginalX,
            Y = line.Start.OriginalY
        });

        linePoints.Add(new Point()
        {
            X = line.Stop.OriginalX,
            Y = line.Stop.OriginalY
        });

        var polyline = new Shape
        {
            Id = $"{lineName}_{lineIndex}",
            Type = "polyline",
            Points = linePoints.ToArray(),
            Style = new Style()
        };

        return polyline;
    }
}