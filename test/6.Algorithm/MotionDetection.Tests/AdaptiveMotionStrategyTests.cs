using Algorithm.General.MotionDetection;
using Algorithm.General.MotionDetection.Strategy;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;
using NUnit.Framework;

namespace MotionDetection.Tests;

[TestFixture]
public class AdaptiveMotionStrategyTests
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
    public void Initialize_ReturnsTrue_ForValidSize()
    {
        var settings = CreateSettings(new Dictionary<string, string>());
        var strategy = new AdaptiveMotionStrategy(settings);
        Assert.That(strategy.Initialize(new Size(120, 120)), Is.True);
    }

    [Test]
    public void DetectMotionRegions_ReturnsList_AndUpdatesHistory()
    {
        var settings = CreateSettings(new Dictionary<string, string>
        {
            { "MorphKernelSize", "3" },
            { "MorphOpenIter", "1" },
            { "MorphCloseIter", "1" },
            { "BaseMotionDetectionMinArea", "10" },
            { "BaseMotionDetectionMaxArea", "10000" },
            { "AspectRatioThreshold", "5.0" },
            { "MaxContoursToProcess", "10" },
            { "BoundingBoxMergeThreshold", "0.2" }
        });
        var strategy = new AdaptiveMotionStrategy(settings);
        Assert.That(strategy.Initialize(new Size(120, 120)), Is.True);

        var recycler = new Mock<Action<Mat>>();
        var scene = new Mat(120, 120, MatType.CV_8UC3, Scalar.Black);
        var frame = CreateFrame(scene, recycler);

        var mask = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mask, new Rect(10, 10, 12, 12), Scalar.White, -1);
        Cv2.Rectangle(mask, new Rect(70, 70, 8, 8), Scalar.White, -1);

        var rois = strategy.DetectMotionRegions(frame, mask, 1);
        Assert.That(rois, Is.Not.Null);
        Assert.That(rois.Count, Is.GreaterThanOrEqualTo(0));

        var historical = strategy.GetHistoricalMotionRois();
        Assert.That(historical.Count, Is.GreaterThanOrEqualTo(0));

        frame.Dispose();
        mask.Dispose();
        recycler.Verify(r => r(It.IsAny<Mat>()), Times.Once);
    }
}
