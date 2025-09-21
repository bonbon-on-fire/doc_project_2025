using Microsoft.Extensions.Options;

namespace AIChat.Server.Configuration.Validators;

/// <summary>
/// Validates AdaptiveBufferingConfiguration settings.
/// </summary>
public sealed class AdaptiveBufferingConfigurationValidator : IValidateOptions<StreamingConfiguration>
{
    public ValidateOptionsResult Validate(string? name, StreamingConfiguration options)
    {
        if (options.AdaptiveBuffering is null)
        {
            return ValidateOptionsResult.Success;
        }

        var config = options.AdaptiveBuffering;
        var errors = new List<string>();

        // Range validations
        if (config.MinSize is < 10 or > 1000)
        {
            errors.Add("AdaptiveBuffering.MinSize must be between 10 and 1000");
        }

        if (config.MaxSize is < 100 or > 10000)
        {
            errors.Add("AdaptiveBuffering.MaxSize must be between 100 and 10000");
        }

        if (config.MinSize >= config.MaxSize)
        {
            errors.Add("AdaptiveBuffering.MinSize must be less than MaxSize");
        }

        if (config.ScaleUpThreshold is < 50 or > 95)
        {
            errors.Add("AdaptiveBuffering.ScaleUpThreshold must be between 50 and 95");
        }

        if (config.ScaleDownThreshold is < 10 or > 50)
        {
            errors.Add("AdaptiveBuffering.ScaleDownThreshold must be between 10 and 50");
        }

        if (config.ScaleDownThreshold >= config.ScaleUpThreshold)
        {
            errors.Add("AdaptiveBuffering.ScaleDownThreshold must be less than ScaleUpThreshold");
        }

        if (config.WindowSizeMinutes is < 1 or > 60)
        {
            errors.Add("AdaptiveBuffering.WindowSizeMinutes must be between 1 and 60");
        }

        if (config.ScaleFactor is < 1.1f or > 3.0f)
        {
            errors.Add("AdaptiveBuffering.ScaleFactor must be between 1.1 and 3.0");
        }

        if (config.ScaleCooldownSeconds is < 10 or > 300)
        {
            errors.Add("AdaptiveBuffering.ScaleCooldownSeconds must be between 10 and 300");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(string.Join("; ", errors))
            : ValidateOptionsResult.Success;
    }
}