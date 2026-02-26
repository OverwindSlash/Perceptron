using Algorithm.General.MotionDetection;
using Algorithm.General.MotionDetection.Strategy;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;

namespace MotionDetection.Tests;

[TestFixture]
public class ClassicMotionDetectionStrategyTests
{
    private static MotionDetectionSettings CreateSettings(Dictionary<string, string> overrides)
    {
        var settings = new MotionDetectionSettings();
        settings.ParsePreferences(overrides);
        return settings;
    }

    private static Frame CreateFrame(Mat scene, Mock<Action<Mat>> recycler)
    {
        recycler.Setup(r => r(It.IsAny<Mat>())).Callback<Mat>(m => m.Dispose());
        var frame = new Frame("source", 1L, 0L, scene, recycler.Object);
        frame.DetectedObjects = Array.Empty<Perceptron.Domain.Entity.ObjectDetection.DetectedObject>();
        frame.Annotation = new Perceptron.Domain.Entity.Annotation.VisualAnnotation("source", DateTime.UtcNow, 1L, scene.Width, scene.Height);
        return frame;
    }

    [Test]
    public void DetectMotionRegions_WhenNotInitialized_ReturnsEmpty()
    {
        var settings = CreateSettings(new Dictionary<string, string>());
        var strategy = new ClassicMotionDetectionStrategy(settings);

        var recycler = new Mock<Action<Mat>>();
        var scene = new Mat(50, 50, MatType.CV_8UC3, Scalar.Black);
        var frame = CreateFrame(scene, recycler);
        var mask = new Mat(50, 50, MatType.CV_8UC1, Scalar.Black);

        var regions = strategy.DetectMotionRegions(frame, mask, 1);

        Assert.That(regions, Is.Empty);

        frame.Dispose();
        mask.Dispose();
        recycler.Verify(r => r(It.IsAny<Mat>()), Times.Once);
    }

    [Test]
    public void DetectMotionRegions_FiltersByAspectRatio()
    {
        var settings = CreateSettings(new Dictionary<string, string>
        {
            { "MorphKernelSize", "1" },
            { "MorphOpenIter", "1" },
            { "MorphCloseIter", "1" },
            { "BaseMotionDetectionMinArea", "20" },
            { "BaseMotionDetectionMaxArea", "5000" },
            { "AspectRatioThreshold", "2.0" },
            { "MaxContoursToProcess", "10" },
            { "BoundingBoxMergeThreshold", "0.25" }
        });
        var strategy = new ClassicMotionDetectionStrategy(settings);
        strategy.Initialize(new Size(100, 100));

        var recycler = new Mock<Action<Mat>>();
        var scene = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var frame = CreateFrame(scene, recycler);
        var mask = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);

        Cv2.Rectangle(mask, new Rect(10, 10, 2, 20), Scalar.White, -1);
        Cv2.Rectangle(mask, new Rect(40, 40, 10, 10), Scalar.White, -1);

        var regions = strategy.DetectMotionRegions(frame, mask, 2);

        Assert.That(regions.Count, Is.EqualTo(1));
        Assert.That(regions[0].Width >= 10 && regions[0].Height >= 10, Is.True);

        frame.Dispose();
        mask.Dispose();
        recycler.Verify(r => r(It.IsAny<Mat>()), Times.Once);
    }

    [Test]
    public void DetectMotionRegions_LimitsContourCount()
    {
        var settings = CreateSettings(new Dictionary<string, string>
        {
            { "MorphKernelSize", "1" },
            { "MorphOpenIter", "1" },
            { "MorphCloseIter", "1" },
            { "BaseMotionDetectionMinArea", "1" },
            { "BaseMotionDetectionMaxArea", "5000" },
            { "AspectRatioThreshold", "10.0" },
            { "MaxContoursToProcess", "2" },
            { "BoundingBoxMergeThreshold", "0.25" }
        });
        var strategy = new ClassicMotionDetectionStrategy(settings);
        strategy.Initialize(new Size(100, 100));

        var recycler = new Mock<Action<Mat>>();
        var scene = new Mat(120, 120, MatType.CV_8UC3, Scalar.Black);
        var frame = CreateFrame(scene, recycler);
        var mask = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);

        Cv2.Rectangle(mask, new Rect(5, 5, 5, 5), Scalar.White, -1);
        Cv2.Rectangle(mask, new Rect(20, 5, 5, 5), Scalar.White, -1);
        Cv2.Rectangle(mask, new Rect(35, 5, 5, 5), Scalar.White, -1);
        Cv2.Rectangle(mask, new Rect(50, 5, 5, 5), Scalar.White, -1);
        Cv2.Rectangle(mask, new Rect(65, 5, 5, 5), Scalar.White, -1);

        var regions = strategy.DetectMotionRegions(frame, mask, 3);

        Assert.That(regions.Count, Is.EqualTo(2));

        frame.Dispose();
        mask.Dispose();
        recycler.Verify(r => r(It.IsAny<Mat>()), Times.Once);
    }

    [Test]
    public void UpdateMotionHistory_ExpiresOldFrames()
    {
        var settings = CreateSettings(new Dictionary<string, string>
        {
            { "MotionHistoryDurationFrames", "2" }
        });
        var strategy = new ClassicMotionDetectionStrategy(settings);

        var roi1 = new Rect(1, 1, 10, 10);
        var roi2 = new Rect(2, 2, 10, 10);

        strategy.UpdateMotionHistory(new List<Rect> { roi1 }, 1);
        strategy.UpdateMotionHistory(new List<Rect> { roi2 }, 4);

        var historical = strategy.GetHistoricalMotionRois();

        Assert.That(historical.Count, Is.EqualTo(1));
        Assert.That(historical[0], Is.EqualTo(roi2));
    }
}
