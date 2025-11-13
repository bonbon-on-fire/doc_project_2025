using AIChat.Server.Services.EventStore;
using AIChat.Server.Services.EventStore.Implementations;
using AIChat.Server.Storage.Sqlite;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIChat.Server.Tests.Services.EventStore;

/// <summary>
/// Basic tests for snapshot store functionality.
/// Tests core snapshot storage operations.
/// </summary>
public class SnapshotIntegrationTests : IDisposable
{
    private readonly TestSqliteConnectionFactory _connectionFactory;
    private readonly SqliteSnapshotStore _snapshotStore;

    public SnapshotIntegrationTests()
    {
        // Create shared in-memory SQLite database for testing (allows multiple connections to the same database)
        _connectionFactory = new TestSqliteConnectionFactory("Data Source=file:memdb1?mode=memory&cache=shared");

        // Initialize schema
        InitializeSchemaAsync().GetAwaiter().GetResult();

        // Create components
        var metricsCollector = new SnapshotMetricsCollector();
        var serializer = new JsonEventSerializer();
        _snapshotStore = new SqliteSnapshotStore(_connectionFactory, serializer, new NullLogger<SqliteSnapshotStore>(), metricsCollector);
    }

    private async Task InitializeSchemaAsync()
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await SchemaHelper.EnsureSchemaAsync(connection);
        await SnapshotSchemaHelper.EnsureSnapshotSchemaAsync(connection);
    }

    public void Dispose()
    {
        _connectionFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateSnapshot_ValidData_ShouldSucceed()
    {
        // Arrange
        const string streamId = "test-stream";
        const long version = 1L;
        var testState = new TestState { Name = "Test", Value = 42 };
        var metadata = new Dictionary<string, object> { ["test"] = "metadata" };

        // Act
        var result = await _snapshotStore.CreateSnapshotAsync(streamId, version, testState, metadata);

        // Assert
        result.Success.Should().BeTrue();
        result.SnapshotId.Should().NotBeNullOrEmpty();
        result.CompressedSize.Should().BeGreaterThan(0);
        result.UncompressedSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetLatestSnapshot_ExistingSnapshot_ShouldReturnData()
    {
        // Arrange
        const string streamId = "restore-stream";
        const long version = 5L;
        var originalState = new TestState { Name = "Original", Value = 100 };

        // Create snapshot first
        var createResult = await _snapshotStore.CreateSnapshotAsync(streamId, version, originalState);
        createResult.Success.Should().BeTrue();

        // Act
        var getResult = await _snapshotStore.GetLatestSnapshotAsync<TestState>(streamId);

        // Assert
        getResult.Success.Should().BeTrue();
        getResult.Data.Should().NotBeNull();
        getResult.Data!.Name.Should().Be("Original");
        getResult.Data.Value.Should().Be(100);
        getResult.Metadata!.Version.Should().Be(version);
    }

    [Fact]
    public async Task HealthCheck_ShouldReturnHealthy()
    {
        // Act
        var health = await _snapshotStore.CheckHealthAsync();

        // Assert
        health.IsHealthy.Should().BeTrue();
        health.Message.Should().Contain("healthy");
    }

    [Fact]
    public async Task GetMetrics_ShouldReturnBasicMetrics()
    {
        // Act
        var metrics = await _snapshotStore.GetMetricsAsync();

        // Assert
        metrics.Should().NotBeNull();
        metrics.TotalSnapshots.Should().BeGreaterOrEqualTo(0);
    }

    /// <summary>
    /// Test state class for snapshot testing.
    /// </summary>
    private sealed class TestState
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
