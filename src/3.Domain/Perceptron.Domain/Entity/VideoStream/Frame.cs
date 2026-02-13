using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.Common;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Extensions;

namespace Perceptron.Domain.Entity.VideoStream;

public class Frame : PropertiesBag, IDisposable
{
    public string SourceId { get; }
    public long FrameId { get; }
    public long OffsetMilliSec { get; }
    public DateTime UtcTimeStamp { get; }

    public Mat Scene { get; }
    public IReadOnlyList<DetectedObject> DetectedObjects { get; set; }

    public VisualAnnotation Annotation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this frame is a generated blank/default frame
    /// returned when the buffer is empty in BlankFrameMode.
    /// </summary>
    public bool IsBlankFrame { get; set; }

    public bool IsDisposed { get; private set; }

    private int _refCount = 0;
    private readonly Action<Mat>? _recycler;

    public Frame(string sourceId, long frameId, long offsetMilliSec, Mat scene, Action<Mat>? recycler = null)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("SourceId cannot be null or whitespace.", nameof(sourceId));
        if (frameId < 0) throw new ArgumentOutOfRangeException(nameof(frameId), "FrameId must be >= 0.");
        //if (offsetMilliSec < 0) throw new ArgumentOutOfRangeException(nameof(offsetMilliSec), "Offset must be >= 0.");
        if (offsetMilliSec < 0) offsetMilliSec = 0;
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (scene.Empty()) throw new ArgumentException("Scene cannot be empty.", nameof(scene));

        SourceId = sourceId;
        FrameId = frameId;
        OffsetMilliSec = offsetMilliSec;
        UtcTimeStamp = DateTime.UtcNow;
        Scene = scene;
        DetectedObjects = new List<DetectedObject>();
        Annotation = new VisualAnnotation(SourceId, UtcTimeStamp, FrameId, Scene.Width, Scene.Height);
        _recycler = recycler;
    }

    /// <summary>
    /// Increments the reference count. 
    /// Call this when you want to keep the frame alive (e.g., storing it in a buffer).
    /// </summary>
    public void Retain()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _refCount);
    }

    public void DrawDetection()
    {
        Scene.DrawDetections(DetectedObjects);
    }

    public Frame Clone()
    {
        ThrowIfDisposed();
        return new Frame(SourceId, FrameId, OffsetMilliSec, Scene.Clone());
    }

    public void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Frame));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_recycler != null)
            {
                // Return the Mat to the pool instead of disposing it
                _recycler(Scene);
            }
            else
            {
                Scene?.Dispose();
            }

            foreach (var detectedObject in DetectedObjects)
            {
                detectedObject?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        // Decrement ref count
        if (Interlocked.Decrement(ref _refCount) > 0)
        {
            return;
        }

        Dispose(true);
        GC.SuppressFinalize(this);
        IsDisposed = true;
    }
}