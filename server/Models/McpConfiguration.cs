using System.Collections.Generic;

namespace AIChat.Server.Models;

public class McpConfiguration
{
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = new();
    public List<McpInputConfig>? Inputs { get; set; }
}

public class McpServerConfig
{
    public string Type { get; set; } = "stdio";
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public string? Description { get; set; }
}

public class McpInputConfig
{
    public string Type { get; set; } = "promptString";
    public string Id { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Password { get; set; }
    public string? DefaultValue { get; set; }
}