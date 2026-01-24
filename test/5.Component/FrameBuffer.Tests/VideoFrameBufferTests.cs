using FrameBuffer.TwoModes;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.FrameBuffer;
using Perceptron.Domain.Entity.VideoStream;
using NUnit.Framework;

namespace FrameBuffer.Tests;

[TestFixture]
public class VideoFrameBufferTests
{
    private VideoFrameBuffer? _buffer;
    private Mat _testMat;

    [SetUp]
    public void Setup()
    {
        _testMat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Red);
    }

    [TearDown]
    public void TearDown()
    {
        _buffer?.Dispose();
        _testMat.Dispose();
    }

    [Test]
    public void Constructor_ShouldInitializeWithDefaultValues_WhenPreferencesIsNull()
    {
        _buffer = new VideoFrameBuffer(null);

        Assert.That(_buffer.BufferSize, Is.EqualTo(100));
        Assert.That(_buffer.Mode, Is.EqualTo(FrameBufferMode.BlockingWait));
    }

    [Test]
    public void Constructor_ShouldInitializeWithProvidedPreferences()
    {
        var preferences = new Dictionary<string, string>
        {
            { "BufferSize", "50" },
            { "Mode", "ReturnBlankFrame" }
        };

        _buffer = new VideoFrameBuffer(preferences);

        Assert.That(_buffer.BufferSize, Is.EqualTo(50));
        Assert.That(_buffer.Mode, Is.EqualTo(FrameBufferMode.ReturnBlankFrame));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentException_WhenBufferSizeIsInvalid()
    {
        var preferences = new Dictionary<string, string>
        {
            { "BufferSize", "0" }
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoFrameBuffer(preferences));
    }

    [Test]
    public void PushFrame_ShouldEnqueueFrame()
    {
        _buffer = new VideoFrameBuffer(null);
        using var frame = CreateTestFrame();

        _buffer.PushFrame(frame);

        Assert.That(_buffer.Count, Is.EqualTo(1));
    }

    [Test]
    public void RetrieveFrame_ShouldReturnFrame_WhenBufferIsNotEmpty()
    {
        _buffer = new VideoFrameBuffer(null);
        using var frame = CreateTestFrame();
        _buffer.PushFrame(frame);

        var retrievedFrame = _buffer.RetrieveFrame();

        Assert.That(retrievedFrame, Is.Not.Null);
        Assert.That(retrievedFrame.SourceId, Is.EqualTo(frame.SourceId));
        // Note: retrievedFrame is same instance but we need to manage disposal
        retrievedFrame.Dispose(); 
    }

    [Test]
    public void RetrieveFrame_ShouldBlock_WhenBufferIsEmpty_AndModeIsBlockingWait()
    {
        _buffer = new VideoFrameBuffer(null); // Default BlockingWait

        var task = Task.Run(() =>
        {
            return _buffer.RetrieveFrame();
        });

        // Give it some time to block
        Thread.Sleep(100);
        Assert.That(task.IsCompleted, Is.False);

        // Push a frame to unblock
        using var frame = CreateTestFrame();
        _buffer.PushFrame(frame);

        // Wait for task completion
        Assert.That(task.Wait(1000), Is.True);
        
        var retrievedFrame = task.Result;
        Assert.That(retrievedFrame, Is.Not.Null);
        retrievedFrame.Dispose();
    }

    [Test]
    public void RetrieveFrame_ShouldReturnBlankFrame_WhenBufferIsEmpty_AndModeIsReturnBlankFrame()
    {
        var preferences = new Dictionary<string, string>
        {
            { "Mode", "ReturnBlankFrame" }
        };
        _buffer = new VideoFrameBuffer(preferences);

        // We need to push at least one frame first to initialize the blank frame template
        using var frame = CreateTestFrame();
        _buffer.PushFrame(frame);
        
        // Retrieve the pushed frame first
        var retrievedFrame1 = _buffer.RetrieveFrame();
        Assert.That(retrievedFrame1.IsBlankFrame, Is.False);
        retrievedFrame1.Dispose();

        // Now buffer is empty, should return blank frame
        var blankFrame = _buffer.RetrieveFrame();

        Assert.That(blankFrame, Is.Not.Null);
        Assert.That(blankFrame.IsBlankFrame, Is.True);
        Assert.That(blankFrame.SourceId, Is.EqualTo("BlankFrame"));
        
        blankFrame.Dispose();
    }
    
    [Test]
    public void RetrieveFrame_ShouldWait_WhenBufferIsEmpty_AndModeIsReturnBlankFrame_ButNoTemplateYet()
    {
        var preferences = new Dictionary<string, string>
        {
            { "Mode", "ReturnBlankFrame" }
        };
        _buffer = new VideoFrameBuffer(preferences);

        // No frames pushed yet, so no blank frame template exists.
        // It should block until a frame is pushed (which initializes the template).

        var task = Task.Run(() =>
        {
            return _buffer.RetrieveFrame();
        });

        Thread.Sleep(100);
        Assert.That(task.IsCompleted, Is.False);

        using var frame = CreateTestFrame();
        _buffer.PushFrame(frame);

        Assert.That(task.Wait(1000), Is.True);
        var retrievedFrame = task.Result;
        
        // The first frame retrieved is the one we pushed (VideoFrameBuffer doesn't consume it to make blank frame, just uses it as template)
        Assert.That(retrievedFrame.IsBlankFrame, Is.False);
        retrievedFrame.Dispose();
    }

    [Test]
    public void FrameDrop_ShouldInvokeHandlers()
    {
        // Set small buffer size
        var preferences = new Dictionary<string, string>
        {
            { "BufferSize", "1" }
        };
        _buffer = new VideoFrameBuffer(preferences);
        
        bool eventRaised = false;
        bool handlerInvoked = false;

        _buffer.OnItemDropped += (f) => eventRaised = true;
        _buffer.RegisterFrameDropHandler((f) => handlerInvoked = true);

        using var frame1 = CreateTestFrame(1);
        using var frame2 = CreateTestFrame(2);

        _buffer.PushFrame(frame1);
        _buffer.PushFrame(frame2); // Should drop frame1

        Assert.That(eventRaised, Is.True);
        Assert.That(handlerInvoked, Is.True);
    }

    private Frame CreateTestFrame(long id = 0)
    {
        return new Frame("Test", id, 0, _testMat.Clone());
    }
}
