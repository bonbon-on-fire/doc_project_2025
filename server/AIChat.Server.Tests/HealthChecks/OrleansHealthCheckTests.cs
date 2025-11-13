using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.HealthChecks;

/// <summary>
/// Unit tests for Orleans health check in Orleans-only mode.
/// These tests verify that the health check correctly reports application health
/// based on Orleans cluster availability.
/// </summary>
public class OrleansHealthCheckTests
{
    private readonly Mock<IGrainFactory> _mockGrainFactory;
    private readonly Mock<ILogger<OrleansHealthCheck>> _mockLogger;
    private readonly OrleansHealthCheck _healthCheck;

    public OrleansHealthCheckTests()
    {
        _mockGrainFactory = new Mock<IGrainFactory>();
        _mockLogger = new Mock<ILogger<OrleansHealthCheck>>();
        _healthCheck = new OrleansHealthCheck(_mockGrainFactory.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_OrleansHealthy_ReturnsHealthy()
    {
        // Arrange
        var mockManagementGrain = new Mock<IManagementGrain>();

        var activeSilos = new Dictionary<SiloAddress, SiloStatus>
        {
            { SiloAddress.New(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 11111), 12345), SiloStatus.Active },
            { SiloAddress.New(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 11112), 12346), SiloStatus.Active }
        };

        mockManagementGrain
            .Setup(g => g.GetHosts(true))
            .ReturnsAsync(activeSilos);

        _mockGrainFactory
            .Setup(gf => gf.GetGrain<IManagementGrain>(0, null))
            .Returns(mockManagementGrain.Object);

        var context = new HealthCheckContext();

        // Act
        var result = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Orleans cluster is healthy", result.Description);

        // Verify data contains Orleans metrics
        Assert.True(result.Data.ContainsKey("orleans_cluster_connected"));
        Assert.True((bool)result.Data["orleans_cluster_connected"]);

        Assert.True(result.Data.ContainsKey("active_silos"));
        Assert.Equal(2, (int)result.Data["active_silos"]);

        Assert.True(result.Data.ContainsKey("gateway_available"));
        Assert.True((bool)result.Data["gateway_available"]);

        // Verify no dual-mode health status
        Assert.False(result.Data.ContainsKey("DirectServiceHealthy"), "Dual-mode health status should not exist");
    }

    [Fact]
    public async Task CheckHealthAsync_OrleansNoActiveSilos_ReturnsUnhealthy()
    {
        // Arrange
        var mockManagementGrain = new Mock<IManagementGrain>();

        // Empty list of silos
        var activeSilos = new Dictionary<SiloAddress, SiloStatus>();

        mockManagementGrain
            .Setup(g => g.GetHosts(true))
            .ReturnsAsync(activeSilos);

        _mockGrainFactory
            .Setup(gf => gf.GetGrain<IManagementGrain>(0, null))
            .Returns(mockManagementGrain.Object);

        var context = new HealthCheckContext();

        // Act
        var result = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Orleans cluster has no active silos", result.Description);

        Assert.True(result.Data.ContainsKey("orleans_cluster_connected"));
        Assert.False((bool)result.Data["orleans_cluster_connected"]);

        Assert.True(result.Data.ContainsKey("active_silos"));
        Assert.Equal(0, (int)result.Data["active_silos"]);
    }

    [Fact]
    public async Task CheckHealthAsync_OrleansUnavailable_ReturnsUnhealthy()
    {
        // Arrange
        _mockGrainFactory
            .Setup(gf => gf.GetGrain<IManagementGrain>(0, null))
            .Throws(new OrleansException("Orleans cluster unavailable"));

        var context = new HealthCheckContext();

        // Act
        var result = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Orleans cluster is unavailable", result.Description);
        Assert.NotNull(result.Exception);

        Assert.True(result.Data.ContainsKey("orleans_cluster_connected"));
        Assert.False((bool)result.Data["orleans_cluster_connected"]);

        Assert.True(result.Data.ContainsKey("error"));
        Assert.Equal("Orleans cluster unavailable", result.Data["error"]);
    }

    [Fact]
    public async Task CheckHealthAsync_ManagementGrainThrows_ReturnsUnhealthy()
    {
        // Arrange
        var mockManagementGrain = new Mock<IManagementGrain>();

        mockManagementGrain
            .Setup(g => g.GetHosts(true))
            .ThrowsAsync(new TimeoutException("Request timeout"));

        _mockGrainFactory
            .Setup(gf => gf.GetGrain<IManagementGrain>(0, null))
            .Returns(mockManagementGrain.Object);

        var context = new HealthCheckContext();

        // Act
        var result = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("unavailable", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Exception);
        Assert.IsType<TimeoutException>(result.Exception);
    }

    [Fact]
    public async Task CheckHealthAsync_ResponseFormat_MeetsSpecification()
    {
        // Arrange
        var mockManagementGrain = new Mock<IManagementGrain>();

        var activeSilos = new Dictionary<SiloAddress, SiloStatus>
        {
            { SiloAddress.New(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 11111), 12345), SiloStatus.Active }
        };

        mockManagementGrain
            .Setup(g => g.GetHosts(true))
            .ReturnsAsync(activeSilos);

        _mockGrainFactory
            .Setup(gf => gf.GetGrain<IManagementGrain>(0, null))
            .Returns(mockManagementGrain.Object);

        var context = new HealthCheckContext();

        // Act
        var result = await _healthCheck.CheckHealthAsync(context);

        // Assert - Verify response format meets requirements
        // Assert.NotNull(result); // Removed: result is a value type (HealthCheckResult)
        Assert.NotNull(result.Data);

        // Must include Orleans cluster connectivity status
        Assert.True(result.Data.ContainsKey("orleans_cluster_connected"));

        // Must include number of active silos
        Assert.True(result.Data.ContainsKey("active_silos"));

        // Must include Orleans gateway availability
        Assert.True(result.Data.ContainsKey("gateway_available"));

        // Must NOT include DirectServiceHealthy (Orleans-only architecture)
        Assert.False(result.Data.ContainsKey("DirectServiceHealthy"));

        // Must NOT include any dual-mode indicators
        Assert.False(result.Data.ContainsKey("mode"));
        Assert.False(result.Data.ContainsKey("routing_mode"));
        Assert.False(result.Data.ContainsKey("fallback_available"));
    }
}

/// <summary>
/// Orleans health check implementation for Orleans-only mode.
/// This will be the actual implementation created in Phase 5.
/// </summary>
public class OrleansHealthCheck : IHealthCheck
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansHealthCheck> _logger;

    public OrleansHealthCheck(
        IGrainFactory grainFactory,
        ILogger<OrleansHealthCheck> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check Orleans cluster connectivity
            var managementGrain = _grainFactory.GetGrain<IManagementGrain>(0);
            var hosts = await managementGrain.GetHosts(onlyActive: true);

            var siloCount = hosts?.Count ?? 0;

            if (siloCount == 0)
            {
                return HealthCheckResult.Unhealthy(
                    "Orleans cluster has no active silos",
                    data: new Dictionary<string, object>
                    {
                        { "orleans_cluster_connected", false },
                        { "active_silos", 0 }
                    });
            }

            return HealthCheckResult.Healthy(
                "Orleans cluster is healthy",
                data: new Dictionary<string, object>
                {
                    { "orleans_cluster_connected", true },
                    { "active_silos", siloCount },
                    { "gateway_available", true }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans health check failed");

            return HealthCheckResult.Unhealthy(
                "Orleans cluster is unavailable",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "orleans_cluster_connected", false },
                    { "error", ex.Message }
                });
        }
    }
}
