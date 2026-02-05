using System.Data.Common;
using Dapper;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using MySqlConnector;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Event;
using Perceptron.Domain.Setting;
using Serilog;

namespace Repository.MinioMySQL;

public class EventRepository : IEventRepository, IDisposable
{
    public string RdbConnectionString { get; }

    public string StorageUrl { get; }
    public string StorageUsername { get; }
    public string StoragePassword { get; }
    public bool WillStoreSnapshot { get; }
    public bool WillStoreVideoClip { get; }

    protected IMinioClient _minio;

    public EventRepository(Dictionary<string, string>? preferences = null)
    {
        RdbConnectionString = EventRepositorySettings.ParseRdbConnectionString(preferences);
        StorageUrl = EventRepositorySettings.ParseStorageUrl(preferences);
        StorageUsername = EventRepositorySettings.ParseStorageUsername(preferences);
        StoragePassword = EventRepositorySettings.ParseStoragePassword(preferences);
        WillStoreSnapshot = EventRepositorySettings.ParseWillStoreSnapshot(preferences);
        WillStoreVideoClip = EventRepositorySettings.ParseWillStoreVideoClip(preferences);

        InitObjectStorageClient();
    }

    protected virtual void InitObjectStorageClient()
    {
        _minio = new MinioClient()
            .WithEndpoint(StorageUrl)
            .WithCredentials(StorageUsername, StoragePassword)
            .WithSSL(false)
            .Build();
    }

    private void TestDatabaseConnection()
    {
        try
        {
            using var connection = CreateConnection();
            connection.Open();

            // 测试连接是否成功
            var result = connection.QuerySingle<int>("SELECT 1");
            if (result == 1)
            {
                Log.Information("Database connection test successful.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database connection test failed. error message: {ex.Message}");
            throw;
        }
    }

    public async Task SaveDomainEventAsync(DomainEvent domainEvent)
    {
        try
        {
            string bucketName = $"{domainEvent.AlgorithmName.Replace(" ", string.Empty).ToLower()}{DateTime.Now.ToString("yyyyMMdd").ToLower()}";
            string imageName = string.Empty;
            string jsonName = string.Empty;
            string videoName = string.Empty;

            // Make a bucket on the server, if not already present.
            var beArgs = new BucketExistsArgs()
                .WithBucket(bucketName);
            bool found = await _minio.BucketExistsAsync(beArgs).ConfigureAwait(false);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs()
                    .WithBucket(bucketName);
                await _minio.MakeBucketAsync(mbArgs).ConfigureAwait(false);

                var policyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BucketPolicy.json");
                var policyTemplate = await File.ReadAllTextAsync(policyPath);
                var policyJson = policyTemplate.Replace("__BUCKET_NAME__", bucketName);
                // Change policy type parameter
                var args = new SetPolicyArgs()
                    .WithBucket(bucketName)
                    .WithPolicy(policyJson);
                await _minio.SetPolicyAsync(args).ConfigureAwait(false);
            }

            if (WillStoreSnapshot)
            {
                imageName = $"{domainEvent.EventId}.jpg";

                // Upload image to bucket.
                var putImageArgs = new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(imageName)
                    .WithFileName(domainEvent.ImageLocalPath)
                    .WithContentType("image/jpg");
                await _minio.PutObjectAsync(putImageArgs).ConfigureAwait(false);


                jsonName = $"{domainEvent.EventId}.json";
                // Upload image to bucket.
                var putJsonArgs = new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(jsonName)
                    .WithFileName(domainEvent.ImageJsonLocalPath)
                    .WithContentType("application/json");
                await _minio.PutObjectAsync(putJsonArgs).ConfigureAwait(false);
            }

            if (WillStoreVideoClip)
            {
                videoName = $"{domainEvent.EventId}.mp4";

                // 等待文件存在
                var timeout = TimeSpan.FromMinutes(5); // 设置5分钟超时
                var startTime = DateTime.Now;
                while (!File.Exists(domainEvent.VideoLocalPath))
                {
                    if (DateTime.Now - startTime > timeout)
                    {
                        throw new TimeoutException($"等待视频文件生成超时: {domainEvent.VideoLocalPath}");
                    }

                    Thread.Sleep(100);
                }

                // 等待文件完全写入完成
                bool fileReady = false;
                long previousSize = -1;
                int stableCount = 0;
                const int requiredStableChecks = 10; // 需要连续10次检查文件大小稳定

                while (!fileReady)
                {
                    if (DateTime.Now - startTime > timeout)
                    {
                        throw new TimeoutException($"等待视频文件写入完成超时: {domainEvent.VideoLocalPath}");
                    }

                    try
                    {
                        var fileInfo = new FileInfo(domainEvent.VideoLocalPath);
                        long currentSize = fileInfo.Length;

                        // 检查文件大小是否稳定（连续多次检查大小不变）
                        if (currentSize == previousSize && currentSize > 0)
                        {
                            stableCount++;
                            if (stableCount >= requiredStableChecks)
                            {
                                // 尝试以只读方式打开文件，确保没有其他进程在写入
                                using var fs = new FileStream(domainEvent.VideoLocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                // 如果能成功打开并读取，说明文件已经完全生成
                                var buffer = new byte[1024];
                                fs.Read(buffer, 0, Math.Min(1024, (int)fs.Length));
                                fileReady = true;
                            }
                        }
                        else
                        {
                            stableCount = 0;
                            previousSize = currentSize;
                        }
                    }
                    catch (IOException)
                    {
                        // 文件仍在被其他进程使用，继续等待
                        stableCount = 0;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 文件访问被拒绝，继续等待
                        stableCount = 0;
                    }

                    if (!fileReady)
                    {
                        Thread.Sleep(200); // 稍微增加等待间隔
                    }
                }

                // Upload video to bucket.
                var putVideoArgs = new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(videoName)
                    .WithFileName(domainEvent.VideoLocalPath)
                    .WithContentType("video/mp4");
                await _minio.PutObjectAsync(putVideoArgs).ConfigureAwait(false);
            }

            domainEvent.BucketName = bucketName;
            domainEvent.ImageId = imageName;
            domainEvent.VideoId = videoName;
        }
        catch (MinioException ex)
        {
            Log.Error($"Save Event into storage failed. Error message: {ex.Message}");
        }

        const string sql = @"
            INSERT INTO events (
                EventId, 
                Timestamp, 
                SourceId, 
                EventType, 
                EventName, 
                AlgorithmName, 
                Message, 
                BucketName, 
                ImageId, 
                VideoId
            ) VALUES (
                @EventId, 
                @Timestamp, 
                @SourceId, 
                @EventType, 
                @EventName, 
                @AlgorithmName, 
                @Message, 
                @BucketName, 
                @ImageId, 
                @VideoId
            )";

        try
        {
            // 为每个操作创建独立的数据库连接，避免并发访问问题
            using var connection = CreateConnection();
            await connection.OpenAsync();

            var parameters = new
            {
                EventId = domainEvent.EventId.ToString(),
                Timestamp = domainEvent.Timestamp,
                SourceId = domainEvent.SourceId,
                EventType = domainEvent.EventType,
                EventName = domainEvent.EventName,
                AlgorithmName = domainEvent.AlgorithmName,
                Message = domainEvent.Message ?? string.Empty,
                BucketName = domainEvent.BucketName ?? string.Empty,
                ImageId = domainEvent.ImageId ?? string.Empty,
                VideoId = domainEvent.VideoId ?? string.Empty
            };

            var rowsAffected = await connection.ExecuteAsync(sql, parameters);
        }
        catch (Exception ex)
        {
            Log.Error($"Insert Event into database failed. Error message: {ex.Message}");
        }
    }

    protected virtual DbConnection CreateConnection()
    {
        return new MySqlConnection(RdbConnectionString);
    }

    public Task<DomainEvent> LoadDomainEventAsync(string eventId)
    {
        throw new NotImplementedException();
    }

    public Task DeleteDomainEventAsync(string eventId)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        // MinioClient实现了IDisposable，但通常不需要显式释放
        // 如果需要可以在这里添加清理逻辑
    }
}