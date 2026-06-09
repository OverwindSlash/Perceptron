using Algorithm.Common.Event;
using Algorithm.Common.LLM;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;

namespace Algorithm.Common.Tests;

public class ShipLabelsByLlmRoutingTests
{
    [Test]
    public void NonTargetResult_DoesNotDisposeSnapshot()
    {
        using var fixture = CreateFixture();
        using var snapshot = CreateScene();
        var result = CreateResult("other", "boat_7", "request", snapshot);

        fixture.Publisher.Publish(result);

        Assert.That(snapshot.IsDisposed, Is.False);
    }

    [Test]
    public void DuplicateResult_DisposesDuplicateSnapshotAndKeepsVerifiedSnapshot()
    {
        using var fixture = CreateFixture();
        using var frame = CreateFrame(1);
        frame.Retain();
        fixture.Algorithm.Analyze(frame);
        var detectedObject = frame.DetectedObjects.Single();
        var requestId =
            detectedObject.GetProperty<string>(LLMPropertyNames.RequestId) ??
            throw new AssertionException("LLM request ID was not created.");
        var objectId = detectedObject.Id;
        var verifiedSnapshot = CreateScene();
        var duplicateSnapshot = CreateScene();

        fixture.Publisher.Publish(
            CreateResult(
                fixture.Algorithm.AlgorithmName,
                objectId,
                requestId,
                verifiedSnapshot));
        fixture.Publisher.Publish(
            CreateResult(
                fixture.Algorithm.AlgorithmName,
                objectId,
                requestId,
                duplicateSnapshot));

        Assert.Multiple(() =>
        {
            Assert.That(verifiedSnapshot.IsDisposed, Is.False);
            Assert.That(duplicateSnapshot.IsDisposed, Is.True);
        });

        fixture.Algorithm.Dispose();
        Assert.That(verifiedSnapshot.IsDisposed, Is.True);
    }

    [Test]
    public void ErrorResult_ClearsPendingRequestSoNextFrameCanRetry()
    {
        using var fixture = CreateFixture();
        using var firstFrame = CreateFrame(1);
        firstFrame.Retain();
        fixture.Algorithm.Analyze(firstFrame);
        var firstObject = firstFrame.DetectedObjects.Single();
        var firstRequestId =
            firstObject.GetProperty<string>(LLMPropertyNames.RequestId) ??
            throw new AssertionException("LLM request ID was not created.");
        using var errorSnapshot = CreateScene();
        var errorResult = CreateResult(
            fixture.Algorithm.AlgorithmName,
            firstObject.Id,
            firstRequestId,
            errorSnapshot);
        errorResult.JsonResult = string.Empty;
        errorResult.IsSuccess = false;

        fixture.Publisher.Publish(errorResult);

        using var secondFrame = CreateFrame(2);
        secondFrame.Retain();
        fixture.Algorithm.Analyze(secondFrame);
        var secondRequestId = secondFrame.DetectedObjects
            .Single()
            .GetProperty<string>(LLMPropertyNames.RequestId);

        Assert.Multiple(() =>
        {
            Assert.That(errorSnapshot.IsDisposed, Is.True);
            Assert.That(secondRequestId, Is.Not.Empty);
            Assert.That(secondRequestId, Is.Not.EqualTo(firstRequestId));
        });
    }

    [Test]
    public void InvalidJsonResult_DisposesTargetSnapshot()
    {
        using var fixture = CreateFixture();
        using var frame = CreateFrame(1);
        frame.Retain();
        fixture.Algorithm.Analyze(frame);
        var detectedObject = frame.DetectedObjects.Single();
        var requestId =
            detectedObject.GetProperty<string>(LLMPropertyNames.RequestId) ??
            throw new AssertionException("LLM request ID was not created.");
        var snapshot = CreateScene();
        var result = CreateResult(
            fixture.Algorithm.AlgorithmName,
            detectedObject.Id,
            requestId,
            snapshot);
        result.JsonResult = "not-json";

        fixture.Publisher.Publish(result);

        Assert.That(snapshot.IsDisposed, Is.True);
    }

    private static Fixture CreateFixture()
    {
        var promptPath = Path.GetTempFileName();
        File.WriteAllText(promptPath, "prompt");
        var services = new ServiceCollection();
        services.AddMessagePipe();
        var provider = services.BuildServiceProvider();
        var algorithm = new Algorithm.Ship.LabelsByLLM.Executor(
            new AlgorithmRuntimeDependencies(
                provider,
                Array.Empty<Perceptron.Domain.Abstraction.RegionManager.IRegionManager>(),
                new AlgorithmEventDispatcherTests.FakeSnapshotManager(),
                new FakeEventRepository(),
                new FakeMessagePoster()),
            new Dictionary<string, string>
            {
                ["PerformLLMAnalysis"] = "true",
                ["LLMPromptFile"] = promptPath,
                ["WillPublishEventMessage"] = "false"
            });
        algorithm.Initialize();
        return new Fixture(
            promptPath,
            provider,
            algorithm,
            provider.GetRequiredService<IPublisher<LLMInferenceResultEvent>>());
    }

    private static Frame CreateFrame(long frameId)
    {
        var frame = new Frame("source", frameId, 0, CreateScene());
        frame.DetectedObjects =
        [
            new DetectedObject(
                frame.SourceId,
                frame.FrameId,
                frame.UtcTimeStamp,
                1,
                "boat",
                0.9f,
                BoundingBox.CreateFromRect(1, 1, 6, 6),
                7)
            {
                IsUnderAnalysis = true
            }
        ];
        return frame;
    }

    private static Mat CreateScene() =>
        new(10, 10, MatType.CV_8UC3, Scalar.White);

    private static LLMInferenceResultEvent CreateResult(
        string requester,
        string objectId,
        string requestId,
        Mat snapshot)
    {
        return new LLMInferenceResultEvent(
            "source",
            LLMInferenceResultEvent.EventType,
            "ship-label",
            requester,
            objectId,
            0.9f,
            "model",
            TimeSpan.FromMilliseconds(10),
            """
            {
              "ShipTypeGroup": "Cargo",
              "ShipTypeDetail": "Container",
              "ShipColor": ["Blue"],
              "ShipDraught": "Medium",
              "ShipViewAngle": "Front",
              "ShipLoadTypes": [],
              "ShipPaintedText": []
            }
            """)
        {
            RequestId = requestId,
            RequesterAlgorithmName = requester,
            DetectedObjectId = objectId,
            Scope = LLMAnalysisScope.Object,
            FrameId = 1,
            UtcTimeStamp = DateTime.UtcNow,
            Snapshot = snapshot
        };
    }

    private sealed record Fixture(
        string PromptPath,
        ServiceProvider Provider,
        Algorithm.Ship.LabelsByLLM.Executor Algorithm,
        IPublisher<LLMInferenceResultEvent> Publisher) : IDisposable
    {
        public void Dispose()
        {
            Algorithm.Dispose();
            Provider.Dispose();
            File.Delete(PromptPath);
        }
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public Task SaveDomainEventAsync(DomainEvent domainEvent) =>
            Task.CompletedTask;

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
