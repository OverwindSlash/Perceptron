using Microsoft.Extensions.Configuration;
using Perceptron.Domain.Event.Pipeline;
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
            {"VideoLoaders:0:Preferences:VideoUri", "D:\\Video\\Ship\\suzhou.mp4"},
            {"VideoLoaders:0:Preferences:VideoCaptureAPI", "FFMPEG"},
            {"VideoLoaders:0:Preferences:VideoAccelerationType", "None"},
            {"VideoLoaders:0:Preferences:VideoAccelerationDeviceId", "0"},
            {"VideoLoaders:0:Preferences:VideoStride", "1"},
            {"VideoLoaders:0:Preferences:MaxRetries", "5"},
            {"VideoLoaders:0:Preferences:RetryDelayMs", "500"},
            {"VideoLoaders:0:Preferences:Loop", "false"},

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
            {"Detector:Preferences:FilterSmallObject", "true"},
            {"Detector:Preferences:MinBboxWidth", "80"},
            {"Detector:Preferences:MinBboxHeight", "50"},
            {"Detector:Preferences:FilterLargeObject", "true"},
            {"Detector:Preferences:MaxBboxWidth", "500"},
            {"Detector:Preferences:MaxBboxHeight", "400"},
            {"Detector:Preferences:RegionDetectionEnabled", "true"},
            {"Detector:Preferences:DetectionRegion", "1452, 656, 1009, 331"},
            {"Detector:Preferences:TileDetectionEnabled", "true"},
            {"Detector:Preferences:TileDetectionSize", "1, 2"},
            {"Detector:Preferences:MaxStitchGapPixel", "3"},
            {"Detector:Preferences:MinVerticalOverlapRatio", "0.8"},
            {"Detector:Preferences:WillSuppressInnerSameObject", "true"},
            {"Detector:Preferences:InnerObjectOverlapRatio", "0.7"},
            {"Detector:Preferences:WillMapObjectTypes", "true"},
            {"Detector:Preferences:SourceObjectTypeNames", "truck,bus"},
            {"Detector:Preferences:DestinationObjectTypeName", "car"},
            {"Detector:Preferences:Names", "Alice,Bob,Charlie,David,Eve"}
        };
    }
}
