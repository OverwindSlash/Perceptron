using Algorithm.Common;
using Algorithm.Ship.Labels.Event;
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

namespace Algorithm.Ship.Labels;

public class Executor : AlgorithmBase, IEventSubscriber<ObjectExpiredEvent>
{
    private const string DefaultModelPath = "Models/ship-label-v2-32B-151130.onnx";
    private const string DefaultExecutionProvider = "cpu";
    private const int DefaultDeviceId = 0;
    private const int DefaultStride = 5;

    public string ModelPath { get; private set; }
    public string ExecProvider { get; private set; }
    public int DeviceId { get; private set; }
    public int Stride { get; private set; }

    public bool WillGenerateObjLabelText { get; private set; }
    public int MinImageAreaOfLabelEvent { get; private set; }

    private ShipLabelPredictor _predictor;

    private int _frameCount = 0;

    // objectId -> (confidence, label)
    private readonly ConcurrentDictionary<string, ShipLabel> _cachedShipLabels = new();

    // event handler
    private ISubscriber<ObjectExpiredEvent> _objectExpiredEventPublisher;
    private IDisposable _disposableOeSubscriber;

    private IPublisher<ShipLabelEvent> _shipLabelEventPublisher;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        AlgorithmName = "Ship labels";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Determine ship labels in video frames.";
    }

    public override bool Initialize()
    {
        var provider = Pipeline.Provider;

        var subscriber = provider.GetService<ISubscriber<ObjectExpiredEvent>>();
        this.SetSubscriber(subscriber);

        _shipLabelEventPublisher = provider.GetService<IPublisher<ShipLabelEvent>>();

        ModelPath = PreferenceParser.ParseStringValue(Preferences, "ModelPath", DefaultModelPath);
        ExecProvider = PreferenceParser.ParseStringValue(Preferences, "ExecutionProvider", DefaultExecutionProvider);
        DeviceId = PreferenceParser.ParseIntValue(Preferences, "DeviceId", DefaultDeviceId);
        Stride = PreferenceParser.ParseIntValue(Preferences, "Stride", DefaultStride);
        WillGenerateObjLabelText = PreferenceParser.ParseBoolValue(Preferences, "WillGenerateObjLabelText", true);
        MinImageAreaOfLabelEvent = PreferenceParser.ParseIntValue(Preferences, "MinImageAreaOfLabelEvent", 50000);

        _predictor = new ShipLabelPredictor(ModelPath, ExecProvider, DeviceId);


        return base.Initialize();
    }

    public void SetSubscriber(ISubscriber<ObjectExpiredEvent> subscriber)
    {
        _objectExpiredEventPublisher = subscriber;
        _disposableOeSubscriber = _objectExpiredEventPublisher.Subscribe(ProcessEvent);
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (detectedObject.Label.ToLower() != "boat")
            {
                continue;
            }

            if (!_cachedShipLabels.TryGetValue(detectedObject.Id, out var shipLabels))
            {
                // 如果缓存中没有，则不管是否是检测帧都进行预测，保证第一次出现的船只能够被标注标签
                var labels = _predictor.Run(detectedObject.Snapshot);
                var shipLabel = JsonSerializer.Deserialize<ShipLabel>(labels);
                shipLabel.Confidence = detectedObject.Confidence;
                shipLabel.Frame = frame;
                shipLabel.Snapshot = detectedObject.Snapshot.Clone(); // Clone snapshot for event use
                shipLabel.JsonLabel = labels;

                _cachedShipLabels.AddOrUpdate(
                    detectedObject.Id,
                    shipLabel,
                    (key, oldValue) => shipLabel
                );

                detectedObject.SetProperty("Labels", labels);
            }
            else
            {
                var cachedConf = shipLabels.Confidence;
                var cachedLabels = shipLabels.JsonLabel;

                // 只有在检测帧且当前检测置信度高于缓存置信度时才进行预测更新，否则继续使用缓存标签，减少计算量
                if ((_frameCount % Stride == 0) && (detectedObject.Confidence > cachedConf))
                {
                    var labels = _predictor.Run(detectedObject.Snapshot);
                    var shipLabel = JsonSerializer.Deserialize<ShipLabel>(labels);
                    shipLabel.Confidence = detectedObject.Confidence;
                    shipLabel.Frame = frame;
                    shipLabel.Snapshot = detectedObject.Snapshot.Clone(); // Clone snapshot for event use
                    shipLabel.JsonLabel = labels;

                    _cachedShipLabels.AddOrUpdate(
                        detectedObject.Id,
                        shipLabel,
                        (key, oldValue) => shipLabel
                    );

                    detectedObject.SetProperty("Labels", labels);
                }
                else
                {
                    // 非检测帧，或置信度不足的检测帧，直接使用缓存标签，减少计算
                    detectedObject.SetProperty("Labels", cachedLabels);
                }
            }

            GenerateObjectLabelAnnotation(frame, detectedObject);
        }

        frame.Dispose();

        return new AnalysisResult(true);
    }

    protected override VisualAnnotation GenerateObjectLabelAnnotation(Frame frame, DetectedObject detectedObject)
    {
        var annotation = frame.Annotation;

        if (!detectedObject.IsUnderAnalysis)
        {
            return annotation;
        }

        // bbox annotation
        if (WillGenerateBBox)
        {
            var rect = ObjAnnoGenerator.GenerateBBox(detectedObject, BBoxStrokeColor, BBoxStrokeWidth);
            annotation.Shapes.Add(rect);
        }

        // object text annotation
        if (WillGenerateObjLabelText)
        {
            var bbox = detectedObject.Bbox;

            var labels = detectedObject.GetProperty<string>("Labels");

            var shipLabels = JsonSerializer.Deserialize<ShipLabel>(labels);

            // type annotation
            var textType = new Shape()
            {
                Id = "text_label_type_" + detectedObject.Id,
                Type = "text",
                //Content = $"Id:{detectedObject.LocalId},T:{shipLabels.ShipTypeGroup},C:{string.Join(',', shipLabels.ShipColor)},D:{shipLabels.ShipDraught}",
                Content = $"TypeGroup:{shipLabels.ShipTypeGroup}",
                Position = new Position()
                {
                    X = bbox.X,
                    Y = bbox.Y - 3 * base.ObjTextFontSize
                },
                Style = new Style()
                {
                    Color = base.ObjTextColor,
                    FontSize = base.ObjTextFontSize,
                }
            };

            annotation.Shapes.Add(textType);

            // color annotation
            var textColor = new Shape()
            {
                Id = "text_label_color_" + detectedObject.Id,
                Type = "text",
                //Content = $"Id:{detectedObject.LocalId},T:{shipLabels.ShipTypeGroup},C:{string.Join(',', shipLabels.ShipColor)},D:{shipLabels.ShipDraught}",
                Content = $"Color:{string.Join(',', shipLabels.ShipColor)}",
                Position = new Position()
                {
                    X = bbox.X,
                    Y = bbox.Y - 2 * base.ObjTextFontSize
                },
                Style = new Style()
                {
                    Color = base.ObjTextColor,
                    FontSize = base.ObjTextFontSize,
                }
            };

            annotation.Shapes.Add(textColor);

            // draught annotation
            var draughtColor = new Shape()
            {
                Id = "text_label_color_" + detectedObject.Id,
                Type = "text",
                //Content = $"Id:{detectedObject.LocalId},T:{shipLabels.ShipTypeGroup},C:{string.Join(',', shipLabels.ShipColor)},D:{shipLabels.ShipDraught}",
                Content = $"Draught:{shipLabels.ShipDraught}",
                Position = new Position()
                {
                    X = bbox.X,
                    Y = bbox.Y - base.ObjTextFontSize
                },
                Style = new Style()
                {
                    Color = base.ObjTextColor,
                    FontSize = base.ObjTextFontSize,
                }
            };

            annotation.Shapes.Add(draughtColor);
        }

        return annotation;
    }

    public void ProcessEvent(ObjectExpiredEvent @event)
    {
        var objectId = @event.Id;

        if (_cachedShipLabels.TryGetValue(objectId, out var shipLabels))
        {
            ProcessShipLabelEvent(@event, shipLabels);
        }

        _cachedShipLabels.TryRemove(objectId, out _);
    }

    private void ProcessShipLabelEvent(ObjectExpiredEvent @event, ShipLabel shipLabels)
    {
        if (!WillPublishEventMessage) return;

        var snapshotArea = shipLabels.Snapshot.Width * shipLabels.Snapshot.Height;
        if (snapshotArea < MinImageAreaOfLabelEvent) return;

        Log.Information("{ShipId} labels -> TypeGroup:{ShipTypeGroup}, Colors:{ShipColors}, Draught:{ShipDraught}",
            @event.Id, shipLabels.ShipTypeGroup, string.Join(',', shipLabels.ShipColor), shipLabels.ShipDraught);

        // 1. Create Event
        var shipLabelEvent = new ShipLabelEvent(
            sourceId: @event.SourceId,
            eventName: EventName,
            algorithmName: AlgorithmName,
            objectId: @event.Id,
            objectLocalId: @event.LocalId,
            confidence: shipLabels.Confidence,
            labels: shipLabels);

        // 2. Generate text annotation for detected object
        var frame = shipLabels.Frame;
        var visualAnnotation = new VisualAnnotation(@event.SourceId, frame.UtcTimeStamp, frame.FrameId, shipLabels.Snapshot.Width,
            shipLabels.Snapshot.Height);

        var text = new Shape()
        {
            Id = "text_label_" + @event.Id,
            Type = "text",
            Content = $"Id:{@event.LocalId}, T:{shipLabels.ShipTypeGroup}\nC:{string.Join(',', shipLabels.ShipColor)}\nD:{shipLabels.ShipDraught}",
            Position = new Position()
            {
                X = 10,
                Y = 10
            },
            Style = new Style()
            {
                Color = base.ObjTextColor,
                FontSize = base.ObjTextFontSize,
            }
        };

        visualAnnotation.AddShape(text);
        shipLabelEvent.Annotations = JsonSerializer.Serialize(visualAnnotation, DomainEvent.JsonOptions);

        // 3. Prepare Snapshot (Synchronously - critical for thread safety)
        Mat? snapshot = null;
        if (WillSaveEventSnapshot)
        {
            snapshot = shipLabels.Snapshot;
        }

        // 4. Async Saving
        string now = DateTime.Now.ToString("yyyyMMddhhmmss");
        Task.Run(async () =>
        {
            try
            {
                using (snapshot) // Ensure disposal of the cloned snapshot
                {
                    string savePath = Path.Combine(EventSnapshotDir, DateTime.UtcNow.ToString("yyyy-MM-dd"));
                    savePath.EnsureDirExistence();

                    if (snapshot != null && !snapshot.IsDisposed)
                    {
                        string imagePath = Path.Combine(savePath, $"{@event.Id}_{now}.jpg");
                        snapshot.SaveImage(imagePath);

                        string annotationPath = Path.Combine(savePath, $"{@event.Id}_{now}.json");
                        await File.WriteAllTextAsync(annotationPath, shipLabelEvent.Annotations);

                        shipLabelEvent.ImageLocalPath = imagePath;
                        shipLabelEvent.ImageJsonLocalPath = annotationPath;
                    }

                    await EventRepository.SaveDomainEventAsync(shipLabelEvent);
                    MessagePoster.PostDomainEventMessage(shipLabelEvent);

                    _shipLabelEventPublisher.Publish(shipLabelEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing event {EventName}", EventName);
            }
        });
    }
    
    public override void Dispose()
    {
        _predictor?.Dispose();
        _disposableOeSubscriber.Dispose();
        base.Dispose();
    }
}
