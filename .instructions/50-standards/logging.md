# LOGGING STANDARDS
<!-- Logging requirements and best practices -->

> **Logging Best Practices**
> Consistent logging ensures effective monitoring and debugging.

### MANDATORY: Logging Requirements
- **REQUIRED**: Log all exceptions
- **REQUIRED**: Log all warnings
- **REQUIRED**: Log information where appropriate and useful
- **FORBIDDEN**: Never log debug information
- **FORBIDDEN**: Never log sensitive information (UserId, Password, or any other sensitive data)
- **REQUIRED**: Use structured logging with proper log levels and include relevant context properties
- **ENFORCEMENT SCOPE**: Apply to all logging implementations in new and modified code
- **COMPLIANCE OUTCOME**: Consistent logging patterns, security compliance, and effective monitoring

**Implementation Examples:**
```csharp
//REQUIRED - Proper exception logging
try
{
    await ProcessConversationData(conversationId);
}
catch (Exception ex)
{
    logger.LogError(ex, "Failed to process conversation data for conversation {ConversationId}", conversationId);
    throw;
}

//REQUIRED - Warning logging
if (retryCount > maxRetries)
{
    logger.LogWarning("Retry limit exceeded for operation {OperationName}, retries: {RetryCount}",
        operationName, retryCount);
}

//REQUIRED - Information logging
logger.LogInformation("Conversation {ConversationId} successfully processed {MessageCount} messages",
    conversationId, processedMessages.Count);

//REQUIRED - Structured logging with context
logger.LogInformation("Message processing completed for conversation {ConversationId} with {MessageCount} messages in {DurationMs}ms",
    conversationId, messageCount, stopwatch.ElapsedMilliseconds);

// FORBIDDEN - Debug logging
logger.LogDebug("Debug information"); // Never use

// FORBIDDEN - Sensitive information logging
logger.LogInformation("User password: {Password}", password); // Security violation
logger.LogInformation("Processing data for user {UserId}", userId); // UserId is sensitive
