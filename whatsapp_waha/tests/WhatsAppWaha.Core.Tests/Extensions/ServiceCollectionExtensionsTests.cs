using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppWaha.Core.Configuration;
using WhatsAppWaha.Core.Extensions;
using WhatsAppWaha.Core.Interfaces;

namespace WhatsAppWaha.Core.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
  private readonly IServiceCollection _services;
  private readonly IConfiguration _configuration;

  public ServiceCollectionExtensionsTests()
  {
    _services = new ServiceCollection();

    // Create configuration with test values
    var configBuilder = new ConfigurationBuilder();
    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Waha:BaseUrl"] = "https://localhost:3000",
      ["Waha:Session"] = "test-session",
      ["Waha:TimeoutSeconds"] = "30",
      ["Ntfy:BaseUrl"] = "https://ntfy.sh",
      ["Ntfy:MessagesTopic"] = "test-messages",
      ["Ntfy:NotificationsTopic"] = "test-notifications",
      ["App:Name"] = "Test App",
      ["App:Version"] = "1.0.0",
      ["App:Environment"] = "Test",
      ["Logging:ConsoleLogLevel"] = "Information",
      ["Logging:FileLogLevel"] = "Warning"
    });
    _configuration = configBuilder.Build();
  }

  [Fact]
  public void AddWhatsAppWahaFramework_ShouldRegisterAllServices()
  {
    // Act
    _services.AddWhatsAppWahaFramework(_configuration);
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    serviceProvider.GetService<IOptions<WahaSettings>>().Should().NotBeNull();
    serviceProvider.GetService<IOptions<NtfySettings>>().Should().NotBeNull();
    serviceProvider.GetService<IOptions<AppSettings>>().Should().NotBeNull();
    serviceProvider.GetService<IOptions<LoggingSettings>>().Should().NotBeNull();
    serviceProvider.GetService<IHttpClientFactory>().Should().NotBeNull();
  }

  [Fact]
  public void AddConfigurationWithValidation_ShouldBindAndValidateWahaSettings()
  {
    // Act
    _services.AddConfigurationWithValidation(_configuration);
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var wahaOptions = serviceProvider.GetRequiredService<IOptions<WahaSettings>>();
    wahaOptions.Value.BaseUrl.Should().Be("https://localhost:3000");
    wahaOptions.Value.Session.Should().Be("test-session");
    wahaOptions.Value.TimeoutSeconds.Should().Be(30);
  }

  [Fact]
  public void AddConfigurationWithValidation_ShouldBindAndValidateNtfySettings()
  {
    // Act
    _services.AddConfigurationWithValidation(_configuration);
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var ntfyOptions = serviceProvider.GetRequiredService<IOptions<NtfySettings>>();
    ntfyOptions.Value.BaseUrl.Should().Be("https://ntfy.sh");
    ntfyOptions.Value.MessagesTopic.Should().Be("test-messages");
    ntfyOptions.Value.NotificationsTopic.Should().Be("test-notifications");
  }

  [Fact]
  public void AddConfigurationWithValidation_ShouldBindAndValidateAppSettings()
  {
    // Act
    _services.AddConfigurationWithValidation(_configuration);
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var appOptions = serviceProvider.GetRequiredService<IOptions<AppSettings>>();
    appOptions.Value.Name.Should().Be("Test App");
    appOptions.Value.Version.Should().Be("1.0.0");
    appOptions.Value.Environment.Should().Be("Test");
  }

  [Fact]
  public void AddConfigurationWithValidation_ShouldBindAndValidateLoggingSettings()
  {
    // Act
    _services.AddConfigurationWithValidation(_configuration);
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var loggingOptions = serviceProvider.GetRequiredService<IOptions<LoggingSettings>>();
    loggingOptions.Value.ConsoleLogLevel.Should().Be("Information");
    loggingOptions.Value.FileLogLevel.Should().Be("Warning");
  }

  [Fact]
  public void AddHttpClientsWithRetryPolicies_ShouldRegisterHttpClients()
  {
    // Arrange
    _services.AddConfigurationWithValidation(_configuration);

    // Act
    _services.AddHttpClientsWithRetryPolicies();
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    httpClientFactory.Should().NotBeNull();

    var wahaClient = httpClientFactory.CreateClient("WahaClient");
    wahaClient.Should().NotBeNull();
    wahaClient.BaseAddress.Should().Be(new Uri("https://localhost:3000"));
    wahaClient.Timeout.Should().Be(TimeSpan.FromSeconds(30));

    var ntfyClient = httpClientFactory.CreateClient("NtfyClient");
    ntfyClient.Should().NotBeNull();
    ntfyClient.BaseAddress.Should().Be(new Uri("https://ntfy.sh"));
  }

  [Fact]
  public void AddHttpClientsWithRetryPolicies_WhenApiKeyProvided_ShouldSetAuthorizationHeader()
  {
    // Arrange
    var configBuilder = new ConfigurationBuilder();
    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Waha:BaseUrl"] = "https://localhost:3000",
      ["Waha:Session"] = "test-session",
      ["Waha:ApiKey"] = "test-api-key",
      ["Ntfy:BaseUrl"] = "https://ntfy.sh",
      ["Ntfy:MessagesTopic"] = "test-messages",
      ["Ntfy:NotificationsTopic"] = "test-notifications"
    });
    var configuration = configBuilder.Build();

    _services.AddConfigurationWithValidation(configuration);

    // Act
    _services.AddHttpClientsWithRetryPolicies();
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var wahaClient = httpClientFactory.CreateClient("WahaClient");

    wahaClient.DefaultRequestHeaders.Authorization.Should().NotBeNull();
    wahaClient.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
    wahaClient.DefaultRequestHeaders.Authorization.Parameter.Should().Be("test-api-key");
  }

  [Fact]
  public void AddApplicationServices_ShouldReturnServiceCollection()
  {
    // Act
    var result = _services.AddApplicationServices();

    // Assert
    result.Should().BeSameAs(_services);
  }

  [Fact]
  public void AddApplicationServices_ShouldRegisterWahaService()
  {
    // Arrange
    _services.AddConfigurationWithValidation(_configuration);
    _services.AddHttpClientsWithRetryPolicies();

    // Act
    _services.AddApplicationServices();
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var wahaService = serviceProvider.GetService<IWahaService>();
    wahaService.Should().NotBeNull();
    wahaService.Should().BeOfType<WhatsAppWaha.Core.Services.WahaService>();
  }

  [Fact]
  public void AddApplicationServices_ShouldRegisterNtfyService()
  {
    // Arrange
    _services.AddConfigurationWithValidation(_configuration);
    _services.AddHttpClientsWithRetryPolicies();

    // Act
    _services.AddApplicationServices();
    var serviceProvider = _services.BuildServiceProvider();

    // Assert
    var ntfyService = serviceProvider.GetService<INtfyService>();
    ntfyService.Should().NotBeNull();
    ntfyService.Should().BeOfType<WhatsAppWaha.Core.Services.NtfyService>();
  }

  [Fact]
  public void ValidateOptionsWithDataAnnotations_WhenValidOptions_ShouldReturnSuccess()
  {
    // Arrange
    var validator = new ValidateOptionsWithDataAnnotations<WahaSettings>();
    var validSettings = new WahaSettings
    {
      BaseUrl = "https://localhost:3000",
      Session = "test-session"
    };

    // Act
    var result = validator.Validate("test", validSettings);

    // Assert
    result.Should().Be(ValidateOptionsResult.Success);
  }

  [Fact]
  public void ValidateOptionsWithDataAnnotations_WhenInvalidOptions_ShouldReturnFailure()
  {
    // Arrange
    var validator = new ValidateOptionsWithDataAnnotations<WahaSettings>();
    var invalidSettings = new WahaSettings
    {
      BaseUrl = "", // Invalid: empty
      Session = "" // Invalid: empty
    };

    // Act
    var result = validator.Validate("test", invalidSettings);

    // Assert
    result.Failed.Should().BeTrue();
    result.Failures.Should().Contain("WAHA BaseUrl is required");
    result.Failures.Should().Contain("WAHA Session is required");
  }

  [Fact]
  public void ValidateOptionsWithDataAnnotations_WhenMultipleValidationErrors_ShouldReturnAllErrors()
  {
    // Arrange
    var validator = new ValidateOptionsWithDataAnnotations<WahaSettings>();
    var invalidSettings = new WahaSettings
    {
      BaseUrl = "not-a-url", // Invalid URL
      Session = "", // Required field empty
      TimeoutSeconds = 1000, // Out of range
      MaxRetryAttempts = 20 // Out of range
    };

    // Act
    var result = validator.Validate("test", invalidSettings);

    // Assert
    result.Failed.Should().BeTrue();
    result.Failures.Should().HaveCountGreaterThan(1);
    result.Failures.Should().Contain(f => f.Contains("BaseUrl"));
    result.Failures.Should().Contain(f => f.Contains("Session"));
    result.Failures.Should().Contain(f => f.Contains("Timeout"));
    result.Failures.Should().Contain(f => f.Contains("MaxRetryAttempts"));
  }
}
