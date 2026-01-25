using Detector.Common;
using Detector.YoloDotNet;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Extensions;
using System.Diagnostics;

namespace Detector.Tests;

public class YoloDotNetDetectorTests
{
    private readonly string _modelPath = @"Models/yolo11m.onnx";

    private readonly YoloDetector _detector;
    private Dictionary<string, string> _pref;

    public YoloDotNetDetectorTests()
    {
        _pref = new Dictionary<string, string>();
        _pref.Add("ModelPath", _modelPath);
        _pref.Add("ExecutionProvider", "cuda");
        _pref.Add("DeviceId", "0");
        _pref.Add("TargetTypes", "");
        _pref.Add("DetectionStride", "1");
        _pref.Add("FilterSmallObject", "false");
        _pref.Add("MinBboxWidth", "0");
        _pref.Add("MinBboxHeight", "0");
        _pref.Add("RegionDetectionEnabled", "false");
        _pref.Add("DetectionRegion", "0,0,0,0");
        _pref.Add("TileDetectionEnabled", "false");
        _pref.Add("TileDetectionSize", "(1,2)");
        _pref.Add("MaxStitchGapPixel", "2");
        _pref.Add("MinVerticalOverlapRatio", "0.9");

        _detector = new YoloDetector(_pref);
        _detector.Init();
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        _detector.Dispose();
    }

    private static void ShowResultImage(IReadOnlyList<DetectedObject> detectedObjects, Mat mat)
    {
        mat.DrawDetections(detectedObjects);

        Cv2.ImShow("test", mat.Resize(new Size(1920, 1080)));
        Cv2.WaitKey();
    }

    [Test]
    public void TestDetectMat()
    {
        using var mat = new Mat("Images/Traffic_001.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        var stopwatch = Stopwatch.StartNew();
        var items = _detector.Detect(frame);
        stopwatch.Stop();
        Console.WriteLine($"detection elapse: {stopwatch.ElapsedMilliseconds}ms");

        //ShowResultImage(items, mat);

        Assert.That(items.Count, Is.EqualTo(17));
    }

    [Test]
    public void TestDetect2KMat()
    {
        using var mat = new Mat("Images/Pedestrian2K.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        var stopwatch = Stopwatch.StartNew();
        var items = _detector.Detect(frame);
        stopwatch.Stop();
        Console.WriteLine($"detection elapse: {stopwatch.ElapsedMilliseconds}ms");

        //ShowResultImage(items, mat);

        Assert.That(items.Count, Is.EqualTo(20));
    }

    [Test]
    public void TestDetect4KMat()
    {
        using var mat = new Mat("Images/Pedestrian4K.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        var stopwatch = Stopwatch.StartNew();
        var items = _detector.Detect(frame);
        stopwatch.Stop();
        Console.WriteLine($"detection elapse: {stopwatch.ElapsedMilliseconds}ms");

        //ShowResultImage(items, mat);

        Assert.That(items.Count, Is.EqualTo(22));
    }

    [Test]
    public void TestDetectHighwayMat()
    {
        using var mat = new Mat("Images/Traffic_002.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        var stopwatch = Stopwatch.StartNew();
        var items = _detector.Detect(frame);
        stopwatch.Stop();
        Console.WriteLine($"detection elapse: {stopwatch.ElapsedMilliseconds}ms");

        //ShowResultImage(items, mat);

        Assert.That(items.Count, Is.EqualTo(10));
    }

    [Test]
    public void TestDetectHighwayForMotionMat()
    {
        using var mat = new Mat("Images/pl_000001.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        var stopwatch = Stopwatch.StartNew();
        var items = _detector.Detect(frame);
        stopwatch.Stop();
        Console.WriteLine($"detection elapse: {stopwatch.ElapsedMilliseconds}ms");

        //ShowResultImage(items, mat);

        Assert.That(items.Count, Is.EqualTo(9));
    }

    [Test]
    public void TestDetectBechmark()
    {
        int repeatTimes = 10;

        using var mat = new Mat("Images/Traffic_001.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < repeatTimes; i++)
        {
            var items = _detector.Detect(frame);
        }
        stopwatch.Stop();

        Console.WriteLine($"detection elapse: {stopwatch.ElapsedMilliseconds / repeatTimes}ms");
    }

    [Test]
    public void TestDetectBatch()
    {
        var imgs = new List<Mat>
        {
            Cv2.ImRead("Images/Traffic_001.jpg"),
            Cv2.ImRead("Images/Traffic_002.jpg"),
            Cv2.ImRead("Images/pl_000001.jpg"),
            Cv2.ImRead("Images/Pedestrian2K.jpg"),
        };

        var frames = new List<Frame>
        {
            new Frame("tempId", 0, 25, imgs[0]),
            new Frame("tempId", 1, 50, imgs[1]),
            new Frame("tempId", 2, 75, imgs[2]),
            new Frame("tempId", 3, 100, imgs[3]),
        };

        var stopwatch = Stopwatch.StartNew();
        var singleResult1 = _detector.Detect(frames[0]);
        var singleResult2 = _detector.Detect(frames[1]);
        var singleResult3 = _detector.Detect(frames[2]);
        var singleResult4 = _detector.Detect(frames[3]);
        stopwatch.Stop();
        Console.WriteLine($"4 frames iteration detection elapse: {stopwatch.ElapsedMilliseconds}ms");

        _detector.DetectBatch(frames);

        var stopwatchBatch = Stopwatch.StartNew();
        var results = _detector.DetectBatch(frames);
        stopwatchBatch.Stop();
        Console.WriteLine($"4 frames batch detection elapse:: {stopwatchBatch.ElapsedMilliseconds}ms");

        Assert.That(results.Count, Is.EqualTo(4));
        Assert.That(results[0].Count, Is.EqualTo(singleResult1.Count));
        Assert.That(results[1].Count, Is.EqualTo(singleResult2.Count));
        Assert.That(results[2].Count, Is.EqualTo(singleResult3.Count));
        Assert.That(results[3].Count, Is.EqualTo(singleResult4.Count));
    }

    [Test]
    public void TestDetectByTile()
    {
        using var mat = new Mat("Images/Traffic_001.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        _pref["tileDetectionEnabled"] = "true";
        _pref["tileDetectionSize"] = "(1,2)";
        _detector.Init();

        var stopwatchBatch = Stopwatch.StartNew();
        var results = _detector.DetectByTile(frame, new Tuple<int, int>(1, 2));
        stopwatchBatch.Stop();
        Console.WriteLine($"1 * 2 tile detection elapse:: {stopwatchBatch.ElapsedMilliseconds}ms");

        //ShowResultImage(results, mat);

        Assert.That(results.Count, Is.EqualTo(28));
    }

    [Test]
    public void TestFilterInnerSameObjects_WithContainedObjects_ShouldRemoveInnerObjects()
    {
        // Arrange
        var detectedObjects = new List<DetectedObject>
        {
            new DetectedObject(
                sourceId: "test",
                frameId: 1,
                utcTimeStamp: DateTime.UtcNow,
                labelId: 1,
                confidence: 0.9f,
                bbox: BoundingBox.CreateFromRect(10, 10, 100, 100),
                label: "person"
            ),
            new DetectedObject(
                sourceId: "test",
                frameId: 1,
                utcTimeStamp: DateTime.UtcNow,
                labelId: 1,
                confidence: 0.8f,
                bbox: BoundingBox.CreateFromRect(20, 20, 50, 50),
                label: "person"
            ),
            new DetectedObject(
                sourceId: "test",
                frameId: 1,
                utcTimeStamp: DateTime.UtcNow,
                labelId: 2,
                confidence: 0.7f,
                bbox: BoundingBox.CreateFromRect(200, 200, 80, 60),
                label: "car"
            )
        };

        // Act
        //var filteredObjects = _detector.FilterInnerSameObjects(detectedObjects);
        var filteredObjects = DetectionFilter.FilterInnerSameObjects(detectedObjects, 0.8f);

        // Assert
        // 注意：使用新的逻辑，当两个person框重合超过阈值时，保留面积更大的外层框(10,10,100,100)，
        // 移除面积较小的内层框(20,20,50,50)，加上car对象，总共保留2个对象
        Assert.That(filteredObjects.Count, Is.EqualTo(2));
        Assert.That(filteredObjects.Any(obj => obj.Label == "car"), Is.True);
        Assert.That(filteredObjects.Any(obj => obj.Label == "person" && obj.Confidence == 0.9f), Is.True);
        Assert.That(filteredObjects.Any(obj => obj.Label == "person" && obj.Confidence == 0.8f), Is.False);
    }

    [Test]
    public void TestFilterInnerSameObjects_WithNonContainedObjects_ShouldKeepAllObjects()
    {
        // Arrange
        var detectedObjects = new List<DetectedObject>
        {
            new DetectedObject(
                sourceId: "test",
                frameId: 1,
                utcTimeStamp: DateTime.UtcNow,
                labelId: 1,
                confidence: 0.9f,
                bbox: BoundingBox.CreateFromRect(10, 10, 50, 50),
                label: "person"
            ),
            new DetectedObject(
                sourceId: "test",
                frameId: 1,
                utcTimeStamp: DateTime.UtcNow,
                labelId: 1,
                confidence: 0.8f,
                bbox: BoundingBox.CreateFromRect(70, 70, 50, 50),
                label: "person"
            )
        };

        // Act
        //var filteredObjects = _detector.FilterInnerSameObjects(detectedObjects);
        var filteredObjects = DetectionFilter.FilterInnerSameObjects(detectedObjects, 0.8f);

        // Assert
        Assert.That(filteredObjects.Count, Is.EqualTo(2));
    }

    [Test]
    public void TestWillSurpressInnerSameObject_WhenEnabled_ShouldFilterResults()
    {
        // Arrange - 创建一个启用了WillSurpressInnerSameObject的检测器
        var prefWithSuppression = new Dictionary<string, string>(_pref)
        {
            ["WillSuppressInnerSameObject"] = "true"
        };

        using var detectorWithSuppression = new YoloDetector(prefWithSuppression);
        detectorWithSuppression.Init();

        using var mat = new Mat("Images/Traffic_001.jpg", ImreadModes.Color);
        using var frame = new Frame("tempId", 0, 0, mat);

        // Act
        var resultsWithSuppression = detectorWithSuppression.Detect(frame);
        var resultsWithoutSuppression = _detector.Detect(frame);

        // Assert - 启用抑制后，结果数量应该小于等于未启用时的数量
        Assert.That(resultsWithSuppression.Count, Is.LessThanOrEqualTo(resultsWithoutSuppression.Count));

        detectorWithSuppression.Dispose();
    }
}