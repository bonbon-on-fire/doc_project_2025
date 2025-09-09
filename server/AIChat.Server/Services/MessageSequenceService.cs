namespace AIChat.Server.Services;

public interface IMessageSequenceService
{
    Task<int> GetNextSequenceNumberAsync(string chatId);
}

public class MessageSequenceService : IMessageSequenceService
{
    public Task<int> GetNextSequenceNumberAsync(string chatId)
    {
        // Deprecated; ChatService now uses IChatStorage.AllocateSequenceAsync
        throw new NotSupportedException("MessageSequenceService is no longer used.");
    }
}
