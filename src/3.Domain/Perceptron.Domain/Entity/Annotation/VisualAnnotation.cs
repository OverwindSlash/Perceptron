namespace Perceptron.Domain.Entity.Annotation;

public class VisualAnnotation
{
    public string Version { get; set; }
    public string SourceId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public long FrameId { get; set; }
    public CoordinateSpace CoordinateSpace { get; set; }
    public List<Shape> Shapes { get; set; }

    public VisualAnnotation()
    {
        Version = "1.0";
        SourceId = string.Empty;
        CoordinateSpace = new CoordinateSpace();
        Shapes = new List<Shape>();
    }

    public VisualAnnotation(string sourceId, DateTimeOffset timestamp, long frameId, int width, int height)
    {
        Version = "1.0";
        SourceId = sourceId;
        Timestamp = timestamp;
        FrameId = frameId;
        CoordinateSpace = new CoordinateSpace
        {
            Type = "pixel",
            Width = width,
            Height = height
        };
        Shapes = new List<Shape>();
    }

    // Load from a Json file
    public static VisualAnnotation FromJson(string json)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var annotation = System.Text.Json.JsonSerializer.Deserialize<VisualAnnotation>(json, options);

        return annotation ?? throw new Exception("Failed to deserialize Annotation from JSON.");
    }

    public void SetCoordinateSpace(int width, int height, string type = "pixel")
    {
        CoordinateSpace = new CoordinateSpace
        {
            Type = type,
            Width = width,
            Height = height
        };
    }

    public void AddShape(Shape? shape)
    {
        if (shape == null) return;

        Shapes.Add(shape);
    }

    public void AddShapes(IEnumerable<Shape>? shapes)
    {
        if (shapes == null) return;

        foreach (var s in shapes)
        {
            if (s != null)
            {
                Shapes.Add(s);
            }
        }
    }
}


public class CoordinateSpace
{
    public string Type { get; set; } = "pixel"; 
    public int Width { get; set; }
    public int Height { get; set; }
}

public class Shape
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "rect";
    public Center? Center { get; set; }
    public int? Radius { get; set; }
    public Style? Style { get; set; }
    public Point[]? Points { get; set; }
    public Origin? Origin { get; set; }
    public Size? Size { get; set; }
    public int? Rotation { get; set; }
    public Position? Position { get; set; }
    public string? Content { get; set; }
    public Align? Align { get; set; }
}

public class Center
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class Style
{
    public string? StrokeColor { get; set; }
    public string? FillColor { get; set; }
    public int? StrokeWidth { get; set; }
    public float? Opacity { get; set; }
    public int[]? Dash { get; set; }
    public int? ZIndex { get; set; }
    public bool? Visible { get; set; }
    public string? Color { get; set; }
    public int? FontSize { get; set; }
    public string? FontFamily { get; set; }
    public string? FontWeight { get; set; }
    public object? BackgroundColor { get; set; }
}

public class Origin
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class Size
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public class Position
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class Align
{
    public string Horizontal { get; set; } = "center";
    public string Vertical { get; set; } = "center";
}

public class Point
{
    public int X { get; set; }
    public int Y { get; set; }
}
