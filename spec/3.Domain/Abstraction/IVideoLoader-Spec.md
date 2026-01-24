# IVideoLoader 接口分析与设计文档

## 1. 概述
`IVideoLoader` 是 `Insight.Domain` 核心域中负责视频流加载与控制的抽象接口。它定义了一套标准化的视频操作契约，旨在屏蔽底层具体的视频采集实现（如 OpenCV, FFmpeg, GStreamer 等），为上层业务提供统一的视频数据来源。

## 2. 核心组件
该模块由以下几个核心文件组成：

*   **[IVideoLoader.cs](IVideoLoader.cs)**: 核心接口，定义了视频加载、控制、数据回调等行为。
*   **[VideoLoaderOptions.cs](VideoLoaderOptions.cs)**: 配置类，用于在打开视频源时指定参数（如硬件加速等）。
*   **[VideoLoaderState.cs](VideoLoaderState.cs)**: 枚举，定义了加载器的生命周期状态（Idle, Opened, Running, Paused, Stopped, Error）。

## 3. 功能特性

### 3.1 生命周期管理
接口提供了完整的生命周期控制方法：
*   `Open(string uri)`: 初始化并打开视频源。
*   `Close()`: 关闭视频源并释放资源。
*   `Dispose()`: 继承自 `IDisposable`，确保资源被正确清理。

### 3.2 播放控制
支持类似于播放器的控制逻辑：
*   **同步控制**: `Play(bool debugMode = false, int debugFrameCount = 0)`, `Pause()`, `Resume()`, `Stop()`。
*   **异步支持**: `PlayAsync(CancellationToken cancellationToken = default)`, `StopAsync()`，适用于需要长时间运行或避免阻塞主线程的场景。
*   **定位 (Seek)**: 支持按帧索引 (`long frameId`) 或时间戳 (`TimeSpan timestamp`) 进行跳转。

### 3.3 数据消费模式
`IVideoLoader` 设计了两种数据消费模式，以适应不同的业务需求：
1.  **缓冲区模式 (Buffer Mode)**:
    *   通过 `AttachBuffer(IVideoFrameBuffer buffer)` 将加载器与缓冲区绑定。
    *   加载器负责将采集到的帧推送到 `IVideoFrameBuffer` 中，消费者从缓冲区读取。这种模式实现了生产者-消费者解耦。
2.  **回调模式 (Callback Mode)**:
    *   通过 `SetFrameCallback(Action<Frame>? frameHandler)` 注册回调函数。
    *   每当有新帧产生时直接调用回调，适用于低延迟或实时处理场景。

### 3.4 状态与监控
*   **状态查询**: `State` 属性实时反映加载器当前状态。

### 3.5 配置灵活性
`IVideoLoader` 支持灵活的配置（部分通过 `Options` 属性暴露，部分作为初始化参数）：
*   `VideoStride`: 视频步进（跳帧），用于降低处理频率。
*   `VideoCaptureApi` & `AccelerationType`: 允许指定底层 API 和硬件加速后端（如 CUDA）。
*   `Loop`: 支持循环播放（常用于测试视频）。
*   `MaxRetries` & `RetryDelayMs`: 重连策略配置（最大重试次数与重试延迟）。

## 4. 设计思路总结

1.  **抽象与解耦**:
    `IVideoLoader` 作为防腐层（Anti-Corruption Layer）的一部分，将视频采集的技术细节（如编解码器、驱动调用）与领域逻辑完全分离。上层应用只需依赖 `IVideoLoader`，无需关心底层是读取本地文件、RTSP 流还是摄像头。

2.  **统一的控制模型**:
    无论底层源是静态文件还是实时流，接口都提供了统一的 `Play/Pause/Stop` 语义，简化了客户端代码的复杂度。

3.  **灵活的数据流向**:
    同时支持 "推向缓冲区" 和 "直接回调" 两种模式，兼顾了高性能缓冲处理（适合分析任务）和实时预览（适合 UI 显示）的需求。

4.  **可扩展的配置**:
    `VideoLoaderOptions` 封装了所有配置项，支持从配置字典解析（实现层逻辑），接口层通过 `Options` 属性暴露当前生效的配置。
