using FluentAssertions;
using System.ComponentModel.DataAnnotations;
using WhatsAppWaha.Core.Configuration;

namespace WhatsAppWaha.Core.Tests.Configuration;

public class WahaSettingsTests
{
  [Fact]
  public void SectionName_ShouldBeCorrect()
  {
    // Arrange & Act & Assert
    WahaSettings.SectionName.Should().Be("Waha");
  }

  [Fact]
  public void DefaultValues_ShouldBeValid()
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "default"
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().BeEmpty();
    settings.TimeoutSeconds.Should().Be(30);
    settings.MaxRetryAttempts.Should().Be(3);
    settings.RetryDelayMs.Should().Be(1000);
    settings.VerifySsl.Should().BeTrue();
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public void BaseUrl_WhenNullOrEmpty_ShouldFailValidation(string? baseUrl)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = baseUrl!,
      Session = "default"
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().ContainSingle()
        .Which.ErrorMessage.Should().Be("WAHA BaseUrl is required");
  }

  [Theory]
  [InlineData("not-a-url")]
  [InlineData("invalid-url")]
  [InlineData("://invalid")]
  public void BaseUrl_WhenInvalidUrl_ShouldFailValidation(string baseUrl)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = baseUrl,
      Session = "default"
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().ContainSingle()
        .Which.ErrorMessage.Should().Be("WAHA BaseUrl must be a valid URL");
  }

  [Theory]
  [InlineData("http://localhost:3000")]
  [InlineData("https://api.waha.example.com")]
  [InlineData("https://192.168.1.100:8080")]
  public void BaseUrl_WhenValidUrl_ShouldPassValidation(string baseUrl)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = baseUrl,
      Session = "default"
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().BeEmpty();
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public void Session_WhenNullOrEmpty_ShouldFailValidation(string? session)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = session!
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().ContainSingle()
        .Which.ErrorMessage.Should().Be("WAHA Session is required");
  }

  [Fact]
  public void Session_WhenTooLong_ShouldFailValidation()
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = new string('a', 51) // 51 characters, max is 50
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().ContainSingle()
        .Which.ErrorMessage.Should().Be("WAHA Session must be between 1 and 50 characters");
  }

  [Theory]
  [InlineData(4)] // Below minimum
  [InlineData(301)] // Above maximum
  public void TimeoutSeconds_WhenOutOfRange_ShouldFailValidation(int timeoutSeconds)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "default",
      TimeoutSeconds = timeoutSeconds
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().ContainSingle()
        .Which.ErrorMessage.Should().Be("WAHA Timeout must be between 5 and 300 seconds");
  }

  [Theory]
  [InlineData(-1)] // Below minimum
  [InlineData(11)] // Above maximum
  public void MaxRetryAttempts_WhenOutOfRange_ShouldFailValidation(int maxRetryAttempts)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "default",
      MaxRetryAttempts = maxRetryAttempts
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().ContainSingle()
        .Which.ErrorMessage.Should().Be("WAHA MaxRetryAttempts must be between 0 and 10");
  }

  [Theory]
  [InlineData(99)] // Below minimum
  [InlineData(10001)] // Above maximum
  public void RetryDelayMs_WhenOutOfRange_ShouldFailValidation(int retryDelayMs)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "default",
      RetryDelayMs = retryDelayMs
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().ContainSingle()
        .Which.ErrorMessage.Should().Be("WAHA RetryDelayMs must be between 100 and 10000 milliseconds");
  }

  [Fact]
  public void ApiKey_WhenNull_ShouldPassValidation()
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "default",
      ApiKey = null
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().BeEmpty();
  }

  [Fact]
  public void ApiKey_WhenProvided_ShouldPassValidation()
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "default",
      ApiKey = "test-api-key"
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().BeEmpty();
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void VerifySsl_ShouldAcceptBothValues(bool verifySsl)
  {
    // Arrange
    var settings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "default",
      VerifySsl = verifySsl
    };

    // Act
    var validationResults = ValidateSettings(settings);

    // Assert
    validationResults.Should().BeEmpty();
  }

  private static List<ValidationResult> ValidateSettings(WahaSettings settings)
  {
    var validationResults = new List<ValidationResult>();
    var validationContext = new ValidationContext(settings, serviceProvider: null, items: null);
    Validator.TryValidateObject(settings, validationContext, validationResults, validateAllProperties: true);
    return validationResults;
  }
}
