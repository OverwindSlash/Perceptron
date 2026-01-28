using MessagePipe;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Service.Pipeline;

namespace Perceptron.Service.Tests.Pipeline;

[TestFixture]
public class VideoFrameSlideWindowTests
{
    private Mock<IPublisher<ObjectExpiredEvent>> _mockObjPublisher;
    private Mock<IPublisher<FrameExpiredEvent>> _mockFramePublisher;
    private VideoFrameSlideWindow _slideWindow;

    [SetUp]
    public void Setup()
    {
        _mockObjPublisher = new Mock<IPublisher<ObjectExpiredEvent>>();
        _mockFramePublisher = new Mock<IPublisher<FrameExpiredEvent>>();
    }

    [TearDown]
    public void TearDown()
    {
        _slideWindow?.Dispose();
    }

    private Frame CreateFrame(long frameId, List<DetectedObject> objects = null)
    {
        var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var frame = new Frame("test_source", frameId, frameId * 100, mat);
        if (objects != null)
        {
            frame.DetectedObjects = objects;
        }
        return frame;
    }

    private DetectedObject CreateObject(string sourceId, long frameId, int labelId, string label, int trackingId)
    {
        return new DetectedObject(
            sourceId: sourceId,
            frameId: frameId,
            utcTimeStamp: DateTime.UtcNow,
            labelId: labelId,
            label: label,
            confidence: 0.9f,
            bbox: BoundingBox.CreateFromRect(10, 10, 50, 50),
            trackingId: trackingId
        );
    }

    [Test]
    public void Initialization_ShouldHaveEmptyQueue()
    {
        _slideWindow = new VideoFrameSlideWindow(10);
        Assert.That(_slideWindow.Frames.Count, Is.EqualTo(0));
    }

    [Test]
    public void AddNewFrame_ShouldAddFrameAndTrackObjects()
    {
        _slideWindow = new VideoFrameSlideWindow(10);
        _slideWindow.SetPublisher(_mockObjPublisher.Object);
        _slideWindow.SetPublisher(_mockFramePublisher.Object);

        var obj1 = CreateObject("src1", 1, 1, "person", 101);
        var frame1 = CreateFrame(1, new List<DetectedObject> { obj1 });

        _slideWindow.AddNewFrame(frame1);

        Assert.That(_slideWindow.Frames.Count, Is.EqualTo(1));
        Assert.That(_slideWindow.IsObjIdAlive(obj1.Id), Is.True);
        var frames = _slideWindow.GetFramesContainObjectId(obj1.Id);
        Assert.That(frames, Has.Count.EqualTo(1));
        Assert.That(frames[0], Is.EqualTo(frame1));
    }

    [Test]
    public void AddNewFrame_WhenWindowFull_ShouldExpireOldestFrameAndObjects()
    {
        // Arrange
        int windowSize = 2;
        _slideWindow = new VideoFrameSlideWindow(windowSize);
        _slideWindow.SetPublisher(_mockObjPublisher.Object);
        _slideWindow.SetPublisher(_mockFramePublisher.Object);

        // Object 101 only in Frame 1
        var obj1 = CreateObject("src1", 1, 1, "person", 101);
        var frame1 = CreateFrame(1, new List<DetectedObject> { obj1 });

        // Object 102 in Frame 2
        var obj2 = CreateObject("src1", 2, 1, "person", 102);
        var frame2 = CreateFrame(2, new List<DetectedObject> { obj2 });
            
        _slideWindow.AddNewFrame(frame1);
        _slideWindow.AddNewFrame(frame2);

        Assert.That(_slideWindow.Frames.Count, Is.EqualTo(2));

        // Act: Add Frame 3, should expire Frame 1
        var frame3 = CreateFrame(3, new List<DetectedObject>());
        _slideWindow.AddNewFrame(frame3);

        // Assert
        Assert.That(_slideWindow.Frames.Count, Is.EqualTo(2)); // Size remains 2 (frame2, frame3)
            
        // Frame 1 expired
        _mockFramePublisher.Verify(p => p.Publish(It.Is<FrameExpiredEvent>(e => e.FrameId == frame1.FrameId)), Times.Once);
        Assert.That(frame1.IsDisposed, Is.True, "Expired frame should be disposed");
            
        // Object 101 expired (since it was only in Frame 1)
        _mockObjPublisher.Verify(p => p.Publish(It.Is<ObjectExpiredEvent>(e => e.Id == obj1.Id)), Times.Once);
            
        Assert.That(_slideWindow.IsObjIdAlive(obj1.Id), Is.False);
        Assert.That(_slideWindow.IsObjIdAlive(obj2.Id), Is.True);
    }

    [Test]
    public void AddNewFrame_WhenObjectPersists_ShouldNotExpireObject()
    {
        // Arrange
        int windowSize = 2;
        _slideWindow = new VideoFrameSlideWindow(windowSize);
        _slideWindow.SetPublisher(_mockObjPublisher.Object);
        _slideWindow.SetPublisher(_mockFramePublisher.Object);

        // Object 101 in Frame 1
        var obj1_f1 = CreateObject("src1", 1, 1, "person", 101);
        var frame1 = CreateFrame(1, new List<DetectedObject> { obj1_f1 });

        // Object 101 in Frame 2 (same ID)
        var obj1_f2 = CreateObject("src1", 2, 1, "person", 101); 
        var frame2 = CreateFrame(2, new List<DetectedObject> { obj1_f2 });
            
        _slideWindow.AddNewFrame(frame1);
        _slideWindow.AddNewFrame(frame2);

        // Act: Add Frame 3, should expire Frame 1
        var frame3 = CreateFrame(3, new List<DetectedObject>());
        _slideWindow.AddNewFrame(frame3);

        // Assert
        // Frame 1 expired
        _mockFramePublisher.Verify(p => p.Publish(It.Is<FrameExpiredEvent>(e => e.FrameId == frame1.FrameId)), Times.Once);
        Assert.That(frame1.IsDisposed, Is.True);

        // Object 101 should NOT be expired because it exists in Frame 2
        _mockObjPublisher.Verify(p => p.Publish(It.Is<ObjectExpiredEvent>(e => e.Id == obj1_f1.Id)), Times.Never);
            
        Assert.That(_slideWindow.IsObjIdAlive(obj1_f1.Id), Is.True);
            
        // Check existence count/frames logic implicitly
        var frames = _slideWindow.GetFramesContainObjectId(obj1_f1.Id);
        Assert.That(frames, Has.Count.EqualTo(1));
        Assert.That(frames[0], Is.EqualTo(frame2));
    }

    [Test]
    public void Dispose_ShouldPublishEventsForRemainingItems()
    {
        // Arrange
        _slideWindow = new VideoFrameSlideWindow(10);
        _slideWindow.SetPublisher(_mockObjPublisher.Object);
        _slideWindow.SetPublisher(_mockFramePublisher.Object);

        var obj1 = CreateObject("src1", 1, 1, "person", 101);
        var frame1 = CreateFrame(1, new List<DetectedObject> { obj1 });
        _slideWindow.AddNewFrame(frame1);

        // Act
        _slideWindow.Dispose();

        // Assert
        _mockObjPublisher.Verify(p => p.Publish(It.Is<ObjectExpiredEvent>(e => e.Id == obj1.Id)), Times.Once);
        _mockFramePublisher.Verify(p => p.Publish(It.Is<FrameExpiredEvent>(e => e.FrameId == frame1.FrameId)), Times.Once);
    }
}