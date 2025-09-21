namespace AIChat.Server.Services.Abstractions;

/// <summary>
/// Abstraction for timer functionality to improve testability.
/// </summary>
public interface ITimer : IDisposable
{
    /// <summary>
    /// Starts the timer with the specified interval.
    /// </summary>
    /// <param name="dueTime">The amount of time to delay before the callback is invoked</param>
    /// <param name="period">The time interval between invocations of the callback</param>
    void Start(TimeSpan dueTime, TimeSpan period);

    /// <summary>
    /// Stops the timer.
    /// </summary>
    void StopTimer();

    /// <summary>
    /// Changes the timer settings.
    /// </summary>
    /// <param name="dueTime">The amount of time to delay before the callback is invoked</param>
    /// <param name="period">The time interval between invocations of the callback</param>
    void Change(TimeSpan dueTime, TimeSpan period);
}

/// <summary>
/// Factory for creating timer instances.
/// </summary>
public interface ITimerFactory
{
    /// <summary>
    /// Creates a new timer instance.
    /// </summary>
    /// <param name="callback">The callback to invoke</param>
    /// <param name="state">State object to pass to callback</param>
    /// <returns>A new timer instance</returns>
    ITimer CreateTimer(TimerCallback callback, object? state);
}

/// <summary>
/// Default implementation of ITimer using System.Threading.Timer.
/// </summary>
public sealed class SystemTimer : ITimer
{
    private readonly Timer _timer;
    private bool _disposed;

    public SystemTimer(TimerCallback callback, object? state)
    {
        _timer = new Timer(callback, state, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(TimeSpan dueTime, TimeSpan period)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Change(dueTime, period);
    }

    public void StopTimer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Change(TimeSpan dueTime, TimeSpan period)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Change(dueTime, period);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Dispose();
    }
}

/// <summary>
/// Default implementation of ITimerFactory.
/// </summary>
public sealed class SystemTimerFactory : ITimerFactory
{
    public ITimer CreateTimer(TimerCallback callback, object? state)
    {
        return new SystemTimer(callback, state);
    }
}