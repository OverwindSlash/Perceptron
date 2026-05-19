using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using System.Collections.Concurrent;

namespace Perceptron.Domain.Tests.Entity;

[TestFixture]
public class DetectedObjectTests
{
    private BoundingBox _defaultBbox;
    private DateTime _utcNow;

    [SetUp]
    public void Setup()
    {
        _defaultBbox = BoundingBox.CreateFromRect(0, 0, 100, 100);
        _utcNow = DateTime.UtcNow;
    }

    [Test]
    public void Constructor_ValidArguments_CreatesObject()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        Assert.That(obj.SourceId, Is.EqualTo("cam1"));
        Assert.That(obj.FrameId, Is.EqualTo(100));
        Assert.That(obj.UtcTimeStamp, Is.EqualTo(_utcNow));
        Assert.That(obj.LabelId, Is.EqualTo(1));
        Assert.That(obj.Label, Is.EqualTo("person"));
        Assert.That(obj.Confidence, Is.EqualTo(0.9f));
        Assert.That(obj.Bbox, Is.EqualTo(_defaultBbox));
        Assert.That(obj.TrackingId, Is.EqualTo(0)); // Default
        Assert.That(obj.IsValid(), Is.True);
    }

    [Test]
    public void Constructor_InvalidArguments_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => new DetectedObject("", 100, _utcNow, 1, "person", 0.9f, _defaultBbox)); // SourceId empty
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectedObject("cam1", -1, _utcNow, 1, "person", 0.9f, _defaultBbox)); // FrameId < 0
        Assert.Throws<ArgumentException>(() => new DetectedObject("cam1", 100, DateTime.Now, 1, "person", 0.9f, _defaultBbox)); // Not UTC
        Assert.Throws<ArgumentException>(() => new DetectedObject("cam1", 100, _utcNow, 1, "", 0.9f, _defaultBbox)); // Label empty
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectedObject("cam1", 100, _utcNow, -1, "person", 0.9f, _defaultBbox)); // LabelId < 0
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectedObject("cam1", 100, _utcNow, 1, "person", 1.2f, _defaultBbox)); // Confidence > 1
        Assert.Throws<ArgumentException>(() => new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, BoundingBox.Empty)); // Empty Bbox
    }

    [Test]
    public void Snapshot_Attach_TakeOwnership_DisposesMatOnObjectDispose()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
            
        obj.AttachSnapshot(mat, takeOwnership: true);
            
        Assert.That(obj.HasSnapshot, Is.True);
        Assert.That(obj.Snapshot, Is.SameAs(mat));

        obj.Dispose();

        Assert.That(mat.IsDisposed, Is.True);
    }

    [Test]
    public void Snapshot_Attach_NoOwnership_DoesNotDisposeOriginalMat()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
            
        obj.AttachSnapshot(mat, takeOwnership: false);
            
        Assert.That(obj.HasSnapshot, Is.True);
        Assert.That(obj.Snapshot, Is.Not.SameAs(mat)); // Should be a clone
        Assert.That(obj.Snapshot.IsDisposed, Is.False);

        obj.Dispose();

        Assert.That(mat.IsDisposed, Is.False); // Original should be alive
    }

    [Test]
    public void Snapshot_Detach_ReleasesResource()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
            
        obj.AttachSnapshot(mat, takeOwnership: true);
        obj.DetachSnapshot();

        Assert.That(obj.HasSnapshot, Is.False);
        Assert.That(obj.Snapshot, Is.Null);
        Assert.That(mat.IsDisposed, Is.True);
    }

    [Test]
    public void Snapshot_Attach_WithExistingSnapshot_DisposesOldSnapshot()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        var mat1 = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var mat2 = new Mat(100, 100, MatType.CV_8UC3, Scalar.White);

        obj.AttachSnapshot(mat1, takeOwnership: true);
        obj.AttachSnapshot(mat2, takeOwnership: true);

        Assert.That(mat1.IsDisposed, Is.True, "Old snapshot should be disposed");
        Assert.That(obj.Snapshot, Is.SameAs(mat2), "New snapshot should be attached");
        Assert.That(mat2.IsDisposed, Is.False, "New snapshot should be alive");
    }

    [Test]
    public void DetectionKey_Logic_GeneratesExpectedKey()
    {
        // DetectionKey format: $"{SourceId}|{FrameId}|{LabelId}|{Bbox.X},{Bbox.Y},{Bbox.Width},{Bbox.Height}"
        var bbox = BoundingBox.CreateFromRect(10, 20, 30, 40);
        var obj = new DetectedObject("cam1", 123, _utcNow, 5, "person", 0.9f, bbox);

        var expectedKey = "cam1|123|5|10,20,30,40";
        Assert.That(obj.DetectionKey, Is.EqualTo(expectedKey));
    }

    [Test]
    public void PropertyBag_Remove_And_GetAll_BehaveCorrectly()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        obj.SetProperty("k1", 1);
        obj.SetProperty("k2", "v2");

        var all = obj.GetAllProperties();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all["k1"], Is.EqualTo(1));

        obj.RemoveProperty("k1");
        Assert.That(obj.GetProperty<int>("k1", -1), Is.EqualTo(-1));
        Assert.That(obj.GetAllProperties().Count, Is.EqualTo(1));
    }

    [Test]
    public void PropertyBag_Concurrent_Access_IsSafe()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        int iterations = 1000;
            
        Parallel.For(0, iterations, i =>
        {
            obj.SetProperty($"key_{i}", i);
            var val = obj.GetProperty<int>($"key_{i}");
            Assert.That(val, Is.EqualTo(i));
        });

        Assert.That(obj.GetAllProperties().Count, Is.EqualTo(iterations));
    }

    [Test]
    public void Snapshot_Clone_ReturnsNewInstance()
    {
        using var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        using var mat = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        obj.AttachSnapshot(mat, takeOwnership: false);

        using var cloned = obj.CloneSnapshot();
        Assert.That(cloned, Is.Not.Null);
        Assert.That(cloned, Is.Not.SameAs(obj.Snapshot));
        Assert.That(cloned.IsDisposed, Is.False);
    }

    [Test]
    public void PropertyBag_SetAndGet_WorksCorrectly()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        obj.SetProperty("age", 25);
        obj.SetProperty("meta", "data");

        Assert.That(obj.GetProperty<int>("age"), Is.EqualTo(25));
        Assert.That(obj.GetProperty<string>("meta"), Is.EqualTo("data"));
        Assert.That(obj.GetProperty<int>("missing", -1), Is.EqualTo(-1));
            
        // Type mismatch
        Assert.That(obj.GetProperty<string>("age"), Is.Null); // default(string) is null
    }

    [Test]
    public void Geometry_IoU_Calculation()
    {
        var obj1 = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, BoundingBox.CreateFromRect(0, 0, 100, 100));
        var obj2 = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, BoundingBox.CreateFromRect(50, 0, 100, 100));
        // Intersection: 50x100 = 5000
        // Union: 100x100 + 100x100 - 5000 = 15000
        // IoU: 5000 / 15000 = 1/3 ~= 0.333

        Assert.That(obj1.CalculateIoU(obj2), Is.EqualTo(1f/3f).Within(0.001));
        Assert.That(obj1.OverlapsWith(obj2, 0.3f), Is.True);
        Assert.That(obj1.OverlapsWith(obj2, 0.4f), Is.False);
    }

    [Test]
    public void Concurrency_Snapshot_ThreadSafety()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
        int iterations = 100;
        var errors = new ConcurrentBag<Exception>();

        Parallel.For(0, 10, i =>
        {
            try
            {
                for (int j = 0; j < iterations; j++)
                {
                    using var mat = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(0));
                    // Randomly attach, detach, check
                    if (j % 3 == 0)
                    {
                        // Attach needs a clone because we are disposing mat in loop
                        obj.AttachSnapshot(mat.Clone(), takeOwnership: true);
                    }
                    else if (j % 3 == 1)
                    {
                        obj.DetachSnapshot();
                    }
                    else
                    {
                        var has = obj.HasSnapshot;
                        using var snap = obj.CloneSnapshot();
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        Assert.That(errors, Is.Empty);
        obj.Dispose();
    }
        
    [Test]
    public void ToString_Format()
    {
        var time = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var obj = new DetectedObject("src1", 10, time, 5, "car", 0.8765f, _defaultBbox, 99);
        var str = obj.ToString();
            
        Assert.That(str, Contains.Substring("src1_car_99")); // Id
        Assert.That(str, Contains.Substring("src=src1"));
        Assert.That(str, Contains.Substring("frame=10"));
        Assert.That(str, Contains.Substring("t=2023-01-01T12:00:00.0000000Z")); // O format
        Assert.That(str, Contains.Substring("label=car(5)"));
        Assert.That(str, Contains.Substring("conf=0.877")); // F3
        Assert.That(str, Contains.Substring("track=99"));
    }

    [Test]
    public void PropertySetters_ValidateInput()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);

        // LabelId
        Assert.Throws<ArgumentOutOfRangeException>(() => obj.LabelId = -1);
        obj.LabelId = 0; // Should work
            
        // Label
        Assert.Throws<ArgumentNullException>(() => obj.Label = null!);
        Assert.Throws<ArgumentException>(() => obj.Label = "");
        Assert.Throws<ArgumentException>(() => obj.Label = "   ");
        obj.Label = "valid"; // Should work

        // TrackingId
        Assert.Throws<ArgumentOutOfRangeException>(() => obj.TrackingId = -1);
        obj.TrackingId = 0; // Should work
    }

    [Test]
    public void Freeze_MakesObjectReadOnly()
    {
        var obj = new DetectedObject("cam1", 100, _utcNow, 1, "person", 0.9f, _defaultBbox);
            
        obj.Freeze();
        Assert.That(obj.IsFrozen, Is.True);

        // Properties
        Assert.Throws<InvalidOperationException>(() => obj.LabelId = 2);
        Assert.Throws<InvalidOperationException>(() => obj.Label = "new");
        Assert.Throws<InvalidOperationException>(() => obj.TrackingId = 2);

        // Snapshot
        using var mat = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(0));
        Assert.Throws<InvalidOperationException>(() => obj.AttachSnapshot(mat));
        Assert.Throws<InvalidOperationException>(() => obj.DetachSnapshot());

        // PropertyBag
        Assert.Throws<InvalidOperationException>(() => obj.SetProperty("k", "v"));
    }
}