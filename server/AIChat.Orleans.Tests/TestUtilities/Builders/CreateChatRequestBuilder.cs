using AIChat.Orleans.Tests.TestUtilities.Mocks;
using AIChat.Server.Services;

namespace AIChat.Orleans.Tests.TestUtilities.Builders;

/// <summary>
/// Builder pattern implementation for creating test CreateChatRequest instances.
/// Follows the fluent interface pattern for easy test data setup.
/// </summary>
public class CreateChatRequestBuilder
{
    private string? _chatId;
    private string _userId = "test-user";
    private string _message = "Test message";
    private string? _systemPrompt = "You are a test assistant";
    private string? _modeId = "default";

    /// <summary>
    /// Creates a new instance of the builder with default values.
    /// </summary>
    public static CreateChatRequestBuilder Create()
    {
        return new();
    }

    /// <summary>
    /// Sets the chat ID for continuing an existing conversation.
    /// </summary>
    public CreateChatRequestBuilder WithChatId(string chatId)
    {
        _chatId = chatId;
        return this;
    }

    /// <summary>
    /// Sets the user ID for the request.
    /// </summary>
    public CreateChatRequestBuilder WithUserId(string userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>
    /// Sets the message content.
    /// </summary>
    public CreateChatRequestBuilder WithMessage(string message)
    {
        _message = message;
        return this;
    }

    /// <summary>
    /// Sets the system prompt for AI behavior.
    /// </summary>
    public CreateChatRequestBuilder WithSystemPrompt(string? systemPrompt)
    {
        _systemPrompt = systemPrompt;
        return this;
    }

    /// <summary>
    /// Sets the mode ID for the chat.
    /// </summary>
    public CreateChatRequestBuilder WithModeId(string? modeId)
    {
        _modeId = modeId;
        return this;
    }

    /// <summary>
    /// Creates a request for Orleans routing testing.
    /// </summary>
    public CreateChatRequestBuilder ForOrleansRouting(string suffix = "")
    {
        _userId = $"orleans-user{(string.IsNullOrEmpty(suffix) ? "" : $"-{suffix}")}";
        _message = "Test message for Orleans routing";
        return this;
    }

    /// <summary>
    /// Creates a request for direct processing testing.
    /// </summary>
    public CreateChatRequestBuilder ForDirectProcessing(string suffix = "")
    {
        _userId = $"direct-user{(string.IsNullOrEmpty(suffix) ? "" : $"-{suffix}")}";
        _message = "Test message for direct processing";
        return this;
    }

    /// <summary>
    /// Creates a request for performance testing.
    /// </summary>
    public CreateChatRequestBuilder ForPerformanceTest(int iteration)
    {
        _userId = $"perf-user-{iteration}";
        _message = $"Performance test message {iteration}";
        return this;
    }

    /// <summary>
    /// Creates a request with invalid data for validation testing.
    /// </summary>
    public CreateChatRequestBuilder WithInvalidData()
    {
        _userId = ""; // Invalid empty user ID
        return this;
    }

    /// <summary>
    /// Builds the final CreateChatRequest instance.
    /// </summary>
    public CreateChatRequest Build()
    {
        return new CreateChatRequest
        {
            ChatId = _chatId,
            UserId = _userId,
            Message = _message,
            SystemPrompt = _systemPrompt,
            ModeId = _modeId,
        };
    }

    /// <summary>
    /// Implicit conversion to CreateChatRequest for convenience.
    /// </summary>
    public static implicit operator CreateChatRequest(CreateChatRequestBuilder builder)
    {
        return builder.Build();
    }
}
