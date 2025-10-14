using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Services;

namespace AIChat.Orleans.Host.Services;

/// <summary>
/// HTTP-based implementation of IChatServiceProxy for Orleans Host.
/// Makes HTTP calls to Server API endpoints to provide real LLM processing to Orleans grains.
/// Avoids circular dependencies by using HTTP communication instead of direct project references.
/// </summary>
public class HttpChatServiceProxy : IChatServiceProxy
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpChatServiceProxy> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the HttpChatServiceProxy.
    /// </summary>
    /// <param name="httpClient">HTTP client for making API calls to Server</param>
    /// <param name="logger">Logger for diagnostics</param>
    public HttpChatServiceProxy(HttpClient httpClient, ILogger<HttpChatServiceProxy> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamChunk> ProcessChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "Processing chat request via HTTP API from Orleans Host. ChatId: {ChatId}, UserId: {UserId}, RequestId: {RequestId}",
            request.ChatId,
            request.UserId,
            request.RequestId
        );

        HttpResponseMessage? response = null;
        Exception? processingException = null;

        try
        {
            // Make HTTP request outside of the yield context
            try
            {
                // Convert Orleans ChatRequest to HTTP request body
                var requestBody = new
                {
                    chatId = request.ChatId,
                    userId = request.UserId,
                    message = request.Message,
                    modeId = request.ModeId
                    // Note: SystemPrompt not included as it's not part of the current SendMessage API
                };

                var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Make HTTP POST to Server's SendMessage endpoint
                response = await _httpClient.PostAsync("api/chat/send", content, cancellationToken);
            }
            catch (Exception ex)
            {
                processingException = ex;
            }

            // Handle errors and yield results
            if (processingException != null)
            {
                _logger.LogError(processingException,
                    "Error making HTTP request from Orleans Host for ChatId: {ChatId}, RequestId: {RequestId}",
                    request.ChatId,
                    request.RequestId
                );

                yield return new StreamChunk
                {
                    OperationId = request.RequestId,
                    ChatId = request.ChatId,
                    Content = "An error occurred while processing your request.",
                    ChunkIndex = 0,
                    IsComplete = true,
                    TotalChunks = 1,
                    Type = StreamChunkType.Error,
                    MessageId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow
                };
                yield break;
            }

            if (response?.IsSuccessStatusCode != true)
            {
                _logger.LogError(
                    "HTTP request failed from Orleans Host for ChatId: {ChatId}. StatusCode: {StatusCode}",
                    request.ChatId,
                    response?.StatusCode
                );

                yield return new StreamChunk
                {
                    OperationId = request.RequestId,
                    ChatId = request.ChatId,
                    Content = $"HTTP Error: {response?.StatusCode} - {response?.ReasonPhrase}",
                    ChunkIndex = 0,
                    IsComplete = true,
                    TotalChunks = 1,
                    Type = StreamChunkType.Error,
                    MessageId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow
                };
                yield break;
            }

            // Process the response outside of try-catch for yield compatibility
            string? responseJson = null;
            Exception? responseException = null;

            try
            {
                responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                responseException = ex;
            }

            if (responseException != null)
            {
                _logger.LogError(responseException,
                    "Error processing HTTP response from Orleans Host for ChatId: {ChatId}",
                    request.ChatId
                );

                yield return new StreamChunk
                {
                    OperationId = request.RequestId,
                    ChatId = request.ChatId,
                    Content = "Error processing server response.",
                    ChunkIndex = 0,
                    IsComplete = true,
                    TotalChunks = 1,
                    Type = StreamChunkType.Error,
                    MessageId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow
                };
                yield break;
            }

            // Simulate streaming by splitting response into chunks
            if (!string.IsNullOrEmpty(responseJson))
            {
                var words = responseJson.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var chunkSize = Math.Max(1, words.Length / 10);
                var chunkIndex = 0;

                for (int i = 0; i < words.Length; i += chunkSize)
                {
                    var chunkWords = words.Skip(i).Take(chunkSize);
                    var chunkContent = string.Join(" ", chunkWords);

                    if (i + chunkSize < words.Length)
                    {
                        chunkContent += " ";
                    }

                    yield return new StreamChunk
                    {
                        OperationId = request.RequestId,
                        ChatId = request.ChatId,
                        MessageId = Guid.NewGuid().ToString(),
                        Content = chunkContent,
                        ChunkIndex = chunkIndex++,
                        IsComplete = false,
                        Type = StreamChunkType.Text,
                        Timestamp = DateTime.UtcNow
                    };

                    // Small delay to simulate streaming
                    await Task.Delay(100, cancellationToken);
                }

                // Send completion chunk
                yield return new StreamChunk
                {
                    OperationId = request.RequestId,
                    ChatId = request.ChatId,
                    MessageId = Guid.NewGuid().ToString(),
                    ChunkIndex = chunkIndex,
                    IsComplete = true,
                    TotalChunks = chunkIndex + 1,
                    Type = StreamChunkType.Complete,
                    Timestamp = DateTime.UtcNow
                };

                _logger.LogInformation(
                    "Completed HTTP-based chat request from Orleans Host for ChatId: {ChatId}. Total chunks: {ChunkCount}",
                    request.ChatId,
                    chunkIndex + 1
                );
            }
        }
        finally
        {
            response?.Dispose();
        }
    }
}
