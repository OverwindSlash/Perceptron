using ComponentCommon;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.FrameBuffer;
using Perceptron.Domain.DataStructure;
using Perceptron.Domain.Entity.VideoStream;
using System.Collections;
using Perceptron.Domain.Setting;

namespace FrameBuffer.TwoModes;

public class VideoFrameBuffer : ComponentBase, IVideoFrameBuffer
{
    private readonly ConcurrentBoundedQueue<Frame> _queue;
    private readonly object _lock = new(); // Coordination lock
    private Frame? _blankFrame;
    private bool _isDisposed;

    // Additional drop handlers managed explicitly to support the interface methods
    private Action<Frame>? _externalDropHandler;

    public int BufferSize { get; }
    public FrameBufferMode Mode { get; }

    public VideoFrameBuffer(Dictionary<string, string>? preferences) 
        : base(preferences)
    {
        BufferSize = FrameBufferSettings.ParseBufferSize(preferences);
        Mode = FrameBufferSettings.ParseFrameBufferMode(preferences);

        // Initialize the inner queue
        // We pass our internal handler to manage disposal and event firing
        _queue = new ConcurrentBoundedQueue<Frame>(BufferSize, OnInternalFrameDropped);
    }

    private void OnInternalFrameDropped(Frame frame)
    {
        // 1. Invoke interface event
        OnItemDropped?.Invoke(frame);
        
        // 2. Invoke registered handlers
        _externalDropHandler?.Invoke(frame);

        // 3. Dispose the frame (since we Retained it when Enqueueing)
        // We assume that if it's dropped from the queue, the queue no longer holds it.
        // We must release the reference we added in Enqueue.
        frame.Dispose();
    }

    #region IVideoFrameBuffer Implementation

    public void PushFrame(Frame frame)
    {
        Enqueue(frame);
    }

    public Frame RetrieveFrame()
    {
        lock (_lock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(VideoFrameBuffer));

            while (_queue.Count == 0)
            {
                if (Mode == FrameBufferMode.ReturnBlankFrame && _blankFrame != null)
                {
                    // Return blank frame
                    // We must retain it because the caller will likely Dispose it (or use it)
                    // The _blankFrame reference held by this class counts as 1.
                    // The one returned to caller counts as +1.
                    _blankFrame.Retain(); 
                    return _blankFrame;
                }

                // BlockingWait mode OR ReturnBlankFrame but no blank frame available yet
                Monitor.Wait(_lock);
                
                if (_isDisposed) throw new ObjectDisposedException(nameof(VideoFrameBuffer));
            }

            if (_queue.TryDequeue(out var frame))
            {
                // Dequeued successfully.
                // We do NOT Dispose here. Ownership (and the refCount incremented in Enqueue) 
                // is transferred to the caller.
                return frame!;
            }

            // Should be unreachable if logic is correct
            throw new InvalidOperationException("Queue is empty after wait.");
        }
    }

    public void RegisterFrameDropHandler(Action<Frame>? frameDropHandler)
    {
        if (frameDropHandler != null)
        {
            _externalDropHandler += frameDropHandler;
        }
    }

    public void UnregisterFrameDropHandler(Action<Frame>? frameDropHandler)
    {
        if (frameDropHandler != null)
        {
            _externalDropHandler -= frameDropHandler;
        }
    }

    #endregion

    #region IConcurrentBoundedQueue Implementation

    public int Count => _queue.Count;
    public int Capacity => _queue.Capacity;
    public bool IsFull => _queue.IsFull;
    public double Utilization => _queue.Utilization;

    public long TotalEnqueued => _queue.TotalEnqueued;
    public long TotalDequeued => _queue.TotalDequeued;
    public long TotalOverwritten => _queue.TotalOverwritten;
    public long TotalDropped => _queue.TotalDropped;
    public int MaxUtilization => _queue.MaxUtilization;

    public double EnqueueThroughput => _queue.EnqueueThroughput;
    public double DequeueThroughput => _queue.DequeueThroughput;

    public event Action<Frame>? OnItemDropped;

    public void Enqueue(Frame item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        lock (_lock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(VideoFrameBuffer));

            // Lazy initialization of blank frame from the first incoming frame
            if (_blankFrame == null)
            {
                CreateBlankFrame(item);
            }

            // Retain the frame because the queue will hold a reference to it.
            // If the queue drops it later, OnInternalFrameDropped will Dispose (release) it.
            // If it is Dequeued, the caller takes ownership of this reference.
            item.Retain();

            _queue.Enqueue(item);
            
            // Wake up any waiting consumers
            Monitor.PulseAll(_lock);
        }
    }

    public void EnqueueRange(IEnumerable<Frame> items)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            Enqueue(item);
        }
    }

    public bool TryDequeue(out Frame? item)
    {
        // This is the non-blocking version from the interface.
        // It delegates to the inner queue.
        // Note: If we use this method, we bypass the Blocking/BlankFrame logic.
        // However, we still need to synchronize with PushFrame/RetrieveFrame if we want strict consistency,
        // but _queue is already thread-safe.
        // The only issue is that _queue.TryDequeue doesn't acquire _lock, so it might race with CreateBlankFrame? 
        // No, CreateBlankFrame is in Enqueue.
        // It might race with RetrieveFrame's Wait? No, _queue handles its own locking.
        
        // However, if we dequeue here, we need to ensure we don't break the accounting.
        // _queue.TryDequeue is fine. It returns the frame. We transfer ownership.
        return _queue.TryDequeue(out item);
    }

    public bool TryPeek(out Frame? item)
    {
        return _queue.TryPeek(out item);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            Monitor.PulseAll(_lock);
        }
    }

    public IEnumerator<Frame> GetEnumerator()
    {
        return _queue.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _queue.GetEnumerator();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                // Dispose blank frame
                if (_blankFrame != null)
                {
                    _blankFrame.Dispose(); // Release our reference
                    _blankFrame = null;
                }
                
                // Clear and dispose queue items
                while (_queue.TryDequeue(out var frame))
                {
                    frame?.Dispose();
                }

                // _queue itself doesn't need disposal? 
                // ConcurrentBoundedQueue implements IDisposable? Yes.
                // But ConcurrentBoundedQueue doesn't seem to dispose items in its Dispose().
                // So we did the right thing above.
                if (_queue is IDisposable disposableQueue)
                {
                    disposableQueue.Dispose();
                }

                // Wake up waiters so they can throw ObjectDisposedException
                Monitor.PulseAll(_lock);
            }
        }
    }

    #endregion

    private void CreateBlankFrame(Frame template)
    {
        try 
        {
            // Create a black image with same size and type
            var scene = new Mat(template.Scene.Size(), template.Scene.Type(), Scalar.Black);
            
            _blankFrame = new Frame(
                sourceId: "BlankFrame",
                frameId: 0,
                offsetMilliSec: 0,
                scene: scene
            )
            {
                IsBlankFrame = true
            };
            
            // Frame constructor initializes refCount = 1.
            // We keep this reference.
        }
        catch (Exception ex)
        {
            // Logging? 
            // Just ignore for now or rethrow? 
            // If we fail to create blank frame, we just won't have one.
            throw new InvalidOperationException("Failed to create blank frame.", ex);
        }
    }
}
