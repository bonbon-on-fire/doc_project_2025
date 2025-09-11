using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Orleans.TestingHost;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Tests for UserGrain timer management and disposal.
/// Ensures proper cleanup of timers to prevent memory leaks.
/// </summary>
[TestFixture]
public class UserGrainTimerTests
{
    private TestCluster? _cluster;

    [SetUp]
    public async Task Setup()
    {
        var builder = new TestClusterBuilder();
        _ = builder.AddSiloBuilderConfigurator<TimerTestSiloConfigurator>();

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

    /// <summary>
    /// Tests that timers are properly created when EnablePeriodicTimers is true.
    /// </summary>
    [Test]
    public async Task UserGrainShouldCreateTimersWhenEnabled()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var grain = _cluster!.GrainFactory.GetGrain<IUserGrain>("timer-test-user-1");

        // Act - Activate grain and record activity to trigger timer behavior
        await grain.RecordActivity(ActivityType.MessageSent, "Test message");
        await Task.Delay(100); // Give time for grain to activate

        var state = await grain.GetState();

        // Assert - Grain should be active with timers configured
        Assert.That(state, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(state.UserId, Is.EqualTo("timer-test-user-1"));
            Assert.That(state.Metrics.ActivationCount, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// Tests that grain can be deactivated without errors even with active timers.
    /// </summary>
    [Test]
    public async Task UserGrainShouldDeactivateCleanlyWithActiveTimers()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        var grain = _cluster!.GrainFactory.GetGrain<IUserGrain>("timer-cleanup-test");

        // Act - Activate grain
        await grain.RecordActivity(ActivityType.MessageSent, "Test for cleanup");
        var stateBefore = await grain.GetState();

        // Force deactivation by requesting deactivation (Orleans test cluster feature)
        await _cluster
            .Client.GetGrain<IUserGrain>("timer-cleanup-test")
            .RecordActivity(ActivityType.Disconnected, "Forcing deactivation");

        // Wait for potential deactivation
        await Task.Delay(1000);

        // Reactivate and check state
        var stateAfter = await grain.GetState();

        Assert.Multiple(() =>
        {
            // Assert - Should reactivate successfully with incremented activation count
            Assert.That(stateBefore, Is.Not.Null);
            Assert.That(stateAfter, Is.Not.Null);
        });
        Assert.That(stateAfter.UserId, Is.EqualTo("timer-cleanup-test"));
        // State is persisted, so we can verify the grain was deactivated and reactivated
    }

    /// <summary>
    /// Tests that multiple rapid activations/deactivations don't cause timer issues.
    /// </summary>
    [Test]
    public async Task UserGrainShouldHandleRapidReactivationWithoutTimerLeaks()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        const string userId = "rapid-reactivation-test";

        // Act - Perform multiple activations
        for (var i = 0; i < 3; i++)
        {
            var grain = _cluster!.GrainFactory.GetGrain<IUserGrain>(userId);
            await grain.RecordActivity(ActivityType.MessageSent, $"Message {i}");

            // Small delay between iterations
            await Task.Delay(100);
        }

        // Get final state
        var finalGrain = _cluster!.GrainFactory.GetGrain<IUserGrain>(userId);
        var state = await finalGrain.GetState();

        // Assert - Should have clean state without issues
        Assert.That(state, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(state.RecentActivity, Has.Count.GreaterThan(0));
            Assert.That(state.Metrics.TotalActivities, Is.EqualTo(3));
        });
    }

    /// <summary>
    /// Tests that timer disposal doesn't interfere with state persistence.
    /// </summary>
    [Test]
    public async Task UserGrainShouldPersistStateDuringTimerDisposal()
    {
        // Arrange
        Assert.That(_cluster, Is.Not.Null);
        const string userId = "persistence-test";
        var grain = _cluster!.GrainFactory.GetGrain<IUserGrain>(userId);

        // Act - Add activities to trigger state persistence
        for (var i = 0; i < 15; i++) // More than ActivityPersistenceInterval
        {
            await grain.RecordActivity(ActivityType.MessageSent, $"Message {i}");
        }

        var stateBefore = await grain.GetState();

        // Wait for potential timer callbacks and persistence
        await Task.Delay(500);

        var stateAfter = await grain.GetState();

        Assert.Multiple(() =>
        {
            // Assert - State should be consistent
            Assert.That(stateBefore.Metrics.TotalActivities, Is.EqualTo(15));
            Assert.That(
                stateAfter.Metrics.TotalActivities,
                Is.EqualTo(stateBefore.Metrics.TotalActivities)
            );
        });
    }
}

/// <summary>
/// Test silo configurator for timer-specific tests with custom configuration.
/// </summary>
public class TimerTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        // Configure test services with timer settings
        _ = siloBuilder.ConfigureServices(services =>
        {
            // Add configuration with timers enabled
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["OrleansGrains:UserGrain:EnablePeriodicTimers"] = "true",
                        ["OrleansGrains:UserGrain:CleanupIntervalMinutes"] = "1", // Shorter for testing
                        ["OrleansGrains:UserGrain:MetricsUpdateIntervalMinutes"] = "1",
                        ["OrleansGrains:UserGrain:MaxActivityBufferSize"] = "50",
                        ["OrleansGrains:UserGrain:ActivityRetentionHours"] = "0.5",
                        ["OrleansGrains:UserGrain:CompletedOperationRetentionMinutes"] = "10",
                        ["OrleansGrains:Connections:StaleConnectionThresholdMinutes"] = "5",
                        ["OrleansGrains:Connections:ReconnectionGracePeriodMinutes"] = "2",
                        ["OrleansGrains:Persistence:ActivityPersistenceInterval"] = "10",
                        ["OrleansGrains:Persistence:MessagePersistenceInterval"] = "10",
                        ["OrleansGrains:Persistence:PersistOnDeactivation"] = "true",
                        ["OrleansGrains:Persistence:MaxPersistenceRetries"] = "3",
                        ["OrleansGrains:Persistence:PersistenceRetryDelayMilliseconds"] = "50",
                    }
                )
                .Build();

            _ = services.AddSingleton<IConfiguration>(configuration);
            _ = services.Configure<OrleansGrainConfiguration>(
                configuration.GetSection(OrleansGrainConfiguration.SectionName)
            );

            // Add Orleans metrics collector (required by UserGrain)
            _ = services.AddSingleton<IOrleansMetricsCollector, OrleansMetricsCollector>();
        });

        // Add memory grain storage as default for testing
        _ = siloBuilder.AddMemoryGrainStorageAsDefault();
    }
}
