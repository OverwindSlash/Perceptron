using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;

namespace Algorithm.Common.Tests;

public class SequenceToImageTargetObjectTests
{
    [Test]
    public void Analyze_WhenTargetObjectIsMissing_DoesNotBuildSequenceImage()
    {
        using var algorithm = CreateAlgorithm(
            new Dictionary<string, string>
            {
                ["SequenceLength"] = "1",
                ["TargetObjectNames"] = "person"
            });
        var frame = CreateFrame(1, "boat");

        var result = algorithm.Analyze(frame);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(frame.HasProperty("SequenceImage"), Is.False);
        });
    }

    [Test]
    public void Analyze_WhenAnyConfiguredTargetMatches_BuildsSequenceImage()
    {
        using var algorithm = CreateAlgorithm(
            new Dictionary<string, string>
            {
                ["SequenceLength"] = "1",
                ["TargetObjectNames"] = "person, car"
            });
        var frame = CreateFrame(1, "PERSON");

        var result = algorithm.Analyze(frame);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(frame.GetProperty<bool>("SequenceImage"), Is.True);
        });
    }

    [Test]
    public void Analyze_WhenTargetObjectNamesAreNotConfigured_PreservesExistingBehavior()
    {
        using var algorithm = CreateAlgorithm(
            new Dictionary<string, string>
            {
                ["SequenceLength"] = "1"
            });
        var frame = CreateFrame(1);

        var result = algorithm.Analyze(frame);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(frame.GetProperty<bool>("SequenceImage"), Is.True);
        });
    }

    [Test]
    public void Analyze_FilteredFramesDoNotCountTowardSequenceLength()
    {
        using var algorithm = CreateAlgorithm(
            new Dictionary<string, string>
            {
                ["SequenceLength"] = "2",
                ["TargetObjectNames"] = "person"
            });
        var filteredFrame = CreateFrame(1, "boat");
        var firstTargetFrame = CreateFrame(2, "person");
        var secondTargetFrame = CreateFrame(3, "person");

        algorithm.Analyze(filteredFrame);
        algorithm.Analyze(firstTargetFrame);
        algorithm.Analyze(secondTargetFrame);

        Assert.Multiple(() =>
        {
            Assert.That(filteredFrame.HasProperty("SequenceImage"), Is.False);
            Assert.That(firstTargetFrame.HasProperty("SequenceImage"), Is.False);
            Assert.That(secondTargetFrame.GetProperty<bool>("SequenceImage"), Is.True);
        });
    }

    private static Algorithm.General.SequenceToImage.Executor CreateAlgorithm(
        Dictionary<string, string> preferences)
    {
        preferences["PerformLLMAnalysis"] = "false";
        preferences["DrawFrameLabels"] = "false";
        preferences["WillPublishEventMessage"] = "false";

        var algorithm = new Algorithm.General.SequenceToImage.Executor(
            new AlgorithmRuntimeDependencies(
                new ServiceCollection().BuildServiceProvider(),
                Array.Empty<IRegionManager>(),
                new AlgorithmEventDispatcherTests.FakeSnapshotManager(),
                new FakeEventRepository(),
                new FakeMessagePoster()),
            preferences);
        algorithm.Initialize();
        return algorithm;
    }

    private static Frame CreateFrame(long frameId, string? label = null)
    {
        var frame = new Frame(
            "source",
            frameId,
            frameId * 40,
            new Mat(10, 10, MatType.CV_8UC3, Scalar.White));
        if (label != null)
        {
            frame.DetectedObjects =
            [
                new DetectedObject(
                    frame.SourceId,
                    frame.FrameId,
                    frame.UtcTimeStamp,
                    1,
                    label,
                    0.9f,
                    BoundingBox.CreateFromRect(1, 1, 6, 6),
                    7)
            ];
        }

        return frame;
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public Task SaveDomainEventAsync(DomainEvent domainEvent) => Task.CompletedTask;

        public Task<DomainEvent> LoadDomainEventAsync(string eventId) =>
            throw new NotSupportedException();

        public Task DeleteDomainEventAsync(string eventId) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMessagePoster : IMessagePoster
    {
        public void PostDomainEventMessage(DomainEvent @event)
        {
        }
    }
}
