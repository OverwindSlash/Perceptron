using MessagePipe;
using Perceptron.Domain.Abstraction.EventHandler;
using Perceptron.Domain.DataStructure;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using System.Collections.Concurrent;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Setting;

namespace Perceptron.Service.Pipeline;

public class VideoFrameSlideWindow : IEventPublisher<ObjectExpiredEvent>, IEventPublisher<FrameExpiredEvent>, IDisposable
{
    private readonly ConcurrentBoundedQueue<Frame> _frames;

    private readonly Dictionary<string, HashSet<Frame>> _objectInWhichFrames;
    private readonly ConcurrentDictionary<string, bool> _objectAliveInSlideWindow;   // actually a set.

    private IPublisher<ObjectExpiredEvent> _objExpiredEventPublisher;
    private IPublisher<FrameExpiredEvent> _frmExpiredEventPublisher;

    public ConcurrentBoundedQueue<Frame> Frames => _frames;

    public VideoFrameSlideWindow(int windowSize = PipelineSettings.DefaultFrameLifetime)
    {
        _frames = new ConcurrentBoundedQueue<Frame>(windowSize, CleanupExpiredFrame);

        _objectInWhichFrames = new Dictionary<string, HashSet<Frame>>();
        _objectAliveInSlideWindow = new ConcurrentDictionary<string, bool>();
    }

    #region CleanUp Functions
    private void CleanupExpiredFrame(Frame expiredFrame)
    {
        if (expiredFrame == null)
        {
            return;
        }

        RemoveFrameObjIdsFromInWhichFramesDict(expiredFrame);

        // Notify expired objects.
        foreach (var detectedObject in expiredFrame.DetectedObjects)
        {
            int existenceCount = GetExistenceCountByObjId(detectedObject.Id);

            // If the existenceCount = 0, it means that the object with the specified id has not
            // reappeared in this slide window lifecycle, and the id can be deleted directly.
            if (existenceCount == 0)
            {
                RemoveObjIdFromAliveInSlideWindow(detectedObject.Id);

                _objectInWhichFrames.Remove(detectedObject.Id);
                _objExpiredEventPublisher.Publish(new ObjectExpiredEvent(detectedObject));
            }
        }

        // Notify expired frame.
        _frmExpiredEventPublisher.Publish(new FrameExpiredEvent(expiredFrame));

        expiredFrame.Dispose();
    }

    private void RemoveFrameObjIdsFromInWhichFramesDict(Frame expiredFrame)
    {
        if (expiredFrame.DetectedObjects == null)
        {
            return;
        }

        foreach (DetectedObject detectedObject in expiredFrame.DetectedObjects)
        {
            if (!_objectInWhichFrames.ContainsKey(detectedObject.Id))
            {
                continue;
            }

            HashSet<Frame> framesContainObjId = _objectInWhichFrames[detectedObject.Id];
            if (framesContainObjId != null)
            {
                framesContainObjId.Remove(expiredFrame);
            }
        }
    }

    private int GetExistenceCountByObjId(string objId)
    {
        if (!_objectInWhichFrames.ContainsKey(objId))
        {
            return 0;
        }

        HashSet<Frame> framesContainObjId = _objectInWhichFrames[objId];
        if (framesContainObjId == null)
        {
            _objectInWhichFrames.Remove(objId);
            return 0;
        }

        return framesContainObjId.Count;
    }

    private void RemoveObjIdFromAliveInSlideWindow(string objectId)
    {
        if (_objectAliveInSlideWindow.ContainsKey(objectId))
        {
            _objectAliveInSlideWindow.TryRemove(objectId, out var value);
        }
    }
    #endregion

    #region AddNewFrame Functions
    public void AddNewFrame(Frame newFrame)
    {
        if (newFrame == null)
        {
            return;
        }

        _frames.Enqueue(newFrame);
        AddNewFrameToObjectInWhichFrames(newFrame);
    }

    private void AddNewFrameToObjectInWhichFrames(Frame newFrame)
    {
        if (newFrame.DetectedObjects == null)
        {
            return;
        }

        foreach (DetectedObject detectedObject in newFrame.DetectedObjects)
        {
            if (!_objectInWhichFrames.ContainsKey(detectedObject.Id))
            {
                // same object may be shown in multiple frame slot
                _objectInWhichFrames.Add(detectedObject.Id, new HashSet<Frame>());
            }

            HashSet<Frame> framesContainObjId = _objectInWhichFrames[detectedObject.Id];
            framesContainObjId.Add(newFrame);

            AddNewAliveObjId(detectedObject.Id);
        }
    }

    private void AddNewAliveObjId(string objectId)
    {
        if (_objectAliveInSlideWindow.ContainsKey(objectId))
        {
            return;
        }

        _objectAliveInSlideWindow.TryAdd(objectId, true);
    }
    #endregion


    public List<Frame> GetFramesContainObjectId(string objId)
    {
        if (!_objectInWhichFrames.ContainsKey(objId))
        {
            return new List<Frame>();
        }

        return _objectInWhichFrames[objId].ToList();
    }

    public bool IsObjIdAlive(string objectId)
    {
        return _objectAliveInSlideWindow.ContainsKey(objectId);
    }

    #region Event Publishers
    public void SetPublisher(IPublisher<ObjectExpiredEvent> publisher)
    {
        _objExpiredEventPublisher = publisher;
    }

    public void PublishEvent(ObjectExpiredEvent @event)
    {
        _objExpiredEventPublisher.Publish(@event);
    }

    public void SetPublisher(IPublisher<FrameExpiredEvent> publisher)
    {
        _frmExpiredEventPublisher = publisher;
    }

    public void PublishEvent(FrameExpiredEvent @event)
    {
        _frmExpiredEventPublisher.Publish(@event);
    }
    #endregion


    public void Dispose()
    {
        foreach (var frame in _frames)
        {
            foreach (var detectedObject in frame.DetectedObjects)
            {
                _objExpiredEventPublisher.Publish(new ObjectExpiredEvent(detectedObject));
            }

            _frmExpiredEventPublisher.Publish(new FrameExpiredEvent(frame));
        }
    }
}