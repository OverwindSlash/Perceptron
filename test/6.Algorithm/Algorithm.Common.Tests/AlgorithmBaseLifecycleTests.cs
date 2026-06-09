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

public class AlgorithmBaseLifecycleTests
{
    [Test]
    public void Initialize_ExecutesHooksInTemplateOrderAndOnlyOnce()
    {
        var algorithm = new TestAlgorithm(CreateDependencies());

        Assert.That(algorithm.Initialize(), Is.True);
        Assert.That(algorithm.Initialize(), Is.True);

        Assert.That(algorithm.Calls, Is.EqualTo(new[]
        {
            "defaults",
            "mode:#8fce00",
            "core"
        }));
        Assert.That(algorithm.IsInitialized, Is.True);
    }

    [Test]
    public void Initialize_WhenCoreThrows_DoesNotMarkInitialized()
    {
        var algorithm = new TestAlgorithm(CreateDependencies())
        {
            InitializeException = new InvalidOperationException("expected")
        };

        Assert.Throws<InvalidOperationException>(() => algorithm.Initialize());
        Assert.That(algorithm.IsInitialized, Is.False);
    }

    [Test]
    public void Initialize_CanRetryAfterCoreFailure()
    {
        var algorithm = new FailOnceAlgorithm(CreateDependencies());

        Assert.Throws<InvalidOperationException>(() => algorithm.Initialize());
        Assert.That(algorithm.Initialize(), Is.True);
        Assert.That(algorithm.IsInitialized, Is.True);
        Assert.That(algorithm.InitializeCoreCallCount, Is.EqualTo(2));
    }

    [Test]
    public void Initialize_SynchronousAlgorithmDoesNotReadLlmPrompt()
    {
        var algorithm = new SynchronousTestAlgorithm(
            CreateDependencies(),
            new Dictionary<string, string>
            {
                ["PerformLLMAnalysis"] = "true",
                ["LLMPromptFile"] = "missing-prompt.md"
            });

        Assert.DoesNotThrow(() => algorithm.Initialize());
        Assert.That(algorithm.IsInitialized, Is.True);
    }

    [Test]
    public void Analyze_ThrowsBeforeInitialization()
    {
        var algorithm = new TestAlgorithm(CreateDependencies());
        using var frame = CreateFrame();

        Assert.Throws<InvalidOperationException>(() => algorithm.Analyze(frame));
    }

    [Test]
    public void Analyze_ReleasesFrameAfterSuccessfulCore()
    {
        var algorithm = new TestAlgorithm(CreateDependencies());
        algorithm.Initialize();
        var frame = CreateFrame();

        var result = algorithm.Analyze(frame);

        Assert.That(result.Success, Is.True);
        Assert.That(frame.IsDisposed, Is.True);
    }

    [Test]
    public void Analyze_ReleasesFrameWhenCoreReturnsFailure()
    {
        var algorithm = new TestAlgorithm(CreateDependencies())
        {
            AnalyzeResult = new AnalysisResult(false)
        };
        algorithm.Initialize();
        var frame = CreateFrame();

        var result = algorithm.Analyze(frame);

        Assert.That(result.Success, Is.False);
        Assert.That(frame.IsDisposed, Is.True);
    }

    [Test]
    public void Analyze_ReleasesFrameWhenCoreThrows()
    {
        var algorithm = new TestAlgorithm(CreateDependencies())
        {
            AnalyzeException = new InvalidOperationException("expected")
        };
        algorithm.Initialize();
        var frame = CreateFrame();

        Assert.Throws<InvalidOperationException>(() => algorithm.Analyze(frame));
        Assert.That(frame.IsDisposed, Is.True);
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        var algorithm = new TestAlgorithm(CreateDependencies());
        algorithm.Initialize();

        algorithm.Dispose();
        algorithm.Dispose();

        Assert.That(algorithm.DisposeCoreCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_CleansCommonResourcesWhenCoreThrows()
    {
        var algorithm = new TestAlgorithm(CreateDependencies())
        {
            DisposeException = new InvalidOperationException("expected")
        };
        algorithm.Initialize();
        var subscriptionDisposed = false;
        algorithm.Track(new CallbackDisposable(() => subscriptionDisposed = true));

        Assert.Throws<InvalidOperationException>(algorithm.Dispose);
        Assert.That(subscriptionDisposed, Is.True);
    }

    private static AlgorithmRuntimeDependencies CreateDependencies()
    {
        return new AlgorithmRuntimeDependencies(
            new ServiceCollection().BuildServiceProvider(),
            Array.Empty<IRegionManager>(),
            new FakeSnapshotManager(),
            new FakeEventRepository(),
            new FakeMessagePoster());
    }

    private static Frame CreateFrame()
    {
        return new Frame("source", 1, 0, new Mat(8, 8, MatType.CV_8UC3, Scalar.Black));
    }

    private sealed class TestAlgorithm(AlgorithmRuntimeDependencies dependencies)
        : AlgorithmBase(dependencies, new Dictionary<string, string>())
    {
        public List<string> Calls { get; } = [];
        public Exception? InitializeException { get; init; }
        public Exception? AnalyzeException { get; init; }
        public Exception? DisposeException { get; init; }
        public AnalysisResult AnalyzeResult { get; init; } = new(true);
        public int DisposeCoreCallCount { get; private set; }

        protected override void ConfigureDefaultPreferences() => Calls.Add("defaults");

        protected override void InitializeMode() => Calls.Add($"mode:{BBoxStrokeColor}");

        protected override void InitializeCore()
        {
            Calls.Add("core");
            if (InitializeException != null)
            {
                throw InitializeException;
            }
        }

        protected override AnalysisResult AnalyzeCore(Frame frame)
        {
            if (AnalyzeException != null)
            {
                throw AnalyzeException;
            }

            return AnalyzeResult;
        }

        protected override void DisposeCore()
        {
            DisposeCoreCallCount++;
            if (DisposeException != null)
            {
                throw DisposeException;
            }
        }

        public void Track(IDisposable disposable) => TrackSubscription(disposable);
    }

    private sealed class SynchronousTestAlgorithm(
        AlgorithmRuntimeDependencies dependencies,
        Dictionary<string, string> preferences)
        : AlgorithmBase(dependencies, preferences)
    {
        protected override AnalysisResult AnalyzeCore(Frame frame) => new(true);
    }

    private sealed class FailOnceAlgorithm(
        AlgorithmRuntimeDependencies dependencies)
        : AlgorithmBase(dependencies, new Dictionary<string, string>())
    {
        public int InitializeCoreCallCount { get; private set; }

        protected override void InitializeCore()
        {
            InitializeCoreCallCount++;
            TrackSubscription(new CallbackDisposable(() => { }));
            if (InitializeCoreCallCount == 1)
            {
                throw new InvalidOperationException("expected");
            }
        }

        protected override AnalysisResult AnalyzeCore(Frame frame) => new(true);
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public Task SaveDomainEventAsync(DomainEvent domainEvent) => Task.CompletedTask;
        public Task<DomainEvent> LoadDomainEventAsync(string eventId) => throw new NotSupportedException();
        public Task DeleteDomainEventAsync(string eventId) => throw new NotSupportedException();
    }

    private sealed class FakeMessagePoster : IMessagePoster
    {
        public void PostDomainEventMessage(DomainEvent @event)
        {
        }
    }

    private sealed class FakeSnapshotManager : AlgorithmEventDispatcherTests.FakeSnapshotManager
    {
    }
}
