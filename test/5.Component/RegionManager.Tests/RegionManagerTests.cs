using MessagePipe;
using Moq;
using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;

namespace RegionManager.Tests;

[TestFixture]
public class RegionManagerTests
{
    private DefinitionBased.RegionManager _regionManager;
    private const int TestImageWidth = 1920;
    private const int TestImageHeight = 1080;
    private static string TestJsonFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "test-analysis-definition.json");

    [SetUp]
    public void SetUp()
    {
        var pref = new Dictionary<string, string>();
        pref.Add("RegionDefinitionFile", TestJsonFile);

        _regionManager = new DefinitionBased.RegionManager(pref);
    }

    [TearDown]
    public void TearDown()
    {
        _regionManager?.Dispose();
    }

    #region Constructor & Init Tests

    [Test]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act

        // Assert
        Assert.That(_regionManager.RegionDefinitionFile, Is.Not.Null);
        Assert.That(_regionManager.RegionDefinition, Is.Null);
        Assert.That(_regionManager.Initialized, Is.False);
    }

    [Test]
    public void Init_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);

        // Assert
        Assert.That(_regionManager.RegionDefinitionFile, Is.Not.Null);
        Assert.That(_regionManager.RegionDefinition, Is.Not.Null);
        Assert.That(_regionManager.Initialized, Is.True);
    }

    #endregion

    #region InitRegionDefinition Tests

    [Test]
    public void LoadAnalysisDefinition_WithValidJsonFile_ShouldLoadSuccessfully()
    {
        // Arrange
        var jsonFile = TestJsonFile;

        // Act
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);

        // Assert
        Assert.That(_regionManager.RegionDefinition, Is.Not.Null);
        Assert.That(_regionManager.RegionDefinition.Name, Is.EqualTo("Test Analysis Definition"));
        Assert.That(_regionManager.RegionDefinition.IsObjectAnalyzableRetain, Is.True);
        Assert.That(_regionManager.RegionDefinition.AnalysisAreas.Count, Is.EqualTo(1));
        Assert.That(_regionManager.RegionDefinition.ExcludedAreas.Count, Is.EqualTo(1));
        Assert.That(_regionManager.RegionDefinition.Lanes.Count, Is.EqualTo(1));
    }

    [Test]
    public void LoadAnalysisDefinition_WithInvalidJsonFile_ShouldDisableRegionManager()
    {
        // Arrange
        var invalidJsonFile = "non-existent-file.json";
        var pref = new Dictionary<string, string>();
        pref.Add("RegionDefinitionFile", invalidJsonFile);

        var regionManager = new DefinitionBased.RegionManager(pref);

        // Act & Assert
        Assert.That(regionManager.Initialized, Is.False);
    }

    [Test]
    public void LoadAnalysisDefinition_ShouldClearPreviousTrackingIds()
    {
        // Arrange
        var jsonFile = TestJsonFile;
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);
        
        // Simulate adding some tracking IDs by processing a frame
        var frame = CreateTestFrame();
        _regionManager.CalcRegionProperties(frame);

        // Act - Load again
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);

        // Assert - Should not throw and should work correctly
        Assert.DoesNotThrow(() => _regionManager.CalcRegionProperties(frame));
    }

    #endregion

    #region CalcRegionProperties Tests

    [Test]
    public void CalcRegionProperties_WithObjectInAnalysisArea_ShouldSetIsUnderAnalysis()
    {
        // Arrange
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);
        
        var frame = CreateTestFrame();
        // Create object in analysis area (0.1-0.5, 0.1-0.5 normalized)
        var detectedObject = CreateDetectedObject("test", 1, 576, 324); // Center at (0.3, 0.3) normalized
        List<DetectedObject> detectedObjects = new List<DetectedObject>();
        detectedObjects.Add(detectedObject);

        frame.DetectedObjects = detectedObjects;

        // Act
        _regionManager.CalcRegionProperties(frame);

        // Assert
        Assert.That(detectedObject.IsUnderAnalysis, Is.True);
    }

    [Test]
    public void CalcRegionProperties_WithObjectInExcludedArea_ShouldNotSetIsUnderAnalysis()
    {
        // Arrange
        var jsonFile = TestJsonFile;
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);
        
        var frame = CreateTestFrame();
        // Create object in excluded area (0.6-0.8, 0.6-0.8 normalized)
        var detectedObject = CreateDetectedObject("test", 1, 1344, 756); // Center at (0.7, 0.7) normalized
        List<DetectedObject> detectedObjects = new List<DetectedObject>();
        detectedObjects.Add(detectedObject);

        frame.DetectedObjects = detectedObjects;

        // Act
        _regionManager.CalcRegionProperties(frame);

        // Assert
        Assert.That(detectedObject.IsUnderAnalysis, Is.False);
    }

    [Test]
    public void CalcRegionProperties_WithObjectInLane_ShouldSetLaneProperty()
    {
        // Arrange
        var jsonFile = TestJsonFile;
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);
        
        var frame = CreateTestFrame();
        // Create object in lane area (0.2-0.4, 0.2-0.4 normalized) and analysis area
        var detectedObject = CreateDetectedObject("test", 1, 576, 324); // Center at (0.3, 0.3) normalized
        List<DetectedObject> detectedObjects = new List<DetectedObject>();
        detectedObjects.Add(detectedObject);

        frame.DetectedObjects = detectedObjects;

        // Act
        _regionManager.CalcRegionProperties(frame);

        // Assert
        Assert.That(detectedObject.IsUnderAnalysis, Is.True);
        var laneIndex = detectedObject.GetProperty<int>("LaneIndex");
        Assert.That(laneIndex, Is.EqualTo(1));
    }

    [Test]
    public void CalcRegionProperties_WithRetainFlagAndPreviouslyAnalyzed_ShouldRetainAnalysis()
    {
        // Arrange
        var jsonFile = TestJsonFile;
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);
        
        var frame1 = CreateTestFrame();
        var detectedObject1 = CreateDetectedObject("test", 1, 576, 324); // In analysis area
        List<DetectedObject> detectedObjects1 = new List<DetectedObject>();
        detectedObjects1.Add(detectedObject1);

        frame1.DetectedObjects = detectedObjects1;

        // First call to set object under analysis
        _regionManager.CalcRegionProperties(frame1);
        
        var frame2 = CreateTestFrame();
        var detectedObject2 = CreateDetectedObject("test", 1, 1000, 1000); // Outside analysis area
        List<DetectedObject> detectedObjects2 = new List<DetectedObject>();
        detectedObjects2.Add(detectedObject2);

        frame2.DetectedObjects = detectedObjects2;

        // Act
        _regionManager.CalcRegionProperties(frame2);

        // Assert - Should retain analysis due to IsObjectAnalyzableRetain = true
        Assert.That(detectedObject2.IsUnderAnalysis, Is.True);
    }

    [Test]
    public void CalcRegionProperties_WithEmptyFrame_ShouldNotThrow()
    {
        // Arrange
        var jsonFile = TestJsonFile;
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);
        
        var frame = CreateTestFrame(); // Empty frame

        // Act & Assert
        Assert.DoesNotThrow(() => _regionManager.CalcRegionProperties(frame));
    }

    [Test]
    public void CalcRegionProperties_WithoutLoadedDefinition_DoNothing()
    {
        // Arrange
        var frame = CreateTestFrame();
        var detectedObject = CreateDetectedObject("test", 1, 100, 100);
        List<DetectedObject> detectedObjects = new List<DetectedObject>();
        detectedObjects.Add(detectedObject);

        frame.DetectedObjects = detectedObjects;

        // Act & Assert
        _regionManager.CalcRegionProperties(frame);
    }

    #endregion

    #region Event Handling Tests

    [Test]
    public void SetSubscriber_ShouldNotThrow()
    {
        // Arrange
        var mockSubscriber = new Mock<ISubscriber<ObjectExpiredEvent>>();

        // Act & Assert
        Assert.DoesNotThrow(() => _regionManager.SetSubscriber(mockSubscriber.Object));
    }

    [Test]
    public void ProcessEvent_ShouldHandleObjectExpiredEvent()
    {
        // Arrange
        var jsonFile = TestJsonFile;
        _regionManager.InitRegionDefinition(TestImageWidth, TestImageHeight);
        
        // Add an object to tracking
        var frame = CreateTestFrame();
        var detectedObject = CreateDetectedObject("test", 1, 576, 324);
        List<DetectedObject> detectedObjects = new List<DetectedObject>();
        detectedObjects.Add(detectedObject);

        frame.DetectedObjects = detectedObjects;

        _regionManager.CalcRegionProperties(frame);
        
        var expiredEvent = new ObjectExpiredEvent(detectedObject);

        // Act & Assert
        Assert.DoesNotThrow(() => _regionManager.ProcessEvent(expiredEvent));
        
        // Wait a bit for the async task to complete
        Thread.Sleep(100);
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_ShouldDisposeCorrectly()
    {
        // Arrange
        var regionManager = new DefinitionBased.RegionManager();

        // Act & Assert
        Assert.DoesNotThrow(() => regionManager.Dispose());
        
        // Multiple dispose calls should not throw
        Assert.DoesNotThrow(() => regionManager.Dispose());
    }

    #endregion

    #region Helper Methods

    private Frame CreateTestFrame()
    {
        var mat = new Mat(TestImageHeight, TestImageWidth, MatType.CV_8UC3);
        return new Frame("test-source", 1, 0, mat);
    }

    private DetectedObject CreateDetectedObject(string sourceId, long frameId, int centerX, int centerY)
    {
        var bbox = BoundingBox.CreateFromRect(centerX - 25, centerY - 25, 50, 50);
        return new DetectedObject(
            sourceId: sourceId,
            frameId: frameId,
            utcTimeStamp: DateTime.UtcNow,
            labelId: 1,
            confidence: 0.9f,
            bbox: bbox,
            label: "person"
        )
        {
            TrackingId = 1
        };
    }

    #endregion
}