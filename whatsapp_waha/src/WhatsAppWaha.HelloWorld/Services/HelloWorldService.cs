using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppWaha.Core.Configuration;
using WhatsAppWaha.Core.Interfaces;

namespace WhatsAppWaha.HelloWorld.Services;

/// <summary>
/// Service for handling Hello World message sending functionality.
/// </summary>
public sealed class HelloWorldService
{
    private readonly IWahaService _wahaService;
    private readonly INtfyService _ntfyService;
    private readonly ILogger<HelloWorldService> _logger;
    private readonly AppSettings _appSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="HelloWorldService"/> class.
    /// </summary>
    /// <param name="wahaService">The WAHA service for message sending.</param>
    /// <param name="ntfyService">The ntfy service for notifications.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="appSettings">The application settings.</param>
    public HelloWorldService(
        IWahaService wahaService,
        INtfyService ntfyService,
        ILogger<HelloWorldService> logger,
        IOptions<AppSettings> appSettings)
    {
        _wahaService = wahaService ?? throw new ArgumentNullException(nameof(wahaService));
        _ntfyService = ntfyService ?? throw new ArgumentNullException(nameof(ntfyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
    }

    /// <summary>
    /// Validates command line arguments.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The validation result.</returns>
    public CommandLineValidationResult ValidateArguments(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return CommandLineValidationResult.CreateFailure(
                "Phone number is required.",
                GetUsageHelp());
        }

        // Check for help flags
        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
        {
            return CommandLineValidationResult.CreateHelp(GetUsageHelp());
        }

        // First argument should be the phone number
        var phoneNumber = args[0];

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return CommandLineValidationResult.CreateFailure(
                "Phone number cannot be empty.",
                GetUsageHelp());
        }

        // Validate phone number format using WAHA service
        if (!_wahaService.ValidatePhoneNumber(phoneNumber))
        {
            return CommandLineValidationResult.CreateFailure(
                $"Invalid phone number format: {phoneNumber}. Expected formats: +1234567890, 1234567890, or 1234567890@c.us",
                GetUsageHelp());
        }

        // Parse additional configuration overrides
        var configOverrides = ParseConfigurationOverrides(args.Skip(1).ToArray());

        return CommandLineValidationResult.CreateSuccess(phoneNumber, configOverrides);
    }

    /// <summary>
    /// Sends a hello world message to the specified phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number to send the message to.</param>
    /// <param name="configOverrides">Optional configuration overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exit code (0 for success, non-zero for failure).</returns>
    public async Task<int> SendHelloWorldMessageAsync(
        string phoneNumber,
        Dictionary<string, string>? configOverrides = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting hello world message send to {PhoneNumber}", phoneNumber);

            // Validate WAHA session first
            _logger.LogDebug("Validating WAHA session...");
            var sessionValid = await _wahaService.ValidateSessionAsync(cancellationToken);

            if (!sessionValid)
            {
                _logger.LogError("WAHA session is not valid or not authenticated. Please check your WAHA configuration and ensure the session is active.");

                _ntfyService.SendErrorNotificationFireAndForget(
                    "Hello World Failed",
                    "WAHA session validation failed. Please check WAHA configuration.");

                return ExitCodes.WahaSessionInvalid;
            }

            _logger.LogDebug("WAHA session validated successfully");

            // Create the hello world message (allow override via --text)
            var message = configOverrides != null && configOverrides.TryGetValue("HelloWorld:Text", out var customText)
                && !string.IsNullOrWhiteSpace(customText)
                ? customText
                : CreateHelloWorldMessage();
            _logger.LogDebug("Sending message: {Message}", message);

            // Send the message
            var result = await _wahaService.SendTextMessageAsync(phoneNumber, message, null, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "Hello world message sent successfully to {PhoneNumber}. Message ID: {MessageId}, Status: {Status}",
                    phoneNumber, result.Id, result.Status);

                // Send success notification
                _ntfyService.SendSuccessNotificationFireAndForget(
                    "Hello World Sent",
                    $"Message successfully sent to {phoneNumber}");

                return ExitCodes.Success;
            }
            else
            {
                _logger.LogError(
                    "Failed to send hello world message to {PhoneNumber}. Error: {Error}, Message: {Message}",
                    phoneNumber, result.Error, result.Message);

                _ntfyService.SendErrorNotificationFireAndForget(
                    "Hello World Failed",
                    $"Failed to send message to {phoneNumber}: {result.Error}");

                return ExitCodes.MessageSendFailed;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Hello world message send was cancelled");
            return ExitCodes.OperationCancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred while sending hello world message to {PhoneNumber}", phoneNumber);

            _ntfyService.SendErrorNotificationFireAndForget(
                "Hello World Error",
                $"Unexpected error sending to {phoneNumber}: {ex.Message}");

            return ExitCodes.UnexpectedError;
        }
    }

    /// <summary>
    /// Gets the usage help text.
    /// </summary>
    /// <returns>The usage help text.</returns>
    private string GetUsageHelp()
    {
        return $"""
            {_appSettings.Name} v{_appSettings.Version} - Hello World WhatsApp Message Sender

            USAGE:
                WhatsAppWaha.HelloWorld.exe <phone-number> [options]

            ARGUMENTS:
                <phone-number>    Phone number to send hello world message to.
                                 Supported formats:
                                 - E.164: +1234567890
                                 - International: 1234567890  
                                 - WAHA Chat ID: 1234567890@c.us

            OPTIONS:
                -h, --help       Show this help message
                --waha-url       Override WAHA base URL
                --waha-session   Override WAHA session name
                --ntfy-url       Override ntfy base URL
                --text           Custom text to send instead of default hello world message

            EXAMPLES:
                WhatsAppWaha.HelloWorld.exe +1234567890
                WhatsAppWaha.HelloWorld.exe 1234567890 --waha-url https://localhost:3000
                WhatsAppWaha.HelloWorld.exe +1234567890 --waha-session my-session

            CONFIGURATION:
                Configuration is loaded from appsettings.json and can be overridden
                with environment variables or command line options.

            EXIT CODES:
                0    Success - Message sent successfully
                1    Invalid arguments or phone number
                2    WAHA session invalid or unavailable  
                3    Message send failed
                4    Operation cancelled
                99   Unexpected error

            For more information, visit: https://github.com/devlikeapro/waha
            """;
    }

    /// <summary>
    /// Creates the hello world message text.
    /// </summary>
    /// <returns>The hello world message.</returns>
    private string CreateHelloWorldMessage()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        return $"""
            🌍 Hello World from {_appSettings.Name}! 

            This is an automated test message sent at {timestamp}.

            If you received this message, the WhatsApp WAHA messaging framework is working correctly! 🎉

            ---
            Powered by WAHA (WhatsApp HTTP API)
            Environment: {_appSettings.Environment}
            Version: {_appSettings.Version}
            """;
    }

    /// <summary>
    /// Parses configuration overrides from command line arguments.
    /// </summary>
    /// <param name="args">The command line arguments (excluding phone number).</param>
    /// <returns>Dictionary of configuration overrides.</returns>
    private static Dictionary<string, string> ParseConfigurationOverrides(string[] args)
    {
        var overrides = new Dictionary<string, string>();

        for (int i = 0; i < args.Length - 1; i += 2)
        {
            var key = args[i];
            var value = args[i + 1];

            switch (key.ToLowerInvariant())
            {
                case "--waha-url":
                    overrides["Waha:BaseUrl"] = value;
                    break;
                case "--waha-session":
                    overrides["Waha:Session"] = value;
                    break;
                case "--ntfy-url":
                    overrides["Ntfy:BaseUrl"] = value;
                    break;
                case "--text":
                    overrides["HelloWorld:Text"] = value;
                    break;
            }
        }

        return overrides;
    }
}

/// <summary>
/// Result of command line argument validation.
/// </summary>
public sealed class CommandLineValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validation was successful.
    /// </summary>
    public bool IsSuccess { get; private init; }

    /// <summary>
    /// Gets a value indicating whether help was requested.
    /// </summary>
    public bool IsHelp { get; private init; }

    /// <summary>
    /// Gets the validated phone number.
    /// </summary>
    public string? PhoneNumber { get; private init; }

    /// <summary>
    /// Gets configuration overrides from command line.
    /// </summary>
    public Dictionary<string, string>? ConfigOverrides { get; private init; }

    /// <summary>
    /// Gets the error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// Gets the help text.
    /// </summary>
    public string? HelpText { get; private init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="phoneNumber">The validated phone number.</param>
    /// <param name="configOverrides">Configuration overrides.</param>
    /// <returns>A successful validation result.</returns>
    public static CommandLineValidationResult CreateSuccess(string phoneNumber, Dictionary<string, string>? configOverrides = null)
    {
        return new CommandLineValidationResult
        {
            IsSuccess = true,
            PhoneNumber = phoneNumber,
            ConfigOverrides = configOverrides
        };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="helpText">The help text.</param>
    /// <returns>A failed validation result.</returns>
    public static CommandLineValidationResult CreateFailure(string errorMessage, string? helpText = null)
    {
        return new CommandLineValidationResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            HelpText = helpText
        };
    }

    /// <summary>
    /// Creates a help validation result.
    /// </summary>
    /// <param name="helpText">The help text.</param>
    /// <returns>A help validation result.</returns>
    public static CommandLineValidationResult CreateHelp(string helpText)
    {
        return new CommandLineValidationResult
        {
            IsHelp = true,
            HelpText = helpText
        };
    }
}

/// <summary>
/// Application exit codes.
/// </summary>
public static class ExitCodes
{
    /// <summary>
    /// Success - Message sent successfully.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// Invalid arguments or phone number.
    /// </summary>
    public const int InvalidArguments = 1;

    /// <summary>
    /// WAHA session invalid or unavailable.
    /// </summary>
    public const int WahaSessionInvalid = 2;

    /// <summary>
    /// Message send failed.
    /// </summary>
    public const int MessageSendFailed = 3;

    /// <summary>
    /// Operation cancelled.
    /// </summary>
    public const int OperationCancelled = 4;

    /// <summary>
    /// Unexpected error.
    /// </summary>
    public const int UnexpectedError = 99;
}