# Vision LLM Asynchronous Confirmation Design

## 1. Background

The current project is an AI video analysis pipeline. It mainly uses traditional vision models, especially YOLO-based object detection, to identify targets in video frames and then run downstream analysis algorithms.

The traditional model path is fast enough for real-time processing:

- Normal frame processing is expected to stay below 40 ms.
- Complex algorithms can use frame skipping.
- Downstream business algorithms rely on `Frame`, `DetectedObject`, object tracking IDs, annotations, and domain events.

The problem is precision. Traditional models can produce false positives and false negatives. The desired improvement is to introduce a visual LLM as a secondary confirmation layer.

However, visual LLM inference is much slower, usually 1-6 seconds per request. If the pipeline waits synchronously for the LLM, real-time frame processing stalls. If the LLM runs asynchronously, the system must preserve and reconcile the historical frame/object context after several seconds have passed.

This document records the current architectural conclusion for future Codex analysis and implementation.

## 2. Existing Reference Implementation

Relevant code paths:

- `src/6.Algorithm/Algorithm.Ship.LabelsByLLM/`
- `src/6.Algorithm/Algorithm.General.LLM/`
- `src/6.Algorithm/Algorithm.General.ObjectOccurrenceByLLM/`
- `src/2.Service/Perceptron.Service/Pipeline/VideoFrameSlideWindow.cs`

Current cooperation model:

1. A business algorithm decides whether LLM analysis is needed.
2. It marks `Frame` or `DetectedObject` properties:
   - `LLMAnalysis`
   - `LLMAnalysisType`
   - `LLMAnalysisPrompt`
3. `Algorithm.General.LLM` detects these properties in the later pipeline stage.
4. It enqueues the work asynchronously.
5. It publishes `LLMInferenceResultEvent` when inference completes.
6. Business algorithms consume the result event and update their own state.

The current `Algorithm.General.LLM` already contains useful ideas:

- Frame-level work keeps the latest frame per `SourceId`.
- Object-level work keeps the latest/best object task per object ID.
- Work is processed asynchronously by a background worker.

This direction is correct, but the architecture should be formalized to avoid lifecycle races and make event confirmation semantics explicit.

## 3. Key Problems Identified

### 3.1 The real-time pipeline must never wait for LLM

The LLM path must not block `Analyze(Frame frame)`. Traditional algorithms should continue producing fast candidate facts. LLM confirmation should happen in a separate asynchronous lane.

### 3.2 LLM results must be correlated to historical evidence

When a result returns several seconds later, the original frame may already be gone from the sliding window. The result must not depend on finding the original live `Frame`.

Every LLM request/result must carry enough identity:

- `RequestId`
- `CandidateEventId`
- `SourceId`
- `FrameId`
- `OffsetMilliSec`
- `UtcTimeStamp`
- `ObjectId`, if object-level
- `TrackKey` or tracking identity, if available
- analysis scope: frame-level or object-level

### 3.3 Object expiration is not the universal event publication point

Some events are lifecycle-summary events. For example, ship label aggregation can naturally publish when an object expires.

Other events must be published immediately after LLM confirmation. Examples:

- fire detection
- smoke detection
- fall detection
- intrusion detection
- dangerous behavior detection

For these events, the flow should be:

```text
traditional model triggers candidate
-> freeze evidence
-> submit LLM confirmation
-> LLM confirms
-> publish immediately
```

Do not wait for `ObjectExpiredEvent` in these cases.

### 3.4 Current object-expiration flow can lose late LLM results

In `Algorithm.Ship.LabelsByLLM`, the final event is currently generated when `ObjectExpiredEvent` arrives. If the object expires before the LLM result returns, the result may later be written into cache but no later object expiration event will consume it.

This is a race caused by asynchronous LLM confirmation and object lifecycle events.

### 3.5 Cloning full `Frame` objects is possible but not ideal

Creating a separate pending queue with cloned frames can avoid expiration of the original `Frame`, but it should not blindly store full `Frame` instances.

Reasons:

- Full frame `Mat` objects are memory-heavy.
- Multi-stream video plus second-level LLM latency can accumulate many frames.
- Current `Frame.Clone()` only clones `Scene`; it does not deeply clone detected objects, properties, annotations, and object snapshots.
- LLM usually only needs immutable evidence: encoded frame JPEG, object crop JPEG, bbox, labels, confidence, timestamps, and candidate event metadata.

The recommended design is to clone evidence, not necessarily clone full domain frames.

## 4. Core Architectural Conclusion

Use three logical lanes:

```text
Fast Lane       : YOLO / tracker / traditional rules / candidate generation
Evidence Lane   : freeze immutable evidence for LLM
Reconcile Lane  : merge LLM result back into candidate event/object state
```

The real-time pipeline produces candidates. The LLM processes immutable evidence. A reconciler decides what to do with the delayed result.

Do not design the system as "LLM returns and continues processing the old frame". Instead, design it as:

```text
LLM returns
-> find CandidateEvent / ObjectVerificationState by RequestId or CandidateEventId
-> validate freshness and lifecycle state
-> confirm, reject, timeout, or finalize
-> publish event if appropriate
```

## 5. Proposed Components

### 5.1 CandidateEventStore

Stores business candidates waiting for LLM confirmation or timeout.

Responsibilities:

- Create candidate event records.
- Track candidate lifecycle state.
- Support lookup by `CandidateEventId`.
- Support timeout scanning.
- Preserve enough business context for publication.

Suggested state:

```csharp
public enum CandidateEventStatus
{
    PendingLLM,
    Confirmed,
    Rejected,
    TimedOut,
    Published,
    Cancelled
}
```

Suggested model:

```csharp
public sealed class CandidateEventState
{
    public string CandidateEventId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public long FrameId { get; init; }
    public long OffsetMilliSec { get; init; }
    public DateTime UtcTimeStamp { get; init; }
    public string AlgorithmName { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string? ObjectId { get; init; }
    public CandidateEventStatus Status { get; set; }
    public string? PendingRequestId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime DeadlineUtc { get; init; }
    public object? TraditionalPayload { get; init; }
    public object? LLMResultPayload { get; set; }
}
```

### 5.2 PendingEvidenceStore

Stores immutable evidence used by LLM and later event publication.

This replaces the idea of a raw pending cloned `Frame` queue.

Suggested model:

```csharp
public sealed record PendingLLMEvidence(
    string RequestId,
    string CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    LLMAnalysisScope Scope,
    byte[] FrameJpeg,
    byte[]? ObjectCropJpeg,
    IReadOnlyList<DetectedObjectEvidence> Objects,
    string Prompt,
    DateTime ExpireAtUtc);

public sealed record DetectedObjectEvidence(
    string ObjectId,
    string LocalId,
    string Label,
    int LabelId,
    int TrackingId,
    float Confidence,
    int X,
    int Y,
    int Width,
    int Height);
```

Benefits:

- Independent from live `Frame` lifetime.
- Safe after `VideoFrameSlideWindow` expires the original frame.
- Lower memory pressure if images are JPEG-encoded.
- Easier to persist or replay.
- Easier to inspect in logs and tests.

### 5.3 LLMAnalysisRequest

Unified input to the LLM subsystem.

```csharp
public enum LLMAnalysisScope
{
    Frame,
    Object
}

public enum LLMQueuePolicy
{
    LatestPerSource,
    LatestBestPerObject,
    EventAnchored,
    DropOldest
}

public sealed record LLMAnalysisRequest(
    string RequestId,
    string? CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    string? ObjectId,
    string? ObjectLocalId,
    string? TrackKey,
    LLMAnalysisScope Scope,
    LLMQueuePolicy QueuePolicy,
    string Prompt,
    byte[] ImageJpeg,
    float? DetectorConfidence,
    double? EvidenceQualityScore,
    DateTime CreatedAtUtc,
    DateTime ExpireAtUtc);
```

### 5.4 LLMAnalysisResult

Unified output from the LLM subsystem.

```csharp
public sealed record LLMAnalysisResult(
    string RequestId,
    string? CandidateEventId,
    string SourceId,
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp,
    string? ObjectId,
    LLMAnalysisScope Scope,
    string ModelName,
    TimeSpan InferenceTime,
    string JsonResult,
    bool IsSuccess,
    bool IsExpiredResult,
    string? ErrorCode,
    DateTime RequestedAtUtc,
    DateTime CompletedAtUtc);
```

`LLMInferenceResultEvent` can either be extended with these fields or replaced by a richer result event while keeping backward compatibility.

### 5.5 LLMRequestScheduler

Schedules requests according to business priority and replacement policy.

Queue policies:

| Policy | Use case | Behavior |
| --- | --- | --- |
| `LatestPerSource` | scene overview, periodic full-frame analysis | keep only latest request per source |
| `LatestBestPerObject` | object attribute confirmation | keep best request per object |
| `EventAnchored` | fire/smoke/fall/intrusion confirmation | never replace by unrelated newer frame; complete or timeout |
| `DropOldest` | low-priority diagnostics | bounded queue drops old work under pressure |

For `EventAnchored` requests, do not use the current "latest frame per source" strategy, because the LLM must confirm the exact evidence that triggered the candidate event.

### 5.6 LLMWorkerPool

Executes visual LLM calls.

Recommended .NET implementation:

- Use `Channel<LLMAnalysisRequest>` instead of `BlockingCollection` for async-friendly bounded queues.
- Use `BackgroundService` or a managed worker abstraction.
- Use `SemaphoreSlim` to limit model concurrency.
- Support cancellation and request timeout.
- Publish `LLMAnalysisResult` or enhanced `LLMInferenceResultEvent`.

Recommended limits:

- `MaxConcurrentFrameRequests`: 1 or low number.
- `MaxConcurrentObjectRequests`: 2-4 depending on GPU/API capacity.
- `PerSourceRateLimit`: avoid flooding from one video source.
- `PerObjectOnlyOnePending`: avoid duplicate object confirmation.

### 5.7 LLMResultReconciler

Consumes LLM results and decides final business action.

Responsibilities:

- Match result by `RequestId` and `CandidateEventId`.
- Check whether result is expired.
- Check candidate lifecycle state.
- Parse LLM JSON.
- Confirm or reject candidate events.
- Publish immediate events when required.
- Finalize object lifecycle events.
- Release evidence resources.

This component should own the rule:

```text
LLM confirms now -> publish now if event type requires immediacy.
Object expired but waiting for LLM -> publish when result arrives or when timeout fires.
LLM result arrives too late -> ignore or store as late diagnostic result.
```

## 6. Event Types and Publication Timing

### 6.1 Immediate confirmation events

Examples:

- fire
- smoke
- fall
- intrusion
- PPE violation
- dangerous behavior

Flow:

```text
traditional algorithm detects candidate
-> create CandidateEventState(PendingLLM)
-> freeze evidence
-> submit EventAnchored LLM request
-> LLM result true
-> publish event immediately
-> mark Published
```

Timeout policy should be configurable:

```text
OnTimeout = PublishTraditional | Drop | PublishUnknown | Retry
```

### 6.2 Object lifecycle summary events

Examples:

- ship label summary
- best object snapshot label
- object attribute aggregation

Flow:

```text
object appears
-> update ObjectVerificationState
-> submit object-level LLM when quality improves
-> cache latest verified payload
-> object expires
-> if verified payload exists: publish final event
-> if pending LLM exists: wait until deadline
-> if result arrives before deadline: publish final event
-> if timeout: publish Unknown / traditional fallback / drop
```

Object expiration remains useful, but only for lifecycle-summary event types.

## 7. Object Verification State Machine

Recommended object state:

```text
Tracking
-> PendingLLM
-> Verified
-> ExpiredWaitingLLM
-> Finalized
```

Rules:

- `Tracking`: object is alive in the sliding window.
- `PendingLLM`: at least one request is in flight.
- `Verified`: usable LLM result exists.
- `ExpiredWaitingLLM`: object expired, but a request is still in flight.
- `Finalized`: final event published, rejected, or timed out.

Suggested model:

```csharp
public sealed class ObjectVerificationState
{
    public string ObjectId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public long BestFrameId { get; set; }
    public float BestDetectorConfidence { get; set; }
    public double BestQualityScore { get; set; }
    public string? PendingRequestId { get; set; }
    public string? LatestResultRequestId { get; set; }
    public object? VerifiedPayload { get; set; }
    public bool IsExpired { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    public DateTime? FinalizeDeadlineUtc { get; set; }
}
```

## 8. Evidence Selection for Object-Level LLM

Do not submit every frame of the same object to the LLM.

Recommended trigger conditions:

- first appearance of target object
- current evidence quality is significantly better than previous pending evidence
- detector confidence improves by threshold, for example `+0.08`
- object has existed for a configured duration but has no LLM result
- object is about to expire or has expired, and no valid result exists

Recommended quality score:

```text
QualityScore =
  detectorConfidence * 0.45
+ bboxAreaRatio * 0.25
+ sharpnessScore * 0.15
+ centerScore * 0.10
- occlusionPenalty * 0.05
```

This is better than using detector confidence only, because the best LLM evidence is often the clearest and largest crop, not necessarily the highest YOLO confidence.

## 9. Frame Expiration and Pending Evidence

`VideoFrameSlideWindow` expires frames based on the sliding window. It publishes:

- `ObjectExpiredEvent`
- `FrameExpiredEvent`

When LLM confirmation is needed, do not rely on the frame still being alive in the slide window.

Recommended approach:

1. At candidate creation time, encode the full frame or object crop to JPEG.
2. Store it in `PendingEvidenceStore`.
3. Attach TTL and capacity limits.
4. Let the original frame expire normally.
5. Use evidence bytes for LLM and event publication.

If later code absolutely requires a `Frame`-like object, introduce an explicit evidence snapshot type rather than relying on current `Frame.Clone()`.

Potential type:

```csharp
public sealed class FrameEvidence
{
    public string SourceId { get; init; } = string.Empty;
    public long FrameId { get; init; }
    public long OffsetMilliSec { get; init; }
    public DateTime UtcTimeStamp { get; init; }
    public byte[] FrameJpeg { get; init; } = [];
    public IReadOnlyList<DetectedObjectEvidence> Objects { get; init; } = [];
    public string? AnnotationJson { get; init; }
}
```

## 10. Recommended Changes to Existing Modules

### 10.1 Algorithm.General.LLM

Current file:

- `src/6.Algorithm/Algorithm.General.LLM/Executor.cs`

Recommended evolution:

1. Keep current property-based integration temporarily for compatibility.
2. Convert marked `Frame` / `DetectedObject` to `LLMAnalysisRequest`.
3. Encode evidence immediately.
4. Enqueue immutable requests.
5. Publish enriched result event.
6. Avoid holding complete `Frame` objects longer than necessary.

Current replacement strategies can be preserved but renamed/formalized:

- `_latestFrameInferenceTasks` -> `LatestPerSource` scheduler.
- `_latestObjectInferenceTasks` -> `LatestBestPerObject` scheduler.

Add a separate `EventAnchored` scheduler for immediate-confirmation events.

### 10.2 Algorithm.Ship.LabelsByLLM

Current file:

- `src/6.Algorithm/Algorithm.Ship.LabelsByLLM/Executor.cs`

Recommended evolution:

1. Replace `_cachedShipLabels` with `ObjectVerificationState`.
2. On object detection, update best evidence and submit LLM only when quality improves.
3. On LLM result, update `VerifiedPayload`.
4. On `ObjectExpiredEvent`:
   - if verified payload exists, publish immediately
   - if pending request exists, mark `ExpiredWaitingLLM`
   - set finalization deadline
5. On late LLM result:
   - if state is `ExpiredWaitingLLM` and before deadline, publish final event
   - if already `Finalized`, ignore or persist as late diagnostic
6. On timeout, apply fallback policy.

This fixes the race where object expiration happens before LLM result arrival.

### 10.3 Algorithm.General.ObjectOccurrenceByLLM

Current file:

- `src/6.Algorithm/Algorithm.General.ObjectOccurrenceByLLM/Executor.cs`

Recommended evolution:

1. When traditional occurrence condition reaches `MinDurationSec`, create `CandidateEventId`.
2. Freeze the exact triggering frame evidence.
3. Submit `LLMAnalysisRequest` with `QueuePolicy.EventAnchored`.
4. Do not allow newer source frames to replace this request.
5. Publish event immediately after LLM confirms.
6. Store rejected and timed-out candidates for observability.

## 11. Suggested Implementation Roadmap

### Phase 1: Message and evidence model

- Add `LLMAnalysisRequest`.
- Add `LLMAnalysisResult` or enrich `LLMInferenceResultEvent`.
- Add `PendingLLMEvidence`.
- Add `DetectedObjectEvidence`.
- Add helper to build evidence from `Frame`.

### Phase 2: Scheduler formalization

- Extract request scheduling from `Algorithm.General.LLM`.
- Implement:
  - `LatestPerSource`
  - `LatestBestPerObject`
  - `EventAnchored`
- Add bounded capacity and TTL.

### Phase 3: Result reconciliation

- Add `CandidateEventStore`.
- Add `LLMResultReconciler`.
- Support immediate publication after LLM confirmation.
- Support timeout policy.

### Phase 4: Ship label lifecycle fix

- Introduce `ObjectVerificationState`.
- Handle `ExpiredWaitingLLM`.
- Finalize object labels after result or timeout.

### Phase 5: Object occurrence/fire-like event flow

- Convert occurrence confirmation to `CandidateEventId` based flow.
- Use `EventAnchored` queue policy.
- Publish immediately after confirmation.

### Phase 6: Observability

Add metrics/logging:

- queue length by policy
- dropped request count
- replaced request count
- LLM latency percentiles
- pending candidate count
- expired evidence count
- timeout count
- late result count
- false/rejected confirmation count

## 12. Operational Safeguards

Recommended configuration:

```text
LLM.MaxConcurrentFrameRequests = 1
LLM.MaxConcurrentObjectRequests = 2
LLM.RequestTimeoutSeconds = 10
LLM.CandidateEventTimeoutSeconds = 12
LLM.ObjectExpiredWaitSeconds = 8
LLM.MaxPendingEvidencePerSource = 30
LLM.MaxPendingEvidenceTotalBytes = configurable
LLM.FrameJpegQuality = 80
LLM.ObjectCropPaddingRatio = 0.10
```

Backpressure behavior must be explicit:

- Immediate safety events should not be silently dropped.
- Scene summary can keep latest only.
- Object labels can keep best evidence only.
- Diagnostic tasks can drop old work.

## 13. Final Design Principle

The core principle is:

```text
Real-time algorithms produce candidates.
LLM analyzes immutable historical evidence.
A reconciler merges delayed results into candidate/object lifecycle state.
Events are published according to business timing requirements, not according to LLM timing or frame lifetime.
```

This keeps the video pipeline real-time, preserves the correct historical context for LLM confirmation, supports both full-frame and object-level analysis, and avoids losing results when frames or objects expire before LLM inference completes.
