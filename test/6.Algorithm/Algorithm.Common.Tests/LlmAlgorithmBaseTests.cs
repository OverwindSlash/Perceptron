using Algorithm.Common.Event;
using Algorithm.Common.LLM;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;

namespace Algorithm.Common.Tests;

public class LlmAlgorithmBaseTests
{
    [Test]
    public void Initialize_WhenDisabled_DoesNotReadPromptOrSubscribe()
    {
        var subscriber = new RecordingSubscriber<LLMInferenceResultEvent>();
        var algorithm = new TestLlmAlgorithm(
            CreateDependencies(subscriber),
            new Dictionary<string, string>
            {
                ["PerformLLMAnalysis"] = "false",
                ["LLMPromptFile"] = "missing.md"
            });

        Assert.DoesNotThrow(() => algorithm.Initialize());
        Assert.That(subscriber.SubscribeCount, Is.Zero);
    }

    [Test]
    public void Initialize_WhenEnabled_LoadsPromptAndSubscribesOnce()
    {
        var promptPath = Path.GetTempFileName();
        File.WriteAllText(promptPath, "prompt-content");
        try
        {
            var subscriber = new RecordingSubscriber<LLMInferenceResultEvent>();
            var algorithm = new TestLlmAlgorithm(
                CreateDependencies(subscriber),
                new Dictionary<string, string>
                {
                    ["PerformLLMAnalysis"] = "true",
                    ["LLMPromptFile"] = promptPath
                });

            algorithm.Initialize();
            algorithm.Initialize();

            Assert.That(algorithm.Prompt, Is.EqualTo("prompt-content"));
            Assert.That(subscriber.SubscribeCount, Is.EqualTo(1));
        }
        finally
        {
            File.Delete(promptPath);
        }
    }

    [Test]
    public void Initialize_WhenPromptIsMissing_ReportsAlgorithmAndAbsolutePath()
    {
        var promptPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}.md");
        var algorithm = new TestLlmAlgorithm(
            CreateDependencies(new RecordingSubscriber<LLMInferenceResultEvent>()),
            new Dictionary<string, string>
            {
                ["PerformLLMAnalysis"] = "true",
                ["LLMPromptFile"] = promptPath
            });

        var exception = Assert.Throws<FileNotFoundException>(
            () => algorithm.Initialize());

        Assert.That(exception!.Message, Does.Contain("test-llm"));
        Assert.That(exception.Message, Does.Contain(Path.GetFullPath(promptPath)));
    }

    [Test]
    public void MarkFrameForLlm_SetsCompleteProtocolAndGeneratesRequestId()
    {
        var algorithm = CreateInitializedAlgorithm();
        using var frame = CreateFrame();
        var deadline = DateTime.UtcNow.AddSeconds(30);

        var requestId = algorithm.MarkFrame(
            frame,
            new LlmRequestOptions
            {
                Scope = LLMAnalysisScope.Frame,
                QueuePolicy = LLMQueuePolicy.EventAnchored,
                CandidateEventId = "candidate",
                ExpireAtUtc = deadline,
                ImageJpeg = [1, 2, 3]
            });

        Assert.That(requestId, Is.Not.Empty);
        Assert.That(frame.GetProperty<bool>(LLMPropertyNames.Analysis), Is.True);
        Assert.That(frame.GetProperty<string>(LLMPropertyNames.AnalysisType), Is.EqualTo("frame"));
        Assert.That(frame.GetProperty<string>(LLMPropertyNames.AnalysisPrompt), Is.EqualTo("prompt"));
        Assert.That(frame.GetProperty<byte[]>(LLMPropertyNames.AnalysisImageJpeg), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(frame.GetProperty<string>(LLMPropertyNames.RequestId), Is.EqualTo(requestId));
        Assert.That(frame.GetProperty<string>(LLMPropertyNames.RequesterAlgorithmName), Is.EqualTo("test-llm"));
        Assert.That(frame.GetProperty<string>(LLMPropertyNames.CandidateEventId), Is.EqualTo("candidate"));
        Assert.That(frame.GetProperty<string>(LLMPropertyNames.QueuePolicy), Is.EqualTo(LLMQueuePolicy.EventAnchored.ToString()));
        Assert.That(frame.GetProperty<DateTime?>(LLMPropertyNames.ExpireAtUtc), Is.EqualTo(deadline));
    }

    [Test]
    public void MarkObjectForLlm_SetsFrameAndObjectProtocol()
    {
        var algorithm = CreateInitializedAlgorithm();
        using var frame = CreateFrame();
        var detectedObject = CreateObject(frame);
        frame.DetectedObjects = [detectedObject];

        var requestId = algorithm.MarkObject(
            frame,
            detectedObject,
            new LlmRequestOptions
            {
                Scope = LLMAnalysisScope.Object,
                QueuePolicy = LLMQueuePolicy.LatestBestPerObject
            });

        Assert.That(frame.GetProperty<string>(LLMPropertyNames.AnalysisType), Is.EqualTo("object"));
        Assert.That(detectedObject.GetProperty<bool>(LLMPropertyNames.Analysis), Is.True);
        Assert.That(detectedObject.GetProperty<string>(LLMPropertyNames.RequestId), Is.EqualTo(requestId));
        Assert.That(detectedObject.GetProperty<string>(LLMPropertyNames.RequesterAlgorithmName), Is.EqualTo("test-llm"));
        Assert.That(detectedObject.GetProperty<string>(LLMPropertyNames.QueuePolicy), Is.EqualTo(LLMQueuePolicy.LatestBestPerObject.ToString()));
    }

    [Test]
    public void MarkFrameForLlm_PreservesProvidedRequestIdAndUtcDeadline()
    {
        var algorithm = CreateInitializedAlgorithm();
        using var frame = CreateFrame();
        var deadline = new DateTime(2026, 6, 9, 12, 30, 0, DateTimeKind.Utc);

        var requestId = algorithm.MarkFrame(
            frame,
            new LlmRequestOptions
            {
                RequestId = "provided-request",
                Scope = LLMAnalysisScope.Frame,
                QueuePolicy = LLMQueuePolicy.EventAnchored,
                ExpireAtUtc = deadline
            });

        Assert.That(requestId, Is.EqualTo("provided-request"));
        Assert.That(
            frame.GetProperty<DateTime?>(LLMPropertyNames.ExpireAtUtc),
            Is.EqualTo(deadline));
    }

    [Test]
    public void RouteLlmResult_ForOtherRequester_DoesNotHandleOrDisposeSnapshot()
    {
        var subscriber = new RecordingSubscriber<LLMInferenceResultEvent>();
        var algorithm = CreateInitializedAlgorithm(subscriber);
        using var snapshot = new Mat(4, 4, MatType.CV_8UC3, Scalar.White);
        var result = CreateResultEvent("other");
        result.Snapshot = snapshot;

        subscriber.Publish(result);

        Assert.That(algorithm.HandledCount, Is.Zero);
        Assert.That(snapshot.IsDisposed, Is.False);
    }

    [Test]
    public void RouteLlmResult_ForMatchingRequester_HandlesOnce()
    {
        var subscriber = new RecordingSubscriber<LLMInferenceResultEvent>();
        var algorithm = CreateInitializedAlgorithm(subscriber);

        subscriber.Publish(CreateResultEvent("test-llm"));

        Assert.That(algorithm.HandledCount, Is.EqualTo(1));
    }

    [Test]
    public void RouteLlmResult_AllowsDerivedScopeFiltering()
    {
        var subscriber = new RecordingSubscriber<LLMInferenceResultEvent>();
        var algorithm = CreateInitializedAlgorithm(subscriber);
        algorithm.AcceptedScope = LLMAnalysisScope.Object;
        var frameResult = CreateResultEvent("test-llm");
        frameResult.Scope = LLMAnalysisScope.Frame;
        var objectResult = CreateResultEvent("test-llm");
        objectResult.Scope = LLMAnalysisScope.Object;

        subscriber.Publish(frameResult);
        subscriber.Publish(objectResult);

        Assert.That(algorithm.HandledCount, Is.EqualTo(1));
    }

    private static TestLlmAlgorithm CreateInitializedAlgorithm(
        RecordingSubscriber<LLMInferenceResultEvent>? subscriber = null)
    {
        var promptPath = Path.GetTempFileName();
        File.WriteAllText(promptPath, "prompt");
        var algorithm = new TestLlmAlgorithm(
            CreateDependencies(subscriber ?? new RecordingSubscriber<LLMInferenceResultEvent>()),
            new Dictionary<string, string>
            {
                ["PerformLLMAnalysis"] = "true",
                ["LLMPromptFile"] = promptPath
            });
        algorithm.Initialize();
        File.Delete(promptPath);
        return algorithm;
    }

    private static AlgorithmRuntimeDependencies CreateDependencies(
        RecordingSubscriber<LLMInferenceResultEvent> subscriber)
    {
        var services = new ServiceCollection()
            .AddSingleton<ISubscriber<LLMInferenceResultEvent>>(subscriber)
            .BuildServiceProvider();
        return new AlgorithmRuntimeDependencies(
            services,
            Array.Empty<IRegionManager>(),
            new FakeSnapshotManager(),
            new FakeEventRepository(),
            new FakeMessagePoster());
    }

    private static Frame CreateFrame()
    {
        return new Frame(
            "source",
            1,
            0,
            new Mat(8, 8, MatType.CV_8UC3, Scalar.Black));
    }

    private static DetectedObject CreateObject(Frame frame)
    {
        return new DetectedObject(
            frame.SourceId,
            frame.FrameId,
            frame.UtcTimeStamp,
            1,
            "boat",
            0.9f,
            BoundingBox.CreateFromRect(1, 1, 4, 4),
            7);
    }

    private static LLMInferenceResultEvent CreateResultEvent(string requester)
    {
        return new LLMInferenceResultEvent(
            "source",
            LLMInferenceResultEvent.EventType,
            "event",
            requester,
            string.Empty,
            0,
            "model",
            TimeSpan.Zero,
            "{}")
        {
            RequestId = Guid.NewGuid().ToString("N"),
            RequesterAlgorithmName = requester
        };
    }

    private sealed class TestLlmAlgorithm(
        AlgorithmRuntimeDependencies dependencies,
        Dictionary<string, string> preferences)
        : LlmAlgorithmBase(dependencies, preferences)
    {
        public int HandledCount { get; private set; }
        public string Prompt => UserPrompt;
        public LLMAnalysisScope? AcceptedScope { get; set; }

        protected override void ConfigureDefaultPreferences()
        {
            AlgorithmName = "test-llm";
        }

        protected override AnalysisResult AnalyzeCore(Frame frame) => new(true);

        protected override bool CanHandleLlmResult(LLMInferenceResultEvent result)
        {
            return base.CanHandleLlmResult(result) &&
                   (AcceptedScope == null || result.Scope == AcceptedScope);
        }

        protected override void HandleLlmResult(LLMInferenceResultEvent result)
        {
            HandledCount++;
        }

        public string MarkFrame(Frame frame, LlmRequestOptions options) =>
            MarkFrameForLlm(frame, options);

        public string MarkObject(
            Frame frame,
            DetectedObject detectedObject,
            LlmRequestOptions options) =>
            MarkObjectForLlm(frame, detectedObject, options);
    }

    private sealed class RecordingSubscriber<T> : ISubscriber<T>
    {
        private readonly List<IMessageHandler<T>> _handlers = [];
        public int SubscribeCount { get; private set; }

        public IDisposable Subscribe(
            IMessageHandler<T> handler,
            params MessageHandlerFilter<T>[] filters)
        {
            SubscribeCount++;
            _handlers.Add(handler);
            return new CallbackDisposable(() => _handlers.Remove(handler));
        }

        public void Publish(T message)
        {
            foreach (var handler in _handlers.ToArray())
            {
                handler.Handle(message);
            }
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
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

    private sealed class FakeSnapshotManager
        : AlgorithmEventDispatcherTests.FakeSnapshotManager
    {
    }
}
