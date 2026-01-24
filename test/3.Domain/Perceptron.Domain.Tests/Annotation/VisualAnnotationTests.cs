using Perceptron.Domain.Annotation;
using System.Text.Json;

namespace Perceptron.Domain.Tests.Annotation;

[TestFixture]
public class VisualAnnotationTests
{
    [Test]
    public void Constructor_ShouldInitializePropertiesCorrectly()
    {
        var sourceId = "test-source";
        var timestamp = DateTimeOffset.UtcNow;
        var frameId = 100L;
        var width = 1920;
        var height = 1080;

        var annotation = new VisualAnnotation(sourceId, timestamp, frameId, width, height);

        Assert.Multiple(() =>
        {
            Assert.That(annotation.Version, Is.EqualTo("1.0"));
            Assert.That(annotation.SourceId, Is.EqualTo(sourceId));
            Assert.That(annotation.Timestamp, Is.EqualTo(timestamp));
            Assert.That(annotation.FrameId, Is.EqualTo(frameId));
            Assert.That(annotation.CoordinateSpace, Is.Not.Null);
            Assert.That(annotation.CoordinateSpace.Width, Is.EqualTo(width));
            Assert.That(annotation.CoordinateSpace.Height, Is.EqualTo(height));
            Assert.That(annotation.CoordinateSpace.Type, Is.EqualTo("pixel"));
            Assert.That(annotation.Shapes, Is.Not.Null);
            Assert.That(annotation.Shapes, Is.Empty);
        });
    }

    [Test]
    public void SetCoordinateSpace_ShouldUpdateValues()
    {
        var annotation = new VisualAnnotation("src", DateTimeOffset.Now, 1, 100, 100);
        
        annotation.SetCoordinateSpace(800, 600, "normalized");

        Assert.Multiple(() =>
        {
            Assert.That(annotation.CoordinateSpace.Width, Is.EqualTo(800));
            Assert.That(annotation.CoordinateSpace.Height, Is.EqualTo(600));
            Assert.That(annotation.CoordinateSpace.Type, Is.EqualTo("normalized"));
        });
    }

    [Test]
    public void AddShape_ShouldAddShapeToList()
    {
        var annotation = new VisualAnnotation("src", DateTimeOffset.Now, 1, 100, 100);
        var shape = new Shape { Id = "s1", Type = "rect" };

        annotation.AddShape(shape);

        Assert.That(annotation.Shapes, Has.Count.EqualTo(1));
        Assert.That(annotation.Shapes[0], Is.SameAs(shape));
    }

    [Test]
    public void AddShape_Null_ShouldNotAdd()
    {
        var annotation = new VisualAnnotation("src", DateTimeOffset.Now, 1, 100, 100);
        
        annotation.AddShape(null);

        Assert.That(annotation.Shapes, Is.Empty);
    }

    [Test]
    public void AddShapes_ShouldAddMultipleShapes()
    {
        var annotation = new VisualAnnotation("src", DateTimeOffset.Now, 1, 100, 100);
        var shapes = new List<Shape>
        {
            new Shape { Id = "s1" },
            new Shape { Id = "s2" }
        };

        annotation.AddShapes(shapes);

        Assert.That(annotation.Shapes, Has.Count.EqualTo(2));
        Assert.That(annotation.Shapes[0].Id, Is.EqualTo("s1"));
        Assert.That(annotation.Shapes[1].Id, Is.EqualTo("s2"));
    }

    [Test]
    public void FromJson_ShouldDeserializeAllShapeTypesAndStyles()
    {
        var json = @"
        {
          ""version"": ""1.0"",
          ""sourceId"": ""test"",
          ""timestamp"": ""2025-01-01T00:00:00Z"",
          ""frameId"": 1,
          ""coordinateSpace"": { ""width"": 100, ""height"": 100 },
          ""shapes"": [
            {
              ""id"": ""r1"",
              ""type"": ""rect"",
              ""origin"": { ""x"": 10, ""y"": 20 },
              ""size"": { ""width"": 30, ""height"": 40 },
              ""rotation"": 45,
              ""style"": { ""fillColor"": ""#FF0000"", ""opacity"": 0.5 }
            },
            {
              ""id"": ""l1"",
              ""type"": ""polyline"",
              ""points"": [ { ""x"": 0, ""y"": 0 }, { ""x"": 10, ""y"": 10 } ],
              ""style"": { ""dash"": [ 5, 5 ], ""strokeWidth"": 2 }
            },
            {
              ""id"": ""p1"",
              ""type"": ""polygon"",
              ""points"": [ { ""x"": 0, ""y"": 0 }, { ""x"": 10, ""y"": 0 }, { ""x"": 5, ""y"": 10 } ],
              ""style"": { ""visible"": false, ""zIndex"": 10 }
            },
            {
                ""id"": ""txt1"",
                ""type"": ""text"",
                ""content"": ""Label"",
                ""position"": { ""x"": 50, ""y"": 50 },
                ""align"": { ""horizontal"": ""left"", ""vertical"": ""bottom"" },
                ""style"": { 
                    ""color"": ""#0000FF"", 
                    ""fontSize"": 12, 
                    ""fontFamily"": ""Arial"", 
                    ""fontWeight"": ""bold"",
                    ""backgroundColor"": ""#FFFFFF"" 
                }
            }
          ]
        }";

        var annotation = VisualAnnotation.FromJson(json);

        Assert.That(annotation.Shapes, Has.Count.EqualTo(4));

        // Rect
        var rect = annotation.Shapes.First(s => s.Id == "r1");
        Assert.Multiple(() =>
        {
            Assert.That(rect.Type, Is.EqualTo("rect"));
            Assert.That(rect.Origin.X, Is.EqualTo(10));
            Assert.That(rect.Origin.Y, Is.EqualTo(20));
            Assert.That(rect.Size.Width, Is.EqualTo(30));
            Assert.That(rect.Size.Height, Is.EqualTo(40));
            Assert.That(rect.Rotation, Is.EqualTo(45));
            Assert.That(rect.Style.FillColor, Is.EqualTo("#FF0000"));
            Assert.That(rect.Style.Opacity, Is.EqualTo(0.5f));
        });

        // Polyline
        var polyline = annotation.Shapes.First(s => s.Id == "l1");
        Assert.Multiple(() =>
        {
            Assert.That(polyline.Type, Is.EqualTo("polyline"));
            Assert.That(polyline.Points, Has.Length.EqualTo(2));
            Assert.That(polyline.Points[1].X, Is.EqualTo(10));
            Assert.That(polyline.Style.Dash, Is.EquivalentTo(new[] { 5, 5 }));
            Assert.That(polyline.Style.StrokeWidth, Is.EqualTo(2));
        });

        // Polygon
        var polygon = annotation.Shapes.First(s => s.Id == "p1");
        Assert.Multiple(() =>
        {
            Assert.That(polygon.Type, Is.EqualTo("polygon"));
            Assert.That(polygon.Points, Has.Length.EqualTo(3));
            Assert.That(polygon.Style.Visible, Is.False);
            Assert.That(polygon.Style.ZIndex, Is.EqualTo(10));
        });

        // Text
        var text = annotation.Shapes.First(s => s.Id == "txt1");
        Assert.Multiple(() =>
        {
            Assert.That(text.Type, Is.EqualTo("text"));
            Assert.That(text.Content, Is.EqualTo("Label"));
            Assert.That(text.Position.X, Is.EqualTo(50));
            Assert.That(text.Align.Horizontal, Is.EqualTo("left"));
            Assert.That(text.Align.Vertical, Is.EqualTo("bottom"));
            Assert.That(text.Style.Color, Is.EqualTo("#0000FF"));
            Assert.That(text.Style.FontSize, Is.EqualTo(12));
            Assert.That(text.Style.FontFamily, Is.EqualTo("Arial"));
            Assert.That(text.Style.FontWeight, Is.EqualTo("bold"));
            
            // Check BackgroundColor type handling
            // Note: System.Text.Json deserializes object property as JsonElement if it's a primitive in JSON
            Assert.That(text.Style.BackgroundColor.ToString(), Is.EqualTo("#FFFFFF"));
        });
    }

    [Test]
    public void Style_DefaultValues_ShouldMatchSpec()
    {
        var style = new Style();
        Assert.Multiple(() =>
        {
            Assert.That(style.StrokeColor, Is.EqualTo("#000000"));
            Assert.That(style.FillColor, Is.Empty);
            Assert.That(style.StrokeWidth, Is.EqualTo(0));
            Assert.That(style.Opacity, Is.EqualTo(1.0f));
            Assert.That(style.Dash, Is.Empty);
            Assert.That(style.Visible, Is.True);
            Assert.That(style.ZIndex, Is.EqualTo(0));
            Assert.That(style.Color, Is.EqualTo("#000000"));
            Assert.That(style.FontSize, Is.EqualTo(0));
            Assert.That(style.FontFamily, Is.EqualTo("Microsoft YaHei"));
            Assert.That(style.FontWeight, Is.EqualTo("normal"));
            Assert.That(style.BackgroundColor, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void FromJson_ShouldDeserializeCorrectly()
    {
        // Using the sample JSON from the spec
        var json = @"
        {
          ""version"": ""1.0"",
          ""sourceId"": ""Cam-POC.Profile_101"",
          ""timestamp"": ""2025-11-26T12:00:00Z"",
          ""frameId"": 12345,
          ""coordinateSpace"": {
            ""type"": ""pixel"",
            ""width"": 1920,
            ""height"": 1080
          },
          ""shapes"": [
            {
              ""id"": ""c1"",
              ""type"": ""circle"",
              ""center"": { ""x"": 200, ""y"": 150 },
              ""radius"": 5,
              ""style"": { ""strokeColor"": ""#FFFF00"", ""strokeWidth"": 1 }
            },
            {
              ""id"": ""t1"",
              ""type"": ""text"",
              ""content"": ""告警""
            }
          ]
        }";

        var annotation = VisualAnnotation.FromJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(annotation.Version, Is.EqualTo("1.0"));
            Assert.That(annotation.SourceId, Is.EqualTo("Cam-POC.Profile_101"));
            Assert.That(annotation.FrameId, Is.EqualTo(12345));
            Assert.That(annotation.Timestamp, Is.EqualTo(DateTimeOffset.Parse("2025-11-26T12:00:00Z")));
            Assert.That(annotation.CoordinateSpace.Width, Is.EqualTo(1920));
            Assert.That(annotation.Shapes, Has.Count.EqualTo(2));
            
            var circle = annotation.Shapes.FirstOrDefault(s => s.Id == "c1");
            Assert.That(circle, Is.Not.Null);
            Assert.That(circle!.Type, Is.EqualTo("circle"));
            Assert.That(circle.Center.X, Is.EqualTo(200));
            Assert.That(circle.Style.StrokeColor, Is.EqualTo("#FFFF00"));

            var text = annotation.Shapes.FirstOrDefault(s => s.Id == "t1");
            Assert.That(text, Is.Not.Null);
            Assert.That(text!.Content, Is.EqualTo("告警"));
        });
    }

    [Test]
    public void FromJson_InvalidJson_ShouldThrowException()
    {
        var invalidJson = "{ invalid }";
        Assert.Throws<JsonException>(() => VisualAnnotation.FromJson(invalidJson));
    }
}
