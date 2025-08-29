# ASYNC PROGRAMMING
<!-- Asynchronous programming patterns and requirements -->

## REQUIRED: Async Patterns
- **MANDATORY**: Use async/await for all asynchronous methods
- **STRUCTURE**: async and await keywords must be in same method
- **FORBIDDEN**: async void (except event handlers)
- **CONSISTENCY**: Maintain async pattern throughout call chain
- **ENFORCEMENT SCOPE**: Apply to all new asynchronous code and when modifying existing async methods
- **COMPLIANCE OUTCOME**: Improved error handling, better performance, and consistent async behavior

**Implementation Examples:**
```csharp
//REQUIRED - Proper async method pattern
public async Task<ConversationData> GetConversationData(string conversationId)
{
    var conversationData = await conversationRepository.GetById(conversationId);
    var messages = await messageService.GetConversationMessages(conversationId);
    return new ConversationData(conversationData, messages);
}

//REQUIRED - Async event handler pattern
private async void OnButtonClick(object sender, EventArgs e)
{
    try
    {
        await ProcessRequest();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing request");
    }
}

// FORBIDDEN - async void for non-event handlers
public async void ProcessData() // Should return Task
{
    await SomeAsyncOperation();
}

// FORBIDDEN - Breaking async chain
public ConversationData GetConversationData(string conversationId)
{
    return GetConversationData(conversationId).Result; // Should maintain async pattern
}
