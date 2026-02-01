# VideoLoader 功能规格说明书

## 1. 概述

`VideoLoader` 是 `Perceptron.Domain.Abstraction.MediaLoader` 的实现组件，基于 OpenCV (`OpenCvSharp`) 库提供视频加载和播放功能。它支持从本地视频文件或网络视频流（如 RTSP、摄像头）中读取帧数据，并将处理后的帧推送到视频帧缓冲区 (`IVideoFrameBuffer`) 或通过回调函数分发。

该组件设计用于处理视频流的生命周期管理，包括连接、播放、暂停、重连、循环播放以及资源释放。

## 2. 核心功能

*   **多源支持**：支持加载本地视频文件和网络视频流（URL）。
*   **帧数据分发**：
    *   **缓冲区推送**：将读取的帧自动推送到绑定的 `IVideoFrameBuffer`。
    *   **事件回调**：支持注册回调函数 (`Action<Frame>`) 实时获取每一帧数据。
*   **播放控制**：提供播放 (Play)、暂停 (Pause)、恢复 (Resume)、停止 (Stop) 等标准控制。
*   **异步运行**：支持通过 `PlayAsync` 在后台线程运行视频采集循环。
*   **跳转 (Seek)**：支持本地视频文件的帧级跳转和时间戳跳转。
*   **丢帧/步进 (Stride)**：支持配置视频步进 (`VideoStride`)，按固定间隔跳过帧，用于降低处理频率。
*   **自动重连**：针对网络流的不稳定性，提供可配置的自动重连机制（重试次数和延迟）。
*   **循环播放**：支持本地视频文件播放结束后自动从头重新播放。
*   **调试模式**：支持仅读取指定数量的帧 (`debugFrameCount`) 后自动停止，便于测试。
*   **硬件加速**：支持通过配置指定 OpenCV 的硬件加速后端和设备。
*   **内存管理**：内部使用 `MatPool` 复用 OpenCV 的 `Mat` 对象，减少内存分配开销。

## 3. 配置参数

`VideoLoader` 通过字典 (`Dictionary<string, string>`) 进行初始化配置，支持以下参数：

| 参数键名 | 类型 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| `SourceId` | String | (无) | **必填**。视频源的唯一标识符。 |
| `VideoCaptureApi` | String | - | 指定 OpenCV 视频捕获 API 后端 (e.g., "FFMPEG", "GSTREAMER")。 |
| `AccelerationType` | String | - | 指定硬件加速类型 (e.g., "CUDA", "D3D11")。 |
| `VideoAccelerationDeviceId` | Int | - | 指定硬件加速设备 ID。 |
| `VideoStride` | Int | 1 | 视频帧步进。设置为 `n` 表示每 `n` 帧取 1 帧（即跳过 `n-1` 帧）。 |
| `MaxRetries` | Int | 0 | 视频流断开时的最大重试次数。 |
| `RetryDelayMs` | Int | 1000 | 重试前的等待延迟（毫秒）。 |
| `Loop` | Bool | false | 是否循环播放（仅适用于本地文件）。 |

## 4. 接口定义

组件实现了 `IVideoLoader` 接口，主要成员如下：

### 属性
*   `SourceId`: 视频源标识。
*   `VideoUri`: 当前加载的视频地址。
*   `Specs`: 视频规格信息（宽、高、FPS、总帧数）。
*   `State`: 当前加载器状态 (`Idle`, `Opened`, `Running`, `Paused`, `Stopped`, `Closed`, `Error`)。
*   `Options`: 当前生效的加载配置选项。

### 方法
*   `AttachBuffer(IVideoFrameBuffer buffer)`: 绑定视频帧缓冲区。
*   `Open(string uri)`: 打开指定的视频 URI，初始化资源但不开始播放。
*   `Play(bool debugMode, int debugFrameCount)`: 同步开始播放循环。
*   `PlayAsync(CancellationToken)`: 异步开始播放。
*   `Pause()` / `Resume()`: 暂停和恢复播放。
*   `Stop()` / `StopAsync()`: 停止播放并取消任务。
*   `Seek(long frameId)` / `Seek(TimeSpan timestamp)`: 跳转到指定位置（仅本地文件）。
*   `SetFrameCallback(Action<Frame>)` / `UnsetFrameCallback`: 注册/注销帧回调。
*   `Close()`: 关闭视频流并释放资源。
*   `Dispose()`: 销毁组件。

## 5. 详细行为描述

### 5.1 初始化与打开 (Open)
*   调用 `Open(uri)` 时，组件会尝试创建 OpenCV 的 `VideoCapture` 对象。
*   如果是本地文件路径，会预先检查文件是否存在。
*   成功打开后，状态转变为 `Opened`，并解析视频规格 (`Specs`)。
*   若打开失败，状态转变为 `Error` 并返回 `false`。

### 5.2 播放循环 (Play)
*   进入播放循环后，状态转变为 `Running`。
*   循环过程中：
    1.  检查暂停状态：若已暂停 (`Paused`)，则挂起线程等待。
    2.  调试模式检查：若开启 `debugMode` 且达到帧数限制，自动退出循环。
    3.  读取帧 (`Grab` & `Retrieve`)：
        *   若读取失败且为本地文件结尾：根据 `Loop` 配置决定重头播放或结束。
        *   若读取失败且为流媒体：尝试根据 `MaxRetries` 进行重连。
    4.  步进过滤：根据 `VideoStride` 跳过不需要的帧。
    5.  封装帧 (`Frame`)：包含 `FrameId`、时间戳、图像数据 (`Mat`)。
    6.  推送到缓冲区 (`_buffer`) 和触发回调 (`frameCallback`)。
    7.  本地文件限速：若是本地文件，根据帧时间戳模拟真实播放速度进行 `Thread.Sleep`。

### 5.3 异常处理与重连
*   在 `Grab()` 失败时，视为流断开。
*   如果重试次数未达到 `MaxRetries`，记录警告日志并等待 `RetryDelayMs` 后尝试重新创建 `VideoCapture`。
*   若重连成功，继续播放；若超过最大重试次数，记录错误日志并停止播放 (`Stop`)。

### 5.4 跳转 (Seek)
*   仅支持本地视频文件。对网络流调用会返回 `false` 并记录警告。
*   支持按帧索引 (`frameId`) 或时间偏移 (`TimeSpan`) 跳转。
*   跳转成功后，内部帧计数器会更新，播放将从新位置继续。

## 6. 状态流转

`VideoLoader` 的状态 (`VideoLoaderState`) 流转逻辑如下：

1.  **初始状态**: `Idle`
2.  **Open 成功**: `Idle` / `Closed` / `Error` -> `Opened`
3.  **Open 失败**: -> `Error`
4.  **Play**: `Opened` -> `Running`
5.  **Pause**: `Running` -> `Paused`
6.  **Resume**: `Paused` -> `Running`
7.  **Stop**: `Running` / `Paused` -> `Stopped`
8.  **Close / Dispose**: 任意状态 -> `Closed`

---
*文档生成依据：Source Code (`IVideoLoader.cs`, `VideoLoader.cs`) & Unit Tests (`OpenCVVideoLoaderTests.cs`)*
