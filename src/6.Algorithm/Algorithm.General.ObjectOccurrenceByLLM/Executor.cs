using Algorithm.Common;
using Algorithm.Common.Event;
using Algorithm.Common.LLM;
using Algorithm.General.ObjectOccurrenceByLLM.Event;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Entity.Annotation;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline;
using Serilog;
using System.Text.Json;

namespace Algorithm.General.ObjectOccurrenceByLLM;

public class Executor : LlmAlgorithmBase
{
    public const string DefaultOccurrenceCheckRegionName = "Occurrence Region";
    public const string DefaultTargetObjectNames = "person";
    public const string DefaultOccurrenceCondition = "or";
    public const bool DefaultEnableProximityCheck = false;
    public const float DefaultProximityThresholdRatio = 1.5f;
    public const string DefaultProximityReferenceObject = "person";
    public const string DefaultProximityReferenceDimension = "width"; // width or height
    public const int DefaultMinDurationFrames = 3;

    public string OccurrenceCheckRegionName { get; private set; } = string.Empty;
    public HashSet<string> TargetObjectNames { get; private set; } = [];
    public string OccurrenceCondition { get; private set; } = string.Empty;
    public bool EnableProximityCheck { get; private set; }
    public string ProximityObject1Label { get; private set; } = string.Empty;
    public string ProximityObject2Label { get; private set; } = string.Empty;
    public float ProximityThresholdRatio { get; private set; }
    public string ProximityReferenceObject { get; private set; } = string.Empty;
    public string ProximityReferenceDimension { get; private set; } = string.Empty;
    public int MinDurationFrames { get; private set; }
    public int CandidateEventTimeoutSeconds { get; private set; }
    public LLMTimeoutPolicy OnTimeout { get; private set; }

    private int _continuousOccurrenceFrames;
    private readonly CandidateEventStore _candidateEvents = new();
    private readonly Dictionary<string, ObjectOccurrenceCandidatePayload> _candidatePayloads = new();
    private readonly object _candidateSync = new();
    private string? _activeCandidateEventId;
    private IPublisher<ObjectOccurrenceLLMEvent> _occurrenceEventPublisher = null!;

    private sealed record ObjectOccurrenceCandidatePayload(
        int DurationFrames,
        List<string> TargetObjectNames,
        string Annotations,
        long FrameId,
        Mat? Snapshot);

    public Executor(AnalysisPipeline pipeline, Dictionary<string, string> preferences) 
        : base(pipeline, preferences)
    {
        AlgorithmName = "Object Occurrence by LLM";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Detects object occurrence in video frames using LLM inference.";
    }

    protected override void InitializeCore()
    {
        _occurrenceEventPublisher =
            Services.GetRequiredService<IPublisher<ObjectOccurrenceLLMEvent>>();

        OccurrenceCheckRegionName = PreferenceParser.ParseStringValue(Preferences, "OccurrenceCheckRegionName", DefaultOccurrenceCheckRegionName);

        var objectNames = PreferenceParser.ParseStringValue(Preferences, "TargetObjectNames", DefaultTargetObjectNames);
        TargetObjectNames = new HashSet<string>(objectNames.Split(',').Select(name => name.Trim().ToLower()));

        OccurrenceCondition = PreferenceParser.ParseStringValue(Preferences, "OccurrenceCondition", DefaultOccurrenceCondition);

        EnableProximityCheck = PreferenceParser.ParseBoolValue(Preferences, "EnableProximityCheck", DefaultEnableProximityCheck);
        if (EnableProximityCheck)
        {
            ProximityObject1Label = PreferenceParser.ParseStringValue(Preferences, "ProximityObject1Label", "").ToLower();
            ProximityObject2Label = PreferenceParser.ParseStringValue(Preferences, "ProximityObject2Label", "").ToLower();
            ProximityThresholdRatio = PreferenceParser.ParseFloatValue(Preferences, "ProximityThresholdRatio", DefaultProximityThresholdRatio);
            ProximityReferenceObject = PreferenceParser.ParseStringValue(Preferences, "ProximityReferenceObject", DefaultProximityReferenceObject).ToLower();
            ProximityReferenceDimension = PreferenceParser.ParseStringValue(Preferences, "ProximityReferenceDimension", DefaultProximityReferenceDimension).ToLower();
        }

        MinDurationFrames = PreferenceParser.ParseIntValue(Preferences, "MinDurationFrames", DefaultMinDurationFrames);
        CandidateEventTimeoutSeconds = PreferenceParser.ParseIntValue(Preferences, "CandidateEventTimeoutSeconds", 12);
        OnTimeout = ParseTimeoutPolicy(PreferenceParser.ParseStringValue(Preferences, "OnTimeout", LLMTimeoutPolicy.Drop.ToString()));
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        var regionManager = RegionManagers.First(rm => rm.SourceId == frame.SourceId);
        var definition = regionManager.RegionDefinition;

        var interestArea = definition.InterestAreas.FirstOrDefault(ia => ia.Name == OccurrenceCheckRegionName);
        if (interestArea == null)
        {
            // 处理未找到兴趣区域的情况
            return new AnalysisResult(false);
        }

        // 筛选出目标对象
        var targetObjects = frame.DetectedObjects
            .Where(o => o.IsUnderAnalysis && TargetObjectNames.Contains(o.Label.ToLower()))
            .ToList();

        // 检查目标对象是否在兴趣区域内
        targetObjects.Where(o =>
        {
            var objectCenter = new NormalizedPoint(frame.Scene.Width, frame.Scene.Height, (int)o.CenterX, (int)o.CenterY);
            return interestArea.IsPointInPolygon(objectCenter);
        })
            .ToList()
            .ForEach(o =>
            {
                GenerateDetectedObjectAnnotation(frame, o);
                o.SetProperty("Occurrence", true);
            });

        // 检查是否发生了目标对象出现
        bool isOccurrence = false;
        if (OccurrenceCondition == "or")
        {
            isOccurrence = targetObjects.Any(o => o.GetProperty<bool>("Occurrence"));
        }
        else if (OccurrenceCondition == "and")
        {
            isOccurrence = targetObjects.Any() && targetObjects.All(o => o.GetProperty<bool>("Occurrence"));
        }

        // 检查对象间距离
        if (isOccurrence && EnableProximityCheck)
        {
            var occurrenceObjects = targetObjects
                .Where(o => o.GetProperty<bool>("Occurrence"))
                .ToList();

            var group1 = occurrenceObjects
                .Where(o => o.Label.ToLower() == ProximityObject1Label)
                .ToList();
            var group2 = occurrenceObjects
                .Where(o => o.Label.ToLower() == ProximityObject2Label)
                .ToList();

            bool proximityMet = false;

            // 遍历所有可能的配对
            foreach (var obj1 in group1)
            {
                foreach (var obj2 in group2)
                {
                    if (obj1 == obj2) continue; // 避免自引用（如果类型相同）

                    var distance = Math.Sqrt(Math.Pow(obj1.CenterX - obj2.CenterX, 2) + Math.Pow(obj1.CenterY - obj2.CenterY, 2));

                    var referenceObj = ProximityReferenceObject == obj2.Label.ToLower() ? obj2 : obj1;
                    var referenceDimension = ProximityReferenceDimension == "height" ? referenceObj.Height : referenceObj.Width;
                    var threshold = referenceDimension * ProximityThresholdRatio;

                    if (distance < threshold)
                    {
                        proximityMet = true;
                        // 标记满足条件的对象
                        obj1.SetProperty("ProximityMet", obj2.Id);
                        obj2.SetProperty("ProximityMet", obj1.Id);
                    }
                }
            }

            if (!proximityMet)
            {
                isOccurrence = false;
            }
        }

        // 根据持续时间判断是否最终认定为对象出现
        if (isOccurrence)
        {
            _continuousOccurrenceFrames++;

            if (MinDurationFrames <= 0 || _continuousOccurrenceFrames >= MinDurationFrames)
            {
                frame.SetProperty("ObjectOccurrence", true);
                ProcessObjectOccurrenceEvent(frame, _continuousOccurrenceFrames);
            }
        }
        else
        {
            _continuousOccurrenceFrames = 0;
            _activeCandidateEventId = null;
        }

        ProcessTimedOutCandidates();

        // 绘制区域与分析结果标注
        GenerateRegionAnnotation(frame, definition);

        return new AnalysisResult(true);
    }

    private void ProcessObjectOccurrenceEvent(Frame frame, int durationFrames)
    {
        if (!WillPublishEventMessage || !WillPerformLlmAnalysis) return;

        if (!string.IsNullOrWhiteSpace(_activeCandidateEventId))
        {
            return;
        }

        var candidateEventId = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var nowUtc = DateTime.UtcNow;
        var deadlineUtc = nowUtc.AddSeconds(CandidateEventTimeoutSeconds);
        var annotations = JsonSerializer.Serialize(frame.Annotation, DomainEvent.JsonOptions);
        Mat? snapshot = null;
        if (WillSaveEventSnapshot)
        {
            snapshot = frame.Scene.Clone();
        }

        var state = new CandidateEventState
        {
            CandidateEventId = candidateEventId,
            SourceId = frame.SourceId,
            FrameId = frame.FrameId,
            OffsetMilliSec = frame.OffsetMilliSec,
            UtcTimeStamp = frame.UtcTimeStamp,
            AlgorithmName = AlgorithmName,
            EventName = EventName,
            Status = CandidateEventStatus.PendingLLM,
            PendingRequestId = requestId,
            CreatedAtUtc = nowUtc,
            DeadlineUtc = deadlineUtc,
            TraditionalPayload = durationFrames
        };

        lock (_candidateSync)
        {
            if (!_candidateEvents.TryAdd(state))
            {
                return;
            }

            _candidatePayloads[candidateEventId] = new ObjectOccurrenceCandidatePayload(
                durationFrames,
                TargetObjectNames.ToList(),
                annotations,
                frame.FrameId,
                snapshot);
            _activeCandidateEventId = candidateEventId;
        }

        // 调用 LLM 对结果进行二次确认，使用 EventAnchored 保护触发证据不被后续帧替换。
        MarkFrameForLlm(
            frame,
            new LlmRequestOptions
            {
                Scope = LLMAnalysisScope.Frame,
                QueuePolicy = LLMQueuePolicy.EventAnchored,
                RequestId = requestId,
                CandidateEventId = candidateEventId,
                ExpireAtUtc = deadlineUtc
            });
    }

    protected override bool CanHandleLlmResult(LLMInferenceResultEvent result)
    {
        return base.CanHandleLlmResult(result) &&
               !string.IsNullOrWhiteSpace(result.CandidateEventId) &&
               result.Scope == LLMAnalysisScope.Frame;
    }

    protected override void HandleLlmResult(LLMInferenceResultEvent result)
    {
        if (result.IsExpiredResult)
        {
            Log.Warning("Ignore late LLM result for candidate event. CandidateEventId: {CandidateEventId}, RequestId: {RequestId}",
                result.CandidateEventId, result.RequestId);
            return;
        }

        var occurrenceResult = DeserializeResult(result);
        if (occurrenceResult == null)
        {
            RejectCandidate(result);
            return;
        }

        if (occurrenceResult.isObjOccurred)
        {
            PublishConfirmedCandidate(result);
            return;
        }

        RejectCandidate(result);
    }

    private OccurredObjectsLLMResult? DeserializeResult(LLMInferenceResultEvent @event)
    {
        try
        {
            var json = LLMJsonSanitizer.StripMarkdownCodeFence(@event.JsonResult);
            return JsonSerializer.Deserialize<OccurredObjectsLLMResult>(json);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Failed to deserialize object occurrence LLM result. CandidateEventId: {CandidateEventId}, Result: {Result}",
                @event.CandidateEventId, @event.JsonResult);
            return null;
        }
    }

    private void PublishConfirmedCandidate(LLMInferenceResultEvent @event)
    {
        if (!_candidateEvents.TryConfirm(@event.CandidateEventId!, ToAnalysisResult(@event)))
        {
            return;
        }

        if (CheckLocalEventInterval())
        {
            Log.Information("Suppress confirmed object occurrence by local interval. CandidateEventId: {CandidateEventId}",
                @event.CandidateEventId);
            _candidateEvents.TryPublish(@event.CandidateEventId!);
            CleanupCandidate(@event.CandidateEventId!);
            return;
        }

        ObjectOccurrenceCandidatePayload? payload;
        lock (_candidateSync)
        {
            _candidatePayloads.TryGetValue(@event.CandidateEventId!, out payload);
        }

        if (payload == null)
        {
            Log.Warning("Candidate payload not found. CandidateEventId: {CandidateEventId}", @event.CandidateEventId);
            return;
        }

        if (!_candidateEvents.TryPublish(@event.CandidateEventId!))
        {
            return;
        }

        var eventMessage = new ObjectOccurrenceLLMEvent(
            @event.SourceId,
            EventName,
            AlgorithmName,
            @event.CandidateEventId!,
            OccurrenceCheckRegionName,
            payload.TargetObjectNames,
            payload.DurationFrames)
        {
            Annotations = payload.Annotations,
            LLMJsonResult = @event.JsonResult
        };

        Log.Warning("{Message}", eventMessage.Message);

        QueueOccurrenceEvent(eventMessage, payload);

        CleanupCandidate(@event.CandidateEventId!);
    }

    private void RejectCandidate(LLMInferenceResultEvent @event)
    {
        if (_candidateEvents.TryReject(@event.CandidateEventId!, ToAnalysisResult(@event)))
        {
            Log.Information("LLM rejected object occurrence candidate. CandidateEventId: {CandidateEventId}, RequestId: {RequestId}",
                @event.CandidateEventId, @event.RequestId);
            CleanupCandidate(@event.CandidateEventId!);
        }
    }

    private void ProcessTimedOutCandidates()
    {
        foreach (var candidate in _candidateEvents.ScanTimedOut(DateTime.UtcNow))
        {
            if (_candidateEvents.TryMarkTimedOut(candidate.CandidateEventId, DateTime.UtcNow))
            {
                Log.Warning("Object occurrence candidate timed out. CandidateEventId: {CandidateEventId}",
                    candidate.CandidateEventId);
                HandleTimedOutCandidate(candidate);
            }
        }
    }

    private void HandleTimedOutCandidate(CandidateEventState candidate)
    {
        switch (OnTimeout)
        {
            case LLMTimeoutPolicy.PublishTraditional:
            case LLMTimeoutPolicy.PublishUnknown:
                PublishTimeoutCandidate(candidate, OnTimeout);
                break;
            case LLMTimeoutPolicy.Retry:
                Log.Information("Object occurrence candidate timeout policy is Retry. CandidateEventId: {CandidateEventId}", candidate.CandidateEventId);
                break;
            case LLMTimeoutPolicy.Drop:
            default:
                Log.Information("Drop timed out object occurrence candidate. CandidateEventId: {CandidateEventId}", candidate.CandidateEventId);
                break;
        }

        CleanupCandidate(candidate.CandidateEventId);
    }

    private void PublishTimeoutCandidate(CandidateEventState candidate, LLMTimeoutPolicy timeoutPolicy)
    {
        ObjectOccurrenceCandidatePayload? payload;
        lock (_candidateSync)
        {
            _candidatePayloads.TryGetValue(candidate.CandidateEventId, out payload);
        }

        if (payload == null)
        {
            return;
        }

        var eventMessage = new ObjectOccurrenceLLMEvent(
            candidate.SourceId,
            EventName,
            AlgorithmName,
            candidate.CandidateEventId,
            OccurrenceCheckRegionName,
            payload.TargetObjectNames,
            payload.DurationFrames)
        {
            Annotations = payload.Annotations,
            LLMJsonResult = timeoutPolicy == LLMTimeoutPolicy.PublishUnknown ? "{\"status\":\"unknown\"}" : string.Empty
        };

        Log.Warning("{Message}", eventMessage.Message);

        QueueOccurrenceEvent(eventMessage, payload);
    }

    private void QueueOccurrenceEvent(
        ObjectOccurrenceLLMEvent eventMessage,
        ObjectOccurrenceCandidatePayload payload)
    {
        TryQueueEvent(
            new EventPublicationRequest<ObjectOccurrenceLLMEvent>
            {
                Event = eventMessage,
                AnnotationJson = payload.Annotations,
                CloneSnapshot = () =>
                    payload.Snapshot == null || payload.Snapshot.IsDisposed
                        ? null
                        : payload.Snapshot.Clone(),
                FrameId = payload.FrameId,
                FilePrefix = "objectOccurrenceLLM",
                StableArtifactId = eventMessage.CandidateEventId,
                PublishInProcess = @event =>
                    _occurrenceEventPublisher.Publish(@event),
                SaveSnapshot = WillSaveEventSnapshot,
                SaveVideoClip = WillSaveEventVideoClip
            });
    }

    protected override void DisposeCore()
    {
        lock (_candidateSync)
        {
            foreach (var payload in _candidatePayloads.Values)
            {
                payload.Snapshot?.Dispose();
            }

            _candidatePayloads.Clear();
            _activeCandidateEventId = null;
        }
    }

    private void CleanupCandidate(string candidateEventId)
    {
        lock (_candidateSync)
        {
            if (_candidatePayloads.Remove(candidateEventId, out var payload))
            {
                payload.Snapshot?.Dispose();
            }

            if (_activeCandidateEventId == candidateEventId)
            {
                _activeCandidateEventId = null;
            }
        }
    }

    private static LLMAnalysisResult ToAnalysisResult(LLMInferenceResultEvent @event)
    {
        return new LLMAnalysisResult(
            @event.RequestId,
            @event.RequesterAlgorithmName,
            @event.CandidateEventId,
            @event.SourceId,
            @event.FrameId,
            @event.OffsetMilliSec,
            @event.UtcTimeStamp,
            string.IsNullOrWhiteSpace(@event.DetectedObjectId) ? null : @event.DetectedObjectId,
            @event.Scope,
            @event.ModelName,
            @event.InferenceTime,
            @event.JsonResult,
            @event.IsSuccess,
            @event.IsExpiredResult,
            @event.ErrorCode,
            @event.RequestedAtUtc,
            @event.CompletedAtUtc);
    }

    public override bool CanHandle(LLMAnalysisResult result)
    {
        return result.RequesterAlgorithmName == AlgorithmName &&
               !string.IsNullOrWhiteSpace(result.CandidateEventId) &&
               result.Scope == LLMAnalysisScope.Frame;
    }

    public override Task HandleAsync(
        LLMAnalysisResult result,
        LLMReconcileContext context,
        CancellationToken cancellationToken)
    {
        var inferenceEvent = LLMInferenceResultEvent.FromAnalysisResult(result, EventName);
        inferenceEvent.QueuePolicy = LLMQueuePolicy.EventAnchored;
        HandleLlmResult(inferenceEvent);
        return Task.CompletedTask;
    }

    private static LLMTimeoutPolicy ParseTimeoutPolicy(string value)
    {
        return Enum.TryParse<LLMTimeoutPolicy>(value, ignoreCase: true, out var policy)
            ? policy
            : LLMTimeoutPolicy.Drop;
    }

    protected override VisualAnnotation GenerateRegionAnnotation(Frame frame, ImageRegionDefinition regionDefinition)
    {
        var annotation = frame.Annotation;

        if (frame.HasProperty("ObjectOccurrence") && frame.GetProperty<bool>("ObjectOccurrence"))
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition, "#F44336"));
        }
        else
        {
            annotation.AddShapes(RegionAnnoGenerator.GenerateInterestAreas(regionDefinition));
        }

        return annotation;
    }
}
