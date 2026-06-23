using Algorithm.Common;
using Algorithm.Ship.Labels.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Algorithm.Ship.Labels;

public class Executor : AlgorithmBase
{
    private const string DefaultModelPath =
        "Models/ship_labels_codex_enhanced.onnx";
    private const string DefaultExecutionProvider = "cpu";
    private const int DefaultDeviceId = 0;
    private const int DefaultStride = 5;

    public string ModelPath { get; private set; } = string.Empty;
    public string ExecProvider { get; private set; } = string.Empty;
    public int DeviceId { get; private set; }
    public int Stride { get; private set; }
    public bool WillGenerateObjLabelText { get; private set; }
    public int MinImageAreaOfLabelEvent { get; private set; }

    private ShipLabelPredictor? _predictor;
    private int _frameCount;
    private readonly ConcurrentDictionary<string, ShipLabelEvidence>
        _cachedShipLabels = new();
    private IPublisher<ShipLabelEvent> _shipLabelEventPublisher = null!;

    public Executor(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Ship labels";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Determine ship labels in video frames.";
    }

    protected override void InitializeCore()
    {
        Subscribe(
            Services.GetRequiredService<ISubscriber<ObjectExpiredEvent>>(),
            ProcessEvent);

        _shipLabelEventPublisher =
            Services.GetRequiredService<IPublisher<ShipLabelEvent>>();

        ModelPath = PreferenceParser.ParseStringValue(
            Preferences,
            "ModelPath",
            DefaultModelPath);
        ExecProvider = PreferenceParser.ParseStringValue(
            Preferences,
            "ExecutionProvider",
            DefaultExecutionProvider);
        DeviceId = PreferenceParser.ParseIntValue(
            Preferences,
            "DeviceId",
            DefaultDeviceId);
        Stride = PreferenceParser.ParseIntValue(
            Preferences,
            "Stride",
            DefaultStride);
        WillGenerateObjLabelText = PreferenceParser.ParseBoolValue(
            Preferences,
            "WillGenerateObjLabelText",
            true);
        MinImageAreaOfLabelEvent = PreferenceParser.ParseIntValue(
            Preferences,
            "MinImageAreaOfLabelEvent",
            50000);

        _predictor = new ShipLabelPredictor(
            ModelPath,
            ExecProvider,
            DeviceId);
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var isDetectionFrame = _frameCount % Stride == 0;
        _frameCount++;

        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (!string.Equals(
                detectedObject.Label,
                "boat",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (detectedObject.Snapshot == null ||
                detectedObject.Snapshot.IsDisposed)
            {
                continue;
            }

            var evidence = GetOrUpdateEvidence(
                frame,
                detectedObject,
                isDetectionFrame);
            detectedObject.SetProperty("Labels", evidence.JsonLabel);
            GenerateObjectLabelAnnotation(frame, detectedObject);
        }

        return new AnalysisResult(true);
    }

    private ShipLabelEvidence GetOrUpdateEvidence(
        Frame frame,
        DetectedObject detectedObject,
        bool isDetectionFrame)
    {
        ShipLabelEvidence? candidate = null;

        while (true)
        {
            if (_cachedShipLabels.TryGetValue(
                detectedObject.Id,
                out var current))
            {
                if (!isDetectionFrame ||
                    detectedObject.Confidence <= current.Confidence)
                {
                    candidate?.Dispose();
                    return current;
                }

                candidate ??= CreateEvidence(frame, detectedObject);
                if (_cachedShipLabels.TryUpdate(
                    detectedObject.Id,
                    candidate,
                    current))
                {
                    current.Dispose();
                    return candidate;
                }

                continue;
            }

            candidate ??= CreateEvidence(frame, detectedObject);
            if (_cachedShipLabels.TryAdd(detectedObject.Id, candidate))
            {
                return candidate;
            }
        }
    }

    private ShipLabelEvidence CreateEvidence(
        Frame frame,
        DetectedObject detectedObject)
    {
        var jsonLabel = _predictor!.Run(detectedObject.Snapshot!);
        var labels =
            JsonSerializer.Deserialize<ShipLabel>(jsonLabel) ??
            new ShipLabel();
        labels.Confidence = detectedObject.Confidence;

        return new ShipLabelEvidence(
            labels,
            jsonLabel,
            frame.FrameId,
            frame.UtcTimeStamp,
            detectedObject.Snapshot!.Clone());
    }

    protected override VisualAnnotation GenerateObjectLabelAnnotation(
        Frame frame,
        DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;
        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        if (WillGenerateBBox)
        {
            annotation.Shapes.Add(
                ObjAnnoGenerator.GenerateBBox(
                    detectedObject,
                    BBoxStrokeColor,
                    BBoxStrokeWidth));
        }

        if (!WillGenerateObjLabelText)
        {
            return annotation;
        }

        var labels = detectedObject.GetProperty<string>("Labels");
        if (string.IsNullOrWhiteSpace(labels))
        {
            return annotation;
        }

        var shipLabels =
            JsonSerializer.Deserialize<ShipLabel>(labels) ??
            new ShipLabel();
        var bbox = detectedObject.Bbox;

        AddLabelText(
            annotation,
            $"text_label_type_{detectedObject.Id}",
            $"T:{shipLabels.ShipTypeGroup}",
            bbox.X,
            bbox.Y - 4 * ObjTextFontSize);
        AddLabelText(
            annotation,
            $"text_label_color_{detectedObject.Id}",
            $"C:{string.Join(',', shipLabels.ShipColor)}",
            bbox.X,
            bbox.Y - 3 * ObjTextFontSize);
        AddLabelText(
            annotation,
            $"text_label_draught_{detectedObject.Id}",
            $"D:{shipLabels.ShipDraught}",
            bbox.X,
            bbox.Y - 2 * ObjTextFontSize);
        AddLabelText(
            annotation,
            $"text_label_view_angle_{detectedObject.Id}",
            $"V:{shipLabels.ShipViewAngle}",
            bbox.X,
            bbox.Y - ObjTextFontSize);

        return annotation;
    }

    private void AddLabelText(
        VisualAnnotation annotation,
        string id,
        string content,
        int x,
        int y)
    {
        annotation.Shapes.Add(new Shape
        {
            Id = id,
            Type = "text",
            Content = content,
            Position = new Position
            {
                X = x,
                Y = y
            },
            Style = new Style
            {
                Color = ObjTextColor,
                FontSize = ObjTextFontSize
            }
        });
    }

    public void ProcessEvent(ObjectExpiredEvent @event)
    {
        if (!_cachedShipLabels.TryRemove(@event.Id, out var evidence))
        {
            return;
        }

        try
        {
            ProcessShipLabelEvent(@event, evidence);
        }
        finally
        {
            evidence.Dispose();
        }
    }

    private void ProcessShipLabelEvent(
        ObjectExpiredEvent @event,
        ShipLabelEvidence evidence)
    {
        if (evidence.SnapshotArea < MinImageAreaOfLabelEvent)
        {
            return;
        }

        var shipLabels = evidence.Labels;
        Log.Information(
            "{ShipId} labels -> TypeGroup:{ShipTypeGroup}, Colors:{ShipColors}, Draught:{ShipDraught}, ViewAngle:{ShipViewAngle}",
            @event.Id,
            shipLabels.ShipTypeGroup,
            string.Join(',', shipLabels.ShipColor),
            shipLabels.ShipDraught,
            shipLabels.ShipViewAngle);

        var shipLabelEvent = new ShipLabelEvent(
            @event.SourceId,
            EventName,
            AlgorithmName,
            @event.Id,
            @event.LocalId,
            shipLabels.Confidence,
            shipLabels);

        var visualAnnotation = new VisualAnnotation(
            @event.SourceId,
            evidence.FrameUtcTimeStamp,
            evidence.FrameId,
            evidence.SnapshotWidth,
            evidence.SnapshotHeight);
        visualAnnotation.AddShape(new Shape
        {
            Id = $"text_label_{@event.Id}",
            Type = "text",
            Content =
                $"Id:{@event.LocalId}, T:{shipLabels.ShipTypeGroup}\n" +
                $"C:{string.Join(',', shipLabels.ShipColor)}\n" +
                $"D:{shipLabels.ShipDraught}\n" +
                $"V:{shipLabels.ShipViewAngle}",
            Position = new Position
            {
                X = 10,
                Y = 10
            },
            Style = new Style
            {
                Color = ObjTextColor,
                FontSize = ObjTextFontSize
            }
        });

        shipLabelEvent.Annotations = JsonSerializer.Serialize(
            visualAnnotation,
            DomainEvent.JsonOptions);

        TryQueueEvent(new EventPublicationRequest<ShipLabelEvent>
        {
            Event = shipLabelEvent,
            AnnotationJson = shipLabelEvent.Annotations,
            CloneSnapshot = evidence.CloneSnapshot,
            FrameId = evidence.FrameId,
            FilePrefix = "shipLabel",
            StableArtifactId = @event.Id,
            PublishInProcess = publishedEvent =>
                _shipLabelEventPublisher.Publish(publishedEvent),
            SaveSnapshot = WillSaveEventSnapshot
        });
    }

    protected override void DisposeCore()
    {
        _predictor?.Dispose();
        _predictor = null;

        foreach (var objectId in _cachedShipLabels.Keys)
        {
            if (_cachedShipLabels.TryRemove(
                objectId,
                out var evidence))
            {
                evidence.Dispose();
            }
        }
    }
}
