using Algorithm.CoastGuard.SmugglingDetection.Event;
using Algorithm.Common;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.EventHandler;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;
using AnnotationSize = Perceptron.Domain.Entity.Annotation.Size;
using CvPoint = OpenCvSharp.Point;

namespace Algorithm.CoastGuard.SmugglingDetection;

public class Executor : AlgorithmBase, IEventSubscriber<ObjectExpiredEvent>
{
    private const string PeopleGatheringFlag = "people_gathering";
    private const string BoatExistenceFlag = "boat_existence";
    private const string PeopleAwayFromBoatFlag = "people_away_from_boat";
    private const string GatheringsPropertyName = "gatherings";

    private const string DefaultPersonLabel = "person";
    private const string DefaultBoatLabel = "boat";
    private const float DefaultWidthBasedApproachFactor = 0.2f;
    private const int DefaultMaxGatheringCount = 3;
    private const int DefaultEventSustainSec = 5;
    private const int DefaultHistoryLengthThresh = 10;
    private const float DefaultDistanceIncreasePercentThresh = 0.7f;
    private const float DefaultMovingAwayPercentThresh = 0.5f;
    private const float DefaultGatheringBoxMergeIouThreshold = 0.3f;
    private const string DefaultGatheringAnnotationColor = "#FF1744";
    private const string DefaultPersonCenterAnnotationColor = "#00BCD4";
    private const string DefaultBoatAnnotationColor = "#FFEB3B";

    public string PersonLabel { get; private set; } = DefaultPersonLabel;
    public string BoatLabel { get; private set; } = DefaultBoatLabel;
    public float WidthBasedApproachFactor { get; private set; }
    public int MaxGatheringCount { get; private set; }
    public int EventSustainSec { get; private set; }
    public int HistoryLengthThresh { get; private set; }
    public float DistanceIncreasePercentThresh { get; private set; }
    public float MovingAwayPercentThresh { get; private set; }
    public float GatheringBoxMergeIouThreshold { get; private set; }
    public string GatheringAnnotationColor { get; private set; } = DefaultGatheringAnnotationColor;
    public string PersonCenterAnnotationColor { get; private set; } = DefaultPersonCenterAnnotationColor;
    public string BoatAnnotationColor { get; private set; } = DefaultBoatAnnotationColor;

    private readonly TtlFlagManager<string> _flagManager = new();
    private readonly ConcurrentDictionary<string, Queue<CvPoint>> _personHistory = new();
    private readonly ConcurrentDictionary<string, CvPoint> _boatPositions = new();

    private IPublisher<SmugglingEvent> _smugglingEventPublisher = null!;
    private ISubscriber<ObjectExpiredEvent> _objectExpiredSubscriber = null!;
    private IDisposable? _objectExpiredSubscription;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Smuggling Detection";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects suspected smuggling by combining people gathering, boat existence, and movement away from the boat.";
    }

    public override bool Initialize()
    {
        var provider = Pipeline.Provider;
        _smugglingEventPublisher = provider.GetRequiredService<IPublisher<SmugglingEvent>>();
        SetSubscriber(provider.GetRequiredService<ISubscriber<ObjectExpiredEvent>>());

        PersonLabel = PreferenceParser.ParseStringValue(Preferences, "PersonLabel", DefaultPersonLabel).ToLowerInvariant();
        BoatLabel = PreferenceParser.ParseStringValue(Preferences, "BoatLabel", DefaultBoatLabel).ToLowerInvariant();
        WidthBasedApproachFactor = PreferenceParser.ParseFloatValue(Preferences, "WidthBasedApproachFactor", DefaultWidthBasedApproachFactor);
        MaxGatheringCount = Math.Max(1, PreferenceParser.ParseIntValue(Preferences, "MaxGatheringCount", DefaultMaxGatheringCount));
        EventSustainSec = Math.Max(1, PreferenceParser.ParseIntValue(Preferences, "EventSustainSec", DefaultEventSustainSec));
        HistoryLengthThresh = Math.Max(2, PreferenceParser.ParseIntValue(Preferences, "HistoryLengthThresh", DefaultHistoryLengthThresh));
        DistanceIncreasePercentThresh = PreferenceParser.ParseFloatValue(Preferences, "DistanceIncreasePercentThresh", DefaultDistanceIncreasePercentThresh);
        MovingAwayPercentThresh = PreferenceParser.ParseFloatValue(Preferences, "MovingAwayPercentThresh", DefaultMovingAwayPercentThresh);
        GatheringBoxMergeIouThreshold = PreferenceParser.ParseFloatValue(Preferences, "GatheringBoxMergeIouThreshold", DefaultGatheringBoxMergeIouThreshold);
        GatheringAnnotationColor = PreferenceParser.ParseStringValue(Preferences, "GatheringAnnotationColor", DefaultGatheringAnnotationColor);
        PersonCenterAnnotationColor = PreferenceParser.ParseStringValue(Preferences, "PersonCenterAnnotationColor", DefaultPersonCenterAnnotationColor);
        BoatAnnotationColor = PreferenceParser.ParseStringValue(Preferences, "BoatAnnotationColor", DefaultBoatAnnotationColor);

        return base.Initialize();
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        try
        {
            if (CheckPeopleGathering(frame))
            {
                _flagManager.SetValue(FlagKey(frame.SourceId, PeopleGatheringFlag), true, EventSustainSec);
            }

            var isPeopleGathering = IsFlagActive(frame.SourceId, PeopleGatheringFlag);
            frame.SetProperty(PeopleGatheringFlag, isPeopleGathering);

            if (CheckBoatExistence(frame))
            {
                _flagManager.SetValue(FlagKey(frame.SourceId, BoatExistenceFlag), true, 2 * EventSustainSec);
            }

            var isBoatExistence = IsFlagActive(frame.SourceId, BoatExistenceFlag);
            frame.SetProperty(BoatExistenceFlag, isBoatExistence);

            if (CheckPeopleAwayFromBoat(frame))
            {
                _flagManager.SetValue(FlagKey(frame.SourceId, PeopleAwayFromBoatFlag), true, EventSustainSec);
            }

            var isPeopleAwayFromBoat = IsFlagActive(frame.SourceId, PeopleAwayFromBoatFlag);
            frame.SetProperty(PeopleAwayFromBoatFlag, isPeopleAwayFromBoat);

            var gatherings = frame.GetProperty<List<SmugglingObjectGroup>>(GatheringsPropertyName) ?? new List<SmugglingObjectGroup>();
            GenerateSmugglingAnnotation(frame, gatherings);

            var isSmugglingDetected = isPeopleGathering && isBoatExistence && isPeopleAwayFromBoat;
            frame.SetProperty("SmugglingDetected", isSmugglingDetected);

            if (isSmugglingDetected)
            {
                var incidentId = CreateIncidentId(frame);
                ProcessSmugglingEvent(
                    frame,
                    incidentId,
                    gatherings,
                    isPeopleGathering,
                    isBoatExistence,
                    isPeopleAwayFromBoat);
            }

            return new AnalysisResult(true);
        }
        finally
        {
            frame.Dispose();
        }
    }

    private bool CheckPeopleGathering(Frame frame)
    {
        var detectedPersons = frame.DetectedObjects
            .Where(obj => obj.IsUnderAnalysis && IsLabel(obj, PersonLabel))
            .ToList();

        var allPersonGatherings = new List<SmugglingObjectGroup>();

        foreach (var outerPerson in detectedPersons)
        {
            var personCluster = new List<DetectedObject> { outerPerson };
            personCluster.AddRange(detectedPersons
                .Where(innerPerson => !ReferenceEquals(outerPerson, innerPerson))
                .Where(innerPerson => CloseTo(outerPerson.Bbox, innerPerson.Bbox, WidthBasedApproachFactor)));

            if (personCluster.Count < MaxGatheringCount)
            {
                continue;
            }

            var currentPersonGathering = new SmugglingObjectGroup(personCluster, "gathering", outerPerson.TrackingId);

            var isCurrentGatheringInnerBbox = false;
            SmugglingObjectGroup? toRemoveGathering = null;

            foreach (var candidateGathering in allPersonGatherings)
            {
                if (candidateGathering.Bbox.Contains(currentPersonGathering.Bbox))
                {
                    isCurrentGatheringInnerBbox = true;
                    break;
                }

                if (currentPersonGathering.Bbox.Contains(candidateGathering.Bbox))
                {
                    toRemoveGathering = candidateGathering;
                }
            }

            if (!isCurrentGatheringInnerBbox)
            {
                allPersonGatherings.Add(currentPersonGathering);
            }

            if (toRemoveGathering != null)
            {
                allPersonGatherings.Remove(toRemoveGathering);
            }
        }

        frame.SetProperty(GatheringsPropertyName, allPersonGatherings);

        return allPersonGatherings.Any(g => g.GroupObjects.Count > MaxGatheringCount);
    }

    private bool CheckBoatExistence(Frame frame)
    {
        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (!detectedObject.IsUnderAnalysis)
            {
                continue;
            }

            if (!IsLabel(detectedObject, BoatLabel))
            {
                continue;
            }

            _boatPositions[frame.SourceId] = new CvPoint(
                (int)Math.Round(detectedObject.CenterX),
                (int)Math.Round(detectedObject.CenterY));

            return true;
        }

        return false;
    }

    private bool CheckPeopleAwayFromBoat(Frame frame)
    {
        if (!_boatPositions.TryGetValue(frame.SourceId, out var boatPosition))
        {
            return false;
        }

        if (boatPosition is { X: 0, Y: 0 })
        {
            return false;
        }

        var gatherings = frame.GetProperty<List<SmugglingObjectGroup>>(GatheringsPropertyName);
        if (gatherings == null)
        {
            return false;
        }

        var fleePersons = new HashSet<string>();

        var detectedPersons = frame.DetectedObjects
            .Where(obj => obj.IsUnderAnalysis && IsLabel(obj, PersonLabel))
            .ToList();

        foreach (var person in detectedPersons)
        {
            var personKey = PersonHistoryKey(person.SourceId, person.TrackingId);
            var history = _personHistory.GetOrAdd(personKey, _ => new Queue<CvPoint>());
            CvPoint[] historySnapshot;

            lock (history)
            {
                history.Enqueue(new CvPoint(
                    (int)Math.Round(person.CenterX),
                    (int)Math.Round(person.CenterY)));

                while (history.Count > HistoryLengthThresh)
                {
                    history.Dequeue();
                }

                historySnapshot = history.ToArray();
            }

            if (IsMovingAway(historySnapshot, boatPosition))
            {
                fleePersons.Add(personKey);
            }
        }

        var isAnyPeopleGatheringFlee = false;

        foreach (var gathering in gatherings)
        {
            if (gathering.GroupObjects.Count == 0)
            {
                continue;
            }

            var fleePersonCount = gathering.GroupObjects.Count(person =>
                fleePersons.Contains(PersonHistoryKey(person.SourceId, person.TrackingId)));

            var isGatheringFlee = (float)fleePersonCount / gathering.GroupObjects.Count > MovingAwayPercentThresh;
            gathering.SetProperty("isFlee", isGatheringFlee);

            isAnyPeopleGatheringFlee |= isGatheringFlee;
        }

        return isAnyPeopleGatheringFlee;
    }

    private bool IsMovingAway(IReadOnlyList<CvPoint> history, CvPoint boatPosition)
    {
        if (history.Count < HistoryLengthThresh / 2)
        {
            return false;
        }

        var distances = history.Select(pos => CalculateDistance(pos, boatPosition)).ToList();

        var increasingDistancesCount = 0;
        for (var i = 1; i < distances.Count; i++)
        {
            if (distances[i] - distances[i - 1] > 0)
            {
                increasingDistancesCount++;
            }
        }

        return (double)increasingDistancesCount / distances.Count >= DistanceIncreasePercentThresh;
    }

    private VisualAnnotation GenerateSmugglingAnnotation(Frame frame, List<SmugglingObjectGroup> gatherings)
    {
        var annotation = frame.Annotation;

        foreach (var detectedObject in frame.DetectedObjects.Where(obj => obj.IsUnderAnalysis && IsLabel(obj, BoatLabel)))
        {
            var rect = ObjAnnoGenerator.GenerateBBox(detectedObject, BoatAnnotationColor, Math.Max(2, BBoxStrokeWidth));
            annotation.AddShape(rect);
        }

        foreach (var detectedObject in frame.DetectedObjects.Where(obj => obj.IsUnderAnalysis && IsLabel(obj, PersonLabel)))
        {
            annotation.AddShape(new Shape
            {
                Id = $"smuggling_person_center_{detectedObject.Id}",
                Type = "circle",
                Center = new Center
                {
                    X = (int)Math.Round(detectedObject.CenterX),
                    Y = (int)Math.Round(detectedObject.CenterY)
                },
                Radius = 5,
                Style = new Style
                {
                    StrokeColor = PersonCenterAnnotationColor,
                    FillColor = PersonCenterAnnotationColor,
                    StrokeWidth = 1,
                    Opacity = 0.85f
                }
            });
        }

        var mergedGatheringBoxes = MergeBoundingBoxes(gatherings.Select(g => g.Bbox), GatheringBoxMergeIouThreshold);
        for (var i = 0; i < mergedGatheringBoxes.Count; i++)
        {
            var bbox = mergedGatheringBoxes[i];
            annotation.AddShape(new Shape
            {
                Id = $"smuggling_gathering_{i}",
                Type = "rect",
                Origin = new Origin { X = bbox.X, Y = bbox.Y },
                Size = new AnnotationSize { Width = bbox.Width, Height = bbox.Height },
                Style = new Style
                {
                    StrokeColor = GatheringAnnotationColor,
                    StrokeWidth = 3
                }
            });
        }

        foreach (var gathering in gatherings)
        {
            var isFlee = gathering.GetProperty<bool>("isFlee");
            var color = isFlee ? GatheringAnnotationColor : "#FF9800";

            annotation.AddShape(new Shape
            {
                Id = $"smuggling_gathering_text_{gathering.TrackingId}",
                Type = "text",
                Content = $"Gathering:{gathering.GroupObjects.Count}",
                Position = new Position
                {
                    X = gathering.Bbox.X,
                    Y = Math.Max(0, gathering.Bbox.Y - ObjTextFontSize)
                },
                Style = new Style
                {
                    Color = color,
                    FontSize = ObjTextFontSize
                }
            });
        }

        return annotation;
    }

    private void ProcessSmugglingEvent(
        Frame frame,
        string incidentId,
        List<SmugglingObjectGroup> gatherings,
        bool peopleGathering,
        bool boatExistence,
        bool peopleAwayFromBoat)
    {
        if (!WillPublishEventMessage) return;
        if (CheckLocalEventInterval()) return;

        var personCountInGatherings = gatherings.Sum(g => g.GroupObjects.Count);

        Log.Warning(
            "Smuggling detected on source {SourceId}. Gatherings:{GatheringCount}, people:{PersonCountInGatherings}",
            frame.SourceId,
            gatherings.Count,
            personCountInGatherings);

        var smugglingEvent = new SmugglingEvent(
            sourceId: frame.SourceId,
            eventName: EventName,
            algorithmName: AlgorithmName,
            incidentId: incidentId,
            frameId: frame.FrameId,
            frameUtcTimeStamp: frame.UtcTimeStamp,
            gatheringCount: gatherings.Count,
            personCountInGatherings: personCountInGatherings,
            peopleGathering: peopleGathering,
            boatExistence: boatExistence,
            peopleAwayFromBoat: peopleAwayFromBoat);

        var annotationJson = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        smugglingEvent.Annotations = annotationJson;

        Mat? snapshot = null;
        if (WillSaveEventSnapshot)
        {
            snapshot = frame.Scene.Clone();
        }

        var frameId = frame.FrameId;
        var sourceId = frame.SourceId;
        var publisher = _smugglingEventPublisher;

        Task.Run(async () =>
        {
            try
            {
                using (snapshot)
                {
                    var savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"), SanitizeFileToken(sourceId));
                    savePath.EnsureDirExistence();

                    if (snapshot != null && !snapshot.IsDisposed)
                    {
                        var imagePath = Path.Combine(savePath, $"{incidentId}.jpg");
                        snapshot.SaveImage(imagePath);

                        var annotationPath = Path.Combine(savePath, $"{incidentId}.json");
                        await File.WriteAllTextAsync(annotationPath, annotationJson);

                        smugglingEvent.ImageLocalPath = imagePath;
                        smugglingEvent.ImageJsonLocalPath = annotationPath;
                    }

                    if (WillSaveEventVideoClip)
                    {
                        var videoPath = Path.Combine(savePath, $"{incidentId}.mp4");
                        await SnapshotManager.GenerateVideoClipAroundFrameAsync(videoPath, frameId);
                        smugglingEvent.VideoLocalPath = videoPath;
                    }

                    await EventRepository.SaveDomainEventAsync(smugglingEvent);
                    MessagePoster.PostDomainEventMessage(smugglingEvent);
                    publisher.Publish(smugglingEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing smuggling event {EventName}", EventName);
            }
        });
    }

    public void SetSubscriber(ISubscriber<ObjectExpiredEvent> subscriber)
    {
        _objectExpiredSubscriber = subscriber;
        _objectExpiredSubscription = _objectExpiredSubscriber.Subscribe(ProcessEvent);
    }

    public void ProcessEvent(ObjectExpiredEvent @event)
    {
        _personHistory.TryRemove(PersonHistoryKey(@event.SourceId, @event.TrackingId), out _);
    }

    public override void Dispose()
    {
        _objectExpiredSubscription?.Dispose();
        base.Dispose();
    }

    private bool IsFlagActive(string sourceId, string flagName)
    {
        _flagManager.TryGetValue(FlagKey(sourceId, flagName), out var isActive);
        return isActive;
    }

    private static string FlagKey(string sourceId, string flagName)
    {
        return $"{sourceId}:{flagName}";
    }

    private static string PersonHistoryKey(string sourceId, int trackingId)
    {
        return $"{sourceId}:{trackingId}";
    }

    private static bool IsLabel(DetectedObject detectedObject, string label)
    {
        return string.Equals(detectedObject.Label, label, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CloseTo(BoundingBox current, BoundingBox other, float threshold)
    {
        var distance = current.MinDistance(other);

        return distance < threshold * Math.Max(current.Width, current.Height) ||
               distance < threshold * Math.Max(other.Width, other.Height);
    }

    private static double CalculateDistance(CvPoint p1, CvPoint p2)
    {
        var dx = p1.X - p2.X;
        var dy = p1.Y - p2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static List<BoundingBox> MergeBoundingBoxes(IEnumerable<BoundingBox> boxes, float iouThreshold)
    {
        var mergedBoxes = new List<BoundingBox>();

        foreach (var box in boxes)
        {
            var current = box;
            var merged = true;

            while (merged)
            {
                merged = false;

                for (var i = 0; i < mergedBoxes.Count; i++)
                {
                    if (mergedBoxes[i].IoU(current) <= iouThreshold)
                    {
                        continue;
                    }

                    current = mergedBoxes[i].Merge(current);
                    mergedBoxes.RemoveAt(i);
                    merged = true;
                    break;
                }
            }

            mergedBoxes.Add(current);
        }

        return mergedBoxes;
    }

    private static string CreateIncidentId(Frame frame)
    {
        var sourceToken = SanitizeFileToken(frame.SourceId);
        return $"smg_{sourceToken}_{frame.UtcTimeStamp:yyyyMMddHHmmssfff}";
    }

    private static string SanitizeFileToken(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
