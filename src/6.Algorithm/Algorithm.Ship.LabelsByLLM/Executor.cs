using Algorithm.Common;
using Algorithm.Common.Event;
using Algorithm.Common.LLM;
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

public class Executor : AlgorithmBase, IEventSubscriber<ObjectExpiredEvent>, ILLMResultHandler
{
    private const int DefaultStride = 5;   

    public int Stride { get; private set; }
    public bool WillGenerateObjLabelText { get; private set; }
    public int MinImageAreaOfLabelEvent { get; private set; }
    public int ObjectExpiredWaitSeconds { get; private set; }
    public LLMTimeoutPolicy ObjectExpiredTimeoutPolicy { get; private set; }
    public string RequesterAlgorithmName => AlgorithmName;

    private int _frameCount = 0;

    // objectId -> verification lifecycle state
    private readonly ConcurrentDictionary<string, ObjectVerificationState> _objectStates = new();

    private sealed class ObjectVerificationState
    {
        public string ObjectId { get; init; } = string.Empty;
        public string SourceId { get; init; } = string.Empty;
        public long BestFrameId { get; set; }
        public float BestDetectorConfidence { get; set; }
        public double BestQualityScore { get; set; }
        public string? PendingRequestId { get; set; }
        public string? LatestResultRequestId { get; set; }
        public ShipLabel? VerifiedPayload { get; set; }
        public bool IsExpired { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public DateTime? ExpiredAtUtc { get; set; }
        public DateTime? FinalizeDeadlineUtc { get; set; }
        public bool IsFinalized { get; set; }
        public ObjectExpiredEvent? ExpiredEvent { get; set; }
    }

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
        ObjectExpiredWaitSeconds = PreferenceParser.ParseIntValue(Preferences, "ObjectExpiredWaitSeconds", 8);
        ObjectExpiredTimeoutPolicy = ParseTimeoutPolicy(PreferenceParser.ParseStringValue(
            Preferences,
            "ObjectExpiredTimeoutPolicy",
            LLMTimeoutPolicy.Drop.ToString()));
        
        return base.Initialize(); ;
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

            var state = _objectStates.GetOrAdd(detectedObject.Id, _ => new ObjectVerificationState
            {
                ObjectId = detectedObject.Id,
                SourceId = frame.SourceId,
                LastSeenUtc = frame.UtcTimeStamp
            });

            ShipLabel? shipLabels = null;
            var qualityScore = LLMEvidenceBuilder.CalculateObjectEvidenceQuality(detectedObject, frame.Scene.Width, frame.Scene.Height);
            var shouldRunLLM = false;

            lock (state)
            {
                state.LastSeenUtc = frame.UtcTimeStamp;

                if (state.VerifiedPayload == null && state.PendingRequestId == null)
                {
                    shouldRunLLM = true;
                }
                else if (isDetectionFrame && qualityScore > state.BestQualityScore + 0.03)
                {
                    shouldRunLLM = true;
                }

                if (shouldRunLLM)
                {
                    var requestId = Guid.NewGuid().ToString("N");
                    state.PendingRequestId = requestId;
                    state.BestFrameId = frame.FrameId;
                    state.BestDetectorConfidence = detectedObject.Confidence;
                    state.BestQualityScore = qualityScore;

                    detectedObject.SetProperty(LLMRequestIdPropertyName, requestId);
                    detectedObject.SetProperty(LLMRequesterAlgorithmNamePropertyName, AlgorithmName);
                    detectedObject.SetProperty(LLMQueuePolicyPropertyName, LLMQueuePolicy.LatestBestPerObject.ToString());
                }
                else
                {
                    shipLabels = state.VerifiedPayload;
                }
            }

            if (shouldRunLLM)
            {
                detectedObject.SetProperty(LLMAnalysisPropertyName, true);

                frame.SetProperty(LLMAnalysisPropertyName, true);
                frame.SetProperty(LLMAnalysisType, "object");
                frame.SetProperty(LLMAnalysisPromptPropertyName, _userPrompt);
                frame.SetProperty(LLMRequesterAlgorithmNamePropertyName, AlgorithmName);
            }
            else if (shipLabels != null)
            {
                detectedObject.SetProperty("ShipLabel", shipLabels);
                GenerateObjectLabelAnnotation(frame, detectedObject);
            }
        }

        ProcessExpiredWaitTimeouts();
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
                Content = $"Type:{shipLabels.ShipTypeGroup}|{shipLabels.ShipTypeDetail}",
                Position = new Position()
                {
                    X = bbox.X,
                    Y = bbox.Y - 6 * base.ObjTextFontSize - 10
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
                    Y = bbox.Y - 5 * base.ObjTextFontSize - 10
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
                    Y = bbox.Y - 4 * base.ObjTextFontSize - 10
                },
                Style = new Style()
                {
                    Color = base.ObjTextColor,
                    FontSize = base.ObjTextFontSize,
                }
            };

            annotation.Shapes.Add(draughtColor);

            // ShipViewAngle annotation
            if (!string.IsNullOrWhiteSpace(shipLabels.ShipViewAngle))
            {
                var angleColor = new Shape()
                {
                    Id = "text_label_angle_" + detectedObject.Id,
                    Type = "text",
                    Content = $"ViewAngle:{shipLabels.ShipViewAngle}",
                    Position = new Position()
                    {
                        X = bbox.X,
                        Y = bbox.Y - 3 * base.ObjTextFontSize - 10
                    },
                    Style = new Style()
                    {
                        Color = base.ObjTextColor,
                        FontSize = base.ObjTextFontSize,
                    }
                };
                annotation.Shapes.Add(angleColor);
            }

            // ShipLoadTypes annotation
            if (shipLabels.ShipLoadTypes != null && shipLabels.ShipLoadTypes.Count > 0)
            {
                var loadTypeColor = new Shape()
                {
                    Id = "text_label_loadtype_" + detectedObject.Id,
                    Type = "text",
                    Content = $"LoadType:{string.Join(',', shipLabels.ShipLoadTypes)}",
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
                annotation.Shapes.Add(loadTypeColor);
            }

            // ShipPaintedText annotation
            if (shipLabels.ShipPaintedText != null && shipLabels.ShipPaintedText.Count > 0 
                                                   && !string.IsNullOrWhiteSpace(shipLabels.ShipPaintedText[0].Text))
            {
                var paintedTextColor = new Shape()
                {
                    Id = "text_label_paintedtext_" + detectedObject.Id,
                    Type = "text",
                    Content = $"PaintedText:{shipLabels.ShipPaintedText}",
                    Position = new Position()
                    {
                        X = bbox.X,
                        Y = bbox.Y + - base.ObjTextFontSize
                    },
                    Style = new Style()
                    {
                        Color = base.ObjTextColor,
                        FontSize = base.ObjTextFontSize,
                    }
                };
                annotation.Shapes.Add(paintedTextColor);
            }
        }

        return annotation;
    }

    // LLM 推理完毕结果事件处理
    public override void ProcessEvent(LLMInferenceResultEvent @event)
    {
        if (@event.RequesterAlgorithmName != AlgorithmName || string.IsNullOrWhiteSpace(@event.DetectedObjectId))
        {
            DisposeSnapshot(@event.Snapshot);
            return;
        }

        if (!TryCreateShipLabel(@event, out var shipLabel))
        {
            DisposeSnapshot(@event.Snapshot);
            return;
        }

        var state = _objectStates.GetOrAdd(@event.DetectedObjectId, _ => new ObjectVerificationState
        {
            ObjectId = @event.DetectedObjectId,
            SourceId = @event.SourceId
        });

        ObjectExpiredEvent? expiredEventToFinalize = null;
        ShipLabel? labelToFinalize = null;

        lock (state)
        {
            if (state.IsFinalized)
            {
                DisposeSnapshot(shipLabel?.Snapshot);
                return;
            }

            DisposeSnapshot(state.VerifiedPayload?.Snapshot);
            state.VerifiedPayload = shipLabel;
            state.PendingRequestId = null;
            state.LatestResultRequestId = @event.RequestId;
            state.BestDetectorConfidence = Math.Max(state.BestDetectorConfidence, @event.Confidence);
            state.BestFrameId = @event.FrameId;

            if (state.IsExpired && state.ExpiredEvent != null)
            {
                var nowUtc = DateTime.UtcNow;
                if (state.FinalizeDeadlineUtc == null || nowUtc <= state.FinalizeDeadlineUtc)
                {
                    expiredEventToFinalize = state.ExpiredEvent;
                    labelToFinalize = state.VerifiedPayload;
                    state.IsFinalized = true;
                }
                else
                {
                    DisposeSnapshot(state.VerifiedPayload?.Snapshot);
                    state.VerifiedPayload = null;
                    state.IsFinalized = true;
                }
            }
        }

        if (expiredEventToFinalize != null && labelToFinalize != null)
        {
            try
            {
                ProcessShipLabelEvent(expiredEventToFinalize, labelToFinalize);
            }
            finally
            {
                _objectStates.TryRemove(@event.DetectedObjectId, out _);
                DisposeSnapshot(labelToFinalize.Snapshot);
            }
        }
    }

    // 对象过期事件处理，清理缓存并根据缓存信息生成事件消息
    public void ProcessEvent(ObjectExpiredEvent @event)
    {
        var objectId = @event.Id;

        if (!_objectStates.TryGetValue(objectId, out var state))
        {
            return;
        }

        ShipLabel? labelToFinalize = null;
        lock (state)
        {
            if (state.IsFinalized)
            {
                return;
            }

            state.IsExpired = true;
            state.ExpiredAtUtc = DateTime.UtcNow;
            state.ExpiredEvent = @event;

            if (state.VerifiedPayload != null)
            {
                labelToFinalize = state.VerifiedPayload;
                state.IsFinalized = true;
            }
            else if (state.PendingRequestId != null)
            {
                state.FinalizeDeadlineUtc = DateTime.UtcNow.AddSeconds(ObjectExpiredWaitSeconds);
                Log.Information("Ship object expired and is waiting for LLM result. ObjectId: {ObjectId}, PendingRequestId: {PendingRequestId}, DeadlineUtc: {DeadlineUtc}",
                    objectId,
                    state.PendingRequestId,
                    state.FinalizeDeadlineUtc);
            }
            else
            {
                state.IsFinalized = true;
            }
        }

        if (labelToFinalize != null)
        {
            Log.Information("Finalize ship label after object expiration. ObjectId: {ObjectId}", objectId);
            try
            {
                ProcessShipLabelEvent(@event, labelToFinalize);
            }
            finally
            {
                _objectStates.TryRemove(objectId, out _);
                DisposeSnapshot(labelToFinalize.Snapshot);
            }
        }
        else if (state.IsFinalized)
        {
            _objectStates.TryRemove(objectId, out _);
        }
    }

    private void ProcessShipLabelEvent(ObjectExpiredEvent @event, ShipLabel shipLabels)
    {
        if (!WillPublishEventMessage) return;

        var snapshotArea = shipLabels.Snapshot.Width * shipLabels.Snapshot.Height;
        if (snapshotArea < MinImageAreaOfLabelEvent) return;

        Log.Information("{ShipId} labels -> Type:{ShipType}, Detail:{ShipDetail}, Colors:{ShipColors}, Draught:{ShipDraught}",
            @event.Id, shipLabels.ShipTypeGroup, shipLabels.ShipTypeDetail, string.Join(',', shipLabels.ShipColor), shipLabels.ShipDraught);

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
        var visualAnnotation = new VisualAnnotation(shipLabels.SourceId, shipLabels.UtcTimeStamp, shipLabels.FrameId, shipLabels.Snapshot.Width,
            shipLabels.Snapshot.Height);

        var text = new Shape()
        {
            Id = "text_label_" + @event.Id,
            Type = "text",
            Content = $"Id:{@event.LocalId}, T:{shipLabels.ShipTypeGroup}|{shipLabels.ShipTypeDetail}\nC:{string.Join(',', shipLabels.ShipColor)}\nD:{shipLabels.ShipDraught}",
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
            snapshot = shipLabels.Snapshot.Clone();
        }

        // 4. Async Saving
        string now = DateTime.Now.ToString("yyyyMMddhhmmss");
        Task.Run(async () =>
        {
            try
            {
                using (snapshot)
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

    private bool TryCreateShipLabel(LLMInferenceResultEvent @event, out ShipLabel? shipLabel)
    {
        shipLabel = null;

        if (string.IsNullOrWhiteSpace(@event.JsonResult))
        {
            Log.Warning("LLM returned empty result. SourceId: {SourceId}, ObjectId: {ObjectId}, Model: {ModelName}",
                @event.SourceId, @event.DetectedObjectId, @event.ModelName);
            return false;
        }

        if (@event.Snapshot == null || @event.Snapshot.IsDisposed)
        {
            Log.Warning("LLM ship label result has no snapshot. SourceId: {SourceId}, ObjectId: {ObjectId}, Model: {ModelName}",
                @event.SourceId, @event.DetectedObjectId, @event.ModelName);
            return false;
        }

        try
        {
            // 移除 @event.JsonResult 前后可能有的 '''json ''' Markdown 格式
            var json = @event.JsonResult.TrimStart("'''json").TrimEnd("'''");
            shipLabel = JsonSerializer.Deserialize<ShipLabel>(json);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Failed to deserialize ship label JSON. SourceId: {SourceId}, ObjectId: {ObjectId}, Model: {ModelName}, Result: {Result}",
                @event.SourceId, @event.DetectedObjectId, @event.ModelName, @event.JsonResult);
            return false;
        }
        catch (NotSupportedException ex)
        {
            Log.Warning(ex, "Unsupported ship label payload. SourceId: {SourceId}, ObjectId: {ObjectId}, Model: {ModelName}, Result: {Result}",
                @event.SourceId, @event.DetectedObjectId, @event.ModelName, @event.JsonResult);
            return false;
        }

        if (shipLabel == null)
        {
            Log.Warning("LLM ship label payload resolved to null. SourceId: {SourceId}, ObjectId: {ObjectId}, Model: {ModelName}, Result: {Result}",
                @event.SourceId, @event.DetectedObjectId, @event.ModelName, @event.JsonResult);
            return false;
        }

        shipLabel.DetectedObjectId = @event.DetectedObjectId;
        shipLabel.Confidence = @event.Confidence;
        shipLabel.SourceId = @event.SourceId;
        shipLabel.FrameId = @event.FrameId;
        shipLabel.UtcTimeStamp = @event.UtcTimeStamp;
        shipLabel.Snapshot = @event.Snapshot;
        shipLabel.JsonLabel = @event.JsonResult;

        // shipLabel.ShipType = string.IsNullOrWhiteSpace(shipLabel.ShipType) ? "Unknown" : shipLabel.ShipType;
        // shipLabel.ShipDraught = string.IsNullOrWhiteSpace(shipLabel.ShipDraught) ? "Unknown" : shipLabel.ShipDraught;
        // shipLabel.ShipColor = shipLabel.ShipColor?
        //     .Where(color => !string.IsNullOrWhiteSpace(color))
        //     .ToList() ?? new List<string>();
        //
        // if (shipLabel.ShipColor.Count == 0)
        // {
        //     shipLabel.ShipColor = new List<string> { "Unknown" };
        // }

        return true;
    }

    private void ProcessExpiredWaitTimeouts()
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var (objectId, state) in _objectStates.ToArray())
        {
            ShipLabel? payloadToDispose = null;
            lock (state)
            {
                if (!state.IsExpired ||
                    state.IsFinalized ||
                    state.FinalizeDeadlineUtc == null ||
                    nowUtc <= state.FinalizeDeadlineUtc)
                {
                    continue;
                }

                Log.Warning("Ship label verification timed out after object expired. ObjectId: {ObjectId}, PendingRequestId: {PendingRequestId}",
                    objectId, state.PendingRequestId);
                if (ObjectExpiredTimeoutPolicy == LLMTimeoutPolicy.PublishUnknown && state.ExpiredEvent != null)
                {
                    PublishUnknownShipLabelEvent(state.ExpiredEvent);
                }

                state.IsFinalized = true;
                payloadToDispose = state.VerifiedPayload;
                state.VerifiedPayload = null;
            }

            _objectStates.TryRemove(objectId, out _);
            DisposeSnapshot(payloadToDispose?.Snapshot);
        }
    }

    public bool CanHandle(LLMAnalysisResult result)
    {
        return result.RequesterAlgorithmName == AlgorithmName &&
               !string.IsNullOrWhiteSpace(result.ObjectId) &&
               result.Scope == LLMAnalysisScope.Object;
    }

    public Task HandleAsync(LLMAnalysisResult result, LLMReconcileContext context, CancellationToken cancellationToken)
    {
        var inferenceEvent = LLMInferenceResultEvent.FromAnalysisResult(result, EventName);
        inferenceEvent.QueuePolicy = LLMQueuePolicy.LatestBestPerObject;
        ProcessEvent(inferenceEvent);
        return Task.CompletedTask;
    }

    private void PublishUnknownShipLabelEvent(ObjectExpiredEvent expiredEvent)
    {
        var unknownLabel = new ShipLabel
        {
            DetectedObjectId = expiredEvent.Id,
            SourceId = expiredEvent.SourceId,
            ShipTypeGroup = "Unknown",
            ShipTypeDetail = "Unknown",
            ShipDraught = "Unknown",
            ShipColor = new List<string> { "Unknown" },
            JsonLabel = "{\"status\":\"unknown\"}"
        };

        var shipLabelEvent = new ShipLabelEvent(
            sourceId: expiredEvent.SourceId,
            eventName: EventName,
            algorithmName: AlgorithmName,
            objectId: expiredEvent.Id,
            objectLocalId: expiredEvent.LocalId,
            confidence: 0,
            labels: unknownLabel);

        Task.Run(async () =>
        {
            try
            {
                await EventRepository.SaveDomainEventAsync(shipLabelEvent);
                MessagePoster.PostDomainEventMessage(shipLabelEvent);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error publishing unknown ship label timeout event. ObjectId: {ObjectId}", expiredEvent.Id);
            }
        });
    }

    private static LLMTimeoutPolicy ParseTimeoutPolicy(string value)
    {
        return Enum.TryParse<LLMTimeoutPolicy>(value, ignoreCase: true, out var policy)
            ? policy
            : LLMTimeoutPolicy.Drop;
    }

    public override void Dispose()
    {
        _disposableOeSubscriber?.Dispose();

        foreach (var state in _objectStates.Values)
        {
            DisposeSnapshot(state.VerifiedPayload?.Snapshot);
        }

        _objectStates.Clear();
        base.Dispose();
    }

    private static void DisposeSnapshot(Mat? snapshot)
    {
        if (snapshot == null || snapshot.IsDisposed)
        {
            return;
        }

        snapshot.Dispose();
    }
}
