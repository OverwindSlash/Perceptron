using Algorithm.General.MotionDetection;
using Algorithm.General.MotionDetection.Strategy;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;

namespace MotionDetection.Tests;

[TestFixture]
public class OptimizedAdvancedStrategyTests
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
    public void Initialize_WithInvalidSize_ReturnsFalse()
    {
        var settings = CreateSettings(new Dictionary<string, string>());
        var strategy = new OptimizedAdvancedStrategy(settings);

        var ok = strategy.Initialize(new Size(0, 0));
        Assert.That(ok, Is.False);
    }

    [Test]
    public void DetectMotionRegions_WhenBuffersEnsureAndFilteringWorks()
    {
        var settings = CreateSettings(new Dictionary<string, string>
        {
            { "MorphKernelSize", "3" },
            { "MorphOpenIter", "1" },
            { "MorphCloseIter", "1" },
            { "BaseMotionDetectionMinArea", "20" },
            { "BaseMotionDetectionMaxArea", "5000" },
            { "AspectRatioThreshold", "2.0" },
            { "MaxContoursToProcess", "10" },
            { "BoundingBoxMergeThreshold", "0.25" },
            { "HeatAdd", "50" },
            { "HeatDecay", "4" },
            { "HeatThreshold", "60" }
        });
        var strategy = new OptimizedAdvancedStrategy(settings);
        Assert.That(strategy.Initialize(new Size(120, 120)), Is.True);

        var recycler = new Mock<Action<Mat>>();
        var scene = new Mat(120, 120, MatType.CV_8UC3, Scalar.Black);
        var frame = CreateFrame(scene, recycler);

        var mask = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mask, new Rect(10, 10, 2, 20), Scalar.White, -1);
        Cv2.Rectangle(mask, new Rect(40, 40, 10, 10), Scalar.White, -1);

        var rois = strategy.DetectMotionRegions(frame, mask, 1);
        Assert.That(rois.Count, Is.EqualTo(1));

        frame.Dispose();
        mask.Dispose();
        recycler.Verify(r => r(It.IsAny<Mat>()), Times.Once);
    }

    [Test]
    public void GetHistoricalMotionRois_CombinesHeatmapAndHistory()
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
            { "BoundingBoxMergeThreshold", "0.2" },
            { "HeatAdd", "80" },
            { "HeatDecay", "2" },
            { "HeatThreshold", "60" }
        });
        var strategy = new OptimizedAdvancedStrategy(settings);
        Assert.That(strategy.Initialize(new Size(100, 100)), Is.True);

        var recycler = new Mock<Action<Mat>>();
        var scene = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var frame = CreateFrame(scene, recycler);

        var mask = new Mat(100, 100, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mask, new Rect(20, 20, 10, 10), Scalar.White, -1);
        var rois = strategy.DetectMotionRegions(frame, mask, 1);
        Assert.That(rois.Count, Is.GreaterThanOrEqualTo(1));

        var historical = strategy.GetHistoricalMotionRois();
        Assert.That(historical.Count, Is.GreaterThanOrEqualTo(1));

        frame.Dispose();
        mask.Dispose();
        recycler.Verify(r => r(It.IsAny<Mat>()), Times.Once);
    }
}
