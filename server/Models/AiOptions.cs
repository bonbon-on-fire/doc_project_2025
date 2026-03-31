namespace AIChat.Server.Models;

// Unified configuration for AI model identifiers.
// Single model only; legacy multi-model fields removed.
public class AiOptions
{
    public string? ModelId { get; set; }
}
