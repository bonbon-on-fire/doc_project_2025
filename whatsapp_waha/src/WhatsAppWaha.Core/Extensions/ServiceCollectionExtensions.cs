using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Polly.CircuitBreaker;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using WhatsAppWaha.Core.Configuration;
using WhatsAppWaha.Core.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Waha; // ✅ Add WAHA SDK namespace for IWahaApiClient
using WahaConfig = WhatsAppWaha.Core.Configuration.WahaSettings; // ✅ Alias to resolve conflict

namespace WhatsAppWaha.Core.Extensions;

/// <summary>
/// Extension methods for configuring services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
  /// <summary>
  /// Adds all WhatsApp WAHA framework services to the DI container.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddWhatsAppWahaFramework(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    // Add and validate configuration
    services.AddConfigurationWithValidation(configuration);

    // Add HTTP clients with retry policies
    services.AddHttpClientsWithRetryPolicies();

    // Add core services (will be implemented in future tasks)
    services.AddApplicationServices();

    return services;
  }

  /// <summary>
  /// Adds configuration models with validation to the DI container.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">The configuration.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddConfigurationWithValidation(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    // Configure and validate WahaSettings
    services.Configure<WahaConfig>(configuration.GetSection(WahaConfig.SectionName));
    services.AddSingleton<IValidateOptions<WahaConfig>, ValidateOptionsWithDataAnnotations<WahaConfig>>();

    // Configure and validate NtfySettings
    services.Configure<NtfySettings>(configuration.GetSection(NtfySettings.SectionName));
    services.AddSingleton<IValidateOptions<NtfySettings>, ValidateOptionsWithDataAnnotations<NtfySettings>>();

    // Configure and validate AppSettings
    services.Configure<AppSettings>(configuration.GetSection(AppSettings.SectionName));
    services.AddSingleton<IValidateOptions<AppSettings>, ValidateOptionsWithDataAnnotations<AppSettings>>();

    // Configure and validate LoggingSettings
    services.Configure<LoggingSettings>(configuration.GetSection(LoggingSettings.SectionName));
    services.AddSingleton<IValidateOptions<LoggingSettings>, ValidateOptionsWithDataAnnotations<LoggingSettings>>();

    return services;
  }

  /// <summary>
  /// Adds HTTP clients with Polly retry policies.
  /// ✅ PURE WAHA SDK APPROACH: HTTP clients are for SDK internal use and NTfy service.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddHttpClientsWithRetryPolicies(this IServiceCollection services)
  {
    // ✅ WAHA SDK INTERNAL: Configure HTTP client for WAHA SDK internal use
    // The SDK client will use this HTTP client internally for all API communication
    services.AddHttpClient("WahaClient", (serviceProvider, client) =>
    {
      var wahaSettings = serviceProvider.GetRequiredService<IOptions<WahaConfig>>().Value;
      client.BaseAddress = new Uri(wahaSettings.BaseUrl);
      client.Timeout = TimeSpan.FromSeconds(wahaSettings.TimeoutSeconds);
      
      // 🔑 CRITICAL: Add API key header for WAHA authentication
      if (!string.IsNullOrEmpty(wahaSettings.ApiKey))
      {
        client.DefaultRequestHeaders.Add("X-Api-Key", wahaSettings.ApiKey);
      }
    })
    .AddPolicyHandler((serviceProvider, request) => 
    {
      var wahaSettings = serviceProvider.GetRequiredService<IOptions<WahaConfig>>().Value;
      return CreateWahaResiliencePolicy(serviceProvider, wahaSettings);
    });

    // Add ntfy HTTP client with retry policy
    services.AddHttpClient("NtfyClient", (serviceProvider, client) =>
    {
      var ntfySettings = serviceProvider.GetRequiredService<IOptions<NtfySettings>>().Value;
      client.BaseAddress = new Uri(ntfySettings.BaseUrl);
      client.Timeout = TimeSpan.FromSeconds(ntfySettings.TimeoutSeconds);

      if (!string.IsNullOrEmpty(ntfySettings.AuthToken))
      {
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ntfySettings.AuthToken}");
      }
    })
    .AddPolicyHandler(CreateRetryPolicy(3, 1000)); // Standard retry for ntfy

    return services;
  }

  /// <summary>
  /// Adds application-specific services.
  /// ✅ PURE WAHA SDK APPROACH: Use official WAHA SDK registration with proper configuration.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddApplicationServices(this IServiceCollection services)
  {
    // ✅ PURE WAHA SDK: Use official WAHA SDK registration method
    // 🔧 WORKAROUND: Handle JSON converter incompatibility in WAHA SDK
    services.AddScoped<IWahaApiClient>((serviceProvider) =>
    {
      var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
      var logger = serviceProvider.GetService<ILogger<WahaApiClient>>();
      var wahaSettings = serviceProvider.GetRequiredService<IOptions<WahaConfig>>().Value;
      
      // 🔑 Use pre-configured HTTP client with BaseURL + API Key
      var httpClient = httpClientFactory.CreateClient("WahaClient");
      
      // 🔍 Log configuration for verification
      logger?.LogInformation("WAHA SDK Client initialized - BaseURL: {BaseUrl}, Session: {Session}, HasApiKey: {HasApiKey}", 
        httpClient.BaseAddress, 
        wahaSettings.Session,
        !string.IsNullOrEmpty(wahaSettings.ApiKey));
        
      // 🔧 WORKAROUND: The WAHA SDK v1.1.0 has a bug with UnixTimestampConverter
      // For now, we'll use the basic constructor and handle the exception in our service
      return new WahaApiClient(httpClient);
    });

    // ✅ Register our pure WAHA SDK service (uses IWahaApiClient only)
    services.AddScoped<WhatsAppWaha.Core.Interfaces.IWahaService, WhatsAppWaha.Core.Services.WahaService>();
    
    // ✅ CLEAN ARCHITECTURE: WahaService now uses pure WAHA SDK approach
    // - No direct HTTP client dependencies
    // - All WAHA API calls go through IWahaApiClient interface
    // - Uses proper SDK methods: SendTextAsync, GetSessionAsync, etc.
    // - No dynamic method discovery - direct method calls

    // Register ntfy service
    services.AddScoped<WhatsAppWaha.Core.Interfaces.INtfyService, WhatsAppWaha.Core.Services.NtfyService>();

    // Placeholder for future service registrations
    // Will be implemented in subsequent tasks:
    // - IMessageProcessor
    // etc.

    return services;
  }

  /// <summary>
  /// Creates a comprehensive resilience policy for WAHA HTTP client with retry and circuit breaker.
  /// </summary>
  /// <param name="serviceProvider">The service provider for dependency injection.</param>
  /// <param name="settings">The WAHA configuration settings.</param>
  /// <returns>The resilience policy.</returns>
  private static IAsyncPolicy<HttpResponseMessage> CreateWahaResiliencePolicy(
      IServiceProvider serviceProvider, 
      WahaConfig settings)
  {
    var logger = serviceProvider.GetService<ILogger<WahaService>>();
    
    // Circuit breaker policy
    var circuitBreakerPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .AdvancedCircuitBreakerAsync(
            failureThreshold: 0.5, // Break when 50% of requests fail
            samplingDuration: TimeSpan.FromSeconds(30), // Sample period
            minimumThroughput: 3, // Minimum requests before breaking
            durationOfBreak: TimeSpan.FromMinutes(1), // Keep circuit open for 1 minute
            onBreak: (result, timespan) =>
            {
              var exceptionMessage = result.Exception?.Message ?? result.Result?.StatusCode.ToString() ?? "Unknown error";
              logger?.LogWarning(
                  "WAHA circuit breaker opened for {Duration} due to consecutive failures. Error: {Error}",
                  timespan, exceptionMessage);
            },
            onReset: () =>
            {
              logger?.LogInformation("WAHA circuit breaker reset - service appears to be healthy");
            });

    // Retry policy with exponential backoff
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: settings.MaxRetryAttempts,
            sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(
                settings.RetryDelayMs * Math.Pow(2, retryAttempt - 1)), // Exponential backoff
            onRetry: (outcome, timespan, retryCount, context) =>
            {
              logger?.LogWarning(
                  "WAHA retry attempt {RetryCount} after {Delay}ms delay. Reason: {Reason}",
                  retryCount, timespan.TotalMilliseconds, 
                  outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
            });

    // Combine policies: Retry -> Circuit Breaker
    return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
  }

  /// <summary>
  /// Creates a simple retry policy with exponential backoff for ntfy.
  /// </summary>
  /// <param name="maxRetryAttempts">Maximum number of retry attempts.</param>
  /// <param name="baseDelayMs">Base delay in milliseconds.</param>
  /// <returns>The retry policy.</returns>
  private static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(int maxRetryAttempts, int baseDelayMs)
  {
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: maxRetryAttempts,
            sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(
                baseDelayMs * Math.Pow(2, retryAttempt - 1)), // Exponential backoff
            onRetry: (outcome, timespan, retryCount, context) =>
            {
              // Basic console logging for ntfy (will be enhanced in Task 3)
              Console.WriteLine($"ntfy retry {retryCount} after {timespan.TotalMilliseconds}ms delay");
            });
  }
}

/// <summary>
/// Generic validator for options using data annotations.
/// </summary>
/// <typeparam name="TOptions">The options type to validate.</typeparam>
public class ValidateOptionsWithDataAnnotations<TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
  /// <summary>
  /// Validates the options using data annotations.
  /// </summary>
  /// <param name="name">The options name.</param>
  /// <param name="options">The options instance.</param>
  /// <returns>The validation result.</returns>
  public ValidateOptionsResult Validate(string? name, TOptions options)
  {
    var validationResults = new List<ValidationResult>();
    var validationContext = new ValidationContext(options, serviceProvider: null, items: null);

    if (Validator.TryValidateObject(options, validationContext, validationResults, validateAllProperties: true))
    {
      return ValidateOptionsResult.Success;
    }

    var errors = validationResults.Select(r => r.ErrorMessage ?? "Unknown validation error").ToList();
    return ValidateOptionsResult.Fail(errors);
  }
}
