# 算法模块实现指南

本文说明如何基于当前架构，以严格 TDD 的方式实现一个新的算法模块。

当前架构的稳定入口是：

- 同步算法和普通事件算法继承 `AlgorithmBase`。
- 发起视觉 LLM 请求的算法继承 `LlmAlgorithmBase`。
- 公共生命周期由基类固定，派生类只实现 Core 钩子。
- 领域事件通过 `AlgorithmEventDispatcher` 发布。
- MessagePipe 订阅通过 `AlgorithmSubscriptionRegistry` 管理。
- 运行时根据配置反射创建 `Executor`。
- 单元测试统一放在 `test/6.Algorithm/` 下。

---

## 1. 开始前先选择实现类型

### 1.1 选择 `AlgorithmBase`

以下场景直接继承 `AlgorithmBase`：

- 只根据当前帧、检测对象、区域定义或业务状态作出判断。
- 直接生成标注或领域事件。
- 订阅对象过期、最佳截图等 MessagePipe 事件，但不请求 LLM。
- 作为基础设施提供者运行，例如 `Algorithm.General.LLM`。

```csharp
public sealed class Executor : AlgorithmBase
{
}
```

### 1.2 选择 `LlmAlgorithmBase`

以下场景继承 `LlmAlgorithmBase`：

- 算法先用传统规则筛选候选证据。
- 需要提交整帧、对象裁剪或组合图像给视觉 LLM。
- 需要根据异步 LLM 结果确认、拒绝或降级候选事件。

```csharp
public sealed class Executor : LlmAlgorithmBase
{
}
```

不要让普通同步算法继承 `LlmAlgorithmBase`。否则它会引入无意义的 prompt、结果订阅和 snapshot 所有权问题。

### 1.3 决定新建工程还是复用已有工程

满足任一条件时，应创建新的算法工程：

- 需要独立 DLL，并在配置的 `Algorithms` 数组中单独启用。
- 有独立的算法名称、配置键、领域事件或部署开关。
- 与现有算法的业务状态机不同。
- 未来可能独立发布、禁用或调整执行顺序。

仅在以下情况下复用已有工程：

- 只是现有算法内部的小策略或辅助类。
- 不需要被运行时独立加载。
- 与现有算法共享同一生命周期、配置和事件语义。

不要把具体业务算法放入 `Algorithm.Common`。该工程只承载多个算法共同使用且语义稳定的基础设施。

---

## 2. 严格执行 TDD 循环

每一个可观察行为都按以下顺序实现：

1. **Red**：先写一个失败测试，确认失败原因正是尚未实现的行为。
2. **Green**：只写让当前测试通过的最小实现。
3. **Refactor**：在测试保持通过的前提下整理命名、重复代码和所有权。
4. 再进入下一个行为。

不要先完成整个 `Executor`，最后再补测试。推荐按以下行为顺序推进：

1. 工程和类型契约。
2. 初始化与配置解析。
3. 单帧判断。
4. 标注生成。
5. 事件内容。
6. 事件发布顺序。
7. 订阅和状态清理。
8. LLM 请求协议与结果路由。
9. snapshot、`Frame`、`Mat` 所有权。
10. Dispose 和初始化失败重试。

执行单个测试：

```powershell
dotnet test test/6.Algorithm/Algorithm.Common.Tests/Algorithm.Common.Tests.csproj `
  --filter "FullyQualifiedName~NewAlgorithmTests.TestName"
```

每完成一个小循环，再运行当前测试类。功能完成后运行全算法测试和全解决方案测试。

---

## 3. Step 1：定义行为基线

写代码前先记录下列内容：

| 项目 | 必须回答的问题 |
| --- | --- |
| 输入 | 使用整帧、检测对象、区域定义还是 MessagePipe 事件？ |
| 输出 | 只修改标注，还是发布领域事件？ |
| 成功语义 | `AnalysisResult(true)` 和 `false` 分别表示什么？ |
| 状态 | 是否跨帧缓存计数、对象、截图或候选事件？ |
| 限频 | 是否使用 `LocalEventIntervalSec`？ |
| 事件 | EventType、EventName、Message 和业务字段是什么？ |
| snapshot | 谁创建、谁持有、何时释放？ |
| LLM | 请求 scope、队列策略、TTL 和超时降级策略是什么？ |
| 配置 | 配置键、类型、默认值和非法值处理方式是什么？ |

建议先形成测试清单，例如：

```text
- Initialize 解析 TargetLabels 和阈值
- 没有目标对象时不发布事件
- 达到阈值时生成标注
- 限频窗口内只发布一次
- 仓储和外部投递成功后才发布 MessagePipe
- Dispose 后释放缓存中的 Mat
```

---

## 4. Step 2：先创建第一个失败测试

### 4.1 选择测试工程

简单算法和架构契约可以复用：

```text
test/6.Algorithm/Algorithm.Common.Tests/
```

适合放在这里的测试：

- 基类选择和生命周期约束。
- 配置解析。
- MessagePipe 路由。
- 事件调度和 snapshot 所有权。
- 小型业务算法的单元测试。

满足以下情况时建议创建独立测试工程：

- 算法业务规则较多。
- 需要大量领域 fixture 或参数化用例。
- 有模型文件、图像样本或算法专用测试资源。
- 独立运行该算法测试更清晰。

独立测试工程仍必须位于：

```text
test/6.Algorithm/Algorithm.General.Example.Tests/
```

创建并接入独立测试工程：

```powershell
dotnet new nunit `
  --name Algorithm.General.Example.Tests `
  --output test/6.Algorithm/Algorithm.General.Example.Tests `
  --framework net10.0

dotnet add `
  test/6.Algorithm/Algorithm.General.Example.Tests/Algorithm.General.Example.Tests.csproj `
  reference `
  src/6.Algorithm/Algorithm.General.Example/Algorithm.General.Example.csproj

dotnet sln Perceptron.slnx add `
  test/6.Algorithm/Algorithm.General.Example.Tests/Algorithm.General.Example.Tests.csproj
```

### 4.2 首个契约测试

首个测试应确认算法使用模板生命周期，而不是重新实现公共入口：

```csharp
using System.Reflection;

namespace Algorithm.Common.Tests;

public class NewAlgorithmContractTests
{
    [Test]
    public void Executor_UsesAlgorithmTemplate()
    {
        var type = typeof(Algorithm.General.Example.Executor);
        var methods = type.GetMethods(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            Assert.That(type.BaseType, Is.EqualTo(typeof(AlgorithmBase)));
            Assert.That(
                methods.Any(x => x.Name == nameof(AlgorithmBase.Initialize)),
                Is.False);
            Assert.That(
                methods.Any(x => x.Name == nameof(AlgorithmBase.Analyze)),
                Is.False);
            Assert.That(
                methods.Any(x => x.Name == nameof(AlgorithmBase.Dispose)),
                Is.False);
        });
    }
}
```

LLM 请求方把基类断言改为：

```csharp
Assert.That(type.BaseType, Is.EqualTo(typeof(LlmAlgorithmBase)));
```

此时因为工程或类型尚不存在，测试应处于 Red。

---

## 5. Step 3：创建算法工程

示例名称使用 `Algorithm.General.Example`：

```powershell
dotnet new classlib `
  --name Algorithm.General.Example `
  --output src/6.Algorithm/Algorithm.General.Example `
  --framework net10.0
```

工程文件保持最小化：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Algorithm.Common\Algorithm.Common.csproj" />
  </ItemGroup>
</Project>
```

将工程加入解决方案：

```powershell
dotnet sln Perceptron.slnx add `
  src/6.Algorithm/Algorithm.General.Example/Algorithm.General.Example.csproj
```

还需要完成两处引用：

1. 在使用该类型的测试工程中增加 `ProjectReference`。
2. 在 `src/0.Runnable/Perceptron.Console/Perceptron.Console.csproj` 中增加 `ProjectReference`。

控制台工程引用算法工程后，构建时算法 DLL 才会稳定进入运行目录，配置中的 `AssemblyFile` 才能被反射加载。

完成后运行首个测试。此时它仍应因 `Executor` 未实现而失败。

---

## 6. Step 4：创建最小 `Executor`

运行时通过反射调用以下构造函数，签名必须保留：

```csharp
Executor(
    AnalysisPipeline pipeline,
    Dictionary<string, string> preferences)
```

为了便于单元测试，建议同时提供依赖构造函数：

```csharp
using Algorithm.Common;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Service.Pipeline;

namespace Algorithm.General.Example;

public sealed class Executor : AlgorithmBase
{
    public Executor(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        SetMetadata();
    }

    public Executor(
        AlgorithmRuntimeDependencies dependencies,
        Dictionary<string, string> preferences)
        : base(dependencies, preferences)
    {
        SetMetadata();
    }

    private void SetMetadata()
    {
        AlgorithmName = "Example";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Describe the observable behavior.";
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        return new AnalysisResult(true);
    }
}
```

注意：

- 不要声明自己的 `Initialize()`、`Analyze()` 或 `Dispose()`。
- `AlgorithmName` 同时参与日志、事件和 LLM 结果路由，应稳定且唯一。
- 运行时构造函数与测试构造函数必须初始化相同元数据。

现在首个契约测试应转为 Green。

---

## 7. Step 5：设计并测试配置

初始化顺序固定为：

```text
ConfigureDefaultPreferences()
  -> 基类解析公共配置
  -> InitializeMode()
  -> InitializeCore()
```

### 7.1 业务配置放入 `InitializeCore`

```csharp
public const string DefaultTargetLabels = "person";
public const float DefaultConfidenceThreshold = 0.7f;

public HashSet<string> TargetLabels { get; private set; } = [];
public float ConfidenceThreshold { get; private set; }

protected override void InitializeCore()
{
    var labels = PreferenceParser.ParseStringValue(
        Preferences,
        "TargetLabels",
        DefaultTargetLabels);
    TargetLabels = labels
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim().ToLowerInvariant())
        .ToHashSet();

    ConfidenceThreshold = PreferenceParser.ParseFloatValue(
        Preferences,
        "ConfidenceThreshold",
        DefaultConfidenceThreshold);
}
```

应先写测试：

```csharp
[Test]
public void Initialize_ParsesBusinessPreferences()
{
    var algorithm = CreateAlgorithm(new Dictionary<string, string>
    {
        ["TargetLabels"] = "person, car",
        ["ConfidenceThreshold"] = "0.8"
    });

    algorithm.Initialize();

    Assert.That(algorithm.TargetLabels, Is.EquivalentTo(new[] { "person", "car" }));
    Assert.That(algorithm.ConfidenceThreshold, Is.EqualTo(0.8f));
}
```

### 7.2 仅在确有需要时覆盖默认配置

`ConfigureDefaultPreferences()` 在公共配置和 LLM 模式配置之前执行，适合设置该算法特有的默认值。

不得覆盖用户已经提供的值：

```csharp
protected override void ConfigureDefaultPreferences()
{
    EnsureDefault("PerformLLMAnalysis", "true");
    EnsureDefault("LLMPromptFile", "example-prompt.md");
}

private void EnsureDefault(string key, string value)
{
    if (!Preferences.ContainsKey(key))
    {
        Preferences[key] = value;
    }
}
```

### 7.3 基类已经解析的公共配置

不要在派生类重复解析以下配置：

| 分类 | 配置键 |
| --- | --- |
| 对象标注 | `GenerateBBox`、`BBoxStrokeColor`、`BBoxStrokeWidth` |
| 对象文字 | `GenerateObjText`、`ObjTextColor`、`ObjTextFontSize` |
| 文字内容 | `ObjTextShowLabel`、`ObjTextShowTrackingId`、`ObjTextShowConfidence` |
| 区域标注 | `GenerateAnalysisAreas`、`GenerateExcludeAreas`、`GenerateLanes`、`GenerateInterestAreas`、`GenerateCountLines` |
| 事件 | `WillPublishEventMessage`、`WillSaveEventSnapshot`、`WillSaveEventVideoClip` |
| 事件路径 | `EventSnapshotDir`、`EventName` |
| 限频与关闭 | `LocalEventIntervalSec`、`EventTaskShutdownTimeoutSeconds` |

### 7.4 初始化失败必须可回滚、可重试

`AlgorithmBase` 会在 `InitializeCore()` 抛异常时调用 `DisposeCore()`。

因此：

- `DisposeCore()` 必须允许资源只初始化了一部分。
- 不要假设所有字段都非空。
- 如果初始化失败后允许再次 `Initialize()`，取消源、队列和信号量必须重新创建。
- 不要在构造函数中启动线程或订阅事件。

需要为高风险资源写“失败一次后重试成功”的测试。

---

## 8. Step 6：从 `Services` 获取依赖并注册订阅

普通服务在 `InitializeCore()` 中获取：

```csharp
private IPublisher<ExampleEvent> _eventPublisher = null!;

protected override void InitializeCore()
{
    _eventPublisher =
        Services.GetRequiredService<IPublisher<ExampleEvent>>();
}
```

MessagePipe 订阅必须通过基类注册器：

```csharp
protected override void InitializeCore()
{
    Subscribe(
        Services.GetRequiredService<ISubscriber<ObjectExpiredEvent>>(),
        HandleObjectExpired);
}

private void HandleObjectExpired(ObjectExpiredEvent expiredEvent)
{
    // 处理对象状态并释放该对象拥有的资源。
}
```

如果第三方 API 直接返回 `IDisposable`，使用：

```csharp
TrackSubscription(externalSource.Subscribe(HandleExternalEvent));
```

禁止：

- 暴露 `SetSubscriber()`。
- 在字段中保存 MessagePipe subscription 并自行释放。
- 重复调用 `subscriber.Subscribe(...)`。
- 在构造函数中订阅。

基类会在 Dispose 或初始化回滚时释放所有已登记订阅。

---

## 9. Step 7：实现并测试 `AnalyzeCore`

### 9.1 Frame 所有权

基类在调用 `AnalyzeCore()` 前执行 `frame.Retain()`，并在 `finally` 中执行 `frame.Dispose()`。

派生类中禁止：

```csharp
frame.Retain();
frame.Dispose();
```

正确写法：

```csharp
protected override AnalysisResult AnalyzeCore(Frame frame)
{
    if (frame.IsBlankFrame || frame.Scene.Empty())
    {
        return new AnalysisResult(true);
    }

    var targets = frame.DetectedObjects
        .Where(x =>
            x.IsUnderAnalysis &&
            TargetLabels.Contains(x.Label.ToLowerInvariant()) &&
            x.Confidence >= ConfidenceThreshold)
        .ToList();

    foreach (var target in targets)
    {
        GenerateDetectedObjectAnnotation(frame, target);
    }

    return new AnalysisResult(true);
}
```

### 9.2 `AnalysisResult` 语义

当前流水线会调用每个算法，但暂未根据 `Success` 中断后续算法。因此建议保持一致语义：

- `true`：该帧已正常处理，包括“没有命中目标”这种正常结果。
- `false`：算法依赖的前置条件缺失，或当前帧无法完成有效分析。
- 真正的编程错误、配置错误和资源错误应抛出异常，不要静默伪装成 `false`。

### 9.3 区域算法

按 SourceId 获取区域管理器：

```csharp
var regionManager = RegionManagers
    .FirstOrDefault(x => x.SourceId == frame.SourceId);
if (regionManager == null)
{
    return new AnalysisResult(false);
}

var definition = regionManager.RegionDefinition;
var interestArea = definition.InterestAreas
    .FirstOrDefault(x => x.Name == RegionName);
if (interestArea == null)
{
    return new AnalysisResult(false);
}

GenerateRegionAnnotation(frame, definition);
```

测试至少覆盖：

- 空帧或无对象。
- 命中和不命中。
- 缺少区域。
- 阈值边界。
- 连续帧状态重置。
- `AnalyzeCore()` 抛异常时基类仍释放 frame。

---

## 10. Step 8：定义领域事件

需要保存标注 JSON 的事件实现 `IAnnotatedAlgorithmEvent`：

```csharp
using Algorithm.Common;
using Perceptron.Domain.Event;
using System.Text.Json;

namespace Algorithm.General.Example.Event;

public sealed class ExampleEvent : DomainEvent, IAnnotatedAlgorithmEvent
{
    public new const string EventType = "Example Event";

    public string ObjectId { get; }
    public string Annotations { get; set; } = string.Empty;

    public ExampleEvent(
        string sourceId,
        string eventName,
        string algorithmName,
        string objectId)
        : base(sourceId, EventType, eventName, algorithmName)
    {
        ObjectId = objectId;
        Message = $"Example event for object '{objectId}'.";
    }

    public override string GenerateJsonContent() =>
        JsonSerializer.Serialize(this, JsonOptions);

    public override string GenerateLogContent() => Message;
}
```

事件测试应确认：

- `EventType` 稳定。
- 业务字段完整。
- `GenerateJsonContent()` 包含必要字段。
- `Message` 可读。
- 类型实现 `IAnnotatedAlgorithmEvent`。

---

## 11. Step 9：通过公共调度器发布事件

不要在派生算法中使用 `Task.Run` 保存事件。使用 `TryQueueEvent()`：

```csharp
private void PublishExampleEvent(Frame frame, DetectedObject target)
{
    var eventMessage = new ExampleEvent(
        frame.SourceId,
        EventName,
        AlgorithmName,
        target.Id);

    var annotationJson = JsonSerializer.Serialize(
        frame.Annotation,
        DomainEvent.JsonOptions);

    TryQueueEvent(
        new EventPublicationRequest<ExampleEvent>
        {
            Event = eventMessage,
            AnnotationJson = annotationJson,
            CloneSnapshot = () => frame.Scene.Clone(),
            FrameId = frame.FrameId,
            FilePrefix = "example",
            StableArtifactId = target.Id,
            PublishInProcess = published =>
                _eventPublisher.Publish(published),
            SaveSnapshot = WillSaveEventSnapshot,
            SaveVideoClip = WillSaveEventVideoClip
        });
}
```

需要本地限频时使用：

```csharp
TryQueueThrottledEvent(request);
```

两者区别：

| 方法 | 行为 |
| --- | --- |
| `TryQueueEvent` | 只检查 `WillPublishEventMessage` 和调度器状态 |
| `TryQueueThrottledEvent` | 额外应用 `LocalEventIntervalSec` |

公共调度器按以下顺序执行：

```text
保存图片/标注/视频
  -> 保存领域事件
  -> 外部 MessagePoster
  -> PublishInProcess(MessagePipe)
```

任一步失败时不会继续执行后续发布步骤。

### 11.1 `EventPublicationRequest` 字段

| 字段 | 用途 |
| --- | --- |
| `Event` | 必填，待发布领域事件 |
| `AnnotationJson` | 写入事件并可保存为 JSON 文件 |
| `CloneSnapshot` | 同步克隆调度器拥有的独立 `Mat` |
| `FrameId` | 生成事件视频时的中心帧 |
| `FilePrefix` | 默认文件名前缀 |
| `StableArtifactId` | 优先用于稳定、可识别的文件名前缀 |
| `RelativeDirectory` | 相对于 `EventSnapshotDir` 的目录 |
| `PublishInProcess` | 持久化和外部投递成功后的 MessagePipe 发布 |
| `SaveSnapshot` | 是否保存图片和标注 JSON |
| `SaveVideoClip` | 是否生成视频片段 |

`CloneSnapshot` 必须返回独立副本。调度器会负责释放该副本。

---

## 12. Step 10：管理跨帧状态和资源

如果算法缓存 `Frame`、`Mat`、模型、线程、信号量或取消源，必须写出所有权规则。

### 12.1 推荐规则

- 缓存 `Mat`：进入缓存时 `Clone()`，缓存拥有该副本。
- 替换缓存：先释放旧值。
- 发布事件：调度器再克隆一份，缓存仍拥有原值。
- 对象过期：移除状态并释放其拥有的所有 `Mat`。
- `DisposeCore()`：释放所有剩余状态。
- 多线程访问字典和对象状态时使用锁或并发容器。

### 12.2 Dispose 模板

```csharp
protected override void DisposeCore()
{
    foreach (var state in _states.Values)
    {
        state.Snapshot?.Dispose();
    }

    _states.Clear();
    _model?.Dispose();
    _model = null;
}
```

`DisposeCore()` 必须：

- 可在初始化只完成一部分时调用。
- 不重复释放同一个所有者的资源。
- 不释放不属于当前算法的 snapshot。
- 不吞掉关键业务资源泄漏。

`AlgorithmBase.Dispose()` 本身已保证只执行一次，并会在 `DisposeCore()` 后释放公共订阅和事件调度器。

---

## 13. Step 11：实现 LLM 请求方

### 13.1 基础模板

```csharp
using Algorithm.Common;
using Algorithm.Common.Event;
using Algorithm.Common.LLM;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Service.Pipeline;

namespace Algorithm.General.ExampleByLLM;

public sealed class Executor : LlmAlgorithmBase
{
    public Executor(
        AnalysisPipeline pipeline,
        Dictionary<string, string> preferences)
        : base(pipeline, preferences)
    {
        SetMetadata();
    }

    public Executor(
        AlgorithmRuntimeDependencies dependencies,
        Dictionary<string, string> preferences)
        : base(dependencies, preferences)
    {
        SetMetadata();
    }

    private void SetMetadata()
    {
        AlgorithmName = "Example by LLM";
        AlgorithmVersion = "1.0.0";
        AlgorithmDescription = "Confirm example candidates with a visual LLM.";
    }

    protected override AnalysisResult AnalyzeCore(Frame frame)
    {
        if (!WillPerformLlmAnalysis)
        {
            return new AnalysisResult(true);
        }

        MarkFrameForLlm(
            frame,
            new LlmRequestOptions
            {
                Scope = LLMAnalysisScope.Frame,
                QueuePolicy = LLMQueuePolicy.EventAnchored,
                ExpireAtUtc = DateTime.UtcNow.AddSeconds(30)
            });

        return new AnalysisResult(true);
    }

    protected override bool CanHandleLlmResult(
        LLMInferenceResultEvent result)
    {
        return base.CanHandleLlmResult(result) &&
               result.Scope == LLMAnalysisScope.Frame;
    }

    protected override void HandleLlmResult(
        LLMInferenceResultEvent result)
    {
        // 此处处理匹配 RequesterAlgorithmName 且通过额外过滤的结果。
        // 如果 result.Snapshot 转移到业务状态，记录新所有者；
        // 否则当前处理器必须在终止路径释放它。
    }
}
```

### 13.2 LLM 模式配置

`LlmAlgorithmBase` 负责：

- 解析 `PerformLLMAnalysis`。
- 在启用时读取 `LLMPromptFile`。
- prompt 不存在时抛出包含算法名和绝对路径的异常。
- 订阅 `LLMInferenceResultEvent`。
- 先按 `RequesterAlgorithmName == AlgorithmName` 路由。

同步算法不会执行这些步骤。

### 13.3 提交整帧请求

```csharp
var requestId = MarkFrameForLlm(
    frame,
    new LlmRequestOptions
    {
        Scope = LLMAnalysisScope.Frame,
        QueuePolicy = LLMQueuePolicy.EventAnchored,
        CandidateEventId = candidateEventId,
        RequestId = requestId,
        ExpireAtUtc = deadlineUtc,
        Prompt = UserPrompt,
        ImageJpeg = customJpeg
    });
```

`RequestId` 为空时基类会生成唯一 ID。`Prompt` 为空时使用已加载的 `UserPrompt`。

### 13.4 提交对象请求

```csharp
var requestId = MarkObjectForLlm(
    frame,
    detectedObject,
    new LlmRequestOptions
    {
        Scope = LLMAnalysisScope.Object,
        QueuePolicy = LLMQueuePolicy.LatestBestPerObject,
        ExpireAtUtc = DateTime.UtcNow.AddSeconds(30)
    });
```

对象请求会同时在 frame 和 `DetectedObject` 上写入协议属性。

### 13.5 队列策略

| 策略 | 使用场景 |
| --- | --- |
| `LatestPerSource` | 每个视频源只保留最新整帧请求 |
| `LatestBestPerObject` | 每个对象保留质量更高的请求 |
| `EventAnchored` | 候选事件证据不可被后续请求替换 |
| `DropOldest` | 队列满时丢弃最旧请求 |

选择原则：

- 事件确认优先使用 `EventAnchored`。
- 连续场景概览可使用 `LatestPerSource`。
- 船舶标签等对象最佳证据使用 `LatestBestPerObject`。

### 13.6 结果过滤

基类只保证 Requester 匹配。派生类仍应按业务增加：

- `Scope`。
- `RequestId` 是否等于当前 pending request。
- `CandidateEventId`。
- `DetectedObjectId` 或 TrackKey。
- 是否已经 finalized。
- 是否过期、失败或重复。

```csharp
protected override bool CanHandleLlmResult(
    LLMInferenceResultEvent result)
{
    return base.CanHandleLlmResult(result) &&
           result.Scope == LLMAnalysisScope.Object &&
           !string.IsNullOrWhiteSpace(result.DetectedObjectId);
}
```

### 13.7 LLM snapshot 所有权

必须遵守：

- 非目标 Requester 的结果不由当前算法释放。
- 进入 `HandleLlmResult()` 后，当前算法负责明确处理 snapshot。
- 解析失败、重复结果、迟到结果和未知对象路径都必须释放目标 snapshot。
- 将 snapshot 存入状态时，状态成为新所有者。
- 状态被替换、超时、发布或 Dispose 时释放。

### 13.8 运行配置中的顺序

LLM 请求方需要先给 frame 写入请求属性，`Algorithm.General.LLM` 再读取并入队。

因此配置顺序必须是：

```json
"Algorithms": [
  {
    "AssemblyFile": "Algorithm.General.ExampleByLLM.dll",
    "FullQualifiedClassName": "Algorithm.General.ExampleByLLM.Executor",
    "Preferences": {
      "PerformLLMAnalysis": true,
      "LLMPromptFile": "example-prompt.md"
    }
  },
  {
    "AssemblyFile": "Algorithm.General.LLM.dll",
    "FullQualifiedClassName": "Algorithm.General.LLM.Executor",
    "Preferences": {
      "ServerUrl": "http://127.0.0.1:8000/v1",
      "ApiKey": "local-key"
    }
  }
]
```

不要同时为同一请求方启用直接 MessagePipe 处理和另一条 Reconciler 消费路径。

---

## 14. Step 12：添加 prompt 或其他资源

LLM prompt 放在算法工程内，并设置复制：

```xml
<ItemGroup>
  <None Update="example-prompt.md">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

测试至少覆盖：

- `PerformLLMAnalysis=false` 时不读取 prompt。
- prompt 存在时只订阅一次。
- prompt 缺失时错误包含算法名和绝对路径。

不要在同步算法工程中加入无用 prompt。

---

## 15. Step 13：接入运行时配置

运行时从配置的 `Algorithms` 数组读取：

```json
{
  "AssemblyFile": "Algorithm.General.Example.dll",
  "FullQualifiedClassName": "Algorithm.General.Example.Executor",
  "Preferences": {
    "TargetLabels": "person,car",
    "ConfidenceThreshold": 0.8,
    "WillPublishEventMessage": true,
    "WillSaveEventSnapshot": true,
    "WillSaveEventVideoClip": false,
    "LocalEventIntervalSec": 3,
    "EventSnapshotDir": "Events/Example",
    "EventName": "ExampleEvent"
  }
}
```

检查：

1. `AssemblyFile` 与构建产物文件名一致。
2. `FullQualifiedClassName` 与 namespace 和类型名完全一致。
3. `Executor` 有运行时要求的构造函数。
4. 控制台工程引用了算法工程。
5. 配置文件被复制到输出目录。
6. LLM 请求方位于 `Algorithm.General.LLM` 之前。

如果新增专用配置文件，还要在控制台工程中增加：

```xml
<None Update="Config\example-settings.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

---

## 16. Step 14：补齐测试矩阵

### 16.1 所有算法

- 基类类型正确。
- 不声明公共 `Initialize/Analyze/Dispose`。
- 初始化读取默认值和自定义值。
- 重复初始化不重复订阅或启动资源。
- 初始化失败后状态正确；需要时可重试。
- 正常、空输入、边界值和异常路径均有测试。
- Dispose 可重复调用。

### 16.2 发布事件的算法

- 事件实现 `IAnnotatedAlgorithmEvent`。
- snapshot 在进入后台任务前克隆。
- 成功和失败路径都释放调度器拥有的 snapshot。
- 仓储失败后不执行外部投递和 MessagePipe。
- MessagePoster 失败后不执行 MessagePipe。
- 同一毫秒多个事件不会覆盖文件。
- Dispose 拒绝新任务并等待已接收任务。

### 16.3 有订阅的算法

- 每个订阅只注册一次。
- Dispose 后不再收到事件。
- 一个订阅释放失败不阻止其他订阅释放。
- 对象过期时状态和资源同步清理。

### 16.4 LLM 请求方

- 禁用 LLM 时不加载 prompt、不订阅结果。
- Requester 不匹配时不处理，也不释放 snapshot。
- Scope、RequestId、CandidateEventId 过滤正确。
- 请求属性完整，UTC deadline 不被修改。
- 重复、失败、过期、迟到结果行为明确。
- 目标结果解析失败时释放 snapshot。
- 超时策略有测试。

### 16.5 测试依赖构造

单元测试优先使用 `AlgorithmRuntimeDependencies`：

```csharp
var dependencies = new AlgorithmRuntimeDependencies(
    services,
    regionManagers,
    fakeSnapshotManager,
    fakeEventRepository,
    fakeMessagePoster);

var algorithm = new Executor(
    dependencies,
    new Dictionary<string, string>());
```

这样可以测试算法而不启动完整 `AnalysisPipeline`。

---

## 17. Step 15：执行验收命令

按由小到大的顺序执行：

```powershell
dotnet test test/6.Algorithm/Algorithm.Common.Tests/Algorithm.Common.Tests.csproj `
  --filter "FullyQualifiedName~NewAlgorithm"

dotnet build `
  src/6.Algorithm/Algorithm.General.Example/Algorithm.General.Example.csproj `
  --no-restore

dotnet test `
  test/6.Algorithm/Algorithm.Common.Tests/Algorithm.Common.Tests.csproj `
  --no-restore

dotnet build Perceptron.slnx --no-restore

dotnet test Perceptron.slnx --no-build --no-restore
```

完成标准：

- 新测试经历过可解释的 Red，再转为 Green。
- 算法工程构建无错误。
- `Algorithm.Common.Tests` 全部通过。
- 全解决方案构建无错误。
- 全解决方案测试无新增失败。
- 没有未跟踪的后台任务、订阅或 snapshot。

---

## 18. 最终检查清单

### 工程

- [ ] 算法职责足够独立，确实需要新工程。
- [ ] 工程只引用必要项目，至少引用 `Algorithm.Common`。
- [ ] 已加入 `Perceptron.slnx`。
- [ ] 控制台和测试工程已增加项目引用。

### 生命周期

- [ ] 只实现 Core 钩子。
- [ ] 没有覆盖 `Initialize/Analyze/Dispose`。
- [ ] 构造函数不启动线程、不订阅、不读取文件。
- [ ] 初始化失败可安全回滚。

### 帧和图像

- [ ] `AnalyzeCore()` 不调用 `frame.Retain/Dispose`。
- [ ] 缓存使用独立 clone。
- [ ] 每个 `Mat` 都有明确唯一所有者。
- [ ] 替换、超时、异常和 Dispose 路径均释放资源。

### 事件

- [ ] 事件实现 `IAnnotatedAlgorithmEvent`。
- [ ] 使用 `TryQueueEvent` 或 `TryQueueThrottledEvent`。
- [ ] 没有自行 `Task.Run` 保存事件。
- [ ] MessagePipe 仅在持久化和外部投递成功后发布。

### 订阅

- [ ] 使用 `Subscribe()` 或 `TrackSubscription()`。
- [ ] 没有独立 `SetSubscriber()`。
- [ ] Dispose 后不再处理消息。

### LLM

- [ ] 仅请求方继承 `LlmAlgorithmBase`。
- [ ] 使用 `MarkFrameForLlm()` 或 `MarkObjectForLlm()`。
- [ ] 队列策略、TTL、候选状态和超时策略明确。
- [ ] 结果按 Requester、Scope、RequestId 和候选状态过滤。
- [ ] 非目标结果不释放 snapshot。
- [ ] 目标结果的所有终止路径都处理 snapshot。
- [ ] 配置顺序为请求方在前、`Algorithm.General.LLM` 在后。

### TDD

- [ ] 每个行为先有失败测试。
- [ ] 单元测试位于 `test/6.Algorithm/`。
- [ ] 生命周期、业务、失败路径和资源所有权均有覆盖。
- [ ] 算法测试、全解决方案构建和全量测试通过。
