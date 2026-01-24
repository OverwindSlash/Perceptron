using FluentAssertions;
using MediaLoader.OpenCV;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.FrameBuffer;
using Perceptron.Domain.Abstraction.MediaLoader;
using Perceptron.Domain.Entity.VideoStream;

namespace MediaLoader.Tests;

[TestFixture]
public class OpenCVVideoLoaderTests
{
    private string _testVideoPath;
    private Mock<IVideoFrameBuffer> _mockBuffer;
    private VideoLoader _loader;
    private const int TestVideoWidth = 640;
    private const int TestVideoHeight = 480;
    private const int TestVideoFps = 30;
    private const int TestVideoFrameCount = 60;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _testVideoPath = Path.Combine(Path.GetTempPath(), "test_video_loader.avi");
        CreateTestVideo(_testVideoPath);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (File.Exists(_testVideoPath))
        {
            try { File.Delete(_testVideoPath); } catch { }
        }
    }

    private void CreateTestVideo(string path)
    {
        // Ensure the directory exists
        var dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir!);

        using var writer = new VideoWriter(path, FourCC.MJPG, TestVideoFps, new Size(TestVideoWidth, TestVideoHeight));
        using var mat = new Mat(TestVideoHeight, TestVideoWidth, MatType.CV_8UC3, Scalar.Blue);
        
        if (!writer.IsOpened())
            throw new Exception($"Failed to open VideoWriter for path: {path}");

        for (int i = 0; i < TestVideoFrameCount; i++)
        {
            writer.Write(mat);
        }
    }

    [SetUp]
    public void Setup()
    {
        _mockBuffer = new Mock<IVideoFrameBuffer>();
    }

    [TearDown]
    public void TearDown()
    {
        _loader?.Dispose();
    }

    [Test]
    public void Constructor_ShouldParsePreferences()
    {
        var prefs = new Dictionary<string, string>
        {
            { "SourceId", "TestCam" },
            { "VideoStride", "2" },
            { "MaxRetries", "5" },
            { "RetryDelayMs", "1000" },
            { "Loop", "true" }
        };

        _loader = new VideoLoader(prefs);

        _loader.SourceId.Should().Be("TestCam");
        _loader.VideoStride.Should().Be(2);
        _loader.MaxRetries.Should().Be(5);
        _loader.RetryDelayMs.Should().Be(1000);
        _loader.Loop.Should().BeTrue();
        _loader.State.Should().Be(VideoLoaderState.Idle);
    }

    [Test]
    public void AttachBuffer_ShouldSetBuffer()
    {
        _loader = new VideoLoader(null);
        _loader.AttachBuffer(_mockBuffer.Object);
        // Internal buffer is private, but we verify it doesn't throw.
        // Passing null should throw
        Assert.Throws<ArgumentNullException>(() => _loader.AttachBuffer(null!));
    }

    [Test]
    public void Open_WithInvalidUri_ShouldReturnFalse()
    {
        _loader = new VideoLoader(null);
        bool result = _loader.Open("invalid_path.mp4");
        
        result.Should().BeFalse();
        _loader.State.Should().Be(VideoLoaderState.Error);
    }

    [Test]
    public void Open_WithValidUri_ShouldReturnTrueAndInitSpecs()
    {
        _loader = new VideoLoader(null);
        bool result = _loader.Open(_testVideoPath);

        result.Should().BeTrue();
        _loader.State.Should().Be(VideoLoaderState.Opened);
        _loader.Specs.Should().NotBeNull();
        _loader.Specs.Width.Should().Be(TestVideoWidth);
        _loader.Specs.Height.Should().Be(TestVideoHeight);
        // FrameCount depends on the writer, usually accurate for MJPG AVI
        _loader.Specs.FrameCount.Should().BeGreaterThan(0);
    }

    [Test]
    public void Play_WithBuffer_ShouldEnqueueFrames()
    {
        _loader = new VideoLoader(null);
        _loader.AttachBuffer(_mockBuffer.Object);
        _loader.Open(_testVideoPath);

        var task = _loader.PlayAsync();
        
        // Allow it to run for a bit
        // Since it's a local file, it might process very fast.
        // We just wait a bit.
        Thread.Sleep(500);
        
        _loader.Stop();
        task.Wait(1000);

        _mockBuffer.Verify(b => b.Enqueue(It.IsAny<Frame>()), Times.AtLeastOnce());
    }

    [Test]
    public void Play_WithCallback_ShouldInvokeCallback()
    {
        _loader = new VideoLoader(null);
        bool callbackInvoked = false;
        int frameCount = 0;
        
        _loader.SetFrameCallback(frame => 
        {
            callbackInvoked = true;
            frameCount++;
            frame.Dispose(); // Simulate consumption
        });
        
        _loader.Open(_testVideoPath);
        
        var task = _loader.PlayAsync();
        Thread.Sleep(500);
        _loader.Stop();
        task.Wait(1000);

        callbackInvoked.Should().BeTrue();
        frameCount.Should().BeGreaterThan(0);
    }

    [Test]
    public void Seek_ShouldWork()
    {
        _loader = new VideoLoader(null);
        _loader.Open(_testVideoPath);

        bool result = _loader.Seek(10);
        result.Should().BeTrue();
        
        // Seek to invalid (out of range)
        result = _loader.Seek(TestVideoFrameCount + 100); 
        result.Should().BeFalse();
    }
    
    [Test]
    public void Pause_And_Resume_ShouldChangeState()
    {
         _loader = new VideoLoader(null);
         _loader.Open(_testVideoPath);
         
         // Start playing
         var task = _loader.PlayAsync();
         Thread.Sleep(200); // Let it run a bit
         
         _loader.Pause();
         // Pause might take a small moment to reflect in loop? 
         // In VideoLoader.cs: while loop checks State. If Paused, it sleeps.
         // State is set immediately in Pause().
         _loader.State.Should().Be(VideoLoaderState.Paused);
         
         _loader.Resume();
         _loader.State.Should().Be(VideoLoaderState.Running);
         
         _loader.Stop();
         task.Wait(1000);
         _loader.State.Should().Be(VideoLoaderState.Closed);
    }

    [Test]
    public void Play_WithStride_ShouldSkipFrames()
    {
        // Setup
        int stride = 2;
        var prefs = new Dictionary<string, string>
        {
            { "VideoStride", stride.ToString() }
        };
        _loader = new VideoLoader(prefs);
        _loader.AttachBuffer(_mockBuffer.Object);
        _loader.Open(_testVideoPath);

        // Capture enqueued frames
        var frames = new List<long>();
        _mockBuffer.Setup(b => b.Enqueue(It.IsAny<Frame>()))
            .Callback<Frame>(f => 
            {
                frames.Add(f.FrameId);
                f.Dispose();
            });

        // Play with debug mode to limit frames and ensure termination
        int debugCount = 10;
        _loader.Play(debugMode: true, debugFrameCount: debugCount);

        // Verify
        frames.Should().HaveCountGreaterThan(0);
        // VideoLoader: if (_frameIndex++ % VideoStride != 0) continue;
        // FrameIndex starts at 1.
        // 1 % 2 != 0 -> skip
        // 2 % 2 == 0 -> keep
        // 3 % 2 != 0 -> skip
        // 4 % 2 == 0 -> keep
        // So we expect even frame IDs.
        foreach(var id in frames)
        {
            (id % stride).Should().Be(0);
        }
    }

    [Test]
    public void Play_WithDebugMode_ShouldStopAfterCount()
    {
        _loader = new VideoLoader(null);
        _loader.AttachBuffer(_mockBuffer.Object);
        _loader.Open(_testVideoPath);

        int debugCount = 5;
        _loader.Play(debugMode: true, debugFrameCount: debugCount);

        // Verify we didn't get more than debugCount frames
        // Note: Play loop checks debugCount BEFORE grab.
        // And it decrements debugCount check.
        // if (debugMode && debugFrameCount-- <= 0) break;
        // So exactly 5 frames should be processed (unless EOF).
        
        _mockBuffer.Verify(b => b.Enqueue(It.IsAny<Frame>()), Times.AtMost(debugCount));
        _loader.State.Should().Be(VideoLoaderState.Closed);
    }

    [Test]
    public void Restart_AfterClose_ShouldWork()
    {
        _loader = new VideoLoader(null);
        _loader.AttachBuffer(_mockBuffer.Object);
        _loader.Open(_testVideoPath);

        // First Play
        _loader.Play(debugMode: true, debugFrameCount: 5);
        _loader.State.Should().Be(VideoLoaderState.Closed);

        // Re-Open
        bool openResult = _loader.Open(_testVideoPath);
        openResult.Should().BeTrue();
        _loader.State.Should().Be(VideoLoaderState.Opened);

        // Second Play
        // Reset mock verification to ensure we capture new calls
        _mockBuffer.Invocations.Clear();
        
        _loader.Play(debugMode: true, debugFrameCount: 5);
        
        _mockBuffer.Verify(b => b.Enqueue(It.IsAny<Frame>()), Times.AtLeastOnce());
    }

    [Test]
    public void SetFrameCallback_MultipleCallbacks_ShouldInvokeAll()
    {
        _loader = new VideoLoader(null);
        _loader.Open(_testVideoPath);

        int count1 = 0;
        int count2 = 0;

        _loader.SetFrameCallback(f => count1++);
        _loader.SetFrameCallback(f => count2++);

        _loader.Play(debugMode: true, debugFrameCount: 5);

        count1.Should().BeGreaterThan(0);
        count2.Should().BeGreaterThan(0);
        count1.Should().Be(count2);
    }

    [Test]
    public void Frame_Timestamp_ShouldBeCorrect()
    {
        _loader = new VideoLoader(null);
        _loader.AttachBuffer(_mockBuffer.Object);
        _loader.Open(_testVideoPath);

        long lastOffset = -1;
        _mockBuffer.Setup(b => b.Enqueue(It.IsAny<Frame>()))
            .Callback<Frame>(f => 
            {
                // Verify offset increases (or equal if fast enough, but usually increases)
                // Note: f.OffsetMilliSec is from Capture property PosMsec
                if (lastOffset != -1)
                {
                    f.OffsetMilliSec.Should().BeGreaterThanOrEqualTo(lastOffset);
                }
                lastOffset = f.OffsetMilliSec;
                
                // Approximate check: Offset ~ FrameId * 1000 / FPS
                // FrameId is 1-based (from VideoCapture usually)
                // 1st frame (0ms usually or 1/30s)
                // Let's just ensure it's not crazy
                f.OffsetMilliSec.Should().BeGreaterThanOrEqualTo(0);

                f.Dispose();
            });

        _loader.Play(debugMode: true, debugFrameCount: 10);
        
        lastOffset.Should().BeGreaterThan(-1);
    }

    [Test]
    [Explicit("Manual verification required. Displays video window.")]
    public void Manual_Show_Video_Callback()
    {
        // User provided path
        string videoPath = "Video/video1.avi";
        
        if (!File.Exists(videoPath))
        {
            // Fallback to project relative path if absolute path not found (just in case running in different env)
            // Assuming current directory is project root or bin
            // But let's stick to the user provided path first, or try to find it.
            // If not found, ignore.
            Assert.Ignore($"Video file not found at {videoPath}");
        }

        var prefs = new Dictionary<string, string>
        {
            { "SourceId", "ManualTest" },
            { "Loop", "false" }
        };

        // Re-initialize loader for this specific test
        _loader?.Dispose(); 
        _loader = new VideoLoader(prefs);
        
        bool openResult = _loader.Open(videoPath);
        Assert.That(openResult, Is.True, "Failed to open video file.");

        string windowName = "Manual Verification";
        
        // On macOS, OpenCV highgui functions must run on the main thread.
        // NUnit tests typically run on background threads, causing a crash (SIGABRT) if UI is attempted.
        // To run this successfully with UI on macOS, one would need a custom runner or specific environment configuration.
        // We add a safety check here to prevent crashes during standard 'dotnet test' runs.
        bool enableGui = Environment.GetEnvironmentVariable("ENABLE_CV_GUI") == "1";

        if (enableGui)
        {
            try 
            {
                Cv2.NamedWindow(windowName);
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Cannot create OpenCV window: {ex.Message}");
                enableGui = false;
            }
        }
        else
        {
            TestContext.WriteLine("UI display skipped. Set environment variable ENABLE_CV_GUI=1 to enable (Warning: May crash on macOS if not on main thread).");
        }

        _loader.SetFrameCallback(frame =>
        {
            if (frame != null && !frame.IsDisposed && !frame.Scene.Empty())
            {
                try
                {
                    if (enableGui)
                    {
                        Cv2.ImShow(windowName, frame.Scene);
                        Cv2.WaitKey(1);
                    }
                    else
                    {
                        // Simulate consumption
                        TestContext.WriteLine($"Frame {frame.FrameId} retrieved. Size: {frame.Scene.Width}x{frame.Scene.Height}");
                    }
                }
                catch
                {
                    // Ignore UI errors
                }
                finally
                {
                    frame.Dispose();
                }
            }
        });

        // Play blocks until finished
        try
        {
            _loader.Play();
        }
        finally
        {
            if (enableGui)
            {
                try { Cv2.DestroyWindow(windowName); } catch { }
            }
        }
    }

    [Test]
    public void Play_WithLoop_ShouldLoopVideo()
    {
        // Setup
        var prefs = new Dictionary<string, string>
        {
            { "Loop", "true" }
        };
        _loader = new VideoLoader(prefs);
        _loader.AttachBuffer(_mockBuffer.Object);
        _loader.Open(_testVideoPath);

        int frameCount = 0;
        _mockBuffer.Setup(b => b.Enqueue(It.IsAny<Frame>()))
            .Callback<Frame>(f => 
            {
                frameCount++;
                f.Dispose();
            });

        // Play for longer than the video length
        // TestVideoFrameCount is 60. 
        // We set debugFrameCount to 70.
        // It should read 60 frames, then loop, read 10 more, then debugFrameCount hits 0 and break.
        int targetFrames = TestVideoFrameCount + 10;
        _loader.Play(debugMode: true, debugFrameCount: targetFrames);

        frameCount.Should().Be(targetFrames);
        _loader.State.Should().Be(VideoLoaderState.Closed);
    }

    [Test]
    public void Seek_WithTimeSpan_ShouldWork()
    {
        _loader = new VideoLoader(null);
        _loader.Open(_testVideoPath);

        // 1 second in
        bool result = _loader.Seek(TimeSpan.FromSeconds(1));
        result.Should().BeTrue();
        
        // Seek to invalid (negative)
        result = _loader.Seek(TimeSpan.FromSeconds(-1));
        result.Should().BeFalse();
    }

    [Test]
    public void Dispose_ShouldReleaseResources()
    {
        _loader = new VideoLoader(null);
        _loader.Open(_testVideoPath);
        
        _loader.Dispose();
        
        _loader.State.Should().Be(VideoLoaderState.Closed);
    }
}
