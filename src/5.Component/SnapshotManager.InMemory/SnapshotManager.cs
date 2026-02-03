using MessagePipe;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Event.SnapshotManager;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Serilog;
using System.Collections.Concurrent;

namespace SnapshotManager.InMemory;

public class SnapshotManager : FrameAndObjectExpiredSubscriber, ISnapshotManager
{
    // frameId -> Scene
    private readonly ConcurrentDictionary<long, Mat> _scenesOfFrame;
    // object snapshot list by objId -> (factor, objectMat)
    private readonly ConcurrentDictionary<string, SortedList<float, Mat>> _snapshotsByScore;

    private readonly string _snapshotsDir = "Snapshots";
    private bool _saveBestSnapshot = false;
    private BestSnapshotBy _bestSnapshotBy = BestSnapshotBy.Confidence;
    private float _snapshotExpansionRatio = 1.2f;
    private int _maxObjectSnapshots = 10;
    private int _minSnapshotWidth = 40;
    private int _minSnapshotHeight = 40;
    private int _videoClipDurationSeconds = 10;
    private double _videoFrameRate = 25.0;

    public string Name => "In-memory snapshot manager";
    public string SnapshotDir => _snapshotsDir;

    private IPublisher<ObjectBestSnapshotCreatedEvent> _objBestSnapshotCreatedEventPublisher;

    public SnapshotManager(Dictionary<string, string> preferences = null)
    {
        _scenesOfFrame = new ConcurrentDictionary<long, Mat>();
        _snapshotsByScore = new ConcurrentDictionary<string, SortedList<float, Mat>>();

        _snapshotsDir = SnapshotSettings.ParseSnapshotDir(preferences);
        _saveBestSnapshot = SnapshotSettings.ParseSaveBestSnapshot(preferences);
        _bestSnapshotBy = SnapshotSettings.ParseBestSnapshotBy(preferences);
        _snapshotExpansionRatio = SnapshotSettings.ParseSnapshotExpansionRatio(preferences);
        _maxObjectSnapshots = SnapshotSettings.ParseMaxSnapshots(preferences);
        _minSnapshotWidth = SnapshotSettings.ParseMinSnapshotWidth(preferences);
        _minSnapshotHeight = SnapshotSettings.ParseMinSnapshotHeight(preferences);
        _videoClipDurationSeconds = SnapshotSettings.ParseVideoClipDurationSeconds(preferences);
        _videoFrameRate = SnapshotSettings.ParseVideoFrameRate(preferences);

        var snapshotFullPath = Path.Combine(Directory.GetCurrentDirectory(), _snapshotsDir);
        snapshotFullPath.EnsureDirExistence();
    }

    public void ProcessSnapshots(Frame frame)
    {
        frame.Retain();

        AddSceneByFrameId(frame.FrameId, frame);
        AddSnapshotOfObjectById(frame);

        frame.Dispose();
    }

    private void AddSceneByFrameId(long frameId, Frame frame)
    {
        if (!_scenesOfFrame.ContainsKey(frameId))
        {
            _scenesOfFrame.TryAdd(frameId, frame.Scene);
        }
    }

    private void AddSnapshotOfObjectById(Frame frame)
    {
        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (!detectedObject.IsUnderAnalysis)
            {
                continue;
            }

            Mat snapshot = TakeSnapshot(frame, detectedObject.Bbox.ScaleAboutCenter(_snapshotExpansionRatio, _snapshotExpansionRatio));
            detectedObject.AttachSnapshot(snapshot, false); // 重要：确保 detectedObject 中的 snapshot 与 _snapshotsByScore 中的 snapshot 有不同的生命周期
            AddSnapshotOfObjectById(detectedObject.Id, CalculateFactor(detectedObject), snapshot);
        }
    }

    public Mat TakeSnapshot(Frame frame, BoundingBox bboxs)
    {
        //return frame.Scene.SubMat(new Rect(bboxs.X, bboxs.Y, bboxs.Width, bboxs.Height)).Clone();

        // 获取当前图像的宽高
        int sceneWidth = frame.Scene.Width;
        int sceneHeight = frame.Scene.Height;

        // 原始Rect
        int x = bboxs.X;
        int y = bboxs.Y;
        int width = bboxs.Width;
        int height = bboxs.Height;

        // 确保X、Y至少从0开始
        x = Math.Max(0, x);
        y = Math.Max(0, y);

        // 如果 X+width 超过图像右边，则进行裁剪
        if (x + width > sceneWidth)
        {
            width = sceneWidth - x;
        }
        // 如果 Y+height 超过图像下边，则进行裁剪
        if (y + height > sceneHeight)
        {
            height = sceneHeight - y;
        }

        // 保险措施：如果裁剪后 width 或 height <= 0，则直接返回空
        if (width <= 0 || height <= 0)
        {
            // 这里可根据业务需求返回null或者一张空的Mat
            return new Mat();
        }

        // 构造新的Rect，保证它在图像内部
        Rect validRect = new Rect(x, y, width, height);

        // 使用有效Rect进行 SubMat 操作
        return frame.Scene.SubMat(validRect);
    }

    public void AddSnapshotOfObjectById(string objId, float score, Mat snapshot)
    {
        if (!_snapshotsByScore.ContainsKey(objId))
        {
            _snapshotsByScore.TryAdd(objId, new SortedList<float, Mat>());
        }

        SortedList<float, Mat> snapshotsOfId = _snapshotsByScore[objId];    // 获取对应对象ID的快照列表
        if (!snapshotsOfId.ContainsKey(score))
        {
            snapshotsOfId.Add(score, snapshot);

            var maxScore = snapshotsOfId.Keys.Max();
            if (score == maxScore)
            {
                ObjectBestSnapshotCreatedEvent bestSnapshotCreatedEvent = new ObjectBestSnapshotCreatedEvent(objId, snapshot);
                PublishEvent(bestSnapshotCreatedEvent);
            }
        }
        else
        {
            snapshotsOfId[score].Dispose();     // 重要：需要释放Mat资源
            snapshotsOfId[score] = snapshot;
        }

        if (snapshotsOfId.Count > _maxObjectSnapshots)
        {
            for (int i = 0; i < snapshotsOfId.Count - _maxObjectSnapshots; i++)
            {
                // remove tail (lowest score)
                snapshotsOfId.Values[i].Dispose();  // 重要：需要释放Mat资源
                snapshotsOfId.RemoveAt(i);
            }
        }
    }

    public Mat GenerateBoxedScene(Mat scene, List<BoundingBox> boundingBoxes)
    {
        Mat boxedScene = scene.Clone();

        foreach (var boundingBox in boundingBoxes)
        {
            boxedScene.Rectangle(boundingBox.Rectangle, Scalar.Crimson, 2);
        }

        return boxedScene;
    }

    private float CalculateFactor(DetectedObject obj)
    {
        switch (_bestSnapshotBy)
        {
            case BestSnapshotBy.Confidence:
                return obj.Confidence;
            case BestSnapshotBy.Area:
                return obj.Width * obj.Height;
            case BestSnapshotBy.Width:
                return obj.Width;
            case BestSnapshotBy.Height:
                return obj.Height;
            default:
                return obj.Confidence;
        }
    }

    public Mat GetSceneByFrameId(long frameId)
    {
        if (_scenesOfFrame.ContainsKey(frameId))
        {
            _scenesOfFrame.TryGetValue(frameId, out var scene);
            return scene;
        }

        return new Mat();
    }

    public int GetCachedSceneCount()
    {
        return _scenesOfFrame.Count;
    }

    public SortedList<float, Mat> GetObjectSnapshotsByObjectId(string id)
    {
        if (!_snapshotsByScore.ContainsKey(id))
        {
            return new SortedList<float, Mat>();
        }

        return _snapshotsByScore[id];
    }

    public Mat GetBestSnapshotByObjectId(string id)
    {
        var snapshots = GetObjectSnapshotsByObjectId(id);
        if (snapshots.Count == 0)
        {
            return new Mat();
        }

        var highestScore = snapshots.Keys.Max();
        Mat highestSnapshot = snapshots[highestScore];

        return highestSnapshot;
    }

    public int GetCachedSnapshotCount()
    {
        return _snapshotsByScore.Count;
    }

    private void ReleaseSceneByFrameId(long frameId)
    {
        if (_scenesOfFrame.ContainsKey(frameId))
        {
            _scenesOfFrame[frameId].Dispose();

            _scenesOfFrame.TryRemove(frameId, out var mat);
        }
    }

    private void ReleaseSnapshotsByObjectId(string id, bool saveBeforeRelease = true)
    {
        if (!_snapshotsByScore.ContainsKey(id))
        {
            return;
        }

        SortedList<float, Mat> snapshots = _snapshotsByScore[id];

        if (saveBeforeRelease)
        {
            var highestScore = snapshots.Keys.Max();
            Mat highestSnapshot = snapshots[highestScore];

            SaveBestSnapshot(id, highestSnapshot);
        }

        foreach (Mat snapshot in snapshots.Values)
        {
            snapshot.Dispose();
        }

        _snapshotsByScore.TryRemove(id, out var removedSnapshots);
    }

    private void SaveBestSnapshot(string id, Mat highestSnapshot)
    {
        // if (highestSnapshot.IsDisposed)
        // {
        //     return;
        // }

        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string filename = id.Replace(':', '_');
        if (highestSnapshot.Width > _minSnapshotWidth && highestSnapshot.Height > _minSnapshotHeight)
        {
            string path = $"{_snapshotsDir}/Best";
            path.EnsureDirExistence();
            var fileSavePath = Path.Combine(path, $"{filename}_{timestamp}.jpg");

            highestSnapshot.SaveImage(fileSavePath);
        }
    }

    public void GenerateVideoClip(string filepath)
    {
        Task.Run(() =>
        {
            var firstFrame = _scenesOfFrame.FirstOrDefault();
            int imageWidth = firstFrame.Value.Width;
            int imageHeight = firstFrame.Value.Height;

            using var writer = new VideoWriter();
            var success = writer.Open(filepath, VideoCaptureAPIs.FFMPEG, FourCC.MP4V, 25, new Size(imageWidth, imageHeight));
            if (!success)
            {
                return;
            }

            List<long> keys = _scenesOfFrame.Keys.OrderBy(id => id).ToList();
            foreach (long frameId in keys)
            {
                if (_scenesOfFrame.TryGetValue(frameId, out var image))
                {
                    writer.Write(image);
                }
            }

            writer.Release();
        });
    }

    /// <summary>
    /// 生成指定帧前后M秒的视频片段
    /// 注意：此方法会等待缓存中有足够的帧数据来生成完整的视频片段。
    /// 如果指定帧前后的帧数不足，会等待新帧到达（最多等待30秒）。
    /// </summary>
    /// <param name="filepath">输出视频文件路径</param>
    /// <param name="centerFrameId">中心帧ID，必须存在于缓存中</param>
    /// <param name="durationSeconds">视频时长（秒），如果为null则使用配置的默认值</param>
    /// <param name="frameRate">帧率，如果为null则使用配置的默认值</param>
    /// <returns>异步任务</returns>
    /// <exception cref="InvalidOperationException">当缓存中没有帧、指定的中心帧不存在或等待超时时抛出</exception>
    public Task GenerateVideoClipAroundFrameAsync(string filepath, long centerFrameId, int? durationSeconds = null, double? frameRate = null)
    {
        return Task.Run(async () =>
        {
            try
            {
                // 使用传入的参数或默认配置
                int requestedDuration = durationSeconds ?? _videoClipDurationSeconds;
                double fps = frameRate ?? _videoFrameRate;
                
                // 获取所有可用的帧ID并排序
                var allAvailableFrameIds = _scenesOfFrame.Keys.OrderBy(frameId => frameId).ToList();
                
                if (allAvailableFrameIds.Count == 0)
                {
                    throw new InvalidOperationException("No frames available in cache");
                }
                
                // 检查中心帧是否存在
                if (!allAvailableFrameIds.Contains(centerFrameId))
                {
                    throw new InvalidOperationException($"Center frame {centerFrameId} is not available in cache");
                }
                
                // 计算理想的帧数（前后各一半）
                int idealTotalFrames = (int)(requestedDuration * fps);
                int idealHalfFrames = idealTotalFrames / 2;
                
                // 找到中心帧在可用帧列表中的索引
                int centerIndex = allAvailableFrameIds.IndexOf(centerFrameId);
                
                // 计算实际可用的前后帧数
                int availableFramesBefore = centerIndex;
                int availableFramesAfter = allAvailableFrameIds.Count - centerIndex - 1;
                
                // 检查是否有足够的帧数，如果不足则等待
                int requiredFramesBefore = idealHalfFrames;
                int requiredFramesAfter = idealHalfFrames;
                
                // 等待足够的帧数（最多等待3秒）
                int maxWaitSeconds = 3;
                int waitIntervalMs = 100; // 每50ms检查一次
                int totalWaitTime = 0;
                
                while ((availableFramesBefore < requiredFramesBefore || availableFramesAfter < requiredFramesAfter) && totalWaitTime < maxWaitSeconds * 1000)
                {
                    // Console.WriteLine($"等待更多帧数据... 当前可用: 前{availableFramesBefore}帧, 后{availableFramesAfter}帧, 需要: 前{requiredFramesBefore}帧, 后{requiredFramesAfter}帧");
                    
                    await Task.Delay(waitIntervalMs);
                    totalWaitTime += waitIntervalMs;
                    
                    // 重新获取可用帧列表
                    allAvailableFrameIds = _scenesOfFrame.Keys.OrderBy(frameId => frameId).ToList();
                    
                    if (!allAvailableFrameIds.Contains(centerFrameId))
                    {
                        throw new InvalidOperationException($"Center frame {centerFrameId} is no longer available in cache");
                    }
                    
                    centerIndex = allAvailableFrameIds.IndexOf(centerFrameId);
                    availableFramesBefore = centerIndex;
                    availableFramesAfter = allAvailableFrameIds.Count - centerIndex - 1;
                }
                
                // 检查是否获得了足够的帧数
                if (availableFramesBefore < requiredFramesBefore || availableFramesAfter < requiredFramesAfter)
                {
                    throw new InvalidOperationException(
                        $"等待超时：无法获得足够的帧数。当前可用: 前{availableFramesBefore}帧, 后{availableFramesAfter}帧, " +
                        $"需要: 前{requiredFramesBefore}帧, 后{requiredFramesAfter}帧");
                }
                
                // 确定最终的帧范围（使用完整的请求范围）
                int startIndex = centerIndex - requiredFramesBefore;
                int endIndex = centerIndex + requiredFramesAfter;
                
                var selectedFrameIds = allAvailableFrameIds.GetRange(startIndex, endIndex - startIndex + 1);
                
                if (selectedFrameIds.Count == 0)
                {
                    throw new InvalidOperationException($"No valid frames found in the selected range around frame {centerFrameId}");   
                }
                
                // 获取第一帧以确定视频尺寸
                if (!_scenesOfFrame.TryGetValue(selectedFrameIds.First(), out var firstFrame) || firstFrame.Empty())
                {
                    throw new InvalidOperationException("First frame is not available or empty");
                }
                
                int imageWidth = firstFrame.Width;
                int imageHeight = firstFrame.Height;
                
                // 创建视频写入器
                using var writer = new VideoWriter();
                var success = writer.Open(filepath, VideoCaptureAPIs.FFMPEG, FourCC.H264, fps, new Size(imageWidth, imageHeight));
                if (!success)
                {
                    throw new InvalidOperationException($"Failed to open video writer for file: {filepath}");
                }
                
                // 写入帧数据
                int writtenFrames = 0;
                foreach (long frameId in selectedFrameIds)
                {
                    if (_scenesOfFrame.TryGetValue(frameId, out var frame) && !frame.Empty())
                    {
                        writer.Write(frame);
                        writtenFrames++;
                    }
                }
                
                writer.Release();
                
                // 计算实际生成的视频时长
                double actualDuration = writtenFrames / fps;
                
                // 记录生成的视频信息
                Log.Information($"Video clip generated: {filepath}");
                Log.Information($"Center frame: {centerFrameId}, Requested duration: {requestedDuration}s, Actual duration: {actualDuration:F2}s, FPS: {fps}");
                Log.Information($"Frame range: {selectedFrameIds.First()} - {selectedFrameIds.Last()}, Written frames: {writtenFrames}/{selectedFrameIds.Count}");
                Log.Information($"Used frames before center: {requiredFramesBefore}, after center: {requiredFramesAfter}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error generating video clip: {ex.Message}");
                throw;
            }
        });
    }

    /// <summary>
    /// 生成指定帧前后M秒的视频片段（同步版本）
    /// 注意：此方法会等待缓存中有足够的帧数据来生成完整的视频片段。
    /// 如果指定帧前后的帧数不足，会等待新帧到达（最多等待30秒）。
    /// </summary>
    /// <param name="filepath">输出视频文件路径</param>
    /// <param name="centerFrameId">中心帧ID，必须存在于缓存中</param>
    /// <param name="durationSeconds">视频时长（秒），如果为null则使用配置的默认值</param>
    /// <param name="frameRate">帧率，如果为null则使用配置的默认值</param>
    /// <exception cref="InvalidOperationException">当缓存中没有帧、指定的中心帧不存在或等待超时时抛出</exception>
    public void GenerateVideoClipAroundFrame(string filepath, long centerFrameId, int? durationSeconds = null, double? frameRate = null)
    {
        GenerateVideoClipAroundFrameAsync(filepath, centerFrameId, durationSeconds, frameRate).Wait();
    }

    public override void ProcessEvent(ObjectExpiredEvent @event)
    {
        Task.Run(() =>
        {
            ReleaseSnapshotsByObjectId(@event.Id, _saveBestSnapshot);
            //ReleaseSnapshotsByObjectId($"cb_{@event.Id}", _saveBestSnapshot);
        }).Wait();
    }

    public override void ProcessEvent(FrameExpiredEvent @event)
    {
        Task.Run(() =>
        {
            ReleaseSceneByFrameId(@event.FrameId);
        }).Wait();
    }

    public void SetPublisher(IPublisher<ObjectBestSnapshotCreatedEvent> publisher)
    {
        _objBestSnapshotCreatedEventPublisher = publisher;
    }

    public void PublishEvent(ObjectBestSnapshotCreatedEvent @event)
    {
        _objBestSnapshotCreatedEventPublisher.Publish(@event);
    }

    public override void Dispose()
    {
        foreach (Mat scene in _scenesOfFrame.Values)
        {
            scene.Dispose();
        }

        base.Dispose();
    }
}