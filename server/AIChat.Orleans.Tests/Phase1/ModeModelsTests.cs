using System.ComponentModel.DataAnnotations;
using AIChat.Orleans.Contracts;
using NUnit.Framework;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Unit tests for Mode-related models and DTOs.
/// </summary>
[TestFixture]
public class ModeModelsTests
{
    [Test]
    public void ModeStateShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var modeState = new ModeState
        {
            ModeId = "test-mode",
            Name = "Test Mode",
            Description = "Test Description",
            Configuration = new ModeConfiguration
            {
                SystemPrompt = "Test prompt",
                Tools = ["tool1"]
            }
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(modeState.ModeId, Is.EqualTo("test-mode"));
            Assert.That(modeState.Name, Is.EqualTo("Test Mode"));
            Assert.That(modeState.Description, Is.EqualTo("Test Description"));
            Assert.That(modeState.Configuration, Is.Not.Null);
            Assert.That(modeState.Status, Is.EqualTo(ModeStatus.Active));
            Assert.That(modeState.Metadata, Is.Not.Null);
        });
    }

    [Test]
    public void ModeInitRequestShouldValidateRequiredFields()
    {
        // Arrange
        var request = new ModeInitRequest
        {
            Name = "Test Mode",
            Description = "Test Description",
            Configuration = new ModeConfiguration
            {
                SystemPrompt = "Test prompt",
                Tools = ["tool1"]
            }
        };

        // Act
        var context = new ValidationContext(request);
        List<ValidationResult> results = [];
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.That(isValid, Is.True, "Valid ModeInitRequest should pass validation");
    }

    [Test]
    public void ModeInitRequestShouldFailValidationWithInvalidName()
    {
        // Arrange
        var request = new ModeInitRequest
        {
            Name = new string('a', 101), // Exceeds max length
            Description = "Test Description",
            Configuration = new ModeConfiguration
            {
                SystemPrompt = "Test prompt",
                Tools = ["tool1"]
            }
        };

        // Act
        var context = new ValidationContext(request);
        List<ValidationResult> results = [];
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.False, "ModeInitRequest with invalid name should fail validation");
            Assert.That(results.Any(r => r.MemberNames.Contains("Name")), Is.True);
        });
    }

    [Test]
    public void ModeConfigurationShouldHaveDefaultValues()
    {
        // Arrange & Act
        var config = new ModeConfiguration
        {
            SystemPrompt = "Test prompt",
            Tools = ["tool1"]
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(config.Parameters, Is.Not.Null);
            Assert.That(config.EnabledFeatures, Is.Not.Null);
            Assert.That(config.DisabledFeatures, Is.Not.Null);
            Assert.That(config.DefaultModel, Is.Null);
            Assert.That(config.Temperature, Is.Null);
            Assert.That(config.MaxTokens, Is.Null);
        });
    }

    [Test]
    public void ModeConfigurationShouldValidateSystemPromptLength()
    {
        // Arrange
        var config = new ModeConfiguration
        {
            SystemPrompt = new string('a', 2001), // Exceeds max length
            Tools = ["tool1"]
        };

        // Act
        var context = new ValidationContext(config);
        List<ValidationResult> results = [];
        var isValid = Validator.TryValidateObject(config, context, results, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.False, "ModeConfiguration with long prompt should fail validation");
            Assert.That(results.Any(r => r.MemberNames.Contains("SystemPrompt")), Is.True);
        });
    }

    [Test]
    public void ModeConfigurationShouldValidateToolsList()
    {
        // Arrange
        var config = new ModeConfiguration
        {
            SystemPrompt = "Test prompt",
            Tools = [] // Empty tools list
        };

        // Act
        var context = new ValidationContext(config);
        List<ValidationResult> results = [];
        var isValid = Validator.TryValidateObject(config, context, results, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.False, "ModeConfiguration with empty tools should fail validation");
            Assert.That(results.Any(r => r.MemberNames.Contains("Tools")), Is.True);
        });
    }

    [Test]
    public void ModeConstraintsShouldHaveOptionalProperties()
    {
        // Arrange & Act
        var constraints = new ModeConstraints
        {
            MaxMessageLength = 1000,
            MaxMessagesPerMinute = 10,
            ContentFilter = ContentFilterLevel.Moderate
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(constraints.MaxMessageLength, Is.EqualTo(1000));
            Assert.That(constraints.MaxMessagesPerMinute, Is.EqualTo(10));
            Assert.That(constraints.ContentFilter, Is.EqualTo(ContentFilterLevel.Moderate));
            Assert.That(constraints.AllowedFileTypes, Is.Null);
            Assert.That(constraints.MaxFileSize, Is.Null);
            Assert.That(constraints.RequiredRoles, Is.Null);
        });
    }

    [Test]
    public void ModeTransitionRequestShouldPreserveContextByDefault()
    {
        // Arrange & Act
        var request = new ModeTransitionRequest
        {
            TargetModeId = "target-mode"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.PreserveContext, Is.True);
            Assert.That(request.PreserveHistory, Is.True);
        });
    }

    [Test]
    public void ModeTransitionResultShouldIndicateSuccess()
    {
        // Arrange & Act
        var result = new ModeTransitionResult
        {
            Success = true,
            TransitionId = "trans-123",
            NewState = new ModeState
            {
                ModeId = "new-mode",
                Name = "New Mode",
                Description = "New Description",
                Configuration = new ModeConfiguration
                {
                    SystemPrompt = "New prompt",
                    Tools = ["tool1"]
                }
            }
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.TransitionId, Is.EqualTo("trans-123"));
            Assert.That(result.NewState, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Warnings, Is.Not.Null);
        });
    }

    [Test]
    public void ModeValidationResultShouldContainErrorsWhenInvalid()
    {
        // Arrange & Act
        var result = new ModeValidationResult
        {
            IsValid = false,
            Errors =
            [
                new()
                {
                    Code = "ERR001",
                    Message = "Invalid configuration",
                    Field = "SystemPrompt",
                    Severity = ValidationSeverity.Error
                }
            ]
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(result.Errors[0].Code, Is.EqualTo("ERR001"));
            Assert.That(result.Errors[0].Severity, Is.EqualTo(ValidationSeverity.Error));
        });
    }

    [Test]
    public void ModeChangeEventShouldCaptureChangeDetails()
    {
        // Arrange & Act
        var changeEvent = new ModeChangeEvent
        {
            EventId = "evt-123",
            ChangeType = ModeChangeType.ConfigurationUpdated,
            TimestampUtc = DateTime.UtcNow,
            Description = "Configuration updated",
            UserId = "user-123",
            PreviousValue = "{\"systemPrompt\":\"old\"}",
            NewValue = "{\"systemPrompt\":\"new\"}"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(changeEvent.EventId, Is.EqualTo("evt-123"));
            Assert.That(changeEvent.ChangeType, Is.EqualTo(ModeChangeType.ConfigurationUpdated));
            Assert.That(changeEvent.Description, Is.EqualTo("Configuration updated"));
            Assert.That(changeEvent.UserId, Is.EqualTo("user-123"));
            Assert.That(changeEvent.PreviousValue, Is.Not.Null);
            Assert.That(changeEvent.NewValue, Is.Not.Null);
        });
    }

    [Test]
    public void ModeTemplateShouldBeAvailableByDefault()
    {
        // Arrange & Act
        var template = new ModeTemplate
        {
            TemplateId = "template-1",
            Name = "Template Name",
            Description = "Template Description",
            DefaultConfiguration = new ModeConfiguration
            {
                SystemPrompt = "Template prompt",
                Tools = ["tool1"]
            }
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(template.IsAvailable, Is.True);
            Assert.That(template.Tags, Is.Not.Null);
            Assert.That(template.Category, Is.Null);
            Assert.That(template.IconUrl, Is.Null);
        });
    }

    [Test]
    public void EnumValuesShouldBeProperlyDefined()
    {
        // Assert ModeStatus
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(ModeStatus.Active), Is.True);
            Assert.That(Enum.IsDefined(ModeStatus.Inactive), Is.True);
            Assert.That(Enum.IsDefined(ModeStatus.Archived), Is.True);
            Assert.That(Enum.IsDefined(ModeStatus.Error), Is.True);
        });

        // Assert ModeChangeType
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(ModeChangeType.Created), Is.True);
            Assert.That(Enum.IsDefined(ModeChangeType.ConfigurationUpdated), Is.True);
            Assert.That(Enum.IsDefined(ModeChangeType.Archived), Is.True);
        });

        // Assert ModeAction
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(ModeAction.View), Is.True);
            Assert.That(Enum.IsDefined(ModeAction.Create), Is.True);
            Assert.That(Enum.IsDefined(ModeAction.Update), Is.True);
            Assert.That(Enum.IsDefined(ModeAction.Delete), Is.True);
        });

        // Assert ValidationSeverity
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(ValidationSeverity.Info), Is.True);
            Assert.That(Enum.IsDefined(ValidationSeverity.Warning), Is.True);
            Assert.That(Enum.IsDefined(ValidationSeverity.Error), Is.True);
            Assert.That(Enum.IsDefined(ValidationSeverity.Critical), Is.True);
        });
    }

    [Test]
    public void ConstraintViolationShouldCaptureViolationDetails()
    {
        // Arrange & Act
        var violation = new ConstraintViolation
        {
            Constraint = "MaxMessageLength",
            ActualValue = 2000,
            ExpectedValue = 1000,
            Message = "Message exceeds maximum length"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(violation.Constraint, Is.EqualTo("MaxMessageLength"));
            Assert.That(violation.ActualValue, Is.EqualTo(2000));
            Assert.That(violation.ExpectedValue, Is.EqualTo(1000));
            Assert.That(violation.Message, Is.EqualTo("Message exceeds maximum length"));
        });
    }

    [Test]
    public void TimeRestrictionsShouldAllowPartialConfiguration()
    {
        // Arrange & Act
        var restrictions = new TimeRestrictions
        {
            AllowedDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            TimeZone = "UTC"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(restrictions.AllowedDays, Has.Count.EqualTo(3));
            Assert.That(restrictions.AllowedDays, Does.Contain(DayOfWeek.Monday));
            Assert.That(restrictions.StartTime, Is.EqualTo(new TimeOnly(9, 0)));
            Assert.That(restrictions.EndTime, Is.EqualTo(new TimeOnly(17, 0)));
            Assert.That(restrictions.TimeZone, Is.EqualTo("UTC"));
        });
    }

    [Test]
    public void RecurrencePatternShouldHaveDefaultInterval()
    {
        // Arrange & Act
        var pattern = new RecurrencePattern
        {
            Type = RecurrenceType.Daily
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(pattern.Type, Is.EqualTo(RecurrenceType.Daily));
            Assert.That(pattern.Interval, Is.EqualTo(1));
            Assert.That(pattern.DaysOfWeek, Is.Null);
            Assert.That(pattern.MaxOccurrences, Is.Null);
            Assert.That(pattern.EndDate, Is.Null);
        });
    }

    [Test]
    public void StateValidationReportShouldProvideComprehensiveStatus()
    {
        // Arrange & Act
        var report = new StateValidationReport
        {
            Status = ValidationStatus.ValidWithWarnings,
            ValidatedAtUtc = DateTime.UtcNow,
            HealthScore = 85,
            ConsistencyChecks =
            [
                new()
                {
                    CheckName = "ConfigurationConsistency",
                    Passed = true,
                    Details = "Configuration is consistent"
                }
            ],
            Recommendations = ["Consider updating system prompt"]
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ValidationStatus.ValidWithWarnings));
            Assert.That(report.HealthScore, Is.EqualTo(85));
            Assert.That(report.ConsistencyChecks, Has.Count.EqualTo(1));
            Assert.That(report.ConsistencyChecks[0].Passed, Is.True);
            Assert.That(report.Recommendations, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void CompatibilityValidationResultShouldCalculateScore()
    {
        // Arrange & Act
        var result = new CompatibilityValidationResult
        {
            IsCompatible = true,
            CompatibilityScore = 75,
            Incompatibilities = [],
            Warnings = ["Some features may not work as expected"]
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsCompatible, Is.True);
            Assert.That(result.CompatibilityScore, Is.InRange(0, 100));
            Assert.That(result.Incompatibilities, Is.Empty);
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
        });
    }
}