using AIChat.Server.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming
{
    // Minimal interfaces for testing - actual implementations are in server project
    public interface IStreamingBridge : IAsyncDisposable
    {
        Task ConvertGrainToHttpStreamAsync<T>(
            IAsyncEnumerable<T> grainStream,
            HttpResponse httpResponse,
            Func<T, string> formatter,
            CancellationToken cancellationToken = default
        );

        Task<bool> HandleBackpressureAsync(
            float bufferUtilization,
            CancellationToken cancellationToken = default
        );

        Task PropagateErrorAsync(
            Exception exception,
            HttpResponse httpResponse,
            CancellationToken cancellationToken = default
        );

        BufferStatistics GetBufferStatistics();
    }

    public interface IStreamingBridgeFactory
    {
        IStreamingBridge CreateBridge();
    }

    public interface IResilientStreamManager : IAsyncDisposable
    {
        Task<T> ProcessStreamWithRecoveryAsync<T>(
            string streamId,
            string userId,
            Func<IStreamingBridge, CancellationToken, Task<T>> streamProcessor,
            CancellationToken cancellationToken = default
        );

        Task<StreamMetrics> GetMetricsAsync();
    }

    public record BufferStatistics
    {
        public int Capacity { get; init; }
        public int CurrentSize { get; init; }
        public float UtilizationPercentage { get; init; }
        public long ItemsProcessed { get; init; }
        public long BackpressureEvents { get; init; }
        public double AverageProcessingTimeMs { get; init; }
    }

    public record StreamMetrics
    {
        public long TotalStreamsProcessed { get; init; }
        public long TotalRecoveryAttempts { get; init; }
        public long SuccessfulRecoveries { get; init; }
        public long TotalMessagesProcessed { get; init; }
        public TimeSpan Uptime { get; init; }
    }
} // End namespace AIChat.Server.Services.Streaming

namespace AIChat.Server.Configuration
{
    public class StreamingConfiguration
    {
        public int BufferSize { get; set; } = 100;
        public int FlushIntervalMs { get; set; } = 100;
        public int MaxConcurrentWrites { get; set; } = 10;
        public int BackpressureThreshold { get; set; } = 80;
        public bool Enabled { get; set; } = true;
        public int TimeoutMs { get; set; } = 30000;

        public bool Validate(out List<string> errors)
        {
            errors = [];
            if (BufferSize <= 0)
            {
                errors.Add("BufferSize must be positive");
            }

            if (FlushIntervalMs <= 0)
            {
                errors.Add("FlushIntervalMs must be positive");
            }

            return errors.Count == 0;
        }
    }

    public class ResilientStreamingConfiguration
    {
        public bool Enabled { get; set; }
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        public int CircuitBreakerThreshold { get; set; } = 5;
        public int CircuitBreakerResetTimeoutMs { get; set; } = 30000;
        public int PartialMessageBufferSize { get; set; } = 100;
        public int MessageTimeoutMs { get; set; } = 30000;
        public int HealthCheckIntervalMs { get; set; } = 10000;

        public bool Validate(out List<string> errors)
        {
            errors = [];
            if (MaxRetryAttempts < 0)
            {
                errors.Add("MaxRetryAttempts must be non-negative");
            }

            if (RetryDelayMs <= 0)
            {
                errors.Add("RetryDelayMs must be positive");
            }

            return errors.Count == 0;
        }
    }
}

namespace AIChat.Server.Models
{
    public class CreateChatRequest
    {
        public string? ChatId { get; set; }
        public required string UserId { get; set; }
        public required string Message { get; set; }
        public string? SystemPrompt { get; set; }
        public string? ModeId { get; set; }
    }
}

namespace AIChat.Server.Models.SSE
{
    public class SSEEnvelope
    {
        public string? ChatId { get; set; }
        public string? MessageId { get; set; }
        public string? Content { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object?>? Metadata { get; set; }
    }

    public static class SSEEventExtensions
    {
        public static SSEEnvelope CreateInitEnvelope(
            string chatId,
            string messageId,
            DateTime timestamp,
            int sequenceNumber
        )
        {
            return new SSEEnvelope
            {
                ChatId = chatId,
                MessageId = messageId,
                Timestamp = timestamp,
                Metadata = new Dictionary<string, object?>
                {
                    ["sequenceNumber"] = sequenceNumber,
                    ["type"] = "init",
                },
            };
        }

        public static SSEEnvelope CreateStreamCompleteEnvelope(string chatId)
        {
            return new SSEEnvelope
            {
                ChatId = chatId,
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object?> { ["type"] = "complete" },
            };
        }

        public static SSEEnvelope CreateErrorEnvelope(
            string chatId,
            string? messageId,
            int sequenceNumber,
            string error
        )
        {
            return new SSEEnvelope
            {
                ChatId = chatId,
                MessageId = messageId,
                Content = error,
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object?>
                {
                    ["sequenceNumber"] = sequenceNumber,
                    ["type"] = "error",
                    ["error"] = error,
                },
            };
        }
    }
}

// Stub for Program class
public partial class Program { }

// Minimal implementations for testing
namespace AIChat.Server.Services.Streaming
{
    public class StreamingBridge : IStreamingBridge
    {
#pragma warning disable IDE0052 // Remove unread private members - Mock implementation
        private readonly ILogger<StreamingBridge> _logger;
        private readonly StreamingConfiguration _configuration;
#pragma warning restore IDE0052

        public StreamingBridge(
            ILogger<StreamingBridge> logger,
            IOptions<StreamingConfiguration> configuration
        )
        {
            _logger = logger;
            _configuration = configuration.Value;
        }

        public Task ConvertGrainToHttpStreamAsync<T>(
            IAsyncEnumerable<T> grainStream,
            HttpResponse httpResponse,
            Func<T, string> formatter,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }

        public Task<bool> HandleBackpressureAsync(
            float bufferUtilization,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(true);
        }

        public Task PropagateErrorAsync(
            Exception exception,
            HttpResponse httpResponse,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }

        public BufferStatistics GetBufferStatistics()
        {
            return new BufferStatistics();
        }

        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }

    public class StreamingBridgeFactory : IStreamingBridgeFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public StreamingBridgeFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IStreamingBridge CreateBridge()
        {
            var logger = _serviceProvider.GetService<ILogger<StreamingBridge>>()!;
            var config = _serviceProvider.GetService<IOptions<StreamingConfiguration>>()!;
            return new StreamingBridge(logger, config);
        }
    }

    public class ResilientStreamManager : IResilientStreamManager
    {
#pragma warning disable IDE0052 // Remove unread private members - Mock implementation
        private readonly ILogger<ResilientStreamManager> _logger;
        private readonly IStreamingBridgeFactory _bridgeFactory;
        private readonly ResilientStreamingConfiguration _configuration;
#pragma warning restore IDE0052

        public ResilientStreamManager(
            ILogger<ResilientStreamManager> logger,
            IStreamingBridgeFactory bridgeFactory,
            IOptions<ResilientStreamingConfiguration> configuration
        )
        {
            _logger = logger;
            _bridgeFactory = bridgeFactory;
            _configuration = configuration.Value;
        }

        public async Task<T> ProcessStreamWithRecoveryAsync<T>(
            string streamId,
            string userId,
            Func<IStreamingBridge, CancellationToken, Task<T>> streamProcessor,
            CancellationToken cancellationToken = default
        )
        {
            var bridge = _bridgeFactory.CreateBridge();
            return await streamProcessor(bridge, cancellationToken);
        }

        public Task<StreamMetrics> GetMetricsAsync()
        {
            return Task.FromResult(
                new StreamMetrics
                {
                    TotalStreamsProcessed = 0,
                    TotalRecoveryAttempts = 0,
                    SuccessfulRecoveries = 0,
                    TotalMessagesProcessed = 0,
                    Uptime = TimeSpan.Zero,
                }
            );
        }

        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
