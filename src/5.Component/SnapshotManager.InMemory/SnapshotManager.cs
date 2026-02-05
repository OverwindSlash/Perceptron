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
    private int _snapshotRetentionDays = 3;
    private int _videoClipDurationSeconds = 10;
    private double _videoFrameRate = 25.0;

    private readonly System.Timers.Timer _cleanupTimer;

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
        _snapshotRetentionDays = SnapshotSettings.ParseSnapshotRetentionDays(preferences);
        _videoClipDurationSeconds = SnapshotSettings.ParseVideoClipDurationSeconds(preferences);
        _videoFrameRate = SnapshotSettings.ParseVideoFrameRate(preferences);

        var snapshotFullPath = Path.Combine(Directory.GetCurrentDirectory(), _snapshotsDir);
        snapshotFullPath.EnsureDirExistence();

        _cleanupTimer = new System.Timers.Timer(TimeSpan.FromHours(1).TotalMilliseconds);
        _cleanupTimer.Elapsed += (sender, e) => CleanupSnapshots();
        _cleanupTimer.AutoReset = true;
        _cleanupTimer.Start();

        // Initial cleanup
        Task.Run(() => CleanupSnapshots());
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

    private void ReleaseSnapshotsByObjectId(ObjectExpiredEvent @event, bool saveBeforeRelease = true)
    {
        if (!_snapshotsByScore.ContainsKey(@event.Id))
        {
            return;
        }

        SortedList<float, Mat> snapshots = _snapshotsByScore[@event.Id];

        if (saveBeforeRelease)
        {
            var highestScore = snapshots.Keys.Max();
            Mat highestSnapshot = snapshots[highestScore];

            SaveBestSnapshot(@event, highestSnapshot);
        }

        foreach (Mat snapshot in snapshots.Values)
        {
            snapshot.Dispose();
        }

        _snapshotsByScore.TryRemove(@event.Id, out var removedSnapshots);
    }

    private void SaveBestSnapshot(ObjectExpiredEvent @event, Mat highestSnapshot)
    {
        // if (highestSnapshot.IsDisposed)
        // {
        //     return;
        // }

        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string date = DateTime.Now.ToString("yyyyMMdd");
        string filename = @event.Id.Replace(':', '_');
        if (highestSnapshot.Width > _minSnapshotWidth && highestSnapshot.Height > _minSnapshotHeight)
        {
            string path = $"{_snapshotsDir}/Best/{date}/{@event.SourceId}";
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
                var allAvailableFrameIds = _scenesOfFrame.Keys.ToList();
                allAvailableFrameIds.Sort();
                
                if (allAvailableFrameIds.Count == 0)
                {
                    throw new InvalidOperationException("No frames available in cache");
                }
                
                // 检查中心帧是否存在
                // 使用 BinarySearch 提高查找效率 (O(log N))
                int centerIndex = allAvailableFrameIds.BinarySearch(centerFrameId);
                if (centerIndex < 0)
                {
                    throw new InvalidOperationException($"Center frame {centerFrameId} is not available in cache");
                }
                
                // 计算理想的帧数（前后各一半）
                int idealTotalFrames = (int)(requestedDuration * fps);
                int idealHalfFrames = idealTotalFrames / 2;
                
                // 计算需要的后向帧数
                int requiredFramesAfter = idealHalfFrames;
                int availableFramesAfter = allAvailableFrameIds.Count - centerIndex - 1;
                
                // 等待足够的帧数（最多等待3秒）
                // 优化：只有在后向帧数不足时才等待。前向帧数不足无法通过等待解决（历史帧不会凭空出现）。
                int maxWaitSeconds = 3;
                int waitIntervalMs = 100; // 每100ms检查一次
                int totalWaitTime = 0;
                
                while (availableFramesAfter < requiredFramesAfter && totalWaitTime < maxWaitSeconds * 1000)
                {
                    await Task.Delay(waitIntervalMs);
                    totalWaitTime += waitIntervalMs;
                    
                    // 重新获取可用帧列表
                    allAvailableFrameIds = _scenesOfFrame.Keys.ToList();
                    allAvailableFrameIds.Sort();
                    
                    centerIndex = allAvailableFrameIds.BinarySearch(centerFrameId);
                    if (centerIndex < 0)
                    {
                        throw new InvalidOperationException($"Center frame {centerFrameId} is no longer available in cache");
                    }
                    
                    availableFramesAfter = allAvailableFrameIds.Count - centerIndex - 1;
                }
                
                // 确定最终的帧范围（Best Effort 策略：尽可能多地包含请求的帧，而不是抛出异常）
                int actualFramesBefore = Math.Min(centerIndex, idealHalfFrames);
                int actualFramesAfter = Math.Min(availableFramesAfter, idealHalfFrames);
                
                // 记录如果是由于超时导致的帧数不足
                if (availableFramesAfter < requiredFramesAfter)
                {
                     Log.Warning($"Video generation timeout: could not get full duration. Requested after: {requiredFramesAfter}, Available: {availableFramesAfter}");
                }
                
                int startIndex = centerIndex - actualFramesBefore;
                int endIndex = centerIndex + actualFramesAfter;
                int count = endIndex - startIndex + 1;
                
                var selectedFrameIds = allAvailableFrameIds.GetRange(startIndex, count);
                
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
                        // 确保帧尺寸一致，如果不一致需要缩放（通常应该是一致的）
                        if (frame.Width != imageWidth || frame.Height != imageHeight)
                        {
                            using var resizedFrame = new Mat();
                            Cv2.Resize(frame, resizedFrame, new Size(imageWidth, imageHeight));
                            writer.Write(resizedFrame);
                        }
                        else
                        {
                            writer.Write(frame);
                        }
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
            ReleaseSnapshotsByObjectId(@event, _saveBestSnapshot);
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

    private void CleanupSnapshots()
    {
        try
        {
            var bestSnapshotsDir = Path.Combine(_snapshotsDir, "Best");
            if (!Directory.Exists(bestSnapshotsDir))
            {
                return;
            }

            var directories = Directory.GetDirectories(bestSnapshotsDir);
            var now = DateTime.Now;
            var retentionDays = _snapshotRetentionDays;

            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                if (DateTime.TryParseExact(dirName, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                {
                    if ((now - date).TotalDays > retentionDays)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            Log.Information("Deleted old snapshot directory: {dir}", dir);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to delete old snapshot directory: {dir}", dir);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during snapshot cleanup");
        }
    }

    public override void Dispose()
    {
        _cleanupTimer?.Stop();
        _cleanupTimer?.Dispose();

        foreach (Mat scene in _scenesOfFrame.Values)
        {
            scene.Dispose();
        }

        base.Dispose();
    }
}