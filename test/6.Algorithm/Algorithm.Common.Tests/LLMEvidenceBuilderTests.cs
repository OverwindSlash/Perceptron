using Algorithm.Common.LLM;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Algorithm.Common.Tests;

public class LLMEvidenceBuilderTests
{
    [Test]
    public void TryBuildFrameJpeg_ReturnsBytes()
    {
        using var frame = CreateFrame();

        var ok = LLMEvidenceBuilder.TryBuildFrameJpeg(frame, 80, out var bytes);

        Assert.That(ok, Is.True);
        Assert.That(bytes.Length, Is.GreaterThan(0));
    }

    [Test]
    public void TryBuildObjectCropJpeg_UsesSnapshotWhenAvailable()
    {
        using var frame = CreateFrame();
        var detectedObject = CreateObject(frame);
        detectedObject.AttachSnapshot(new Mat(new Size(16, 16), MatType.CV_8UC3, Scalar.White));
        frame.DetectedObjects = [detectedObject];

        var ok = LLMEvidenceBuilder.TryBuildObjectCropJpeg(frame, detectedObject, 85, 0.1, out var bytes);

        Assert.That(ok, Is.True);
        Assert.That(bytes.Length, Is.GreaterThan(0));
    }

    [Test]
    public void TryBuildObjectCropJpeg_FallsBackToPaddedFrameCrop()
    {
        using var frame = CreateFrame();
        var detectedObject = CreateObject(frame);
        frame.DetectedObjects = [detectedObject];

        var ok = LLMEvidenceBuilder.TryBuildObjectCropJpeg(frame, detectedObject, 85, 0.2, out var bytes);

        Assert.That(ok, Is.True);
        Assert.That(bytes.Length, Is.GreaterThan(0));
    }

    [Test]
    public void TryBuildObjectCropJpeg_ReturnsFalseForDisposedObject()
    {
        using var frame = CreateFrame();
        var detectedObject = CreateObject(frame);
        detectedObject.Dispose();

        var ok = LLMEvidenceBuilder.TryBuildObjectCropJpeg(frame, detectedObject, 85, 0.1, out var bytes);

        Assert.That(ok, Is.False);
        Assert.That(bytes, Is.Empty);
    }

    private static Frame CreateFrame()
    {
        return new Frame("source", 1, 0, new Mat(new Size(64, 64), MatType.CV_8UC3, Scalar.Black));
    }

    private static DetectedObject CreateObject(Frame frame)
    {
        return new DetectedObject(
            frame.SourceId,
            frame.FrameId,
            frame.UtcTimeStamp,
            labelId: 1,
            label: "boat",
            confidence: 0.9f,
            bbox: BoundingBox.CreateFromRect(20, 20, 20, 20),
            trackingId: 7);
    }
}
