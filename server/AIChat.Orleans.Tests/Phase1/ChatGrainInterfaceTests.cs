using AIChat.Orleans.Contracts;
using NUnit.Framework;
using Orleans.TestingHost;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Unit tests for IChatGrain interface and its segregated interfaces.
/// Tests interface contracts and expected behaviors.
/// </summary>
[TestFixture]
public class ChatGrainInterfaceTests
{
    private TestCluster? _cluster;

    [SetUp]
    public async Task Setup()
    {
        var builder = new TestClusterBuilder();
        _ = builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();

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
    public void IChatGrainShouldImplementAllSegregatedInterfaces()
    {
        // Arrange
        var chatGrainType = typeof(IChatGrain);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IChatStateGrain).IsAssignableFrom(chatGrainType), Is.True, "IChatGrain should implement IChatStateGrain");
            Assert.That(typeof(IChatMessagingGrain).IsAssignableFrom(chatGrainType), Is.True, "IChatGrain should implement IChatMessagingGrain");
            Assert.That(typeof(IChatStreamingGrain).IsAssignableFrom(chatGrainType), Is.True, "IChatGrain should implement IChatStreamingGrain");
            Assert.That(typeof(IChatParticipantGrain).IsAssignableFrom(chatGrainType), Is.True, "IChatGrain should implement IChatParticipantGrain");
        });
    }

    [Test]
    public void IChatStateGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var stateGrainType = typeof(IChatStateGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(stateGrainType), Is.True);
    }

    [Test]
    public void IChatMessagingGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var messagingGrainType = typeof(IChatMessagingGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(messagingGrainType), Is.True);
    }

    [Test]
    public void IChatStreamingGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var streamingGrainType = typeof(IChatStreamingGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(streamingGrainType), Is.True);
    }

    [Test]
    public void IChatParticipantGrainShouldInheritFromIGrainWithStringKey()
    {
        // Arrange
        var participantGrainType = typeof(IChatParticipantGrain);

        // Assert
        Assert.That(typeof(IGrainWithStringKey).IsAssignableFrom(participantGrainType), Is.True);
    }

    [Test]
    public void AllInterfacesShouldHaveAliasAttribute()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IChatGrain),
            typeof(IChatStateGrain),
            typeof(IChatMessagingGrain),
            typeof(IChatStreamingGrain),
            typeof(IChatParticipantGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            var aliasAttribute = (AliasAttribute?)Attribute.GetCustomAttribute(interfaceType, typeof(AliasAttribute));
            Assert.That(aliasAttribute, Is.Not.Null, $"{interfaceType.Name} should have AliasAttribute");
            // AliasAttribute constructor takes a string, so if it exists, it has a value
            Assert.That(aliasAttribute, Is.Not.Null, $"{interfaceType.Name} alias should not be null");
        }
    }

    [Test]
    public void IChatStateGrainMethodsShouldHaveAliasAttributes()
    {
        // Arrange
        var methods = typeof(IChatStateGrain).GetMethods()
            .Where(m => m.DeclaringType == typeof(IChatStateGrain));

        // Assert
        foreach (var method in methods)
        {
            var aliasAttribute = (AliasAttribute?)Attribute.GetCustomAttribute(method, typeof(AliasAttribute));
            Assert.That(aliasAttribute, Is.Not.Null, $"Method {method.Name} should have AliasAttribute");
        }
    }

    [Test]
    public void IChatMessagingGrainMethodsShouldHaveAliasAttributes()
    {
        // Arrange
        var methods = typeof(IChatMessagingGrain).GetMethods()
            .Where(m => m.DeclaringType == typeof(IChatMessagingGrain));

        // Assert
        foreach (var method in methods)
        {
            var aliasAttribute = (AliasAttribute?)Attribute.GetCustomAttribute(method, typeof(AliasAttribute));
            Assert.That(aliasAttribute, Is.Not.Null, $"Method {method.Name} should have AliasAttribute");
        }
    }

    [Test]
    public void IChatStreamingGrainMethodsShouldHaveAliasAttributes()
    {
        // Arrange
        var methods = typeof(IChatStreamingGrain).GetMethods()
            .Where(m => m.DeclaringType == typeof(IChatStreamingGrain));

        // Assert
        foreach (var method in methods)
        {
            var aliasAttribute = (AliasAttribute?)Attribute.GetCustomAttribute(method, typeof(AliasAttribute));
            Assert.That(aliasAttribute, Is.Not.Null, $"Method {method.Name} should have AliasAttribute");
        }
    }

    [Test]
    public void IChatParticipantGrainMethodsShouldHaveAliasAttributes()
    {
        // Arrange
        var methods = typeof(IChatParticipantGrain).GetMethods()
            .Where(m => m.DeclaringType == typeof(IChatParticipantGrain));

        // Assert
        foreach (var method in methods)
        {
            var aliasAttribute = (AliasAttribute?)Attribute.GetCustomAttribute(method, typeof(AliasAttribute));
            Assert.That(aliasAttribute, Is.Not.Null, $"Method {method.Name} should have AliasAttribute");
        }
    }

    [Test]
    public void AllMethodsShouldReturnTasks()
    {
        // Arrange
        var interfaces = new[]
        {
            typeof(IChatStateGrain),
            typeof(IChatMessagingGrain),
            typeof(IChatStreamingGrain),
            typeof(IChatParticipantGrain)
        };

        // Assert
        foreach (var interfaceType in interfaces)
        {
            var methods = interfaceType.GetMethods()
                .Where(m => m.DeclaringType == interfaceType);

            foreach (var method in methods)
            {
                Assert.That(method.ReturnType.IsAssignableTo(typeof(Task)), Is.True,
                    $"Method {interfaceType.Name}.{method.Name} should return a Task or Task<T>");
            }
        }
    }

    [Test]
    public void IChatStateGrainInitializeAsyncShouldHaveCorrectSignature()
    {
        // Arrange
        var method = typeof(IChatStateGrain).GetMethod(nameof(IChatStateGrain.InitializeAsync));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(Task<ChatState>)));

            var parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(2), "Method should have ChatInitRequest and CancellationToken parameters");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(ChatInitRequest)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(CancellationToken)));
            Assert.That(parameters[1].HasDefaultValue, Is.True, "CancellationToken should have default value");
        });
    }

    [Test]
    public void IChatMessagingGrainProcessMessageAsyncShouldHaveCorrectSignature()
    {
        // Arrange
        var method = typeof(IChatMessagingGrain).GetMethod(nameof(IChatMessagingGrain.ProcessMessageAsync));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(Task<MessageResult>)));

            var parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(2), "Method should have ChatMessage and CancellationToken parameters");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(ChatMessage)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(CancellationToken)));
            Assert.That(parameters[1].HasDefaultValue, Is.True, "CancellationToken should have default value");
        });
    }

    [Test]
    public void IChatStreamingGrainStartStreamAsyncShouldHaveCorrectSignature()
    {
        // Arrange
        var method = typeof(IChatStreamingGrain).GetMethod(nameof(IChatStreamingGrain.StartStreamAsync));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(Task<StreamHandle>)));

            var parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(2), "Method should have StreamMessage and CancellationToken parameters");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(StreamMessage)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(CancellationToken)));
            Assert.That(parameters[1].HasDefaultValue, Is.True, "CancellationToken should have default value");
        });
    }

    [Test]
    public void IChatParticipantGrainCheckPermissionAsyncShouldHaveCorrectSignature()
    {
        // Arrange
        var method = typeof(IChatParticipantGrain).GetMethod(nameof(IChatParticipantGrain.CheckPermissionAsync));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(Task<bool>)));

            var parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(3), "Method should have participantId, action, and CancellationToken parameters");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(string)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(ChatAction)));
            Assert.That(parameters[2].ParameterType, Is.EqualTo(typeof(CancellationToken)));
            Assert.That(parameters[2].HasDefaultValue, Is.True, "CancellationToken should have default value");
        });
    }

    [Test]
    public void InterfaceSegregationShouldAllowIndependentUsage()
    {
        // This test verifies that each interface can be used independently
        // without requiring the full IChatGrain interface

        // Arrange
        Type stateGrainType = typeof(IChatStateGrain);
        Type messagingGrainType = typeof(IChatMessagingGrain);
        Type streamingGrainType = typeof(IChatStreamingGrain);
        Type participantGrainType = typeof(IChatParticipantGrain);

        // Assert - Each interface should be independent
        Assert.Multiple(() =>
        {
            Assert.That(stateGrainType.GetInterfaces(), Does.Not.Contain(messagingGrainType));
            Assert.That(stateGrainType.GetInterfaces(), Does.Not.Contain(streamingGrainType));
            Assert.That(stateGrainType.GetInterfaces(), Does.Not.Contain(participantGrainType));

            Assert.That(messagingGrainType.GetInterfaces(), Does.Not.Contain(stateGrainType));
            Assert.That(messagingGrainType.GetInterfaces(), Does.Not.Contain(streamingGrainType));
            Assert.That(messagingGrainType.GetInterfaces(), Does.Not.Contain(participantGrainType));

            Assert.That(streamingGrainType.GetInterfaces(), Does.Not.Contain(stateGrainType));
            Assert.That(streamingGrainType.GetInterfaces(), Does.Not.Contain(messagingGrainType));
            Assert.That(streamingGrainType.GetInterfaces(), Does.Not.Contain(participantGrainType));

            Assert.That(participantGrainType.GetInterfaces(), Does.Not.Contain(stateGrainType));
            Assert.That(participantGrainType.GetInterfaces(), Does.Not.Contain(messagingGrainType));
            Assert.That(participantGrainType.GetInterfaces(), Does.Not.Contain(streamingGrainType));
        });
    }
}
