namespace AIChat.Server.Services.Abstractions;

/// <summary>
/// Abstraction for system time to improve testability.
/// </summary>
public interface ISystemTime
{
    /// <summary>
    /// Gets the current UTC time.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Gets the current local time.
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// Gets the current UTC time as DateTimeOffset.
    /// </summary>
    DateTimeOffset UtcNowOffset { get; }
}

/// <summary>
/// Default implementation of ISystemTime using system clock.
/// </summary>
public sealed class SystemTime : ISystemTime
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateTime Now => DateTime.Now;

    /// <inheritdoc />
    public DateTimeOffset UtcNowOffset => DateTimeOffset.UtcNow;
}
