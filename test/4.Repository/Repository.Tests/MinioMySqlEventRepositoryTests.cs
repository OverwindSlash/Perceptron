using System.Data;
using System.Data.Common;
using Minio;
using Minio.DataModel.Args;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Perceptron.Domain.Event;
using Repository.MinioMySQL;

namespace Repository.Tests;

public class MinioMySqlEventRepositoryTests
{
    private Mock<IMinioClient> _mockMinioClient = null!;
    private Mock<DbConnection> _mockDbConnection = null!;
    private Mock<DbCommand> _mockDbCommand = null!;
    private TestableEventRepository _repository = null!;

    public class TestableEventRepository : EventRepository
    {
        public DbConnection ConnectionToReturn { get; set; } = null!;

        public TestableEventRepository(Dictionary<string, string> preferences)
            : base(preferences)
        {
        }

        public void SetMinioClient(IMinioClient minioClient)
        {
            _minio = minioClient;
        }

        protected override void InitObjectStorageClient()
        {
            // Do nothing in tests to avoid creating real Minio client
        }

        protected override DbConnection CreateConnection()
        {
            return ConnectionToReturn;
        }
    }

    public class TestDomainEvent : DomainEvent
    {
        public TestDomainEvent(string sourceId, string eventType, string eventName, string algorithmName)
            : base(sourceId, eventType, eventName, algorithmName)
        {
        }

        public override string GenerateJsonContent() => "{}";
        public override string GenerateLogContent() => "log";
    }

    [SetUp]
    public void Setup()
    {
        _mockMinioClient = new Mock<IMinioClient>();
        _mockDbConnection = new Mock<DbConnection>();
        _mockDbCommand = new Mock<DbCommand>();

        // Setup DbConnection to return our mock command
        _mockDbConnection.Protected()
            .Setup<DbCommand>("CreateDbCommand")
            .Returns(_mockDbCommand.Object);

        _mockDbConnection.Setup(x => x.OpenAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Setup DbCommand for Dapper
        _mockDbCommand.Setup(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        
        // Setup DbCommand.Parameters collection (Dapper needs this)
        var mockParameters = new Mock<DbParameterCollection>();
        _mockDbCommand.Protected()
            .Setup<DbParameterCollection>("DbParameterCollection")
            .Returns(mockParameters.Object);
        
        // Setup Minio default behaviors
        _mockMinioClient.Setup(x => x.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup DbCommand.CreateParameter
        var mockParameter = new Mock<DbParameter>();
        _mockDbCommand.Protected()
            .Setup<DbParameter>("CreateDbParameter")
            .Returns(mockParameter.Object);
        
        // Setup DbParameterCollection.Add
        mockParameters.Setup(p => p.Add(It.IsAny<object>())).Returns(0);
    }

    [TearDown]
    public void TearDown()
    {
        _repository?.Dispose();
    }

    [Test]
    public async Task SaveDomainEventAsync_ShouldUploadSnapshot_AndInsertToDb()
    {
        // Arrange
        var preferences = new Dictionary<string, string>
        {
            { "WillStoreSnapshot", "true" },
            { "WillStoreVideoClip", "false" },
            { "RdbConnectionString", "server=test" },
            { "StorageUrl", "http://test" }
        };

        _repository = new TestableEventRepository(preferences);
        _repository.SetMinioClient(_mockMinioClient.Object);
        _repository.ConnectionToReturn = _mockDbConnection.Object;

        var domainEvent = new TestDomainEvent("source1", "type1", "name1", "algo1");
        domainEvent.ImageLocalPath = "test.jpg";
        domainEvent.ImageJsonLocalPath = "test.json";

        // Act
        await _repository.SaveDomainEventAsync(domainEvent);

        // Assert
        // 1. Verify Minio interactions
        _mockMinioClient.Verify(x => x.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        // PutObject should be called for image and json (2 times)
        _mockMinioClient.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // 2. Verify DB interactions
        // Command should be executed (Insert)
        _mockDbCommand.Verify(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SaveDomainEventAsync_WhenBucketDoesNotExist_ShouldCreateBucketAndSetPolicy()
    {
        // Arrange
        var preferences = new Dictionary<string, string>
        {
            { "WillStoreSnapshot", "false" },
            { "WillStoreVideoClip", "false" }
        };

        _repository = new TestableEventRepository(preferences);
        _repository.SetMinioClient(_mockMinioClient.Object);
        _repository.ConnectionToReturn = _mockDbConnection.Object;
        
        // Mock BucketExists to return false
        _mockMinioClient.Setup(x => x.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var domainEvent = new TestDomainEvent("source1", "type1", "name1", "algo1");

        // Act
        await _repository.SaveDomainEventAsync(domainEvent);

        // Assert
        _mockMinioClient.Verify(x => x.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockMinioClient.Verify(x => x.SetPolicyAsync(It.IsAny<SetPolicyArgs>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
