using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AIChat.Server.Services.Translation.Models;

/// <summary>
/// Represents a REST API request message for protocol translation.
/// Wraps HTTP requests into a structured format for translation to Orleans messages.
/// </summary>
public sealed class RestMessage
{
    /// <summary>
    /// HTTP method for the request (GET, POST, PUT, DELETE, etc.).
    /// </summary>
    [Required]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Request path (e.g., "/api/chat/123/message").
    /// </summary>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// HTTP headers from the request.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>
    /// Query parameters from the request URL.
    /// </summary>
    public Dictionary<string, string> QueryParameters { get; set; } = [];

    /// <summary>
    /// Path parameters extracted from the URL route.
    /// </summary>
    public Dictionary<string, string> PathParameters { get; set; } = [];

    /// <summary>
    /// Request body content (typically JSON for API requests).
    /// </summary>
    public object? Body { get; set; }

    /// <summary>
    /// User identifier associated with the request (from authentication).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Timestamp when the REST message was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata for the REST operation.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Content type of the request body.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Creates a REST message for sending a chat message.
    /// </summary>
    /// <param name="chatId">Chat ID from the URL path</param>
    /// <param name="sendMessageRequest">Request body containing message data</param>
    /// <param name="userId">User ID from authentication</param>
    /// <returns>REST message for sending a chat message</returns>
    public static RestMessage CreateSendMessage(string chatId, object sendMessageRequest, string? userId = null)
    {
        return new RestMessage
        {
            Method = "POST",
            Path = $"/api/chat/{chatId}/message",
            Body = sendMessageRequest,
            UserId = userId,
            PathParameters = new Dictionary<string, string> { ["chatId"] = chatId },
            ContentType = "application/json",
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a REST message for getting chat history.
    /// </summary>
    /// <param name="page">Page number</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="userId">User ID from authentication</param>
    /// <returns>REST message for getting chat history</returns>
    public static RestMessage CreateGetChatHistory(int page = 1, int pageSize = 20, string? userId = null)
    {
        return new RestMessage
        {
            Method = "GET",
            Path = "/api/chat/history",
            UserId = userId,
            QueryParameters = new Dictionary<string, string>
            {
                ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["pageSize"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a REST message for getting a specific chat.
    /// </summary>
    /// <param name="chatId">Chat ID from the URL path</param>
    /// <param name="userId">User ID from authentication</param>
    /// <returns>REST message for getting a chat</returns>
    public static RestMessage CreateGetChat(string chatId, string? userId = null)
    {
        return new RestMessage
        {
            Method = "GET",
            Path = $"/api/chat/{chatId}",
            UserId = userId,
            PathParameters = new Dictionary<string, string> { ["chatId"] = chatId },
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a REST message for creating a new chat.
    /// </summary>
    /// <param name="createChatRequest">Request body containing chat creation data</param>
    /// <param name="userId">User ID from authentication</param>
    /// <returns>REST message for creating a chat</returns>
    public static RestMessage CreateNewChat(object createChatRequest, string? userId = null)
    {
        return new RestMessage
        {
            Method = "POST",
            Path = "/api/chat",
            Body = createChatRequest,
            UserId = userId,
            ContentType = "application/json",
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a REST message for deleting a chat.
    /// </summary>
    /// <param name="chatId">Chat ID from the URL path</param>
    /// <param name="userId">User ID from authentication</param>
    /// <returns>REST message for deleting a chat</returns>
    public static RestMessage CreateDeleteChat(string chatId, string? userId = null)
    {
        return new RestMessage
        {
            Method = "DELETE",
            Path = $"/api/chat/{chatId}",
            UserId = userId,
            PathParameters = new Dictionary<string, string> { ["chatId"] = chatId },
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets a path parameter value.
    /// </summary>
    /// <param name="parameterName">Name of the path parameter</param>
    /// <returns>Parameter value or empty string if not found</returns>
    public string GetPathParameter(string parameterName)
    {
        return PathParameters.TryGetValue(parameterName, out var value) ? value : string.Empty;
    }

    /// <summary>
    /// Gets a query parameter value.
    /// </summary>
    /// <param name="parameterName">Name of the query parameter</param>
    /// <returns>Parameter value or empty string if not found</returns>
    public string GetQueryParameter(string parameterName)
    {
        return QueryParameters.TryGetValue(parameterName, out var value) ? value : string.Empty;
    }

    /// <summary>
    /// Gets a header value.
    /// </summary>
    /// <param name="headerName">Name of the header</param>
    /// <returns>Header value or empty string if not found</returns>
    public string GetHeader(string headerName)
    {
        return Headers.TryGetValue(headerName, out var value) ? value : string.Empty;
    }

    /// <summary>
    /// Gets the request body as a specific type.
    /// </summary>
    /// <typeparam name="T">Type to deserialize the body to</typeparam>
    /// <returns>Deserialized body or default value if unable to convert</returns>
    public T? GetBodyAs<T>()
    {
        if (Body == null)
        {
            return default;
        }

        if (Body is T directCast)
        {
            return directCast;
        }

        if (Body is JsonElement jsonElement)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            catch
            {
                return default;
            }
        }

        if (Body is string jsonString)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(jsonString);
            }
            catch
            {
                return default;
            }
        }

        // Try to serialize to JSON and back to convert
        try
        {
            var json = JsonSerializer.Serialize(Body);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Validates the REST message structure.
    /// </summary>
    /// <returns>Validation result</returns>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(Method))
        {
            errors.Add("HTTP Method is required");
        }

        if (string.IsNullOrEmpty(Path))
        {
            errors.Add("Request Path is required");
        }

        // Validate method-specific requirements
        if (Method?.ToUpperInvariant() is "POST" or "PUT" or "PATCH" && Body == null)
        {
            errors.Add($"{Method} requests typically require a body");
        }

        // Validate path format
        if (!string.IsNullOrEmpty(Path) && !Path.StartsWith('/'))
        {
            errors.Add("Request path should start with '/'");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Determines the operation type based on the HTTP method and path.
    /// </summary>
    /// <returns>Operation type string</returns>
    public string GetOperationType()
    {
        if (string.IsNullOrEmpty(Method) || string.IsNullOrEmpty(Path))
        {
            return "Unknown";
        }

        var method = Method.ToUpperInvariant();
        var path = Path.ToLowerInvariant();

        return (method, path) switch
        {
            ("POST", var p) when p.Contains("/message") => "SendMessage",
            ("GET", var p) when p.Contains("/history") => "GetChatHistory",
            ("GET", var p) when p.EndsWith("/chat", StringComparison.Ordinal) || p.Contains("/chat/") => "GetChat",
            ("POST", var p) when p.EndsWith("/chat", StringComparison.Ordinal) => "CreateChat",
            ("DELETE", var p) when p.Contains("/chat/") => "DeleteChat",
            ("GET", var p) when p.Contains("/tasks") => "GetTasks",
            ("POST", var p) when p.Contains("/stream") => "StartStream",
            ("DELETE", var p) when p.Contains("/operation") => "CancelOperation",
            ("GET", var p) when p.Contains("/operation") => "GetOperationStatus",
            _ => $"{method}_{Path.Split('/').LastOrDefault()}"
        };
    }

    /// <summary>
    /// Extracts chat ID from the request path if present.
    /// </summary>
    /// <returns>Chat ID or null if not found</returns>
    public string? ExtractChatId()
    {
        if (PathParameters.TryGetValue("chatId", out var chatId))
        {
            return chatId;
        }

        // Try to extract from path pattern /api/chat/{id}
        var pathSegments = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var chatIndex = Array.FindIndex(pathSegments, s => s.Equals("chat", StringComparison.OrdinalIgnoreCase));

        if (chatIndex >= 0 && chatIndex + 1 < pathSegments.Length)
        {
            return pathSegments[chatIndex + 1];
        }

        return null;
    }
}

/// <summary>
/// Represents a REST API response message for protocol translation.
/// Wraps Orleans operation results into HTTP response format.
/// </summary>
public sealed class RestResponse
{
    /// <summary>
    /// HTTP status code for the response.
    /// </summary>
    public int StatusCode { get; set; } = 200;

    /// <summary>
    /// Response body content.
    /// </summary>
    public object? Body { get; set; }

    /// <summary>
    /// HTTP headers for the response.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>
    /// Content type of the response body.
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Timestamp when the response was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a successful REST response.
    /// </summary>
    /// <param name="data">Response data</param>
    /// <param name="statusCode">HTTP status code (default: 200)</param>
    /// <returns>Successful REST response</returns>
    public static RestResponse Success(object? data, int statusCode = 200)
    {
        return new RestResponse
        {
            StatusCode = statusCode,
            Body = data,
            ContentType = "application/json",
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates an error REST response.
    /// </summary>
    /// <param name="errorMessage">Error message</param>
    /// <param name="statusCode">HTTP status code (default: 500)</param>
    /// <returns>Error REST response</returns>
    public static RestResponse Error(string errorMessage, int statusCode = 500)
    {
        return new RestResponse
        {
            StatusCode = statusCode,
            Body = new { Error = errorMessage },
            ContentType = "application/json",
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a not found REST response.
    /// </summary>
    /// <param name="message">Not found message</param>
    /// <returns>Not found REST response</returns>
    public static RestResponse NotFound(string message = "Resource not found")
    {
        return Error(message, 404);
    }

    /// <summary>
    /// Creates a bad request REST response.
    /// </summary>
    /// <param name="message">Bad request message</param>
    /// <returns>Bad request REST response</returns>
    public static RestResponse BadRequest(string message = "Bad request")
    {
        return Error(message, 400);
    }

    /// <summary>
    /// Creates an unauthorized REST response.
    /// </summary>
    /// <param name="message">Unauthorized message</param>
    /// <returns>Unauthorized REST response</returns>
    public static RestResponse Unauthorized(string message = "Unauthorized")
    {
        return Error(message, 401);
    }

    /// <summary>
    /// Creates a created REST response for successful resource creation.
    /// </summary>
    /// <param name="data">Created resource data</param>
    /// <param name="location">Location header for the created resource</param>
    /// <returns>Created REST response</returns>
    public static RestResponse Created(object? data, string? location = null)
    {
        var response = new RestResponse
        {
            StatusCode = 201,
            Body = data,
            ContentType = "application/json",
            Timestamp = DateTime.UtcNow
        };

        if (!string.IsNullOrEmpty(location))
        {
            response.Headers["Location"] = location;
        }

        return response;
    }

    /// <summary>
    /// Creates a no content REST response for successful operations with no return data.
    /// </summary>
    /// <returns>No content REST response</returns>
    public static RestResponse NoContent()
    {
        return new RestResponse
        {
            StatusCode = 204,
            Body = null,
            Timestamp = DateTime.UtcNow
        };
    }
}