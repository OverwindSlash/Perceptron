# 算法模块设计规范 (Algorithm Module Specification)

## 1. 设计思路 (Design Philosophy)

算法模块采用 **接口定义 (Interface Definition)** 与 **基类抽象 (Base Class Abstraction)** 相结合的设计模式，旨在统一算法的生命周期管理、配置注入方式以及执行调用标准。

### 核心组件
*   **`IAlgorithmModule`**: 定义了算法模块的最小契约。
    *   **元数据**: `AlgorithmName`, `AlgorithmVersion`, `AlgorithmDescription`。
    *   **生命周期**: `Initialize()` (初始化), `Dispose()` (清理)。
    *   **核心能力**: `Analyze(Frame frame)` (执行分析)。
    *   **状态**: `IsInitialized` (是否已初始化)。
*   **`AlgorithmBase`**: 实现了 `IAlgorithmModule` 的抽象基类，提供了标准的基础设施实现。
    *   **依赖注入**: 通过构造函数接收并保存 `AnalysisPipeline` 引用。
    *   **配置管理**: 接收并保存 `Dictionary<string, string>` 类型的偏好设置 (`preferences`)。
    *   **标准初始化**: 在 `Initialize` 中设置 `IsInitialized` 标志。

## 2. 设计原则 (Design Principles)

1.  **统一契约 (Unified Contract)**: 所有算法必须实现 `IAlgorithmModule` (通常通过继承 `AlgorithmBase`)，确保宿主程序可以以统一的方式加载、初始化和调用任何算法。
2.  **配置驱动 (Configuration Driven)**: 算法的行为通过键值对 (`preferences`) 进行动态配置，而非硬编码。初始化阶段负责解析这些配置。
3.  **管道集成 (Pipeline Integration)**: 算法被设计为 `AnalysisPipeline` 的一部分，通过 `Analyze` 方法接收上下文 (`Frame`) 进行处理。
4.  **单一职责 (Single Responsibility)**: 每个算法类应专注于特定的分析任务（如生成调试标注、执行特定检测等）。

## 3. 新算法模块开发步骤 (Steps to Create a New Algorithm Module)

如果您需要创建一个新的算法模块，请遵循以下步骤：

### 步骤 1: 创建类并继承基类
创建一个新的类，继承自 `Algorithm.Common.AlgorithmBase`。

```csharp
using Algorithm.Common;
using Perceptron.Domain.Entity.Pipeline;
using Perceptron.Domain.Entity.VideoStream;

namespace Algorithm.MyNewAlgorithm;

public class MyExecutor : AlgorithmBase
{
    // ...
}
```

### 步骤 2: 实现构造函数
在构造函数中，必须调用基类构造函数以注入 Pipeline 和 Preferences。同时，初始化算法的元数据。

```csharp
public MyExecutor(AnalysisPipeline pipeline, Dictionary<string, string> preferences)
    : base(pipeline, preferences)
{
    AlgorithmName = "MyNewAlgorithm";
    AlgorithmVersion = "1.0.0";
    AlgorithmDescription = "描述该算法的功能。";
}
```

### 步骤 3: 重写初始化方法 (Initialize)
重写 `Initialize` 方法以解析配置参数。建议使用 `PreferenceParser` 工具类（如果可用）或安全的方式读取字典。**必须**在方法末尾调用 `base.Initialize()`。

```csharp
private bool _myConfigOption;

public override bool Initialize()
{
    // 解析配置示例
    if (_preferences.TryGetValue("MyConfigOption", out string val))
    {
        bool.TryParse(val, out _myConfigOption);
    }
    else 
    {
        _myConfigOption = true; // 默认值
    }
    
    // 如果有 PreferenceParser 辅助类：
    // _myConfigOption = PreferenceParser.ParseBoolValue(_preferences, "MyConfigOption", true);

    return base.Initialize();
}
```

### 4. 实现核心分析逻辑 (Analyze)
实现 `Analyze` 方法。这是算法的核心入口。

```csharp
public override AnalysisResult Analyze(Frame frame)
{
    // 1. 获取输入数据
    // var objects = frame.DetectedObjects;

    // 2. 执行逻辑
    // ...

    // 3. 修改 Frame (如果需要，例如添加标注)
    // frame.Annotation.Shapes.Add(...);

    // 4. 返回结果
    return new AnalysisResult(true);
}
```

## 4. 注意事项 (Notes)

*   **配置解析**: 始终为配置项提供合理的默认值，不要假设配置字典中包含所有键。
*   **性能**: `Analyze` 方法会在每一帧被调用，应尽量避免在此方法中进行重型对象的分配或耗时的 IO 操作。
*   **线程安全**: 如果 Pipeline 是多线程运行的，确保算法内部状态的访问是线程安全的。通常建议算法尽量保持无状态，或仅依赖 `Initialize` 阶段设定的只读状态。
*   **异常处理**: 确保 `Analyze` 方法内的异常被合理处理，避免导致整个分析流程中断。
