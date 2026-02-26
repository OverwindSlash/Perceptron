using OpenCvSharp;
using Perceptron.Domain.Abstraction.EventHandler;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Event.SnapshotManager;

namespace Perceptron.Domain.Abstraction.SnapshotManager;

public interface ISnapshotManager : IEventPublisher<ObjectBestSnapshotCreatedEvent>, IEventSubscriber<ObjectExpiredEvent>, IEventSubscriber<FrameExpiredEvent>
{
    public string SnapshotDir { get; }

    void ProcessSnapshots(Frame frame);

    public Mat GetSceneByFrameId(long frameId);
    public int GetCachedSceneCount();

    public SortedList<float, Mat> GetObjectSnapshotsByObjectId(string objId);
    public Mat GetBestSnapshotByObjectId(string objId);
    public int GetCachedSnapshotCount();

    Mat TakeSnapshot(Frame frame, BoundingBox bboxs);
    void AddSnapshotOfObject(Frame frame, DetectedObject detectedObject, float score, Mat snapshot);

    Mat GenerateBoxedScene(Mat scene, List<BoundingBox> boundingBoxes);

    /// <summary>
    /// 生成指定帧前后M秒的视频片段（异步版本）
    /// </summary>
    /// <param name="filepath">输出视频文件路径</param>
    /// <param name="centerFrameId">中心帧ID</param>
    /// <param name="durationSeconds">视频时长（秒），如果为null则使用配置的默认值</param>
    /// <param name="frameRate">帧率，如果为null则使用配置的默认值</param>
    /// <returns>异步任务</returns>
    Task GenerateVideoClipAroundFrameAsync(string filepath, long centerFrameId, int? durationSeconds = null, double? frameRate = null);

    /// <summary>
    /// 生成指定帧前后M秒的视频片段（同步版本）
    /// </summary>
    /// <param name="filepath">输出视频文件路径</param>
    /// <param name="centerFrameId">中心帧ID</param>
    /// <param name="durationSeconds">视频时长（秒），如果为null则使用配置的默认值</param>
    /// <param name="frameRate">帧率，如果为null则使用配置的默认值</param>
    void GenerateVideoClipAroundFrame(string filepath, long centerFrameId, int? durationSeconds = null, double? frameRate = null);
}