using OpenCvSharp;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Event;
using Serilog;

namespace Algorithm.Common;

public sealed class AlgorithmEventDispatcher : IDisposable
{
    private static long _artifactSequence;

    private readonly IEventRepository _eventRepository;
    private readonly IMessagePoster _messagePoster;
    private readonly ISnapshotManager _snapshotManager;
    private readonly string _eventSnapshotDirectory;
    private readonly TimeSpan _shutdownTimeout;
    private readonly object _sync = new();
    private readonly HashSet<Task> _tasks = [];
    private bool _isStopping;

    public AlgorithmEventDispatcher(
        IEventRepository eventRepository,
        IMessagePoster messagePoster,
        ISnapshotManager snapshotManager,
        string eventSnapshotDirectory,
        TimeSpan shutdownTimeout)
    {
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _messagePoster = messagePoster ?? throw new ArgumentNullException(nameof(messagePoster));
        _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        _eventSnapshotDirectory = string.IsNullOrWhiteSpace(eventSnapshotDirectory)
            ? AlgorithmConstants.DefaultEventSnapshotDir
            : eventSnapshotDirectory;
        _shutdownTimeout = shutdownTimeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(AlgorithmConstants.DefaultEventTaskShutdownTimeoutSeconds)
            : shutdownTimeout;
    }

    public bool TryQueue<TEvent>(EventPublicationRequest<TEvent> request)
        where TEvent : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Event);

        lock (_sync)
        {
            if (_isStopping)
            {
                return false;
            }

            Mat? snapshot = null;
            try
            {
                if (request.SaveSnapshot && request.CloneSnapshot != null)
                {
                    snapshot = request.CloneSnapshot();
                }

                var artifactBasePath = CreateArtifactBasePath(request);
                var task = Task.Run(() => PublishAsync(request, snapshot, artifactBasePath));
                _tasks.Add(task);
                _ = task.ContinueWith(
                    completedTask => RemoveTask(completedTask),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return true;
            }
            catch
            {
                snapshot?.Dispose();
                throw;
            }
        }
    }

    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_sync)
            {
                tasks = [.. _tasks];
            }

            if (tasks.Length == 0)
            {
                return;
            }

            await Task.WhenAll(tasks);
        }
    }

    private async Task PublishAsync<TEvent>(
        EventPublicationRequest<TEvent> request,
        Mat? snapshot,
        string artifactBasePath)
        where TEvent : DomainEvent
    {
        try
        {
            if (request.Event is IAnnotatedAlgorithmEvent annotatedEvent)
            {
                annotatedEvent.Annotations = request.AnnotationJson;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(artifactBasePath)!);

            if (request.SaveSnapshot)
            {
                if (snapshot != null && !snapshot.IsDisposed)
                {
                    var imagePath = artifactBasePath + ".jpg";
                    snapshot.SaveImage(imagePath);
                    request.Event.ImageLocalPath = imagePath;
                }

                if (!string.IsNullOrWhiteSpace(request.AnnotationJson))
                {
                    var annotationPath = artifactBasePath + ".json";
                    await File.WriteAllTextAsync(annotationPath, request.AnnotationJson);
                    request.Event.ImageJsonLocalPath = annotationPath;
                }
            }

            if (request.SaveVideoClip && request.FrameId.HasValue)
            {
                var videoPath = artifactBasePath + ".mp4";
                await _snapshotManager.GenerateVideoClipAroundFrameAsync(
                    videoPath,
                    request.FrameId.Value);
                request.Event.VideoLocalPath = videoPath;
            }

            await _eventRepository.SaveDomainEventAsync(request.Event);
            _messagePoster.PostDomainEventMessage(request.Event);
            request.PublishInProcess?.Invoke(request.Event);
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Failed to publish algorithm event. AlgorithmName: {AlgorithmName}, EventName: {EventName}, EventType: {EventType}, SourceId: {SourceId}, FrameId: {FrameId}",
                request.Event.AlgorithmName,
                request.Event.EventName,
                request.Event.EventType,
                request.Event.SourceId,
                request.FrameId);
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    private string CreateArtifactBasePath<TEvent>(EventPublicationRequest<TEvent> request)
        where TEvent : DomainEvent
    {
        var directory = request.RelativeDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = DateTime.UtcNow.ToString("yyyy-MM-dd");
        }

        var stableId = string.IsNullOrWhiteSpace(request.StableArtifactId)
            ? request.FilePrefix
            : request.StableArtifactId;
        var safeStableId = SanitizeFileName(stableId);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var sequence = Interlocked.Increment(ref _artifactSequence);
        var fileName = $"{safeStableId}_{timestamp}_{sequence:D6}";
        return Path.Combine(_eventSnapshotDirectory, directory, fileName);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalidCharacters.Contains(character) ? '_' : character));
    }

    private void RemoveTask(Task task)
    {
        lock (_sync)
        {
            _tasks.Remove(task);
        }
    }

    public void Dispose()
    {
        Task[] tasks;
        lock (_sync)
        {
            if (_isStopping)
            {
                return;
            }

            _isStopping = true;
            tasks = [.. _tasks];
        }

        if (tasks.Length == 0)
        {
            return;
        }

        var completed = Task.WaitAll(tasks, _shutdownTimeout);
        if (!completed)
        {
            Log.Warning(
                "Algorithm event dispatcher shutdown timed out with {PendingTaskCount} unfinished tasks.",
                tasks.Count(task => !task.IsCompleted));
        }
    }
}
