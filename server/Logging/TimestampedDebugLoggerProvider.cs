using System.Diagnostics;
using System.Text;

namespace AIChat.Server.Logging;

/// <summary>
/// Debug logger provider that writes to the Visual Studio Debug/Immediate window
/// with a UTC timestamp prefix, log level, category, and message.
/// </summary>
public sealed class TimestampedDebugLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new TimestampedDebugLogger(categoryName);
    }

    public void Dispose() { }

    private sealed class TimestampedDebugLogger(string category) : ILogger
    {
        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!((ILogger)this).IsEnabled(logLevel))
            {
                return;
            }

            var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
            var level = logLevel.ToString().ToLowerInvariant();
            var message = formatter(state, exception);

            var sb = new StringBuilder();
            _ = sb.Append(ts)
                .Append(' ')
                .Append(level)
                .Append(' ')
                .Append(category)
                .Append(':')
                .Append(' ')
                .Append(message);

            if (exception != null)
            {
                _ = sb.AppendLine().Append(exception);
            }

            Debug.WriteLine(sb.ToString());
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
