# Using .NET User Secrets with MCP Configuration

This guide explains how to store sensitive MCP server configuration values (like API keys) using .NET User Secrets instead of environment variables.

## Overview

The MCP Client Manager now supports reading configuration values from multiple sources in this priority order:
1. **User Secrets** (highest priority)
2. **Environment Variables**
3. **Default Values** (lowest priority)

This allows you to securely store API keys and other sensitive data without exposing them in your code or configuration files.

## Setting Up User Secrets

### 1. Initialize User Secrets (Already Done)
The project already has User Secrets configured with ID: `16d0d730-3c71-4ed2-9e03-b9c8a40204bc`

### 2. Add Secrets Using .NET CLI

Open a terminal in the `server` directory and use these commands:

```bash
# Add Brave Search API Key
dotnet user-secrets set "BRAVE_API_KEY" "your-brave-api-key-here"

# Add OpenWeather API Key
dotnet user-secrets set "OPENWEATHER_API_KEY" "your-openweather-api-key-here"

# Add custom MCP server API key
dotnet user-secrets set "MCP_API_KEY" "your-mcp-api-key-here"

# Add workspace directory path
dotnet user-secrets set "WORKSPACE_DIR" "C:/Users/YourName/workspace"
```

### 3. List All Secrets
```bash
dotnet user-secrets list
```

### 4. Remove a Secret
```bash
dotnet user-secrets remove "BRAVE_API_KEY"
```

### 5. Clear All Secrets
```bash
dotnet user-secrets clear
```

## How It Works in appsettings.json

When you use variable references in your MCP configuration:

```json
{
  "Mcp": {
    "McpServers": {
      "brave-search": {
        "Type": "stdio",
        "Command": "npx",
        "Args": ["-y", "@modelcontextprotocol/server-brave-search"],
        "Env": {
          "BRAVE_API_KEY": "${BRAVE_API_KEY}"
        }
      }
    }
  }
}
```

The `${BRAVE_API_KEY}` will be resolved by:
1. First checking User Secrets
2. If not found, checking environment variables
3. If still not found, using empty string or default value

## Variable Reference Patterns

### Simple Variable
```json
"BRAVE_API_KEY": "${BRAVE_API_KEY}"
```
Looks for `BRAVE_API_KEY` in User Secrets, then environment variables.

### Variable with Default
```json
"WORKSPACE_DIR": "${WORKSPACE_DIR:-./workspace}"
```
Uses `WORKSPACE_DIR` from User Secrets/environment, or defaults to `./workspace`.

### Input Reference
```json
"OPENWEATHER_API_KEY": "${input:weather-api-key}"
```
References an input defined in the `Inputs` section, which then looks for the configured environment variable name.

## Example: Complete Setup for Brave Search

### Step 1: Set the secret
```bash
dotnet user-secrets set "BRAVE_API_KEY" "your-actual-api-key"
```

### Step 2: Configure in appsettings.json
```json
{
  "Mcp": {
    "McpServers": {
      "brave-search": {
        "Type": "stdio",
        "Command": "npx",
        "Args": ["-y", "@modelcontextprotocol/server-brave-search"],
        "Env": {
          "BRAVE_API_KEY": "${BRAVE_API_KEY}"
        },
        "Enabled": true
      }
    }
  }
}
```

### Step 3: Run the application
The MCP Client Manager will automatically read `BRAVE_API_KEY` from User Secrets and pass it to the MCP server.

## Benefits of Using User Secrets

1. **Security**: API keys are not stored in source control
2. **Developer-Specific**: Each developer can have their own API keys
3. **No Environment Setup**: No need to set environment variables before running
4. **Integrated with .NET**: Works seamlessly with the configuration system
5. **Easy Management**: Simple CLI commands to add/remove/list secrets

## Storage Location

User Secrets are stored outside your project directory:

- **Windows**: `%APPDATA%\Microsoft\UserSecrets\16d0d730-3c71-4ed2-9e03-b9c8a40204bc\secrets.json`
- **Linux/macOS**: `~/.microsoft/usersecrets/16d0d730-3c71-4ed2-9e03-b9c8a40204bc/secrets.json`

## Visual Studio Integration

If you're using Visual Studio:
1. Right-click on the server project
2. Select "Manage User Secrets"
3. Add your secrets in JSON format:

```json
{
  "BRAVE_API_KEY": "your-brave-api-key",
  "OPENWEATHER_API_KEY": "your-openweather-api-key",
  "WORKSPACE_DIR": "C:/Users/YourName/workspace"
}
```

## Troubleshooting

### Secret Not Being Read
1. Ensure you're in the correct directory when setting secrets
2. Verify the secret name matches exactly (case-sensitive)
3. Check that User Secrets are loaded in Program.cs (already configured)
4. Use `dotnet user-secrets list` to verify the secret exists

### Debugging Secret Resolution
The MCP Client Manager logs the resolution process. Check logs for:
- Which source provided the value (User Secrets vs Environment)
- If a default value was used
- Any errors in variable expansion

## Production Deployment

User Secrets are **only for development**. In production, use:
- Azure Key Vault
- Environment variables on the server
- Secure configuration providers
- Managed identity for cloud resources