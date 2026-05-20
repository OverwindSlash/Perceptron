using Algorithm.Common;
using Algorithm.Common.Event;
using Algorithm.Ship.LabelsByLLM.Event;
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

namespace Algorithm.Ship.LabelsByLLM;

public class Executor : AlgorithmBase, IEventSubscriber<ObjectExpiredEvent>
{
    private const int DefaultStride = 5;
    private const string LLMAnalysisPropertyName = "LLMAnalysis";
    private const string LLMAnalysisPromptPropertyName = "LLMAnalysisPrompt";

    public int Stride { get; private set; }
    public bool WillGenerateObjLabelText { get; private set; }
    public int MinImageAreaOfLabelEvent { get; private set; }

    private int _frameCount = 0;
    private string userPrompt = string.Empty;

    // objectId -> (confidence, label)
    private readonly ConcurrentDictionary<string, ShipLabel> _cachedShipLabels = new();

    // event handler
    private ISubscriber<ObjectExpiredEvent> _objectExpiredEventPublisher;
    private IDisposable _disposableOeSubscriber;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences) 
        : base(pipeline, preferences)
    {
        AlgorithmName = "Ship labels by llm";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Determine ship labels in video frames using llm inference.";
    }

    public override bool Initialize()
    {
        var provider = Pipeline.Provider;

        var subscriber = provider.GetService<ISubscriber<ObjectExpiredEvent>>();
        this.SetSubscriber(subscriber);

        Stride = PreferenceParser.ParseIntValue(Preferences, "Stride", DefaultStride);
        WillGenerateObjLabelText = PreferenceParser.ParseBoolValue(Preferences, "WillGenerateObjLabelText", true);
        MinImageAreaOfLabelEvent = PreferenceParser.ParseIntValue(Preferences, "MinImageAreaOfLabelEvent", 50000);
        
        base.Initialize();

        if (File.Exists(LLMPromptFile))
        {
            userPrompt = File.ReadAllText(LLMPromptFile);
        }

        return true;
    }

    public void SetSubscriber(ISubscriber<ObjectExpiredEvent> subscriber)
    {
        _objectExpiredEventPublisher = subscriber;
        _disposableOeSubscriber = _objectExpiredEventPublisher.Subscribe(ProcessEvent);
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        var isDetectionFrame = _frameCount % Stride == 0;
        _frameCount++;

        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (detectedObject.Label.ToLower() != "boat")
            {
                continue;
            }

            // 按以下规则判定是否需要调用 LLM 进行推理
            // 1. _cachedShipLabels 中是否有指定 detectedObject.Id 的缓存，如果没有，则调用 LLM 进行推理
            // 2. 如果 _cachedShipLabels 中有指定 detectedObject.Id 的缓存，则判断当前 detectedObject.Confidence 是否大于缓存的置信度，若大于则调用 LLM 进行推理，否则继续使用缓存标签，减少计算量
            // 3. 需要调用 LLM 的，只需要设置 detectedObject.SetProperty(LLMAnalysisPropertyName, true); detectedObject.SetProperty(LLMAnalysisPromptPropertyName, userPrompt);

            var shouldRunLLM = false;
            if (!_cachedShipLabels.TryGetValue(detectedObject.Id, out var shipLabels))
            {
                shouldRunLLM = true;
            }
            else if (isDetectionFrame && detectedObject.Confidence > shipLabels.Confidence)
            {
                shouldRunLLM = true;
            }

            if (shouldRunLLM)
            {
                detectedObject.SetProperty(LLMAnalysisPropertyName, true);

                frame.SetProperty(LLMAnalysisPropertyName, true);
                frame.SetProperty(LLMAnalysisPromptPropertyName, userPrompt);
            }
            else
            {
                detectedObject.SetProperty("ShipLabel", shipLabels);
                GenerateObjectLabelAnnotation(frame, detectedObject);
            }
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

            var shipLabels = detectedObject.GetProperty<ShipLabel>("ShipLabel");

            // type annotation
            var textType = new Shape()
            {
                Id = "text_label_type_" + detectedObject.Id,
                Type = "text",
                //Content = $"Id:{detectedObject.LocalId},T:{shipLabels.ShipType},C:{string.Join(',', shipLabels.ShipColor)},D:{shipLabels.ShipDraught}",
                Content = $"Type:{shipLabels.ShipType}",
                Position = new Position()
                {
                    X = bbox.X,
                    Y = bbox.Y - 3 * base.ObjTextFontSize - 20
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
                //Content = $"Id:{detectedObject.LocalId},T:{shipLabels.ShipType},C:{string.Join(',', shipLabels.ShipColor)},D:{shipLabels.ShipDraught}",
                Content = $"Color:{string.Join(',', shipLabels.ShipColor)}",
                Position = new Position()
                {
                    X = bbox.X,
                    Y = bbox.Y - 2 * base.ObjTextFontSize - 10
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
                //Content = $"Id:{detectedObject.LocalId},T:{shipLabels.ShipType},C:{string.Join(',', shipLabels.ShipColor)},D:{shipLabels.ShipDraught}",
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

    public override void ProcessEvent(LLMInferenceResultEvent @event)
    {
        var shipLabel = JsonSerializer.Deserialize<ShipLabel>(@event.JsonResult);
        shipLabel.DetectedObjectId = @event.DetectedObjectId;
        shipLabel.Confidence = @event.Confidence;
        shipLabel.Frame = @event.Frame;
        shipLabel.Snapshot = @event.Snapshot;
        shipLabel.JsonLabel = @event.JsonResult;

        _cachedShipLabels.AddOrUpdate(
            @event.DetectedObjectId,
            shipLabel,
            (key, oldValue) => shipLabel
        );
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

        Log.Information("{ShipId} labels -> Type:{ShipType}, Colors:{ShipColors}, Draught:{ShipDraught}",
            @event.Id, shipLabels.ShipType, string.Join(',', shipLabels.ShipColor), shipLabels.ShipDraught);

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
            Content = $"Id:{@event.LocalId}, T:{shipLabels.ShipType}\nC:{string.Join(',', shipLabels.ShipColor)}\nD:{shipLabels.ShipDraught}",
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

                    //_shipLabelEventPublisher.Publish(shipLabelEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing event {EventName}", EventName);
            }
        });
    }
}
