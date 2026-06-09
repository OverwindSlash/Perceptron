using OpenCvSharp;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Event.SnapshotManager;

namespace Algorithm.Common.Tests;

public class AlgorithmEventDispatcherTests
{
    private string _outputDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _outputDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "AlgorithmEventDispatcherTests",
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
        else if (File.Exists(_outputDirectory))
        {
            File.Delete(_outputDirectory);
        }
    }

    [Test]
    public async Task TryQueue_ClonesBeforeBackgroundWorkAndDisposesAfterSuccess()
    {
        var releaseRepository = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakeEventRepository(async _ => await releaseRepository.Task);
        using var dispatcher = CreateDispatcher(repository);
        using var source = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);
        Mat? ownedSnapshot = null;
        var cloneCount = 0;

        var queued = dispatcher.TryQueue(CreateRequest(
            cloneSnapshot: () =>
            {
                cloneCount++;
                ownedSnapshot = source.Clone();
                return ownedSnapshot;
            }));

        Assert.That(queued, Is.True);
        Assert.That(cloneCount, Is.EqualTo(1));
        Assert.That(ownedSnapshot, Is.Not.Null);
        Assert.That(ownedSnapshot!.IsDisposed, Is.False);

        releaseRepository.SetResult();
        await dispatcher.WhenIdleAsync();

        Assert.That(ownedSnapshot.IsDisposed, Is.True);
    }

    [Test]
    public async Task TryQueue_PublishesInRequiredOrder()
    {
        var calls = new List<string>();
        var repository = new FakeEventRepository(_ =>
        {
            calls.Add("repository");
            return Task.CompletedTask;
        });
        var poster = new FakeMessagePoster(_ => calls.Add("poster"));
        using var dispatcher = CreateDispatcher(repository, poster);

        dispatcher.TryQueue(CreateRequest(
            saveSnapshot: false,
            publishInProcess: _ => calls.Add("message-pipe")));
        await dispatcher.WhenIdleAsync();

        Assert.That(calls, Is.EqualTo(new[] { "repository", "poster", "message-pipe" }));
    }

    [Test]
    public async Task TryQueue_DoesNotCloneWhenSnapshotSavingIsDisabled()
    {
        using var dispatcher = CreateDispatcher();
        var cloneCount = 0;

        dispatcher.TryQueue(CreateRequest(
            saveSnapshot: false,
            cloneSnapshot: () =>
            {
                cloneCount++;
                return new Mat(8, 8, MatType.CV_8UC3);
            }));
        await dispatcher.WhenIdleAsync();

        Assert.That(cloneCount, Is.Zero);
    }

    [Test]
    public async Task TryQueue_ForwardsFrameIdWhenVideoSavingIsEnabled()
    {
        var snapshotManager = new RecordingSnapshotManager();
        using var dispatcher = CreateDispatcher(snapshotManager: snapshotManager);
        var request = CreateRequest(saveSnapshot: false) with
        {
            FrameId = 42,
            SaveVideoClip = true
        };

        dispatcher.TryQueue(request);
        await dispatcher.WhenIdleAsync();

        Assert.That(snapshotManager.CenterFrameId, Is.EqualTo(42));
        Assert.That(snapshotManager.OutputPath, Does.EndWith(".mp4"));
    }

    [Test]
    public async Task TryQueue_DisposesSnapshotWhenPersistenceFails()
    {
        var repository = new FakeEventRepository(_ => throw new InvalidOperationException("expected"));
        using var dispatcher = CreateDispatcher(repository);
        Mat? ownedSnapshot = null;

        dispatcher.TryQueue(CreateRequest(cloneSnapshot: () =>
        {
            ownedSnapshot = new Mat(8, 8, MatType.CV_8UC3);
            return ownedSnapshot;
        }));
        await dispatcher.WhenIdleAsync();

        Assert.That(ownedSnapshot, Is.Not.Null);
        Assert.That(ownedSnapshot!.IsDisposed, Is.True);
    }

    [Test]
    public async Task TryQueue_DisposesSnapshotWhenArtifactWriteFails()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_outputDirectory)!);
        await File.WriteAllTextAsync(_outputDirectory, "blocks directory creation");
        using var dispatcher = CreateDispatcher();
        Mat? ownedSnapshot = null;

        dispatcher.TryQueue(CreateRequest(cloneSnapshot: () =>
        {
            ownedSnapshot = new Mat(8, 8, MatType.CV_8UC3);
            return ownedSnapshot;
        }));
        await dispatcher.WhenIdleAsync();

        Assert.That(ownedSnapshot, Is.Not.Null);
        Assert.That(ownedSnapshot!.IsDisposed, Is.True);
    }

    [Test]
    public async Task TryQueue_DoesNotPublishInProcessWhenPosterFails()
    {
        var poster = new FakeMessagePoster(
            _ => throw new InvalidOperationException("expected"));
        using var dispatcher = CreateDispatcher(poster: poster);
        var published = false;

        dispatcher.TryQueue(CreateRequest(
            saveSnapshot: false,
            publishInProcess: _ => published = true));
        await dispatcher.WhenIdleAsync();

        Assert.That(published, Is.False);
    }

    [Test]
    public async Task TryQueue_UsesUniqueMillisecondSequenceFileNames()
    {
        using var dispatcher = CreateDispatcher();

        dispatcher.TryQueue(CreateRequest(cloneSnapshot: CreateSnapshot));
        dispatcher.TryQueue(CreateRequest(cloneSnapshot: CreateSnapshot));
        await dispatcher.WhenIdleAsync();

        var imageFiles = Directory.GetFiles(_outputDirectory, "*.jpg", SearchOption.AllDirectories);
        Assert.That(imageFiles, Has.Length.EqualTo(2));
        Assert.That(
            imageFiles.Select(Path.GetFileName).Distinct().ToArray(),
            Has.Length.EqualTo(2));
        Assert.That(imageFiles.All(path => Path.GetFileNameWithoutExtension(path).StartsWith("stable_")), Is.True);
        Assert.That(
            imageFiles.All(path =>
                System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(path),
                    "^stable_[0-9]{17}_[0-9]{6}\\.jpg$")),
            Is.True);
    }

    [Test]
    public void Dispose_RejectsNewTasks()
    {
        var dispatcher = CreateDispatcher();
        dispatcher.Dispose();

        var queued = dispatcher.TryQueue(CreateRequest(saveSnapshot: false));

        Assert.That(queued, Is.False);
    }

    [Test]
    public async Task Dispose_WaitsForAcceptedTasks()
    {
        var releaseRepository = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new FakeEventRepository(
            async _ => await releaseRepository.Task);
        var dispatcher = CreateDispatcher(repository);
        dispatcher.TryQueue(CreateRequest(saveSnapshot: false));

        var disposeTask = Task.Run(dispatcher.Dispose);
        await Task.Delay(50);
        Assert.That(disposeTask.IsCompleted, Is.False);

        releaseRepository.SetResult();
        await disposeTask;
    }

    private AlgorithmEventDispatcher CreateDispatcher(
        IEventRepository? repository = null,
        IMessagePoster? poster = null,
        ISnapshotManager? snapshotManager = null)
    {
        return new AlgorithmEventDispatcher(
            repository ?? new FakeEventRepository(),
            poster ?? new FakeMessagePoster(),
            snapshotManager ?? new FakeSnapshotManager(),
            _outputDirectory,
            TimeSpan.FromSeconds(2));
    }

    private static EventPublicationRequest<TestDomainEvent> CreateRequest(
        bool saveSnapshot = true,
        Func<Mat?>? cloneSnapshot = null,
        Action<TestDomainEvent>? publishInProcess = null)
    {
        return new EventPublicationRequest<TestDomainEvent>
        {
            Event = new TestDomainEvent(),
            AnnotationJson = "{}",
            CloneSnapshot = cloneSnapshot,
            FrameId = 1,
            FilePrefix = "event",
            StableArtifactId = "stable",
            PublishInProcess = publishInProcess,
            SaveSnapshot = saveSnapshot
        };
    }

    private static Mat CreateSnapshot() => new(8, 8, MatType.CV_8UC3, Scalar.White);

    private sealed class TestDomainEvent()
        : DomainEvent("source", "test-type", "test-name", "test-algorithm"), IAnnotatedAlgorithmEvent
    {
        public string Annotations { get; set; } = string.Empty;
        public override string GenerateJsonContent() => "{}";
        public override string GenerateLogContent() => Message;
    }

    private sealed class FakeEventRepository(
        Func<DomainEvent, Task>? save = null) : IEventRepository
    {
        public Task SaveDomainEventAsync(DomainEvent domainEvent) =>
            save?.Invoke(domainEvent) ?? Task.CompletedTask;

        public Task<DomainEvent> LoadDomainEventAsync(string eventId) =>
            throw new NotSupportedException();

        public Task DeleteDomainEventAsync(string eventId) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMessagePoster(Action<DomainEvent>? post = null) : IMessagePoster
    {
        public void PostDomainEventMessage(DomainEvent @event) => post?.Invoke(@event);
    }

    internal class FakeSnapshotManager : ISnapshotManager
    {
        public string SnapshotDir => string.Empty;
        public void ProcessSnapshots(Frame frame) => throw new NotSupportedException();
        public Mat GetSceneByFrameId(long frameId) => throw new NotSupportedException();
        public int GetCachedSceneCount() => 0;
        public SortedList<float, Mat> GetObjectSnapshotsByObjectId(string objId) => throw new NotSupportedException();
        public Mat GetBestSnapshotByObjectId(string objId) => throw new NotSupportedException();
        public int GetCachedSnapshotCount() => 0;
        public Mat TakeSnapshot(Frame frame, BoundingBox bboxs) => throw new NotSupportedException();
        public void AddSnapshotOfObject(Frame frame, DetectedObject detectedObject, float score, Mat snapshot) =>
            throw new NotSupportedException();
        public Mat GenerateBoxedScene(Mat scene, List<BoundingBox> boundingBoxes) => throw new NotSupportedException();
        public virtual Task GenerateVideoClipAroundFrameAsync(
            string filepath,
            long centerFrameId,
            int? durationSeconds = null,
            double? frameRate = null) => Task.CompletedTask;
        public void GenerateVideoClipAroundFrame(
            string filepath,
            long centerFrameId,
            int? durationSeconds = null,
            double? frameRate = null) => throw new NotSupportedException();
        public void SetPublisher(MessagePipe.IPublisher<ObjectBestSnapshotCreatedEvent> publisher) =>
            throw new NotSupportedException();
        public void PublishEvent(ObjectBestSnapshotCreatedEvent @event) =>
            throw new NotSupportedException();
        public void SetSubscriber(MessagePipe.ISubscriber<ObjectExpiredEvent> subscriber) =>
            throw new NotSupportedException();
        public void SetSubscriber(MessagePipe.ISubscriber<FrameExpiredEvent> subscriber) =>
            throw new NotSupportedException();
        public void ProcessEvent(ObjectExpiredEvent @event) => throw new NotSupportedException();
        public void ProcessEvent(FrameExpiredEvent @event) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class RecordingSnapshotManager : FakeSnapshotManager
    {
        public string? OutputPath { get; private set; }
        public long? CenterFrameId { get; private set; }

        public override Task GenerateVideoClipAroundFrameAsync(
            string filepath,
            long centerFrameId,
            int? durationSeconds = null,
            double? frameRate = null)
        {
            OutputPath = filepath;
            CenterFrameId = centerFrameId;
            return Task.CompletedTask;
        }
    }
}
