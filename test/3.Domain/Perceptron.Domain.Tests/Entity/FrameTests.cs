using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Tests.Entity;

[TestFixture]
public class FrameTests
{
    private Mat _validScene;

    [SetUp]
    public void Setup()
    {
        // 创建一个简单的 100x100 3通道图像
        _validScene = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
    }

    [TearDown]
    public void TearDown()
    {
        if (!_validScene.IsDisposed)
        {
            _validScene.Dispose();
        }
    }

    [Test]
    public void Constructor_ValidArgs_ShouldCreateInstance()
    {
        // Arrange
        var sourceId = "test_source";
        long frameId = 1;
        long offset = 100;

        // Act
        // 注意：Frame 会接管 _validScene 的所有权吗？
        // Frame 实现了 IDisposable 并在 Dispose 时释放 Scene。
        // 为了避免 Setup/TearDown 中的 _validScene 被释放导致问题，这里我们 Clone 一份传进去，
        // 或者在 TearDown 中检查 IsDisposed。
        // Frame 的构造函数没有说明是否接管所有权，但 Dispose 方法会释放它。
        // 通常意味着 Frame 拥有该 Mat。
        // 为了测试安全，我们 Clone 一份。
        using var sceneClone = _validScene.Clone();
        using var frame = new Frame(sourceId, frameId, offset, sceneClone);

        // Assert
        Assert.That(frame.SourceId, Is.EqualTo(sourceId));
        Assert.That(frame.FrameId, Is.EqualTo(frameId));
        Assert.That(frame.OffsetMilliSec, Is.EqualTo(offset));
        Assert.That(frame.Scene, Is.EqualTo(sceneClone));
        Assert.That(frame.DetectedObjects, Is.Not.Null);
        Assert.That(frame.DetectedObjects, Is.Empty);
        Assert.That(frame.Annotation, Is.Not.Null);
        Assert.That(frame.UtcTimeStamp.Date, Is.EqualTo(DateTime.UtcNow.Date));
        Assert.That(frame.IsDisposed, Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_InvalidSourceId_ShouldThrowArgumentException(string? invalidSourceId)
    {
        Assert.Throws<ArgumentException>(() => new Frame(invalidSourceId!, 1, 0, _validScene));
    }

    [Test]
    public void Constructor_NegativeFrameId_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Frame("src", -1, 0, _validScene));
    }

    [Test]
    public void Constructor_NegativeOffset_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Frame("src", 1, -1, _validScene));
    }

    [Test]
    public void Constructor_NullScene_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Frame("src", 1, 0, null!));
    }

    [Test]
    public void Constructor_EmptyScene_ShouldThrowArgumentException()
    {
        using var emptyScene = new Mat();
        Assert.Throws<ArgumentException>(() => new Frame("src", 1, 0, emptyScene));
    }

    [Test]
    public void Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        using var sceneClone = _validScene.Clone();
        using var frame = new Frame("src", 1, 100, sceneClone);
        frame.SetProperty("Key", "Value");

        // Act
        using var clone = frame.Clone();

        // Assert
        Assert.That(clone, Is.Not.SameAs(frame));
        Assert.That(clone.SourceId, Is.EqualTo(frame.SourceId));
        Assert.That(clone.FrameId, Is.EqualTo(frame.FrameId));
        Assert.That(clone.Scene, Is.Not.SameAs(frame.Scene)); // Mat 应该是不同的实例
        Assert.That(clone.Scene.Size(), Is.EqualTo(frame.Scene.Size()));
        
        // PropertiesBag 的属性默认不被 Clone 复制
        Assert.That(clone.GetProperty<string>("Key"), Is.Null);
    }

    [Test]
    public void Dispose_ShouldReleaseResources()
    {
        // Arrange
        // 这里必须创建一个新的 Mat，因为 Frame Dispose 会释放它
        var scene = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var frame = new Frame("src", 1, 0, scene);
        
        // Act
        frame.Dispose();

        // Assert
        Assert.That(frame.IsDisposed, Is.True);
        Assert.That(scene.IsDisposed, Is.True); 
    }

    [Test]
    public void Dispose_ShouldDisposeDetectedObjects()
    {
        // Arrange
        using var sceneClone = _validScene.Clone();
        var frame = new Frame("src", 1, 0, sceneClone);
        
        var bbox = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var detectedObject = new DetectedObject("src", 1, DateTime.UtcNow, 1, "label", 0.9f, bbox);
        
        var objects = new List<DetectedObject> { detectedObject };
        frame.DetectedObjects = objects;

        // Act
        frame.Dispose();

        // Assert
        Assert.That(detectedObject.IsDisposed, Is.True);
    }

    [Test]
    public void ThrowIfDisposed_ShouldThrow_AfterDispose()
    {
        // Arrange
        using var sceneClone = _validScene.Clone();
        var frame = new Frame("src", 1, 0, sceneClone);
        frame.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => frame.ThrowIfDisposed());
        Assert.Throws<ObjectDisposedException>(() => frame.Clone());
    }

    [Test]
    public void DrawDetection_ShouldNotThrow()
    {
        // Arrange
        using var sceneClone = _validScene.Clone();
        using var frame = new Frame("src", 1, 0, sceneClone);
        
        var bbox = BoundingBox.CreateFromRect(10, 10, 50, 50);
        var detectedObject = new DetectedObject("src", 1, DateTime.UtcNow, 1, "person", 0.9f, bbox);
        frame.DetectedObjects = new List<DetectedObject> { detectedObject };

        // Act
        Assert.DoesNotThrow(() => frame.DrawDetection());
        
        // 简单验证 Scene 仍然有效
        Assert.That(frame.Scene.IsDisposed, Is.False);
    }

    [Test]
    public void PropertiesBag_ShouldWork()
    {
        // Arrange
        using var sceneClone = _validScene.Clone();
        using var frame = new Frame("src", 1, 0, sceneClone);

        // Act
        frame.SetProperty("TestKey", 123);
        var value = frame.GetProperty<int>("TestKey");

        // Assert
        Assert.That(value, Is.EqualTo(123));
    }

    [Test]
    public void Retain_ShouldPreventDisposal_UntilRefCountIsZero()
    {
        // Arrange
        var scene = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        var frame = new Frame("src", 1, 0, scene);

        // Act
        frame.Retain(); // RefCount = 2
        frame.Dispose(); // RefCount = 1

        // Assert
        Assert.That(frame.IsDisposed, Is.True, "Frame should be disposed when RefCount reaches 0");
        Assert.That(scene.IsDisposed, Is.True, "Scene should be disposed when Frame is disposed");
    }

    [Test]
    public void Retain_ShouldThrow_IfAlreadyDisposed()
    {
        // Arrange
        using var sceneClone = _validScene.Clone();
        var frame = new Frame("src", 1, 0, sceneClone);
        frame.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => frame.Retain());
    }

    [Test]
    public void Dispose_WithRecycler_ShouldRecycleScene()
    {
        // Arrange
        var scene = new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        bool recycled = false;
        Action<Mat> recycler = (m) => { recycled = true; m.Dispose(); };
        
        var frame = new Frame("src", 1, 0, scene, recycler);

        // Act
        frame.Dispose();

        // Assert
        Assert.That(frame.IsDisposed, Is.True);
        Assert.That(recycled, Is.True, "Recycler action should be called");
        Assert.That(scene.IsDisposed, Is.True, "Scene should be disposed by the recycler in this test");
    }
}
