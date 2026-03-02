using MessagePipe;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Event.SnapshotManager;
using Serilog;

namespace SnapshotManager.Tests
{
    [TestFixture]
    public class SnapshotManagerTests
    {
        private SnapshotManager.InMemory.SnapshotManager _snapshotManager;
        private Mock<IPublisher<ObjectBestSnapshotCreatedEvent>> _mockPublisher;
        private string _tempDir;

        [SetUp]
        public void Setup()
        {
            // Configure dummy logger
            Log.Logger = new LoggerConfiguration().CreateLogger();

            _tempDir = Path.Combine(Path.GetTempPath(), "SnapshotManagerTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);

            _mockPublisher = new Mock<IPublisher<ObjectBestSnapshotCreatedEvent>>();
            
            // Configuration
            var config = new Dictionary<string, string>
            {
                { "SnapshotsDir", _tempDir },
                { "MaxSnapshots", "5" },
                { "SaveBestSnapshot", "true" },
                { "MinSnapshotWidth", "1" },
                { "MinSnapshotHeight", "1" },
                { "VideoClipDurationSeconds", "1" }, // 1 second duration
                { "VideoFrameRate", "10" }           // 10 FPS
            };

            _snapshotManager = new SnapshotManager.InMemory.SnapshotManager(config);
            _snapshotManager.SetPublisher(_mockPublisher.Object);
        }

        [TearDown]
        public void TearDown()
        {
            (_snapshotManager as IDisposable)?.Dispose();

            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            Log.CloseAndFlush();
        }

        [Test]
        public void ProcessSnapshots_ShouldCacheSceneAndSnapshots()
        {
            // Arrange
            var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Blue);
            var frame = new Frame("src1", 1, 0, mat);
            // Retain frame to simulate upstream buffer/ownership, ensuring it survives ProcessSnapshots logic
            frame.Retain(); 
            
            var bbox = BoundingBox.CreateFromRect(10, 10, 20, 20);
            var detectedObject = new DetectedObject("src1", 1, DateTime.UtcNow, 1, "person", 0.9f, bbox, 101);
            detectedObject.IsUnderAnalysis = true; 
            
            frame.DetectedObjects = new List<DetectedObject> { detectedObject };

            // Act
            _snapshotManager.ProcessSnapshots(frame);

            // Assert
            Assert.That(_snapshotManager.GetCachedSceneCount(), Is.EqualTo(1));
            var snapshots = _snapshotManager.GetObjectSnapshotsByObjectId(detectedObject.Id);
            Assert.That(snapshots.Count, Is.EqualTo(1));
        }

        [Test]
        public void ProcessSnapshots_ShouldNotCacheSnapshots_WhenObjectNotUnderAnalysis()
        {
            // Arrange
            var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Blue);
            var frame = new Frame("src1", 2, 0, mat);
            frame.Retain();

            var bbox = BoundingBox.CreateFromRect(10, 10, 20, 20);
            var detectedObject = new DetectedObject("src1", 2, DateTime.UtcNow, 1, "person", 0.9f, bbox, 102);
            detectedObject.IsUnderAnalysis = false; // Not under analysis
            
            frame.DetectedObjects = new List<DetectedObject> { detectedObject };

            // Act
            _snapshotManager.ProcessSnapshots(frame);

            // Assert
            Assert.That(_snapshotManager.GetCachedSceneCount(), Is.EqualTo(1)); // Scene is still cached
            var snapshots = _snapshotManager.GetObjectSnapshotsByObjectId(detectedObject.Id);
            Assert.That(snapshots.Count, Is.EqualTo(0));
        }

        [Test]
        public void AddSnapshot_ShouldPublishBestSnapshotEvent_WhenNewSnapshotIsBest()
        {
             // Arrange
            var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Blue);
            var frame = new Frame("src1", 3, 0, mat);
            frame.Retain();

            var bbox = BoundingBox.CreateFromRect(10, 10, 20, 20);
            // High confidence
            var detectedObject = new DetectedObject("src1", 3, DateTime.UtcNow, 1, "person", 0.99f, bbox, 103);
            detectedObject.IsUnderAnalysis = true;
            frame.DetectedObjects = new List<DetectedObject> { detectedObject };

            // Act
            _snapshotManager.ProcessSnapshots(frame);

            // Assert
            _mockPublisher.Verify(p => p.Publish(It.Is<ObjectBestSnapshotCreatedEvent>(e => e.DetectedObject.Id == detectedObject.Id)), Times.Once);
        }

        [Test]
        public void ProcessEvent_ObjectExpired_ShouldReleaseSnapshotsAndSaveBest()
        {
            // Arrange
            var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Blue);
            var frame = new Frame("src1", 4, 0, mat);
            frame.Retain();

            // Use larger bbox to ensure snapshot is larger than MinSnapshotWidth (40x40)
            var bbox = BoundingBox.CreateFromRect(10, 10, 50, 50);
            var detectedObject = new DetectedObject("src1", 4, DateTime.UtcNow, 1, "person", 0.9f, bbox, 104);
            detectedObject.IsUnderAnalysis = true;
            frame.DetectedObjects = new List<DetectedObject> { detectedObject };

            _snapshotManager.ProcessSnapshots(frame);
            
            // Verify snapshots are cached BEFORE expiration
            var initialSnapshots = _snapshotManager.GetObjectSnapshotsByObjectId(detectedObject.Id);
            Assert.That(initialSnapshots.Count, Is.GreaterThan(0), "Snapshots should be cached before expiration");

            // Act
            var expireEvent = new ObjectExpiredEvent(frame.SourceId, detectedObject.Id, detectedObject.LocalId, detectedObject.LabelId, detectedObject.Label, detectedObject.TrackingId);
            _snapshotManager.ProcessEvent(expireEvent);

            // Assert
            // Snapshots in memory should be cleared
            var snapshots = _snapshotManager.GetObjectSnapshotsByObjectId(detectedObject.Id);
            Assert.That(snapshots.Count, Is.EqualTo(0));
            
            // Best snapshot should be saved to disk
            // File pattern logic: BaseDirectory/Best/filename_timestamp.jpg
            // filename = id.Replace(':', '_') => src1_1_104 (id is source_labelId_trackingId)
            var bestDir = Path.Combine(_tempDir, "Best");
            if (Directory.Exists(bestDir))
            {
                var files = Directory.GetFiles(bestDir, "*.jpg", SearchOption.AllDirectories);
                Assert.That(files.Length, Is.GreaterThan(0));
            }
            else
            {
                Assert.Fail($"Best directory not found at {bestDir}");
            }
            
            frame.Dispose();
        }

        [Test]
        public void ProcessEvent_FrameExpired_ShouldReleaseScene()
        {
            // Arrange
            var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Blue);
            var frame = new Frame("src1", 5, 0, mat);
            frame.Retain();
            frame.DetectedObjects = new List<DetectedObject>();

            _snapshotManager.ProcessSnapshots(frame);
            Assert.That(_snapshotManager.GetCachedSceneCount(), Is.EqualTo(1));

            // Act
            var expireEvent = new FrameExpiredEvent(5);
            _snapshotManager.ProcessEvent(expireEvent);

            // Assert
            Assert.That(_snapshotManager.GetCachedSceneCount(), Is.EqualTo(0));
            
            frame.Dispose();
        }

        [Test]
        public async Task GenerateVideoClipAroundFrameAsync_ShouldGenerateVideo()
        {
            // Arrange
            // Config: Duration 1s, FPS 10. Total 10 frames.
            // We need frames around center. Center is 5.
            // Need frames 0 to 9 ideally.
            // Frame interval = 1000ms / 10 = 100ms.
            
            var frames = new List<Frame>();
            for (int i = 0; i < 15; i++)
            {
                var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Blue);
                // Frame(string sourceId, long frameId, long offsetMilliSec, Mat scene)
                var frame = new Frame("src1", i, i * 100, mat);
                frame.Retain();
                frames.Add(frame);
                
                frame.DetectedObjects = new List<DetectedObject>();
                _snapshotManager.ProcessSnapshots(frame);
            }

            var videoPath = Path.Combine(_tempDir, "test_video.mp4");

            try 
            {
                // Act
                // Generate video around frame 7 (center), duration 1s (0.5s before, 0.5s after -> 5 frames before, 5 after)
                // Center 7. Before: 2,3,4,5,6. After: 8,9,10,11,12. Total 11 frames approx.
                // Range 2-12.
                await _snapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, 7, 1, 10);

                // Assert
                Assert.That(File.Exists(videoPath), Is.True);
                var fileInfo = new FileInfo(videoPath);
                Assert.That(fileInfo.Length, Is.GreaterThan(0));
            }
            finally
            {
                foreach(var f in frames) f.Dispose();
            }
        }
    }
}
