using Microsoft.Extensions.Configuration;
using Perceptron.Service.Pipeline;

namespace Perceptron.Service.Tests.Pipeline;

[TestFixture]
public class AnalysisPipelineTests
{
    private IConfiguration _configuration;
    private AnalysisPipeline? _pipeline;

    [SetUp]
    public void Setup()
    {
        // Setup configuration with default valid settings
        var myConfiguration = GetValidConfigurationDictionary();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();
    }

    [TearDown]
    public void TearDown()
    {
        _pipeline?.Dispose();
    }

    [Test]
    public void Constructor_ShouldInitializeCorrectly_WhenConfigurationIsValid()
    {
        // Act
        _pipeline = new AnalysisPipeline(_configuration);

        // Assert
        Assert.That(_pipeline, Is.Not.Null);
        Assert.That(_pipeline!.Provider, Is.Not.Null, "ServiceProvider should be initialized.");
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenPipelineSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove Pipeline section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("Pipeline")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("Pipeline settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenVideoLoadersSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove VideoLoaders section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("VideoLoaders")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("VideoLoader settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenInputFrameBufferSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove InputFrameBuffer section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("InputFrameBuffer")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("InputFrameBuffer settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenDetectorSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove Detector section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("Detector")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("Detector settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenOutputFrameBufferSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove OutputFrameBuffer section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("OutputFrameBuffer")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("OutputFrameBuffer settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenAnnotationRenderSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove AnnotationRender section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("AnnotationRender")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("AnnotationRender settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenMessagePosterSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove MessagePoster section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("MessagePoster")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("MessagePoster settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenEventRepositorySectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove EventRepository section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("EventRepository")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("EventRepository settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenAnnotationSenderSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove AnnotationSender section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("AnnotationSender")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("AnnotationSender settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenRegionManagerSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove RegionManager section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("RegionManager")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("RegionManager settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenTrackerSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove Tracker section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("Tracker")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("Tracker settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenSnapshotSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove Snapshot section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("Snapshot")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("Snapshot settings corrupted"));
    }

    [Test]
    public void Constructor_ShouldThrowInvalidDataException_WhenAlgorithmsSectionIsMissing()
    {
        // Arrange
        var configDict = GetValidConfigurationDictionary();
        // Remove Algorithms section
        var keysToRemove = configDict.Keys.Where(k => k.StartsWith("Algorithms")).ToList();
        foreach (var key in keysToRemove)
        {
            configDict.Remove(key);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => new AnalysisPipeline(config));
        Assert.That(ex!.Message, Does.Contain("Algorithm settings corrupted"));
    }

    private Dictionary<string, string?> GetValidConfigurationDictionary()
    {
        return new Dictionary<string, string?>
        {
            // Pipeline
            {"Pipeline:FrameLifetime", "200"},
            {"Pipeline:EnableDebugDisplay", "true"},
            {"Pipeline:EnableDetectionRegionDisplay", "true"},
            {"Pipeline:EnableAnnotationServer", "true"},
            {"Pipeline:AnnotationServerPrefix", "/perceptron"},
            {"Pipeline:EnableAnnotationUdp", "true"},
            {"Pipeline:AnnotationUdpHost", "192.168.1.100"},
            {"Pipeline:AnnotationUdpPort", "9090"},
            {"Pipeline:RealtimeDisplayWidth", "1280"},
            {"Pipeline:RealtimeDisplayTitle", "Perceptron Realtime Display"},

            // VideoLoaders (List) - Item 0
            {"VideoLoaders:0:AssemblyFile", "MediaLoader.OpenCV.dll"},
            {"VideoLoaders:0:FullQualifiedClassName", "MediaLoader.OpenCV.VideoLoader"},
            {"VideoLoaders:0:Preferences:SourceId", "Suzhou-Cam-001"},
            {"VideoLoaders:0:Preferences:VideoUri", "D:\\Video\\Ship\\suzhou.ts"},
            {"VideoLoaders:0:Preferences:VideoCaptureAPI", "FFMPEG"},
            {"VideoLoaders:0:Preferences:VideoAccelerationType", "None"},
            {"VideoLoaders:0:Preferences:VideoAccelerationDeviceId", "0"},
            {"VideoLoaders:0:Preferences:VideoStride", "1"},
            {"VideoLoaders:0:Preferences:MaxRetries", "5"},
            {"VideoLoaders:0:Preferences:RetryDelayMs", "500"},
            {"VideoLoaders:0:Preferences:Loop", "false"},

            // VideoLoaders (List) - Item 1
            {"VideoLoaders:1:AssemblyFile", "MediaLoader.OpenCV.dll"},
            {"VideoLoaders:1:FullQualifiedClassName", "MediaLoader.OpenCV.VideoLoader"},
            {"VideoLoaders:1:Preferences:SourceId", "Sea-Cam-002"},
            {"VideoLoaders:1:Preferences:VideoUri", "D:\\Video\\Ship\\test.mp4"},
            {"VideoLoaders:1:Preferences:VideoCaptureAPI", "FFMPEG"},
            {"VideoLoaders:1:Preferences:VideoAccelerationType", "D3D11"},
            {"VideoLoaders:1:Preferences:VideoAccelerationDeviceId", "0"},
            {"VideoLoaders:1:Preferences:VideoStride", "1"},
            {"VideoLoaders:1:Preferences:MaxRetries", "3"},
            {"VideoLoaders:1:Preferences:RetryDelayMs", "1000"},
            {"VideoLoaders:1:Preferences:Loop", "false"},

            // InputFrameBuffer
            {"InputFrameBuffer:AssemblyFile", "FrameBuffer.TwoModes.dll"},
            {"InputFrameBuffer:FullQualifiedClassName", "FrameBuffer.TwoModes.VideoFrameBuffer"},
            {"InputFrameBuffer:Preferences:BufferName", "InputFrameBuffer"},
            {"InputFrameBuffer:Preferences:BufferSize", "100"},
            {"InputFrameBuffer:Preferences:Mode", "BlockingWait"},

            // OutputFrameBuffer
            {"OutputFrameBuffer:AssemblyFile", "FrameBuffer.TwoModes.dll"},
            {"OutputFrameBuffer:FullQualifiedClassName", "FrameBuffer.TwoModes.VideoFrameBuffer"},
            {"OutputFrameBuffer:Preferences:BufferName", "OutputFrameBuffer"},
            {"OutputFrameBuffer:Preferences:BufferSize", "100"},
            {"OutputFrameBuffer:Preferences:Mode", "BlockingWait"},

            // Detector
            {"Detector:AssemblyFile", "Detector.YoloDotNet.dll"},
            {"Detector:FullQualifiedClassName", "Detector.YoloDotNet.YoloDetector"},
            {"Detector:Preferences:ModelPath", "Models/yolo11m.onnx"},
            {"Detector:Preferences:ModelConfig", "Models/yolo11.json"},
            {"Detector:Preferences:ExecutionProvider", "cuda"},
            {"Detector:Preferences:DeviceId", "0"},
            {"Detector:Preferences:ClassNum", "80"},
            {"Detector:Preferences:ConfThresh", "0.5"},
            {"Detector:Preferences:TargetTypes", "person,boat"},
            {"Detector:Preferences:DetectionStride", "2"},
            {"Detector:Preferences:FilterSmallObject", "false"},
            {"Detector:Preferences:MinBboxWidth", "80"},
            {"Detector:Preferences:MinBboxHeight", "50"},
            {"Detector:Preferences:FilterLargeObject", "false"},
            {"Detector:Preferences:MaxBboxWidth", "500"},
            {"Detector:Preferences:MaxBboxHeight", "400"},
            {"Detector:Preferences:TileDetectionEnabled", "false"},
            {"Detector:Preferences:TileDetectionSize", "1, 2"},
            {"Detector:Preferences:MaxStitchGapPixel", "3"},
            {"Detector:Preferences:MinVerticalOverlapRatio", "0.8"},
            {"Detector:Preferences:WillSuppressInnerSameObject", "true"},
            {"Detector:Preferences:InnerObjectOverlapRatio", "0.7"},
            {"Detector:Preferences:WillMapObjectTypes", "false"},
            {"Detector:Preferences:SourceObjectTypeNames", "truck,bus"},
            {"Detector:Preferences:DestinationObjectTypeName", "car"},
            {"Detector:Preferences:Names", "Alice,Bob,Charlie,David,Eve"},

            // RegionManager (List) - Item 0
            {"RegionManager:0:AssemblyFile", "RegionManager.DefinitionBased.dll"},
            {"RegionManager:0:FullQualifiedClassName", "RegionManager.DefinitionBased.RegionManager"},
            {"RegionManager:0:Preferences:SourceId", "Suzhou-Cam-001"},
            {"RegionManager:0:Preferences:RegionDefinitionFile", "test-region.json"},

            // Tracker
            {"Tracker:AssemblyFile", "Tracker.Sort.dll"},
            {"Tracker:FullQualifiedClassName", "Tracker.Sort.SortTracker"},
            {"Tracker:Preferences:IouThreshold", "0.1"},
            {"Tracker:Preferences:MaxMisses", "30"},

            // Snapshot
            {"Snapshot:AssemblyFile", "SnapshotManager.InMemory.dll"},
            {"Snapshot:FullQualifiedClassName", "SnapshotManager.InMemory.SnapshotManager"},
            {"Snapshot:Preferences:SnapshotsDir", "Snapshots"},
            {"Snapshot:Preferences:SaveBestSnapshot", "true"},
            {"Snapshot:Preferences:BestSnapshotBy", "confidence"},
            {"Snapshot:Preferences:MaxSnapshots", "10"},
            {"Snapshot:Preferences:MinSnapshotWidth", "10"},
            {"Snapshot:Preferences:MinSnapshotHeight", "10"},
            {"Snapshot:Preferences:SnapshotRetentionDays", "3"},
            {"Snapshot:Preferences:VideoClipDurationSeconds", "4"},
            {"Snapshot:Preferences:VideoFrameRate", "25"},
            {"Snapshot:Preferences:SnapshotExpansionRatio", "1.2"},

            // MessagePoster
            {"MessagePoster:AssemblyFile", "MessagePoster.RestfulJson.dll"},
            {"MessagePoster:FullQualifiedClassName", "MessagePoster.RestfulJson.MessagePoster"},
            {"MessagePoster:Preferences:WillPostMessage", "true"},
            {"MessagePoster:Preferences:DestinationUrl", "http://127.0.0.1:5000/pipeline_event"},
            {"MessagePoster:Preferences:CheckDuplicateEvent", "true"},
            {"MessagePoster:Preferences:EventSuppressionIntervals:Suzhou-Cam-001_船舶处于警戒区", "30"},

            // EventRepository
            {"EventRepository:AssemblyFile", "Repository.MinioMySQL.dll"},
            {"EventRepository:FullQualifiedClassName", "Repository.MinioMySQL.EventRepository"},
            {"EventRepository:Preferences:RdbConnectionString", "server=127.0.0.1;port=3306;uid=root;pwd=cs202304;database=perceptron"},
            {"EventRepository:Preferences:StorageUrl", "127.0.0.1:9000"},
            {"EventRepository:Preferences:StorageUsername", "admin"},
            {"EventRepository:Preferences:StoragePassword", "cs202304"},
            {"EventRepository:Preferences:WillStoreSnapshot", "true"},
            {"EventRepository:Preferences:WillStoreVideoClip", "false"},

            // AnnotationSender
            {"AnnotationSender:AssemblyFile", "AnnotationSender.Udp.dll"},
            {"AnnotationSender:FullQualifiedClassName", "AnnotationSender.Udp.AnnotationUdpSender"},
            {"AnnotationSender:Preferences:EnableAnnotationUdpSender", "true"},
            {"AnnotationSender:Preferences:AnnotationUdpDestinationHost", "127.0.0.1"},
            {"AnnotationSender:Preferences:AnnotationUdpDestinationPort", "9999"},

            // AnnotationRender
            {"AnnotationRender:AssemblyFile", "AnnotationRender.OpenCV.dll"},
            {"AnnotationRender:FullQualifiedClassName", "AnnotationRender.OpenCV.Render"},
            {"AnnotationRender:Preferences:DefaultStyleFile", "default-style.json"},

            // Algorithms (List) - Item 0
            {"Algorithms:0:AssemblyFile", "Algorithm.GenerateDebugAnnotations.dll"},
            {"Algorithms:0:FullQualifiedClassName", "Algorithm.GenerateDebugAnnotations.Executor"},
            {"Algorithms:0:Preferences:GenerateBBox", "true"},
            {"Algorithms:0:Preferences:BBoxStrokeColor", "#8fce00"},
            {"Algorithms:0:Preferences:BBoxStrokeWidth", "2"},
            {"Algorithms:0:Preferences:GenerateObjText", "true"},
            {"Algorithms:0:Preferences:ObjTextColor", "#ffff00"},
            {"Algorithms:0:Preferences:ObjTextFontSize", "30"},
            {"Algorithms:0:Preferences:ObjTextShowLabel", "true"},
            {"Algorithms:0:Preferences:ObjTextShowTrackingId", "true"},
            {"Algorithms:0:Preferences:ObjTextShowConfidence", "true"},
            {"Algorithms:0:Preferences:GenerateAnalysisAreas", "false"},
            {"Algorithms:0:Preferences:AnalysisAreaStrokeColor", "#7dda58"},
            {"Algorithms:0:Preferences:GenerateExcludeAreas", "true"},
            {"Algorithms:0:Preferences:ExcludeAreaStrokeColor", "#e36667"},
            {"Algorithms:0:Preferences:GenerateLanes", "true"},
            {"Algorithms:0:Preferences:LanesStrokeColor", "#e8e8e8"},
            {"Algorithms:0:Preferences:GenerateInterestAreas", "true"},
            {"Algorithms:0:Preferences:InterestAreasStrokeColor", "#ffeca1"},
            {"Algorithms:0:Preferences:GenerateCountLines", "true"},
            {"Algorithms:0:Preferences:EnterLineStrokeColor", "#4e4e4e"},
            {"Algorithms:0:Preferences:LeaveLineStrokeColor", "#4e4e4e"}
        };
    }
}
