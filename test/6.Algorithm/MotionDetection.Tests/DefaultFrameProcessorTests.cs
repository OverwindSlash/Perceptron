using Algorithm.General.MotionDetection;
using Algorithm.General.MotionDetection.Core;
using Algorithm.General.MotionDetection.Strategy;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Entity.VideoStream;
using NUnit.Framework;

namespace MotionDetection.Tests;

[TestFixture]
public class DefaultFrameProcessorTests
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
    public void BaselineEstablishes_AfterConfiguredFrameCount()
    {
        var settings = CreateSettings(new Dictionary<string, string>
        {
            { "BaselineFrameCount", "2" },
            { "MaxProcessWidth", "200" },
            { "MaxProcessHeight", "200" }
        });
        var resource = new DefaultResourceManager();
        var perf = new DefaultPerformanceMonitor(settings);
        var strategy = new ClassicMotionDetectionStrategy(settings);
        var processor = new DefaultFrameProcessor(resource, perf, strategy);

        Assert.That(processor.Initialize(new Size(64, 64), settings), Is.True);

        var recycler = new Mock<Action<Mat>>();
        var scene = new Mat(64, 64, MatType.CV_8UC3, Scalar.Black);
        var frame1 = CreateFrame(scene.Clone(), recycler);
        var frame2 = CreateFrame(scene.Clone(), recycler);

        var r1 = processor.ProcessFrame(frame1, 1);
        var r2 = processor.ProcessFrame(frame2, 2);

        var status = processor.GetStatus();
        Assert.That(status.BaselineEstablished, Is.True);
        Assert.That(perf.GetMetrics().TotalFramesProcessed, Is.EqualTo(2));

        frame1.Dispose();
        frame2.Dispose();
        scene.Dispose();
        processor.Dispose();
    }

    [Test]
    public void Resizes_OnFrameSizeChange_AndResetsBaseline()
    {
        var settings = CreateSettings(new Dictionary<string, string>
        {
            { "BaselineFrameCount", "2" },
            { "MaxProcessWidth", "200" },
            { "MaxProcessHeight", "200" }
        });
        var resource = new DefaultResourceManager();
        var perf = new DefaultPerformanceMonitor(settings);
        var strategy = new ClassicMotionDetectionStrategy(settings);
        var processor = new DefaultFrameProcessor(resource, perf, strategy);

        Assert.That(processor.Initialize(new Size(64, 64), settings), Is.True);

        var recycler = new Mock<Action<Mat>>();
        var scene1 = new Mat(64, 64, MatType.CV_8UC3, Scalar.Black);
        var frame1 = CreateFrame(scene1, recycler);
        processor.ProcessFrame(frame1, 1);

        var scene2 = new Mat(120, 120, MatType.CV_8UC3, Scalar.Black);
        var frame2 = CreateFrame(scene2, recycler);
        processor.ProcessFrame(frame2, 2);

        var status = processor.GetStatus();
        Assert.That(status.OriginalSize, Is.EqualTo(new Size(120, 120)));
        Assert.That(status.BaselineEstablished, Is.False);

        frame1.Dispose();
        frame2.Dispose();
        scene2.Dispose();
        processor.Dispose();
    }
}
