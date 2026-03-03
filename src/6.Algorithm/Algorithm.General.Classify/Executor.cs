using Algorithm.Common;
using Algorithm.General.Classify.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Event.SnapshotManager;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using SkiaSharp;
using System.Text.Json;
using YoloDotNet;
using YoloDotNet.Enums;
using YoloDotNet.ExecutionProvider.Cuda;
using YoloDotNet.Models;

namespace Algorithm.General.Classify;

public class Executor : AlgorithmBase
{
    public const string DefaultModelPath = "Models/ship_cls_l.onnx";
    public const string DefaultExecutionProvider = "cpu";
    public const int DefaultDeviceId = 0;
    public const int DefaultStride = 1;

    public string ModelPath { get; private set; }
    public string ExecProvider { get; private set; }
    public int DeviceId { get; private set; }
    public int Stride { get; private set; }
    public bool WillGenerateObjLabelText { get; private set; }

    private Yolo _predictor;

    private IPublisher<ObjectClassifiedEvent> _classifyEventPublisher;

    private ISubscriber<ObjectBestSnapshotCreatedEvent> _objBeastSnapshotSubscriber;
    private IDisposable _disposableObjSnapshotSubscriber;

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences) 
        : base(pipeline, preferences)
    {
        AlgorithmName = "Classify Object";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Classify object in video frames.";
    }

    public override bool Initialize()
    {
        var provider = Pipeline.Provider;
        _classifyEventPublisher = provider.GetRequiredService<IPublisher<ObjectClassifiedEvent>>();

        _objBeastSnapshotSubscriber = provider.GetRequiredService<ISubscriber<ObjectBestSnapshotCreatedEvent>>();
        _disposableObjSnapshotSubscriber = _objBeastSnapshotSubscriber.Subscribe(ProcessObjectBestSnapshotEvent);

        ModelPath = PreferenceParser.ParseStringValue(Preferences, "ModelPath", DefaultModelPath);
        ExecProvider = PreferenceParser.ParseStringValue(Preferences, "ExecutionProvider", DefaultExecutionProvider);
        DeviceId = PreferenceParser.ParseIntValue(Preferences, "DeviceId", DefaultDeviceId);
        Stride = PreferenceParser.ParseIntValue(Preferences, "Stride", DefaultStride);
        WillGenerateObjLabelText = PreferenceParser.ParseBoolValue(Preferences, "WillGenerateObjLabelText", true);

        var yoloOptions = new YoloOptions()
        {
            //OnnxModel = _modelPath,
            ImageResize = ImageResize.Proportional,
            SamplingOptions = new(SKFilterMode.Nearest, SKMipmapMode.None)
        };

        switch (ExecProvider.ToLower())
        {
            case "cpu":
            case "cuda":
                yoloOptions.ExecutionProvider = new CudaExecutionProvider(ModelPath, DeviceId);
                break;
            // case "coreML":
            //     yoloOptions.ExecutionProvider = new CoreMLExecutionProvider(ModelPath);
            //     break;
            default:
                yoloOptions.ExecutionProvider = new CudaExecutionProvider(ModelPath, DeviceId);
                break;
        }

        _predictor?.Dispose();
        _predictor = new Yolo(yoloOptions);

        return base.Initialize();
    }

    public override AnalysisResult Analyze(Frame frame)
    {
        frame.Retain();

        foreach (var detectedObject in frame.DetectedObjects)
        {
            if (!detectedObject.IsUnderAnalysis)
            {
                continue;
            }

            if (detectedObject.Snapshot != null)
            {
                using SKBitmap snapshot = detectedObject.Snapshot.ToSKBitmap();
                var classification = _predictor.RunClassification(snapshot, classes: 1);

                detectedObject.SetProperty("classify_label", classification[0].Label);
                detectedObject.SetProperty("classify_conf", classification[0].Confidence);
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

            var label = detectedObject.GetProperty<string>("classify_label");
            var conf = detectedObject.GetProperty<double>("classify_conf");

            // text annotation
            var text = new Shape()
            {
                Id = "text_label_" + detectedObject.Id,
                Type = "text",
                Content = $"Id:{detectedObject.LocalId},C:{label}:{conf:F2}",
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

            annotation.Shapes.Add(text);
        }

        return annotation;
    }

    private void ProcessClassifyEvent(Frame frame, DetectedObject detectedObject, List<Classification> classification)
    {
        if (!WillPublishEventMessage) return;

        if (CheckLocalEventInterval()) return;

        Log.Information("{DetectedObjectId} classify to '{Label}' with conf:{Confidence:F4}", detectedObject.Id, classification[0].Label, classification[0].Confidence);

        // 1. Create event
        var classifyEvent = new ObjectClassifiedEvent(
            sourceId: frame.SourceId,
            eventName: EventName,
            algorithmName: AlgorithmName,
            objectId: detectedObject.Id,
            label: classification[0].Label,
            conf: classification[0].Confidence);

        // 2. Serialize Annotations (Synchronously)
        var annotationJson = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        classifyEvent.Annotations = annotationJson;

        // 3. Prepare Snapshot (Synchronously - critical for thread safety)
        Mat? snapshot = null;
        if (WillSaveEventSnapshot)
        {
            // Clone the scene because frame.Scene might be disposed/reused in the main loop
            snapshot = frame.Scene.Clone();
        }

        var frameId = frame.FrameId;

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
                        string imagePath = Path.Combine(savePath, $"objectClassify_{now}.jpg");
                        snapshot.SaveImage(imagePath);

                        string annotationPath = Path.Combine(savePath, $"objectClassify_{now}.json");
                        await File.WriteAllTextAsync(annotationPath, annotationJson);

                        classifyEvent.ImageLocalPath = imagePath;
                        classifyEvent.ImageJsonLocalPath = annotationPath;
                    }

                    await EventRepository.SaveDomainEventAsync(classifyEvent);
                    MessagePoster.PostDomainEventMessage(classifyEvent);

                    _classifyEventPublisher.Publish(classifyEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing object classify event {EventName}", EventName);
            }
        });
    }

    public void ProcessObjectBestSnapshotEvent(ObjectBestSnapshotCreatedEvent @event)
    {
        if (@event.ObjectSnapshot != null)
        {
            using SKBitmap snapshot = @event.ObjectSnapshot.ToSKBitmap();
            var classification = _predictor.RunClassification(snapshot, classes: 1);
            
            @event.DetectedObject.SetProperty("classify_label", classification[0].Label);
            @event.DetectedObject.SetProperty("classify_conf", classification[0].Confidence);

            //Log.Warning("ObjId:{eventId} with detection conf: {score}, has classified to {label} with conf: {conf}", @event.DetectedObject.Id, @event.Score, classification[0].Label, classification[0].Confidence);
            
            ProcessClassifyEvent(@event.Frame, @event.DetectedObject, classification);
        }
    }

    public override void Dispose()
    {
        _disposableObjSnapshotSubscriber?.Dispose();
        _predictor?.Dispose();
        base.Dispose();
    }
}