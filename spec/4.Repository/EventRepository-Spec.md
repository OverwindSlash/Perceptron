# EventRepository Specification

## 1. 概述
`EventRepository` 是 `IEventRepository` 接口的实现，负责持久化存储领域事件（Domain Event）。它采用了混合存储架构：
- **对象存储 (Minio)**: 用于存储大文件，如快照图片、元数据 JSON 文件和视频片段。
- **关系型数据库 (MySQL)**: 用于存储事件的结构化元数据，支持快速查询和检索。

## 2. 接口定义
该组件实现了 `Perceptron.Domain.Abstraction.Repository.IEventRepository` 接口：

```csharp
public interface IEventRepository
{
    Task SaveDomainEventAsync(DomainEvent domainEvent);
    Task<DomainEvent> LoadDomainEventAsync(string eventId);
    Task DeleteDomainEventAsync(string eventId);
}
```

## 3. 配置与初始化
`EventRepository` 在构造函数中接收一个 `Dictionary<string, string>` 类型的配置字典，并通过 `EventRepositorySettings` 解析以下配置项：

| 配置项 | 说明 |
|Ref | --- |
| `RdbConnectionString` | MySQL 数据库连接字符串 |
| `StorageUrl` | Minio 对象存储服务地址 |
| `StorageUsername` | Minio Access Key |
| `StoragePassword` | Minio Secret Key |
| `WillStoreSnapshot` | 是否存储快照图片 (bool) |
| `WillStoreVideoClip` | 是否存储视频片段 (bool) |

初始化过程中会构建 `MinioClient` 实例，配置 Endpoint 和凭证，且默认禁用 SSL。

## 4. 核心功能逻辑

### 4.1 保存领域事件 (`SaveDomainEventAsync`)
该方法是核心功能，执行流程如下：

#### 4.1.1 对象存储准备 (Minio)
1.  **Bucket 命名规则**: `{AlgorithmName}{Date}`。
    - `AlgorithmName`: 去除空格并转为小写。
    - `Date`: 格式为 `yyyyMMdd`。
    - 示例: `yolov820231027`。
2.  **Bucket 检查与创建**:
    - 检查 Bucket 是否存在。
    - 如果不存在，则创建 Bucket。
    - **设置策略**: 为新创建的 Bucket 设置只读策略 (`s3:GetObject`)，允许 `AWS: *` 访问资源 `arn:aws:s3:::{bucketName}/*`。

#### 4.1.2 快照存储
如果配置 `WillStoreSnapshot` 为 `true`：
1.  **图片上传**:
    - 文件名: `{EventId}.jpg`。
    - 源路径: `domainEvent.ImageLocalPath`。
    - Content-Type: `image/jpg`。
2.  **JSON 元数据上传**:
    - 文件名: `{EventId}.json`。
    - 源路径: `domainEvent.ImageJsonLocalPath`。
    - Content-Type: `application/json`。

#### 4.1.3 视频存储
如果配置 `WillStoreVideoClip` 为 `true`：
1.  **等待文件生成**:
    - 轮询检查 `domainEvent.VideoLocalPath` 是否存在。
    - 超时时间: 5分钟。
2.  **等待文件写入完成**:
    - 检查文件大小稳定性：连续 10 次检查（间隔 200ms+），如果大小不变且大于 0，视为写入稳定。
    - 尝试以只读共享模式打开文件流，确保文件未被独占锁定。
    - 处理 `IOException` 和 `UnauthorizedAccessException`，遇到异常继续等待。
3.  **视频上传**:
    - 文件名: `{EventId}.mp4`。
    - 源路径: `domainEvent.VideoLocalPath`。
    - Content-Type: `video/mp4`。

#### 4.1.4 数据库存储 (MySQL)
在对象存储操作完成后，将事件元数据插入 `events` 表。

**SQL 语句**:
```sql
INSERT INTO events (
    EventId, Timestamp, SourceId, EventType, EventName, 
    AlgorithmName, Message, BucketName, ImageId, VideoId
) VALUES (...)
```

**字段映射**:
- `BucketName`: 存储使用的 Bucket 名称。
- `ImageId`: 上传的图片文件名（如 `{EventId}.jpg`）。
- `VideoId`: 上传的视频文件名（如 `{EventId}.mp4`）。
- 其他字段直接映射 `DomainEvent` 属性。

### 4.2 加载领域事件 (`LoadDomainEventAsync`)
*当前状态*: **未实现** (`NotImplementedException`)。

### 4.3 删除领域事件 (`DeleteDomainEventAsync`)
*当前状态*: **未实现** (`NotImplementedException`)。

## 5. 异常处理
- **数据库连接测试**: `TestDatabaseConnection` 方法用于测试连接，失败时记录日志并抛出异常。
- **Minio 异常**: `SaveDomainEventAsync` 中捕获 `MinioException`，记录 Error 日志但不中断后续数据库操作。
- **数据库异常**: `SaveDomainEventAsync` 中捕获数据库操作异常，记录 Error 日志。
- **视频等待超时**: 如果视频文件生成或写入超时，抛出 `TimeoutException`。
