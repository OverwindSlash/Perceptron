# MessagePoster Component Specification

## 1. 概述 (Overview)

`MessagePoster` 是一个用于将领域事件 (Domain Event) 发送到外部 HTTP 服务的组件。它实现了 `IMessagePoster` 接口，支持将事件内容序列化为 JSON 格式并通过 HTTP POST 请求发送。

此外，该组件支持基于事件类型和源 ID 的重复事件抑制功能，以防止在短时间内发送过多的相同事件。

## 2. 接口定义 (Interface Definition)

组件实现了 `IMessagePoster` 接口：

```csharp
public interface IMessagePoster
{
    void PostDomainEventMessage(DomainEvent @event);
}
```

## 3. 配置项 (Configuration)

组件的配置通过 `MessagePosterSettings` 类管理，支持以下配置项：

| 配置项名称 | 类型 | 默认值 | 描述 |
| :--- | :--- | :--- | :--- |
| `WillPostMessage` | `bool` | `true` | 是否启用消息发送功能。 |
| `DestinationUrl` | `string` | `http://127.0.0.1/perceptron-msg` | 接收消息的目标 URL 地址。 |
| `CheckDuplicateEvent` | `bool` | `false` | 是否启用重复事件检测和抑制功能。 |
| `EventSuppressionIntervals` | `Dictionary<string, int>` | `{}` (Empty) | 定义特定事件类型的抑制时间间隔（毫秒）。键为 `EventName`，值为时间间隔。 |

### 配置示例 (JSON)

```json
{
  "WillPostMessage": "true",
  "DestinationUrl": "http://localhost:8080/api/events",
  "CheckDuplicateEvent": "true",
  "EventSuppressionIntervals": "{\"PersonDetected\": 5000, \"MotionDetected\": 2000}"
}
```

## 4. 功能逻辑 (Functional Logic)

### 4.1 消息发送 (Message Posting)

1.  **开关检查**：首先检查 `WillPostMessage` 配置。如果为 `false`，则直接忽略事件，不执行任何操作。
2.  **抑制检查**：如果 `CheckDuplicateEvent` 为 `true`，则调用抑制逻辑（见 4.2）。如果事件被判定为需要抑制，则终止发送流程。
3.  **序列化**：调用 `DomainEvent.GenerateJsonContent()` 方法将事件对象序列化为 JSON 字符串。
4.  **异步发送**：
    *   构建 `HttpClient` 并向 `DestinationUrl` 发送 POST 请求。
    *   Content-Type 设置为 `application/json`。
    *   发送过程在后台任务 (`Task.Run`) 中异步执行，不阻塞主线程。
    *   包含基本的异常捕获，忽略网络错误，防止组件崩溃。

### 4.2 事件抑制 (Event Suppression)

当 `CheckDuplicateEvent` 开启时，组件会根据以下逻辑判断是否抑制当前事件：

1.  **配置查找**：根据当前事件的 `EventName` 在 `EventSuppressionIntervals` 字典中查找配置的时间间隔。
    *   如果未找到对应配置，默认**不抑制**该事件。
2.  **唯一键生成**：组合 `SourceId` 和 `EventName` 生成唯一键：`{SourceId}_{EventName}`。
3.  **时间间隔判断**：
    *   组件维护一个线程安全的缓存 (`ConcurrentDictionary`)，记录每个键上一次成功发送的时间。
    *   如果缓存中存在该键，计算当前时间与上次发送时间的差值。
    *   如果差值 **小于** 配置的间隔时间，则判定为重复/频繁事件，**予以抑制**（不发送）。
    *   如果差值 **大于或等于** 间隔时间，或者缓存中不存在该键，则**允许发送**，并更新缓存中的时间为当前时间。

## 5. 代码参考 (Code Reference)

*   **接口**: [IMessagePoster.cs](../../../src/3.Domain/Perceptron.Domain/Abstraction/MessagePoster/IMessagePoster.cs)
*   **设置**: [MessagePosterSettings.cs](../../../src/3.Domain/Perceptron.Domain/Setting/MessagePosterSettings.cs)
*   **实现**: [MessagePoster.cs](../../../src/5.Component/MessagePoster.RestfulJson/MessagePoster.cs)
