using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WhatsAppWaha.Core.Extensions;
using WhatsAppWaha.Core.Interfaces;
using WhatsAppWaha.HelloWorld.Services;

namespace WhatsAppWaha.HelloWorld;

/// <summary>
/// Main program entry point for the Hello World WhatsApp message sender.
/// </summary>
internal class Program
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        var cancellationToken = new CancellationTokenSource();
        
        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellationToken.Cancel();
        };

        try
        {
            // Build configuration
            var configuration = BuildConfiguration(args);
            
            // Configure logging
            ConfigureLogging(configuration);

            // Build host and services
            using var host = CreateHost(configuration, args);

            // Create a service scope for scoped services
            using var scope = host.Services.CreateScope();
            
            // Get the HelloWorld service
            var helloWorldService = scope.ServiceProvider.GetRequiredService<HelloWorldService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("WhatsApp WAHA Hello World application starting...");

            // Handle interactive mode if no arguments provided
            string? phoneNumber;
            Dictionary<string, string>? configOverrides = null;
            
            if (args.Length == 0)
            {
                // Interactive mode - prompt user for phone number
                Console.WriteLine("🌍 Hello World WhatsApp Message Sender");
                Console.WriteLine("=====================================");
                Console.WriteLine();
                Console.Write("Please enter the phone number to send 'Hello World' to: ");
                
                phoneNumber = Console.ReadLine()?.Trim();
                
                if (string.IsNullOrWhiteSpace(phoneNumber))
                {
                    Console.Error.WriteLine("❌ Phone number cannot be empty.");
                    return ExitCodes.InvalidArguments;
                }
                
                // Validate the entered phone number
                var wahaService = scope.ServiceProvider.GetRequiredService<IWahaService>();
                if (!wahaService.ValidatePhoneNumber(phoneNumber))
                {
                    Console.Error.WriteLine($"❌ Invalid phone number format: {phoneNumber}");
                    Console.Error.WriteLine("Expected formats: +1234567890, 1234567890, or 1234567890@c.us");
                    return ExitCodes.InvalidArguments;
                }
                
                Console.WriteLine($"📱 Target phone number: {phoneNumber}");
                Console.WriteLine();
            }
            else
            {
                // Command line mode - validate arguments
                var validationResult = helloWorldService.ValidateArguments(args);

                if (validationResult.IsHelp)
                {
                    Console.WriteLine(validationResult.HelpText);
                    return ExitCodes.Success;
                }

                if (!validationResult.IsSuccess)
                {
                    Console.Error.WriteLine($"Error: {validationResult.ErrorMessage}");
                    Console.Error.WriteLine();
                    
                    if (!string.IsNullOrEmpty(validationResult.HelpText))
                    {
                        Console.Error.WriteLine(validationResult.HelpText);
                    }
                    
                    return ExitCodes.InvalidArguments;
                }
                
                phoneNumber = validationResult.PhoneNumber!;
                configOverrides = validationResult.ConfigOverrides;
            }

            // Send hello world message
            var exitCode = await helloWorldService.SendHelloWorldMessageAsync(
                phoneNumber, 
                configOverrides,
                cancellationToken.Token);

            if (exitCode == ExitCodes.Success)
            {
                logger.LogInformation("Hello world message sent successfully!");
                Console.WriteLine($"✅ Hello world message sent successfully to {phoneNumber}!");
            }
            else
            {
                logger.LogError("Hello world message sending failed with exit code: {ExitCode}", exitCode);
                Console.Error.WriteLine($"❌ Failed to send hello world message. Exit code: {exitCode}");
            }

            return exitCode;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation was cancelled by user.");
            return ExitCodes.OperationCancelled;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Unexpected error: {ex.Message}");
            
            // Try to log if logger is available
            try
            {
                Log.Fatal(ex, "Unexpected error in Hello World application");
            }
            catch
            {
                // If logging fails, just continue
            }

            return ExitCodes.UnexpectedError;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Builds the configuration from appsettings.json, environment variables, and command line arguments.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The built configuration.</returns>
    private static IConfiguration BuildConfiguration(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true);

        // Add user secrets in development environment
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        if (environment == "Development")
        {
            builder.AddUserSecrets<Program>();
        }

        builder.AddEnvironmentVariables()
               .AddCommandLine(args);

        return builder.Build();
    }

    /// <summary>
    /// Configures Serilog logging.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    private static void ConfigureLogging(IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: Enum.Parse<Serilog.Events.LogEventLevel>(
                    configuration["Logging:ConsoleLogLevel"] ?? "Information"))
            .WriteTo.File(
                path: configuration["Logging:FilePath"] ?? "logs/hello-world-.log",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: Enum.Parse<Serilog.Events.LogEventLevel>(
                    configuration["Logging:FileLogLevel"] ?? "Warning"))
            .CreateLogger();
    }

    /// <summary>
    /// Creates and configures the dependency injection host.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The configured host.</returns>
    private static IHost CreateHost(IConfiguration configuration, string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices(services =>
            {
                // Add WhatsApp WAHA framework services
                services.AddWhatsAppWahaFramework(configuration);
                
                // Add HelloWorld specific services
                services.AddScoped<HelloWorldService>();
            })
            .Build();
    }
}
