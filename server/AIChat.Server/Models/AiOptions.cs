namespace AIChat.Server.Models;

/// <summary>
/// Unified configuration for AI model identifiers.
/// Single model only; legacy multi-model fields removed.
/// </summary>
public class AiOptions
{
    public string? ModelId { get; set; }
}
