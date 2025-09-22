using AIChat.Orleans.Contracts;
using NUnit.Framework;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Unit tests for ModeGrain exception classes.
/// </summary>
[TestFixture]
public class ModeGrainExceptionTests
{
    [Test]
    public void ModeGrainExceptionShouldSetPropertiesCorrectly()
    {
        // Arrange & Act
        var exception = new ModeGrainException("Test error message", "mode-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("Test error message"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
            Assert.That(exception.CorrelationId, Is.Not.Null);
            Assert.That(exception.CorrelationId, Is.Not.Empty);
        });
    }

    [Test]
    public void ModeGrainExceptionShouldHandleNullModeId()
    {
        // Arrange & Act
        var exception = new ModeGrainException("Test error message", null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("Test error message"));
            Assert.That(exception.ModeId, Is.Null);
            Assert.That(exception.CorrelationId, Is.Not.Null);
        });
    }

    [Test]
    public void ModeGrainExceptionShouldIncludeInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ModeGrainException("Test error message", innerException, "mode-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("Test error message"));
            Assert.That(exception.InnerException, Is.EqualTo(innerException));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void ModeNotFoundExceptionShouldFormatMessage()
    {
        // Arrange & Act
        var exception = new ModeNotFoundException("mode-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("was not found"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void ModeAlreadyExistsExceptionShouldFormatMessage()
    {
        // Arrange & Act
        var exception = new ModeAlreadyExistsException("mode-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("already exists"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void ModeArchivedExceptionShouldFormatMessage()
    {
        // Arrange & Act
        var exception = new ModeArchivedException("mode-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("archived"));
            Assert.That(exception.Message, Does.Contain("cannot be modified"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void InvalidModeConfigurationExceptionShouldIncludeMultipleErrors()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2", "Error 3" };

        // Act
        var exception = new InvalidModeConfigurationException("mode-123", errors);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("Error 1"));
            Assert.That(exception.Message, Does.Contain("Error 2"));
            Assert.That(exception.Message, Does.Contain("Error 3"));
            Assert.That(exception.ValidationErrors, Has.Count.EqualTo(3));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void InvalidModeConfigurationExceptionShouldHandleSingleError()
    {
        // Arrange & Act
        var exception = new InvalidModeConfigurationException("mode-123", "Single error");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("Single error"));
            Assert.That(exception.ValidationErrors, Has.Count.EqualTo(1));
            Assert.That(exception.ValidationErrors[0], Is.EqualTo("Single error"));
        });
    }

    [Test]
    public void InvalidModeTransitionExceptionShouldIncludeTransitionDetails()
    {
        // Arrange & Act
        var exception = new InvalidModeTransitionException("source-mode", "target-mode", "Incompatible modes");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("source-mode"));
            Assert.That(exception.Message, Does.Contain("target-mode"));
            Assert.That(exception.Message, Does.Contain("Incompatible modes"));
            Assert.That(exception.SourceModeId, Is.EqualTo("source-mode"));
            Assert.That(exception.TargetModeId, Is.EqualTo("target-mode"));
            Assert.That(exception.Reason, Is.EqualTo("Incompatible modes"));
            Assert.That(exception.ModeId, Is.EqualTo("source-mode"));
        });
    }

    [Test]
    public void ModeTemplateNotFoundExceptionShouldFormatMessage()
    {
        // Arrange & Act
        var exception = new ModeTemplateNotFoundException("template-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("template-123"));
            Assert.That(exception.Message, Does.Contain("was not found"));
            Assert.That(exception.TemplateId, Is.EqualTo("template-123"));
            Assert.That(exception.ModeId, Is.Null);
        });
    }

    [Test]
    public void InvalidPromptExceptionShouldIncludeMultipleIssues()
    {
        // Arrange
        var issues = new List<string> { "Too long", "Contains forbidden words", "Invalid format" };

        // Act
        var exception = new InvalidPromptException("mode-123", issues);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("Too long"));
            Assert.That(exception.Message, Does.Contain("Contains forbidden words"));
            Assert.That(exception.Message, Does.Contain("Invalid format"));
            Assert.That(exception.Issues, Has.Count.EqualTo(3));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void InvalidPromptExceptionShouldHandleSingleIssue()
    {
        // Arrange & Act
        var exception = new InvalidPromptException("mode-123", "Prompt is too short");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("Prompt is too short"));
            Assert.That(exception.Issues, Has.Count.EqualTo(1));
            Assert.That(exception.Issues[0], Is.EqualTo("Prompt is too short"));
        });
    }

    [Test]
    public void InvalidToolExceptionShouldIncludeToolIds()
    {
        // Arrange
        var invalidTools = new List<string> { "tool1", "tool2", "tool3" };

        // Act
        var exception = new InvalidToolException("mode-123", invalidTools);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("tool1"));
            Assert.That(exception.Message, Does.Contain("tool2"));
            Assert.That(exception.Message, Does.Contain("tool3"));
            Assert.That(exception.InvalidToolIds, Has.Count.EqualTo(3));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void InvalidToolExceptionShouldHandleSingleTool()
    {
        // Arrange & Act
        var exception = new InvalidToolException("mode-123", "invalid-tool");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("invalid-tool"));
            Assert.That(exception.InvalidToolIds, Has.Count.EqualTo(1));
            Assert.That(exception.InvalidToolIds[0], Is.EqualTo("invalid-tool"));
        });
    }

    [Test]
    public void NoTransitionToRollbackExceptionShouldFormatMessage()
    {
        // Arrange & Act
        var exception = new NoTransitionToRollbackException("mode-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("no transition to rollback"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void PresetNotFoundExceptionShouldIncludePresetId()
    {
        // Arrange & Act
        var exception = new PresetNotFoundException("mode-123", "preset-456");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("preset-456"));
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("was not found"));
            Assert.That(exception.PresetId, Is.EqualTo("preset-456"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void InvalidPresetExceptionShouldIncludeReason()
    {
        // Arrange & Act
        var exception = new InvalidPresetException("mode-123", "preset-456", "Preset requires different configuration");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("preset-456"));
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("Preset requires different configuration"));
            Assert.That(exception.PresetId, Is.EqualTo("preset-456"));
            Assert.That(exception.Reason, Is.EqualTo("Preset requires different configuration"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void InvalidScheduleExceptionShouldIncludeReason()
    {
        // Arrange & Act
        var exception = new InvalidScheduleException("mode-123", "Schedule time is in the past");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("Schedule time is in the past"));
            Assert.That(exception.Reason, Is.EqualTo("Schedule time is in the past"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void ScheduleNotFoundExceptionShouldIncludeScheduleId()
    {
        // Arrange & Act
        var exception = new ScheduleNotFoundException("mode-123", "schedule-789");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("schedule-789"));
            Assert.That(exception.Message, Does.Contain("mode-123"));
            Assert.That(exception.Message, Does.Contain("was not found"));
            Assert.That(exception.ScheduleId, Is.EqualTo("schedule-789"));
            Assert.That(exception.ModeId, Is.EqualTo("mode-123"));
        });
    }

    [Test]
    public void AllExceptionsShouldInheritFromModeGrainException()
    {
        // Arrange
        var exceptionTypes = new[]
        {
            typeof(ModeNotFoundException),
            typeof(ModeAlreadyExistsException),
            typeof(ModeArchivedException),
            typeof(InvalidModeConfigurationException),
            typeof(InvalidModeTransitionException),
            typeof(ModeTemplateNotFoundException),
            typeof(InvalidPromptException),
            typeof(InvalidToolException),
            typeof(NoTransitionToRollbackException),
            typeof(PresetNotFoundException),
            typeof(InvalidPresetException),
            typeof(InvalidScheduleException),
            typeof(ScheduleNotFoundException)
        };

        // Assert
        foreach (var exceptionType in exceptionTypes)
        {
            Assert.That(typeof(ModeGrainException).IsAssignableFrom(exceptionType), Is.True,
                $"{exceptionType.Name} should inherit from ModeGrainException");
        }
    }

    [Test]
    public void AllExceptionsShouldHaveCorrelationId()
    {
        // Arrange
        var exceptions = new ModeGrainException[]
        {
            new ModeNotFoundException("mode-1"),
            new ModeAlreadyExistsException("mode-2"),
            new ModeArchivedException("mode-3"),
            new InvalidModeConfigurationException("mode-4", "error"),
            new InvalidModeTransitionException("source", "target", "reason"),
            new ModeTemplateNotFoundException("template-1"),
            new InvalidPromptException("mode-5", "issue"),
            new InvalidToolException("mode-6", "tool"),
            new NoTransitionToRollbackException("mode-7"),
            new PresetNotFoundException("mode-8", "preset"),
            new InvalidPresetException("mode-9", "preset", "reason"),
            new InvalidScheduleException("mode-10", "reason"),
            new ScheduleNotFoundException("mode-11", "schedule")
        };

        // Assert
        foreach (var exception in exceptions)
        {
            Assert.Multiple(() =>
            {
                Assert.That(exception.CorrelationId, Is.Not.Null,
                    $"{exception.GetType().Name} should have a CorrelationId");
                Assert.That(exception.CorrelationId, Is.Not.Empty,
                    $"{exception.GetType().Name} CorrelationId should not be empty");
                Assert.That(Guid.TryParse(exception.CorrelationId, out _), Is.True,
                    $"{exception.GetType().Name} CorrelationId should be a valid GUID");
            });
        }
    }
}