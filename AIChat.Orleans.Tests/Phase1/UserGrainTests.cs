using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Grains;
using AIChat.Orleans.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Orleans.TestingHost;
using Orleans.Hosting;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Phase 1 tests for UserGrain shadow mode functionality.
/// Tests basic grain operations without affecting existing SSE system.
/// </summary>
[TestFixture]
public class UserGrainTests
{
    private TestCluster? _cluster;

    [SetUp]
    public async Task Setup()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Test]
    public async Task UserGrain_ShouldActivate_InTestCluster()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var grain = _cluster.GrainFactory.GetGrain<IUserGrain>("test-user-1");

        // Act
        var state = await grain.GetState();

        // Assert
        Assert.That(state, Is.Not.Null);
        Assert.That(state.UserId, Is.EqualTo("test-user-1"));
        Assert.That(state.Connections, Is.Not.Null);
        Assert.That(state.ActiveChats, Is.Not.Null);
        Assert.That(state.RecentActivity, Is.Not.Null);
    }

    [Test]
    public async Task UserGrain_ShouldRecordActivity_InShadowMode()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var grain = _cluster.GrainFactory.GetGrain<IUserGrain>("test-user-2");

        // Act
        await grain.RecordActivity(ActivityType.MessageSent, "test metadata");
        var state = await grain.GetState();

        // Assert
        Assert.That(state.RecentActivity.Count, Is.EqualTo(1));
        Assert.That(state.RecentActivity.First().Type, Is.EqualTo(ActivityType.MessageSent));
        Assert.That(state.RecentActivity.First().Metadata, Is.EqualTo("test metadata"));
        Assert.That(state.Metrics.TotalActivities, Is.EqualTo(1));
        Assert.That(state.LastActivity, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
    }

    [Test]
    public async Task UserGrain_ShouldMaintainCircularActivityBuffer()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var grain = _cluster.GrainFactory.GetGrain<IUserGrain>("test-user-3");

        // Act - Record 102 activities to test circular buffer (max 100)
        for (int i = 0; i < 102; i++)
        {
            await grain.RecordActivity(ActivityType.MessageSent, $"activity-{i}");
        }
        var state = await grain.GetState();

        // Assert
        Assert.That(state.RecentActivity.Count, Is.EqualTo(100)); // Should cap at 100
        Assert.That(state.Metrics.TotalActivities, Is.EqualTo(102)); // Total should be accurate
        
        // Should contain the last 100 activities
        var activities = state.RecentActivity.ToList();
        Assert.That(activities[0].Metadata, Is.EqualTo("activity-2")); // First two were removed
        Assert.That(activities[99].Metadata, Is.EqualTo("activity-101")); // Last activity
    }

    [Test]
    public async Task UserGrain_HealthCheck_ShouldReturnHealthyForActiveGrain()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var grain = _cluster.GrainFactory.GetGrain<IUserGrain>("test-user-4");

        // Act
        await grain.RecordActivity(ActivityType.Connected, "user connected");
        var healthResult = await grain.CheckHealth();

        // Assert
        Assert.That(healthResult, Is.Not.Null);
        Assert.That(healthResult.IsHealthy, Is.True);
        Assert.That(healthResult.GrainId, Is.EqualTo("test-user-4"));
        Assert.That(healthResult.LastActivity, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
        Assert.That(healthResult.Metrics, Is.Not.Null);
        Assert.That(healthResult.Metrics.TotalActivities, Is.EqualTo(1));
        Assert.That(healthResult.Warnings, Is.Empty);
    }

    [Test]
    public async Task UserGrain_Phase2Methods_ShouldBeStubbed()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var grain = _cluster.GrainFactory.GetGrain<IUserGrain>("test-user-5");

        // Act & Assert - These should not throw but also not do anything in Phase 1
        await grain.RegisterConnection("conn-1", "client-1");
        await grain.UnregisterConnection("conn-1");
        await grain.SubscribeToChat("conn-1", "chat-1");
        await grain.UnsubscribeFromChat("conn-1", "chat-1");

        // These should return dummy values
        var message = new ChatMessage { Id = "msg-1", ChatId = "chat-1", UserId = "test-user-5", Content = "test" };
        await grain.RelayMessage(message);
        
        var chunk = new StreamChunk { OperationId = "op-1", ChatId = "chat-1", Content = "chunk" };
        await grain.RelayStreamChunk(chunk);

        var operationId = await grain.ProcessMessageWithBackground(message);
        Assert.That(operationId, Is.Not.Null);
        Assert.That(operationId, Is.Not.Empty);

        await grain.NotifyOperationStarted(operationId, "chat-1");
        await grain.NotifyOperationCompleted(operationId, true);

        // Verify grain state is unaffected by stubbed methods
        var state = await grain.GetState();
        Assert.That(state.Connections, Is.Empty);
        Assert.That(state.ActiveChats, Is.Empty);
        Assert.That(state.ActiveOperations, Is.Empty);
    }

    [Test]
    public async Task MultipleGrains_ShouldWorkConcurrently()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var userIds = new[] { "user-a", "user-b", "user-c", "user-d", "user-e" };
        var grains = userIds.Select(id => _cluster.GrainFactory.GetGrain<IUserGrain>(id)).ToList();

        // Act - Record activities concurrently
        var tasks = grains.Select(async (grain, index) =>
        {
            for (int i = 0; i < 10; i++)
            {
                await grain.RecordActivity(ActivityType.MessageSent, $"user-{index}-activity-{i}");
            }
        });
        await Task.WhenAll(tasks);

        // Assert - Check each grain has correct state
        for (int i = 0; i < grains.Count; i++)
        {
            var state = await grains[i].GetState();
            Assert.That(state.UserId, Is.EqualTo(userIds[i]));
            Assert.That(state.Metrics.TotalActivities, Is.EqualTo(10));
            Assert.That(state.RecentActivity.Count, Is.EqualTo(10));
        }
    }

    [Test]
    public async Task UserGrain_ShouldPersistStateAcrossReactivation()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var userId = "test-user-persistence";
        var grain1 = _cluster.GrainFactory.GetGrain<IUserGrain>(userId);

        // Act - Record some activities and force deactivation
        await grain1.RecordActivity(ActivityType.Connected, "first connection");
        await grain1.RecordActivity(ActivityType.MessageSent, "first message");
        
        // Get the grain again (may be different instance)
        var grain2 = _cluster.GrainFactory.GetGrain<IUserGrain>(userId);
        var state = await grain2.GetState();

        // Assert - State should persist
        Assert.That(state.UserId, Is.EqualTo(userId));
        Assert.That(state.Metrics.TotalActivities, Is.EqualTo(2));
        Assert.That(state.RecentActivity.Count, Is.EqualTo(2));
        
        var activities = state.RecentActivity.ToList();
        Assert.That(activities[0].Type, Is.EqualTo(ActivityType.Connected));
        Assert.That(activities[1].Type, Is.EqualTo(ActivityType.MessageSent));
    }
}

/// <summary>
/// Test silo configurator for Orleans testing.
/// </summary>
public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryGrainStorage("UserGrainStorage")
            .AddMemoryGrainStorage("PubSubStore")
            
            // Configure services required by grains
            .ConfigureServices(services =>
            {
                // Add Orleans metrics collector (required by UserGrain)
                services.AddSingleton<IOrleansMetricsCollector, OrleansMetricsCollector>();
                
                // Add Orleans grain configuration with test-friendly settings
                services.Configure<OrleansGrainConfiguration>(config =>
                {
                    config.UserGrain.MaxActivityBufferSize = 50;
                    config.UserGrain.CleanupIntervalMinutes = 1;
                    config.UserGrain.EnablePeriodicTimers = false;
                    config.Connections.MaxConnectionsPerUser = 5;
                    config.Persistence.ActivityPersistenceInterval = 5;
                });
            })
            
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
                // Only show errors for Orleans runtime during tests
                logging.AddFilter("Orleans", LogLevel.Error);
                logging.AddFilter("Microsoft", LogLevel.Error);
                // But allow our Orleans components to log at Debug level
                logging.AddFilter("AIChat.Orleans", LogLevel.Debug);
            });
    }
}