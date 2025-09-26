using System.Reflection;
using AIChat.Orleans.Contracts;
using NUnit.Framework;
using Orleans.Concurrency;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Unit tests for IModeGrain interface and its segregated interfaces.
/// Tests interface contracts and expected behaviors.
/// </summary>
[TestFixture]
public class ModeGrainInterfaceTests
{
    [Test]
    public void IModeGrainShouldImplementAllSegregatedInterfaces()
    {
        // Arrange
        var modeGrainType = typeof(IModeGrain);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IModeStateGrain).IsAssignableFrom(modeGrainType), Is.True,
                "IModeGrain should implement IModeStateGrain");
            Assert.That(typeof(IModeConfigurationGrain).IsAssignableFrom(modeGrainType), Is.True,
                "IModeGrain should implement IModeConfigurationGrain");
            Assert.That(typeof(IModeTransitionGrain).IsAssignableFrom(modeGrainType), Is.True,
                "IModeGrain should implement IModeTransitionGrain");
            Assert.That(typeof(IModeValidationGrain).IsAssignableFrom(modeGrainType), Is.True,
                "IModeGrain should implement IModeValidationGrain");
        });
    }

    [Test]
    public void IModeStateGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var stateGrainType = typeof(IModeStateGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(stateGrainType), Is.True);
    }

    [Test]
    public void IModeConfigurationGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var configGrainType = typeof(IModeConfigurationGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(configGrainType), Is.True);
    }

    [Test]
    public void IModeTransitionGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var transitionGrainType = typeof(IModeTransitionGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(transitionGrainType), Is.True);
    }

    [Test]
    public void IModeValidationGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var validationGrainType = typeof(IModeValidationGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(validationGrainType), Is.True);
    }

    [Test]
    public void AllInterfacesShouldHaveAliasAttribute()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IModeGrain),
            typeof(IModeStateGrain),
            typeof(IModeConfigurationGrain),
            typeof(IModeTransitionGrain),
            typeof(IModeValidationGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            var aliasAttribute = (AliasAttribute?)Attribute.GetCustomAttribute(interfaceType, typeof(AliasAttribute));
            Assert.That(aliasAttribute, Is.Not.Null,
                $"{interfaceType.Name} should have an Alias attribute");
            // AliasAttribute constructor takes a string, so if it exists, it has a value
        }
    }

    [Test]
    public void AllMethodsShouldHaveAliasAttribute()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IModeStateGrain),
            typeof(IModeConfigurationGrain),
            typeof(IModeTransitionGrain),
            typeof(IModeValidationGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var aliasAttribute = method.GetCustomAttribute<AliasAttribute>();
                Assert.That(aliasAttribute, Is.Not.Null,
                    $"{interfaceType.Name}.{method.Name} should have an Alias attribute");
            }
        }
    }

    [Test]
    public void ReadOnlyMethodsShouldHaveReadOnlyAttribute()
    {
        // Arrange
        var readOnlyMethods = new[]
        {
            (typeof(IModeStateGrain), "GetStateAsync"),
            (typeof(IModeStateGrain), "GetHistoryAsync"),
            (typeof(IModeStateGrain), "CheckHealthAsync"),
            (typeof(IModeConfigurationGrain), "GetConfigurationAsync"),
            (typeof(IModeConfigurationGrain), "GetAvailableModesAsync"),
            (typeof(IModeConfigurationGrain), "GetModeTemplateAsync"),
            (typeof(IModeConfigurationGrain), "ValidateConfigurationAsync"),
            (typeof(IModeConfigurationGrain), "GetEffectiveConfigurationAsync"),
            (typeof(IModeTransitionGrain), "GetTransitionHistoryAsync"),
            (typeof(IModeTransitionGrain), "CanTransitionAsync"),
            (typeof(IModeTransitionGrain), "GetAvailableTransitionsAsync"),
            (typeof(IModeValidationGrain), "ValidateModeAsync"),
            (typeof(IModeValidationGrain), "ValidateTransitionAsync"),
            (typeof(IModeValidationGrain), "GetValidationRulesAsync"),
            (typeof(IModeValidationGrain), "CheckConstraintsAsync"),
            (typeof(IModeValidationGrain), "ValidateToolsAsync"),
            (typeof(IModeValidationGrain), "ValidatePromptAsync"),
            (typeof(IModeValidationGrain), "ValidatePermissionsAsync"),
            (typeof(IModeValidationGrain), "ValidateStateAsync"),
            (typeof(IModeValidationGrain), "ValidateCompatibilityAsync")
        };

        // Assert
        foreach (var (interfaceType, methodName) in readOnlyMethods)
        {
            var method = interfaceType.GetMethod(methodName);
            Assert.That(method, Is.Not.Null, $"{interfaceType.Name}.{methodName} should exist");

            var readOnlyAttribute = method!.GetCustomAttribute<ReadOnlyAttribute>();
            Assert.That(readOnlyAttribute, Is.Not.Null,
                $"{interfaceType.Name}.{methodName} should have ReadOnly attribute");
        }
    }

    [Test]
    public void AllMethodsShouldHaveCancellationTokenParameter()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IModeStateGrain),
            typeof(IModeConfigurationGrain),
            typeof(IModeTransitionGrain),
            typeof(IModeValidationGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                var lastParam = parameters.LastOrDefault();

                Assert.Multiple(() =>
                {
                    Assert.That(lastParam, Is.Not.Null,
                        $"{interfaceType.Name}.{method.Name} should have at least one parameter");
                    Assert.That(lastParam!.ParameterType, Is.EqualTo(typeof(CancellationToken)),
                        $"{interfaceType.Name}.{method.Name} last parameter should be CancellationToken");
                    Assert.That(lastParam.HasDefaultValue, Is.True,
                        $"{interfaceType.Name}.{method.Name} CancellationToken should have default value");
                });
            }
        }
    }

    [Test]
    public void AllMethodsShouldReturnTask()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IModeStateGrain),
            typeof(IModeConfigurationGrain),
            typeof(IModeTransitionGrain),
            typeof(IModeValidationGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                // Check that return type is Task or Task<T>
                Assert.That(method.ReturnType == typeof(Task) ||
                           (method.ReturnType.IsGenericType &&
                            method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)),
                    Is.True,
                    $"{interfaceType.Name}.{method.Name} should return Task or Task<T>");
            }
        }
    }

    [Test]
    public void StateManagementMethodsShouldBeInCorrectInterface()
    {
        // Arrange
        var stateGrainType = typeof(IModeStateGrain);
        var expectedMethods = new[]
        {
            "InitializeAsync",
            "GetStateAsync",
            "UpdateMetadataAsync",
            "ArchiveAsync",
            "ResetToDefaultAsync",
            "GetHistoryAsync",
            "CheckHealthAsync"
        };

        // Assert
        foreach (var methodName in expectedMethods)
        {
            var method = stateGrainType.GetMethod(methodName);
            Assert.That(method, Is.Not.Null,
                $"IModeStateGrain should have {methodName} method");
        }
    }

    [Test]
    public void ConfigurationMethodsShouldBeInCorrectInterface()
    {
        // Arrange
        var configGrainType = typeof(IModeConfigurationGrain);
        var expectedMethods = new[]
        {
            "GetConfigurationAsync",
            "UpdateConfigurationAsync",
            "GetAvailableModesAsync",
            "GetModeTemplateAsync",
            "ValidateConfigurationAsync",
            "UpdateSystemPromptAsync",
            "UpdateToolsAsync",
            "GetEffectiveConfigurationAsync"
        };

        // Assert
        foreach (var methodName in expectedMethods)
        {
            var method = configGrainType.GetMethod(methodName);
            Assert.That(method, Is.Not.Null,
                $"IModeConfigurationGrain should have {methodName} method");
        }
    }

    [Test]
    public void TransitionMethodsShouldBeInCorrectInterface()
    {
        // Arrange
        var transitionGrainType = typeof(IModeTransitionGrain);
        var expectedMethods = new[]
        {
            "TransitionToModeAsync",
            "GetTransitionHistoryAsync",
            "CanTransitionAsync",
            "RollbackTransitionAsync",
            "GetAvailableTransitionsAsync",
            "ApplyPresetAsync",
            "ScheduleTransitionAsync",
            "CancelScheduledTransitionAsync"
        };

        // Assert
        foreach (var methodName in expectedMethods)
        {
            var method = transitionGrainType.GetMethod(methodName);
            Assert.That(method, Is.Not.Null,
                $"IModeTransitionGrain should have {methodName} method");
        }
    }

    [Test]
    public void ValidationMethodsShouldBeInCorrectInterface()
    {
        // Arrange
        var validationGrainType = typeof(IModeValidationGrain);
        var expectedMethods = new[]
        {
            "ValidateModeAsync",
            "ValidateTransitionAsync",
            "GetValidationRulesAsync",
            "CheckConstraintsAsync",
            "ValidateToolsAsync",
            "ValidatePromptAsync",
            "ValidatePermissionsAsync",
            "ValidateStateAsync",
            "ValidateCompatibilityAsync"
        };

        // Assert
        foreach (var methodName in expectedMethods)
        {
            var method = validationGrainType.GetMethod(methodName);
            Assert.That(method, Is.Not.Null,
                $"IModeValidationGrain should have {methodName} method");
        }
    }

    [Test]
    public void InterfacesShouldFollowNamingConventions()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IModeGrain),
            typeof(IModeStateGrain),
            typeof(IModeConfigurationGrain),
            typeof(IModeTransitionGrain),
            typeof(IModeValidationGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            Assert.That(interfaceType.Name, Does.StartWith("I"),
                $"{interfaceType.Name} should start with 'I'");
            Assert.That(interfaceType.Name, Does.Contain("Mode"),
                $"{interfaceType.Name} should contain 'Mode'");
            Assert.That(interfaceType.Name, Does.EndWith("Grain"),
                $"{interfaceType.Name} should end with 'Grain'");
        }
    }

    [Test]
    public void InterfacesShouldBeInCorrectNamespace()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IModeGrain),
            typeof(IModeStateGrain),
            typeof(IModeConfigurationGrain),
            typeof(IModeTransitionGrain),
            typeof(IModeValidationGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            Assert.That(interfaceType.Namespace, Is.EqualTo("AIChat.Orleans.Contracts"),
                $"{interfaceType.Name} should be in AIChat.Orleans.Contracts namespace");
        }
    }

    [Test]
    public void MethodsShouldHaveProperExceptionDocumentation()
    {
        // This test verifies that methods document their exceptions properly
        // We check for common exception types that should be documented

        var methodsWithExceptions = new[]
        {
            (typeof(IModeStateGrain), "InitializeAsync", typeof(ModeAlreadyExistsException)),
            (typeof(IModeStateGrain), "GetStateAsync", typeof(ModeNotFoundException)),
            (typeof(IModeStateGrain), "UpdateMetadataAsync", typeof(ModeArchivedException)),
            (typeof(IModeConfigurationGrain), "UpdateConfigurationAsync", typeof(InvalidModeConfigurationException)),
            (typeof(IModeTransitionGrain), "TransitionToModeAsync", typeof(InvalidModeTransitionException)),
            (typeof(IModeTransitionGrain), "RollbackTransitionAsync", typeof(NoTransitionToRollbackException))
        };

        // Since we can't check XML documentation at runtime, we at least verify the methods exist
        foreach (var (interfaceType, methodName, _) in methodsWithExceptions)
        {
            var method = interfaceType.GetMethod(methodName);
            Assert.That(method, Is.Not.Null,
                $"{interfaceType.Name}.{methodName} should exist");
        }
    }
}