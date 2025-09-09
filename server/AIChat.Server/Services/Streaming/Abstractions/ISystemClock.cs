namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Provides an abstraction over system time for improved testability.
/// </summary>
public interface ISystemClock
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Gets the current local date and time.
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// Gets the current UTC date.
    /// </summary>
    DateOnly UtcToday { get; }

    /// <summary>
    /// Gets a high-resolution timestamp for performance measurements.
    /// </summary>
    long GetTimestamp();

    /// <summary>
    /// Converts a timestamp to elapsed time.
    /// </summary>
    /// <param name="startTimestamp">The starting timestamp</param>
    /// <returns>Elapsed time since the timestamp</returns>
    TimeSpan GetElapsedTime(long startTimestamp);
}

/// <summary>
/// Default implementation of ISystemClock using actual system time.
/// </summary>
public class SystemClock : ISystemClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateTime Now => DateTime.Now;

    /// <inheritdoc />
    public DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <inheritdoc />
    public long GetTimestamp()
    {
        return Environment.TickCount64;
    }

    /// <inheritdoc />
    public TimeSpan GetElapsedTime(long startTimestamp)
    {
        var currentTimestamp = GetTimestamp();
        var elapsedMs = currentTimestamp - startTimestamp;
        return TimeSpan.FromMilliseconds(elapsedMs);
    }
}
