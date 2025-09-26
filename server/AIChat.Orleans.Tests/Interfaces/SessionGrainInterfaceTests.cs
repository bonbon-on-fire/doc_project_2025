using System.Reflection;
using AIChat.Orleans.Contracts;
using Orleans.Concurrency;
using Xunit;

namespace AIChat.Orleans.Tests.Interfaces;

/// <summary>
/// Unit tests for ISessionGrain interface and its segregated interfaces.
/// </summary>
public class SessionGrainInterfaceTests
{
    [Fact]
    public void ISessionGrainShouldInheritFromAllSegregatedInterfaces()
    {
        // Assert
        Assert.True(typeof(ISessionStateGrain).IsAssignableFrom(typeof(ISessionGrain)));
        Assert.True(typeof(ISessionConnectionGrain).IsAssignableFrom(typeof(ISessionGrain)));
        Assert.True(typeof(ISessionProtocolGrain).IsAssignableFrom(typeof(ISessionGrain)));
        Assert.True(typeof(ISessionMonitoringGrain).IsAssignableFrom(typeof(ISessionGrain)));
    }

    [Fact]
    public void ISessionStateGrainShouldInheritFromIGrainWithStringKey()
    {
        // Assert
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(ISessionStateGrain)));
    }

    [Fact]
    public void ISessionConnectionGrainShouldInheritFromIGrainWithStringKey()
    {
        // Assert
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(ISessionConnectionGrain)));
    }

    [Fact]
    public void ISessionProtocolGrainShouldInheritFromIGrainWithStringKey()
    {
        // Assert
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(ISessionProtocolGrain)));
    }

    [Fact]
    public void ISessionMonitoringGrainShouldInheritFromIGrainWithStringKey()
    {
        // Assert
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(ISessionMonitoringGrain)));
    }

    [Fact]
    public void ISessionGrainShouldHaveAliasAttribute()
    {
        // Arrange
        var aliasAttribute = typeof(ISessionGrain).GetCustomAttribute<AliasAttribute>();

        // Assert
        Assert.NotNull(aliasAttribute);
        // AliasAttribute doesn't expose Value property directly in newer versions
        // Just verify the attribute is present
    }

    [Fact]
    public void ISessionStateGrainShouldHaveCorrectMethodsWithCancellationToken()
    {
        // Arrange
        var methods = typeof(ISessionStateGrain).GetMethods();

        // Assert - Check key methods exist with CancellationToken
        Assert.Contains(methods, m => m.Name == "InitializeAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetStateAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "UpdateMetadataAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "ArchiveAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetHistoryAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "CheckHealthAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "SaveSnapshotAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "RestoreSnapshotAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
    }

    [Fact]
    public void ISessionConnectionGrainShouldHaveCorrectMethodsWithCancellationToken()
    {
        // Arrange
        var methods = typeof(ISessionConnectionGrain).GetMethods();

        // Assert - Check key methods exist with CancellationToken
        Assert.Contains(methods, m => m.Name == "ConnectAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "DisconnectAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "ReconnectAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetConnectionStatusAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "HeartbeatAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetConnectionMetricsAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "RegisterConnectionAttemptAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "CleanupConnectionAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "UpdateConnectionConfigurationAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
    }

    [Fact]
    public void ISessionProtocolGrainShouldHaveCorrectMethodsWithCancellationToken()
    {
        // Arrange
        var methods = typeof(ISessionProtocolGrain).GetMethods();

        // Assert - Check key methods exist with CancellationToken
        Assert.Contains(methods, m => m.Name == "GetProtocolConfigurationAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "UpdateProtocolConfigurationAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "SwitchProtocolAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "ValidateProtocolAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetProtocolStateAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "UpdateProtocolStateAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "NegotiateProtocolAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetAvailableProtocolsAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "HandleProtocolMessageAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
    }

    [Fact]
    public void ISessionMonitoringGrainShouldHaveCorrectMethodsWithCancellationToken()
    {
        // Arrange
        var methods = typeof(ISessionMonitoringGrain).GetMethods();

        // Assert - Check key methods exist with CancellationToken
        Assert.Contains(methods, m => m.Name == "GetMetricsAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "RecordMetricAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetPerformanceStatsAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "CollectDiagnosticsAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "RegisterAlertAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetActiveAlertsAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "AcknowledgeAlertAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "StartTraceAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "StopTraceAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
        Assert.Contains(methods, m => m.Name == "GetResourceUsageAsync" &&
            m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
    }

    [Fact]
    public void ReadOnlyMethodsShouldHaveReadOnlyAttribute()
    {
        // Arrange
        var readOnlyMethods = new[]
        {
            (typeof(ISessionStateGrain), "GetStateAsync"),
            (typeof(ISessionStateGrain), "GetHistoryAsync"),
            (typeof(ISessionStateGrain), "CheckHealthAsync"),
            (typeof(ISessionConnectionGrain), "GetConnectionStatusAsync"),
            (typeof(ISessionConnectionGrain), "GetConnectionMetricsAsync"),
            (typeof(ISessionProtocolGrain), "GetProtocolConfigurationAsync"),
            (typeof(ISessionProtocolGrain), "ValidateProtocolAsync"),
            (typeof(ISessionProtocolGrain), "GetProtocolStateAsync"),
            (typeof(ISessionProtocolGrain), "GetAvailableProtocolsAsync"),
            (typeof(ISessionMonitoringGrain), "GetMetricsAsync"),
            (typeof(ISessionMonitoringGrain), "GetPerformanceStatsAsync"),
            (typeof(ISessionMonitoringGrain), "CollectDiagnosticsAsync"),
            (typeof(ISessionMonitoringGrain), "GetActiveAlertsAsync"),
            (typeof(ISessionMonitoringGrain), "GetResourceUsageAsync")
        };

        // Act & Assert
        foreach (var (type, methodName) in readOnlyMethods)
        {
            var method = type.GetMethod(methodName);
            Assert.NotNull(method);
            var hasReadOnly = method.GetCustomAttribute<ReadOnlyAttribute>() != null;
            Assert.True(hasReadOnly, $"{type.Name}.{methodName} should have ReadOnly attribute");
        }
    }

    [Fact]
    public void AllMethodsShouldHaveAliasAttribute()
    {
        // Arrange
        var interfaceTypes = new[]
        {
            typeof(ISessionStateGrain),
            typeof(ISessionConnectionGrain),
            typeof(ISessionProtocolGrain),
            typeof(ISessionMonitoringGrain)
        };

        // Act & Assert
        foreach (var type in interfaceTypes)
        {
            var methods = type.GetMethods()
                .Where(m => m.DeclaringType == type); // Only methods declared in this interface

            foreach (var method in methods)
            {
                var hasAlias = method.GetCustomAttribute<AliasAttribute>() != null;
                Assert.True(hasAlias, $"{type.Name}.{method.Name} should have Alias attribute");
            }
        }
    }

    [Fact]
    public void AllAsyncMethodsShouldReturnTask()
    {
        // Arrange
        var interfaceTypes = new[]
        {
            typeof(ISessionStateGrain),
            typeof(ISessionConnectionGrain),
            typeof(ISessionProtocolGrain),
            typeof(ISessionMonitoringGrain)
        };

        // Act & Assert
        foreach (var type in interfaceTypes)
        {
            var methods = type.GetMethods()
                .Where(m => m.DeclaringType == type && m.Name.EndsWith("Async", StringComparison.Ordinal));

            foreach (var method in methods)
            {
                var returnType = method.ReturnType;
                var isTask = returnType == typeof(Task) ||
                            (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>));
                Assert.True(isTask, $"{type.Name}.{method.Name} should return Task or Task<T>");
            }
        }
    }

    [Theory]
    [InlineData(typeof(ISessionStateGrain), 10)]
    [InlineData(typeof(ISessionConnectionGrain), 12)]
    [InlineData(typeof(ISessionProtocolGrain), 9)]
    [InlineData(typeof(ISessionMonitoringGrain), 10)]
    public void InterfaceShouldHaveExpectedNumberOfMethods(Type interfaceType, int expectedCount)
    {
        // Arrange
        var methods = interfaceType.GetMethods()
            .Where(m => m.DeclaringType == interfaceType)
            .ToList();

        // Assert
        Assert.Equal(expectedCount, methods.Count);
    }

    [Fact]
    public void ISessionGrainShouldNotDefineAdditionalMethods()
    {
        // Arrange
        var methods = typeof(ISessionGrain).GetMethods()
            .Where(m => m.DeclaringType == typeof(ISessionGrain))
            .ToList();

        // Assert - ISessionGrain should not define any methods of its own
        Assert.Empty(methods);
    }

    [Fact]
    public void AllCancellationTokenParametersShouldHaveDefaultValue()
    {
        // Arrange
        var interfaceTypes = new[]
        {
            typeof(ISessionStateGrain),
            typeof(ISessionConnectionGrain),
            typeof(ISessionProtocolGrain),
            typeof(ISessionMonitoringGrain)
        };

        // Act & Assert
        foreach (var type in interfaceTypes)
        {
            var methods = type.GetMethods()
                .Where(m => m.DeclaringType == type);

            foreach (var method in methods)
            {
                var cancellationTokenParams = method.GetParameters()
                    .Where(p => p.ParameterType == typeof(CancellationToken));

                foreach (var param in cancellationTokenParams)
                {
                    Assert.True(param.HasDefaultValue,
                        $"{type.Name}.{method.Name}'s CancellationToken parameter should have a default value");
                    // The default value for CancellationToken parameters is null when accessed via reflection
                    Assert.Null(param.DefaultValue);
                }
            }
        }
    }
}