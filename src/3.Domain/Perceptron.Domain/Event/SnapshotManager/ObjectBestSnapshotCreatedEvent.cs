using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Event.SnapshotManager;

public class ObjectBestSnapshotCreatedEvent : EventBase
{
    public Frame Frame { get; set; }
    public DetectedObject DetectedObject { get; set; }
    public Mat ObjectSnapshot { get; }
    public float Score { get; set; }

    public ObjectBestSnapshotCreatedEvent(Frame frame, DetectedObject detectedObject, Mat snapshot, float score)
    {
        Frame = frame;
        DetectedObject = detectedObject;
        ObjectSnapshot = snapshot;
        Score = score;
    }
}