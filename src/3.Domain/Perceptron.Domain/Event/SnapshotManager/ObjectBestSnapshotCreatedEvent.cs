using OpenCvSharp;

namespace Perceptron.Domain.Event.SnapshotManager;

public class ObjectBestSnapshotCreatedEvent : EventBase
{
    public string ObjectId { get; }
    public Mat ObjectSnapshot { get; }

    public ObjectBestSnapshotCreatedEvent(string detectdObjectId, Mat snapshot)
    {
        ObjectId = detectdObjectId;
        ObjectSnapshot = snapshot;
    }
}