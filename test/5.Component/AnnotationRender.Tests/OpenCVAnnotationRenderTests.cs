using AnnotationRender.OpenCV;
using FluentAssertions;
using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;

namespace AnnotationRender.Tests;

[TestFixture]
public class OpenCVAnnotationRenderTests
{
    private Render _render;
    private Mat _canvas;
    private const int Width = 200;
    private const int Height = 200;

    [SetUp]
    public void Setup()
    {
        // Initialize Render with empty preferences
        _render = new Render(new Dictionary<string, string>());
        // Create a black canvas
        _canvas = new Mat(Height, Width, MatType.CV_8UC3, new Scalar(0, 0, 0));
    }

    [TearDown]
    public void TearDown()
    {
        _canvas?.Dispose();
    }

    [Test]
    public void DrawAnnotations_ShouldNotThrow_WhenAnnotationIsEmpty()
    {
        var annotation = new VisualAnnotation();
        var act = () => _render.DrawAnnotations(_canvas, annotation);
        act.Should().NotThrow();
    }

    [Test]
    public void DrawRect_ShouldDrawWhiteRectangle()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 50, Y = 50 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 50, Height = 50 },
            Style = new Style { FillColor = "#FFFFFF", Opacity = 1.0f, StrokeWidth = 0 }
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Check center of rectangle (75, 75) -> Should be White
        var centerPixel = result.Get<Vec3b>(75, 75);
        centerPixel.Item0.Should().Be(255, "Blue channel should be 255");
        centerPixel.Item1.Should().Be(255, "Green channel should be 255");
        centerPixel.Item2.Should().Be(255, "Red channel should be 255");

        // Check outside (20, 20) -> Should be Black
        var outsidePixel = result.Get<Vec3b>(20, 20);
        outsidePixel.Item0.Should().Be(0);
    }

    [Test]
    public void DrawRect_WithRotation_ShouldDrawRotatedRectangle()
    {
        // Draw a thin rectangle rotated 45 degrees
        // Origin (80, 80), Size (40, 10). Center (100, 85).
        // If not rotated, it spans X:80-120, Y:80-90.
        // Rotated 45 degrees, it should span diagonally.
        
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 80, Y = 80 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 40, Height = 10 },
            Rotation = 45,
            Style = new Style { FillColor = "#FFFFFF", Opacity = 1.0f }
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Center should definitely be white
        // Center is 80 + 20 = 100, 80 + 5 = 85.
        var centerPixel = result.Get<Vec3b>(85, 100); // Row 85, Col 100
        centerPixel.Item0.Should().Be(255);

        // A point that would be inside if NOT rotated, but outside if rotated (or vice versa).
        // Unrotated: (115, 85) is inside.
        // Rotated 45 deg: The main axis is diagonal.
        // Let's rely on finding non-zero pixels and checking their bounding box or distribution?
        // Or just trust the center check + visual observation if we were humans.
        // For unit test, checking center is a good sanity check that it drew *something* there.
        // Let's checking bounding box of drawn pixels.
        
        using var nonZero = new Mat();
        Cv2.FindNonZero(result.CvtColor(ColorConversionCodes.BGR2GRAY), nonZero);
        var boundingRect = Cv2.BoundingRect(nonZero);
        
        // If it was 40x10 unrotated, bbox would be approx 40x10.
        // Rotated 45 deg, bbox width should be 40*cos(45) + 10*sin(45) approx 28 + 7 = 35.
        // Height should be similar.
        // Bounding rect should be roughly square-ish compared to 40x10.
        
        boundingRect.Width.Should().BeGreaterThan(20);
        boundingRect.Height.Should().BeGreaterThan(20);
    }

    [Test]
    public void DrawText_VerticalAlign_ShouldAffectPosition()
    {
        var pos = new Position { X = 100, Y = 100 };
        var style = new Style { Color = "#FFFFFF", FontSize = 20, StrokeWidth = 1 };
        var content = "----"; // Use dashes to see vertical position clearly

        // Top Align
        var annTop = new VisualAnnotation();
        annTop.AddShape(new Shape { Type = "text", Content = content, Position = pos, Style = style, Align = new Align { Vertical = "top" } });
        using var resTop = _render.DrawAnnotations(new Mat(200, 200, MatType.CV_8UC3, Scalar.All(0)), annTop);
        
        // Bottom Align
        var annBottom = new VisualAnnotation();
        annBottom.AddShape(new Shape { Type = "text", Content = content, Position = pos, Style = style, Align = new Align { Vertical = "bottom" } });
        using var resBottom = _render.DrawAnnotations(new Mat(200, 200, MatType.CV_8UC3, Scalar.All(0)), annBottom);

        double GetAverageY(Mat img)
        {
            using var gray = img.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var nonZero = new Mat();
            Cv2.FindNonZero(gray, nonZero);
            if (nonZero.Rows == 0) return 0;
            
            long sumY = 0;
            var indexer = nonZero.GetGenericIndexer<OpenCvSharp.Point>();
            for (int i = 0; i < nonZero.Rows; i++)
            {
                sumY += indexer[i].Y;
            }
            return (double)sumY / nonZero.Rows;
        }

        double yTop = GetAverageY(resTop);
        double yBottom = GetAverageY(resBottom);

        // "top" align means text is below the anchor. Y values should be higher.
        // "bottom" align means text is above the anchor. Y values should be lower.
        
        yTop.Should().BeGreaterThan(yBottom, "Top aligned text should be drawn below the anchor, resulting in higher Y pixel coordinates than Bottom aligned text.");
    }

    [Test]
    public void DrawText_WithBackgroundColor_ShouldDrawBackgroundRect()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "text",
            Content = "Test",
            Position = new Position { X = 50, Y = 50 },
            Style = new Style 
            { 
                Color = "#FFFFFF", 
                BackgroundColor = "#FF0000", // Red
                Opacity = 1.0f 
            },
            Align = new Align { Vertical = "top" }
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // We expect Red pixels (0, 0, 255) in the background.
        // Text is White (255, 255, 255).
        // Let's count red pixels.
        
        int redPixelCount = 0;
        var indexer = result.GetGenericIndexer<Vec3b>();
        
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                var pixel = indexer[y, x];
                // Pure Red: B=0, G=0, R=255
                if (pixel.Item0 == 0 && pixel.Item1 == 0 && pixel.Item2 == 255)
                {
                    redPixelCount++;
                }
            }
        }

        redPixelCount.Should().BeGreaterThan(10, "Should have some red background pixels");
    }

    [Test]
    public void DrawCircle_ShouldDrawCircle()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "circle",
            Center = new Center { X = 100, Y = 100 },
            Radius = 20,
            Style = new Style { FillColor = "#00FF00", Opacity = 1.0f } // Green
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Center (100, 100) -> Green (0, 255, 0)
        var pixel = result.Get<Vec3b>(100, 100);
        pixel.Item0.Should().Be(0);
        pixel.Item1.Should().Be(255);
        pixel.Item2.Should().Be(0);
    }

    [Test]
    public void DrawPolyline_ShouldDrawLines()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "polyline",
            Points = new[] { new Perceptron.Domain.Entity.Annotation.Point { X = 10, Y = 10 }, new Perceptron.Domain.Entity.Annotation.Point { X = 100, Y = 10 } },
            Style = new Style { StrokeColor = "#0000FF", StrokeWidth = 2, Opacity = 1.0f } // Blue
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Check midpoint (55, 10) -> Should be Blue (255, 0, 0)
        var pixel = result.Get<Vec3b>(10, 55);
        pixel.Item0.Should().Be(255);
        pixel.Item1.Should().Be(0);
        pixel.Item2.Should().Be(0);
    }

    [Test]
    public void DrawPolygon_ShouldFillAndStroke()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "polygon",
            Points = new[] { new Perceptron.Domain.Entity.Annotation.Point { X = 50, Y = 50 }, new Perceptron.Domain.Entity.Annotation.Point { X = 100, Y = 50 }, new Perceptron.Domain.Entity.Annotation.Point { X = 75, Y = 100 } },
            Style = new Style { FillColor = "#00FF00", StrokeColor = "#0000FF", StrokeWidth = 2, Opacity = 1.0f }
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Center (75, 75) -> Should be Green (Fill)
        var centerPixel = result.Get<Vec3b>(75, 75);
        centerPixel.Item0.Should().Be(0);
        centerPixel.Item1.Should().Be(255);
        centerPixel.Item2.Should().Be(0);

        // Edge (near 50, 50) -> Should be Blue (Stroke)
        // Line from (50,50) to (100,50) is horizontal at Y=50.
        var edgePixel = result.Get<Vec3b>(50, 75); 
        edgePixel.Item0.Should().Be(255);
        edgePixel.Item1.Should().Be(0);
        edgePixel.Item2.Should().Be(0);
    }

    [Test]
    public void DrawRect_WithStrokeAndDash_ShouldDrawDashedBorder()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 10, Y = 10 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 100, Height = 20 },
            Style = new Style { StrokeColor = "#FFFFFF", StrokeWidth = 1, Dash = new int[] { 5, 5 }, Opacity = 1.0f }
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Top edge is from (10,10) to (110,10).
        bool foundWhite = false;
        bool foundBlack = false;
        
        // Check Y=10, but also check Y=9, Y=11 in case of off-by-one rendering
        for (int y = 9; y <= 11; y++)
        {
            for (int x = 10; x <= 110; x++)
            {
                var p = result.Get<Vec3b>(y, x);
                if (p.Item0 > 128) foundWhite = true; // Allow for anti-aliasing
                if (p.Item0 < 50) foundBlack = true;
            }
        }

        foundWhite.Should().BeTrue("Should have white segments");
        foundBlack.Should().BeTrue("Should have black gaps (dashed)");
    }

    [Test]
    public void DrawShape_WithOpacity_ShouldBlendColors()
    {
        // Draw White rect with 0.5 opacity on Black background
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 0, Y = 0 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 10, Height = 10 },
            Style = new Style { FillColor = "#FFFFFF", Opacity = 0.5f }
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        var pixel = result.Get<Vec3b>(5, 5);
        // 0.5 * 255 + 0.5 * 0 = 127.5 -> 127 or 128
        pixel.Item0.Should().BeInRange(120, 135);
    }

    [Test]
    public void DrawShapes_WithZIndex_ShouldRespectOrder()
    {
        var annotation = new VisualAnnotation();
        // Shape 1: Blue, Z=1
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 0, Y = 0 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 20, Height = 20 },
            Style = new Style { FillColor = "#0000FF", ZIndex = 1, Opacity = 1.0f } 
        });
        // Shape 2: Green, Z=2
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 5, Y = 5 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 10, Height = 10 },
            Style = new Style { FillColor = "#00FF00", ZIndex = 2, Opacity = 1.0f } 
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Center (10, 10) should be Green (Z=2 is on top)
        var centerPixel = result.Get<Vec3b>(10, 10);
        centerPixel.Item0.Should().Be(0);
        centerPixel.Item1.Should().Be(255); // Green
        centerPixel.Item2.Should().Be(0);
    }

    [Test]
    public void DrawShape_WithVisibleFalse_ShouldNotDraw()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 0, Y = 0 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 20, Height = 20 },
            Style = new Style { FillColor = "#FFFFFF", Visible = false }
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        // Should be black
        var pixel = result.Get<Vec3b>(10, 10);
        pixel.Item0.Should().Be(0);
    }

    [Test]
    public void DrawText_HorizontalAlign_ShouldAffectPosition()
    {
        var pos = new Position { X = 100, Y = 100 };
        var style = new Style { Color = "#FFFFFF", FontSize = 20 };
        var content = "----"; 

        // Left (Default)
        var annLeft = new VisualAnnotation();
        annLeft.AddShape(new Shape { Type = "text", Content = content, Position = pos, Style = style, Align = new Align { Horizontal = "left" } });
        using var resLeft = _render.DrawAnnotations(new Mat(200, 200, MatType.CV_8UC3, Scalar.All(0)), annLeft);
        
        // Right
        var annRight = new VisualAnnotation();
        annRight.AddShape(new Shape { Type = "text", Content = content, Position = pos, Style = style, Align = new Align { Horizontal = "right" } });
        using var resRight = _render.DrawAnnotations(new Mat(200, 200, MatType.CV_8UC3, Scalar.All(0)), annRight);

        double GetAverageX(Mat img)
        {
            using var gray = img.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var nonZero = new Mat();
            Cv2.FindNonZero(gray, nonZero);
            if (nonZero.Rows == 0) return 0;
            
            long sumX = 0;
            var indexer = nonZero.GetGenericIndexer<OpenCvSharp.Point>();
            for (int i = 0; i < nonZero.Rows; i++)
            {
                sumX += indexer[i].X;
            }
            return (double)sumX / nonZero.Rows;
        }

        double xLeft = GetAverageX(resLeft);
        double xRight = GetAverageX(resRight);

        // Left aligned text should be to the right of anchor (higher X).
        // Right aligned text should be to the left of anchor (lower X).
        xLeft.Should().BeGreaterThan(xRight);
    }

    [Test]
    public void DrawShape_WithRgbaColor_ShouldParseCorrectly()
    {
        var annotation = new VisualAnnotation();
        annotation.AddShape(new Shape
        {
            Type = "rect",
            Origin = new Origin { X = 0, Y = 0 },
            Size = new Perceptron.Domain.Entity.Annotation.Size { Width = 20, Height = 20 },
            Style = new Style { FillColor = "rgba(0, 255, 0, 1)", Opacity = 1.0f } // Green
        });

        using var result = _render.DrawAnnotations(_canvas, annotation);

        var pixel = result.Get<Vec3b>(10, 10);
        pixel.Item0.Should().Be(0);
        pixel.Item1.Should().Be(255); // Green
        pixel.Item2.Should().Be(0);
    }
}
