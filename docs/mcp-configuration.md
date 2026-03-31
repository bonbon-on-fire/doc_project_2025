# MCP Server Configuration Guide

This document explains how to configure and use MCP (Model Context Protocol) servers in the AIChat application.

## Overview

MCP servers provide additional tools and capabilities to the LLM through a standardized protocol. They can be configured in `appsettings.json` and are automatically registered with the FunctionRegistry, making them available to the AI assistant.

## Configuration Structure

MCP servers are configured in the `Mcp` section of `appsettings.json`:

```json
{
  "Mcp": {
    "McpServers": {
      "server-name": {
        "Type": "stdio|sse",
        "Command": "command-to-start-server",
        "Args": ["arg1", "arg2"],
        "Env": {
          "ENV_VAR": "value"
        },
        "Url": "http://server-url",
        "Headers": {
          "Header-Name": "Header-Value"
        },
        "WorkingDirectory": "/path/to/dir",
        "Enabled": true,
        "Priority": 1,
        "Description": "Server description"
      }
    },
    "Inputs": [
      {
        "Type": "promptString",
        "Id": "input-id",
        "Description": "Input description",
        "Password": true,
        "DefaultValue": "ENV_VAR_NAME"
      }
    ]
  }
}
```

## Server Types

### STDIO Servers

STDIO servers communicate via standard input/output. They're typically NPM packages or local executables.

**Required fields:**
- `Type`: "stdio"
- `Command`: The command to execute
- `Args`: Command arguments (optional)

**Example:**
```json
{
  "filesystem": {
    "Type": "stdio",
    "Command": "npx",
    "Args": ["-y", "@modelcontextprotocol/server-filesystem"],
    "Env": {
      "WORKSPACE_DIR": "./workspace"
    },
    "Description": "File system operations",
    "Enabled": true
  }
}
```

### SSE/HTTP Servers

SSE servers communicate via Server-Sent Events over HTTP.

**Required fields:**
- `Type`: "sse"
- `Url`: The server URL

**Example:**
```json
{
  "api-server": {
    "Type": "sse",
    "Url": "http://localhost:3000/mcp",
    "Headers": {
      "Authorization": "Bearer ${API_KEY}"
    },
    "Description": "Custom API server",
    "Enabled": true
  }
}
```

## Environment Variables

Environment variables can be used in configuration:

### Direct expansion
```json
"Env": {
  "API_KEY": "${MY_API_KEY}"
}
```

### With defaults
```json
"Env": {
  "WORKSPACE": "${WORKSPACE_DIR:-./default-workspace}"
}
```

### Input references
```json
"Env": {
  "API_KEY": "${input:api-key-input}"
}
```

## Input Configuration

Inputs define configurable values that can be referenced in server configurations:

```json
"Inputs": [
  {
    "Type": "promptString",
    "Id": "api-key-input",
    "Description": "API Key",
    "Password": true,
    "DefaultValue": "DEFAULT_API_KEY_ENV_VAR"
  }
]
```

## Server Priority

The `Priority` field determines the order in which servers are initialized and which functions take precedence when there are conflicts:
- Lower numbers = higher priority
- Default is 0

## Enabling/Disabling Servers

Use the `Enabled` field to control which servers are active:
```json
{
  "server-name": {
    "Enabled": false
  }
}
```

## Common MCP Servers

### 1. File System Server
```json
{
  "filesystem": {
    "Type": "stdio",
    "Command": "npx",
    "Args": ["-y", "@modelcontextprotocol/server-filesystem"],
    "Env": {
      "WORKSPACE_DIR": "./workspace"
    },
    "Enabled": true
  }
}
```

### 2. Weather Server
```json
{
  "weather": {
    "Type": "stdio",
    "Command": "npx",
    "Args": ["-y", "@modelcontextprotocol/server-weather"],
    "Env": {
      "OPENWEATHER_API_KEY": "${OPENWEATHER_API_KEY}"
    },
    "Enabled": true
  }
}
```

### 3. Sequential Thinking Tools
```json
{
  "sequential-thinking": {
    "Type": "stdio",
    "Command": "npx",
    "Args": ["-y", "mcp-sequentialthinking-tools"],
    "Enabled": true
  }
}
```

## Conflict Resolution

When multiple providers offer the same function, the FunctionRegistry uses the configured conflict resolution strategy:
- `PreferMcp`: MCP functions take precedence (default)
- `TakeFirst`: First registered wins
- `TakeLast`: Last registered wins
- `Throw`: Throw an error on conflicts

## Implementation Details

### Service Registration

The MCP client manager is registered as a singleton in `Program.cs`:
```csharp
builder.Services.Configure<McpConfiguration>(builder.Configuration.GetSection("Mcp"));
builder.Services.AddSingleton<IMcpClientManager, McpClientManager>();
```

### Integration with ChatService

MCP clients are automatically added to the FunctionRegistry in `ChatService`:
```csharp
var mcpClients = await _mcpClientManager.GetActiveClientsAsync();
await registry.AddMcpClientsAsync(mcpClients, "McpServers", logger);
```

### Lifecycle Management

- Clients are initialized on first use or when `InitializeClientsAsync()` is called
- Connections are maintained throughout the application lifecycle
- Clients are properly disposed when the application shuts down

## Troubleshooting

### Server not connecting
1. Check the `Enabled` field is set to `true`
2. Verify the command/URL is correct
3. Check environment variables are set
4. Review logs for connection errors

### Functions not available
1. Ensure the server connected successfully
2. Check for function name conflicts
3. Verify the conflict resolution strategy

### Performance issues
1. Consider server priority settings
2. Disable unused servers
3. Check server response times in logs

## Example Full Configuration

```json
{
  "Mcp": {
    "McpServers": {
      "filesystem": {
        "Type": "stdio",
        "Command": "npx",
        "Args": ["-y", "@modelcontextprotocol/server-filesystem"],
        "Env": {
          "WORKSPACE_DIR": "${WORKSPACE_DIR:-./workspace}"
        },
        "Description": "File system operations",
        "Enabled": true,
        "Priority": 1
      },
      "custom-api": {
        "Type": "sse",
        "Url": "${API_URL:-http://localhost:3000/mcp}",
        "Headers": {
          "Authorization": "Bearer ${input:api-key}",
          "X-Client-Id": "aichat"
        },
        "Description": "Custom API integration",
        "Enabled": true,
        "Priority": 2
      }
    },
    "Inputs": [
      {
        "Type": "promptString",
        "Id": "api-key",
        "Description": "API Key for custom server",
        "Password": true,
        "DefaultValue": "CUSTOM_API_KEY"
      }
    ]
  }
}
```