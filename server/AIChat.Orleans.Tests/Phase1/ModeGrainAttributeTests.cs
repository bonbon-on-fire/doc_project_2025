using System.Reflection;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Contracts.Attributes;
using NUnit.Framework;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Unit tests for production attributes on ModeGrain interfaces.
/// Tests that all methods have appropriate production-quality attributes.
/// </summary>
[TestFixture]
public class ModeGrainAttributeTests
{
    [Test]
    public void RateLimitAttributeShouldBeProperlyConfigured()
    {
        // Arrange
        var attribute = new RateLimitAttribute(10, 60, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(attribute.MaxCalls, Is.EqualTo(10));
            Assert.That(attribute.WindowSeconds, Is.EqualTo(60));
            Assert.That(attribute.PerUser, Is.True);
        });
    }

    [Test]
    public void TelemetryAttributeShouldBeProperlyConfigured()
    {
        // Arrange
        var attribute = new TelemetryAttribute(TelemetryLevel.Normal, true, false);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(attribute.Level, Is.EqualTo(TelemetryLevel.Normal));
            Assert.That(attribute.IncludeParameters, Is.True);
            Assert.That(attribute.IncludeResult, Is.False);
        });
    }

    [Test]
    public void CacheHintAttributeShouldBeProperlyConfigured()
    {
        // Arrange
        var attribute = new CacheHintAttribute(true, 300, "test-key");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(attribute.IsCacheable, Is.True);
            Assert.That(attribute.DurationSeconds, Is.EqualTo(300));
            Assert.That(attribute.KeyPattern, Is.EqualTo("test-key"));
        });
    }

    [Test]
    public void SecurityAttributeShouldBeProperlyConfigured()
    {
        // Arrange
        var attribute = new SecurityAttribute(true, "Admin,User", true, DataClassification.Confidential);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(attribute.RequireAuthorization, Is.True);
            Assert.That(attribute.RequiredRoles, Is.EqualTo("Admin,User"));
            Assert.That(attribute.Audit, Is.True);
            Assert.That(attribute.Classification, Is.EqualTo(DataClassification.Confidential));
        });
    }

    [Test]
    public void InitializeAsyncShouldHaveAppropriateAttributes()
    {
        // Arrange
        var method = typeof(IModeStateGrain).GetMethod("InitializeAsync");
        Assert.That(method, Is.Not.Null, "InitializeAsync method should exist");

        // Act
        var rateLimitAttr = method!.GetCustomAttribute<RateLimitAttribute>();
        var telemetryAttr = method.GetCustomAttribute<TelemetryAttribute>();
        var securityAttr = method.GetCustomAttribute<SecurityAttribute>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(rateLimitAttr, Is.Not.Null, "Should have RateLimit attribute");
            Assert.That(rateLimitAttr!.MaxCalls, Is.EqualTo(10), "Should limit to 10 calls");
            Assert.That(rateLimitAttr.PerUser, Is.True, "Should be per-user rate limit");

            Assert.That(telemetryAttr, Is.Not.Null, "Should have Telemetry attribute");
            Assert.That(telemetryAttr!.Level, Is.EqualTo(TelemetryLevel.Normal), "Should have Normal telemetry");

            Assert.That(securityAttr, Is.Not.Null, "Should have Security attribute");
            Assert.That(securityAttr!.RequireAuthorization, Is.True, "Should require authorization");
            Assert.That(securityAttr.Audit, Is.True, "Should audit operations");
        });
    }

    [Test]
    public void GetStateAsyncShouldHaveCachingAndRateLimitAttributes()
    {
        // Arrange
        var method = typeof(IModeStateGrain).GetMethod("GetStateAsync");
        Assert.That(method, Is.Not.Null, "GetStateAsync method should exist");

        // Act
        var cacheHintAttr = method!.GetCustomAttribute<CacheHintAttribute>();
        var rateLimitAttr = method.GetCustomAttribute<RateLimitAttribute>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(cacheHintAttr, Is.Not.Null, "Should have CacheHint attribute");
            Assert.That(cacheHintAttr!.IsCacheable, Is.True, "Should be cacheable");
            Assert.That(cacheHintAttr.DurationSeconds, Is.EqualTo(30), "Should cache for 30 seconds");

            Assert.That(rateLimitAttr, Is.Not.Null, "Should have RateLimit attribute");
            Assert.That(rateLimitAttr!.MaxCalls, Is.EqualTo(100), "Should allow 100 calls");
        });
    }

    [Test]
    public void ArchiveAsyncShouldHaveSecurityRoleRestrictions()
    {
        // Arrange
        var method = typeof(IModeStateGrain).GetMethod("ArchiveAsync");
        Assert.That(method, Is.Not.Null, "ArchiveAsync method should exist");

        // Act
        var securityAttr = method!.GetCustomAttribute<SecurityAttribute>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(securityAttr, Is.Not.Null, "Should have Security attribute");
            Assert.That(securityAttr!.RequiredRoles, Is.Not.Null, "Should have required roles");
            Assert.That(securityAttr.RequiredRoles, Does.Contain("Admin"), "Should require Admin role");
            Assert.That(securityAttr.RequiredRoles, Does.Contain("ModeManager"), "Should allow ModeManager role");
        });
    }

    [Test]
    public void HealthCheckShouldNotBeCacheable()
    {
        // Arrange
        var method = typeof(IModeStateGrain).GetMethod("CheckHealthAsync");
        Assert.That(method, Is.Not.Null, "CheckHealthAsync method should exist");

        // Act
        var cacheHintAttr = method!.GetCustomAttribute<CacheHintAttribute>();
        var rateLimitAttr = method.GetCustomAttribute<RateLimitAttribute>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(cacheHintAttr, Is.Not.Null, "Should have CacheHint attribute");
            Assert.That(cacheHintAttr!.IsCacheable, Is.False, "Health checks should not be cached");

            Assert.That(rateLimitAttr, Is.Not.Null, "Should have RateLimit attribute");
            Assert.That(rateLimitAttr!.PerUser, Is.False, "Should have global rate limit");
        });
    }

    [Test]
    public void UpdateConfigurationAsyncShouldHaveHighSecurityLevel()
    {
        // Arrange
        var method = typeof(IModeConfigurationGrain).GetMethod("UpdateConfigurationAsync");
        Assert.That(method, Is.Not.Null, "UpdateConfigurationAsync method should exist");

        // Act
        var securityAttr = method!.GetCustomAttribute<SecurityAttribute>();
        var telemetryAttr = method.GetCustomAttribute<TelemetryAttribute>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(securityAttr, Is.Not.Null, "Should have Security attribute");
            Assert.That(securityAttr!.Classification, Is.EqualTo(DataClassification.Confidential),
                "Configuration updates should be classified as Confidential");
            Assert.That(securityAttr.Audit, Is.True, "Should audit configuration changes");

            Assert.That(telemetryAttr, Is.Not.Null, "Should have Telemetry attribute");
            Assert.That(telemetryAttr!.IncludeParameters, Is.True, "Should include parameters in telemetry");
        });
    }

    [Test]
    public void GetAvailableModesAsyncShouldHaveLongCacheDuration()
    {
        // Arrange
        var method = typeof(IModeConfigurationGrain).GetMethod("GetAvailableModesAsync");
        Assert.That(method, Is.Not.Null, "GetAvailableModesAsync method should exist");

        // Act
        var cacheHintAttr = method!.GetCustomAttribute<CacheHintAttribute>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(cacheHintAttr, Is.Not.Null, "Should have CacheHint attribute");
            Assert.That(cacheHintAttr!.IsCacheable, Is.True, "Should be cacheable");
            Assert.That(cacheHintAttr.DurationSeconds, Is.GreaterThan(60),
                "Templates should have longer cache duration since they change infrequently");
        });
    }

    [Test]
    public void UpdateSystemPromptAsyncShouldNotLogPromptContent()
    {
        // Arrange
        var method = typeof(IModeConfigurationGrain).GetMethod("UpdateSystemPromptAsync");
        Assert.That(method, Is.Not.Null, "UpdateSystemPromptAsync method should exist");

        // Act
        var telemetryAttr = method!.GetCustomAttribute<TelemetryAttribute>();
        var securityAttr = method.GetCustomAttribute<SecurityAttribute>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(telemetryAttr, Is.Not.Null, "Should have Telemetry attribute");
            Assert.That(telemetryAttr!.IncludeParameters, Is.False,
                "Should NOT include parameters to avoid logging sensitive prompts");

            Assert.That(securityAttr, Is.Not.Null, "Should have Security attribute");
            Assert.That(securityAttr!.Classification, Is.EqualTo(DataClassification.Confidential),
                "Prompts should be classified as Confidential");
        });
    }

    [Test]
    public void AllAttributeEnumsHaveProperValues()
    {
        // Test TelemetryLevel enum
        Assert.Multiple(() =>
        {
            Assert.That((int)TelemetryLevel.Minimal, Is.EqualTo(0));
            Assert.That((int)TelemetryLevel.Normal, Is.EqualTo(1));
            Assert.That((int)TelemetryLevel.Detailed, Is.EqualTo(2));
            Assert.That((int)TelemetryLevel.Verbose, Is.EqualTo(3));
        });

        // Test DataClassification enum
        Assert.Multiple(() =>
        {
            Assert.That((int)DataClassification.Public, Is.EqualTo(0));
            Assert.That((int)DataClassification.Internal, Is.EqualTo(1));
            Assert.That((int)DataClassification.Confidential, Is.EqualTo(2));
            Assert.That((int)DataClassification.Restricted, Is.EqualTo(3));
        });
    }
}