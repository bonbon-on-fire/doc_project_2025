# Agent Mode Deployment and Loading Flow

## Overview
This document explains how Agent Cards (modes) are loaded and connected to the **Orleans ModeGrain** in the deployed application.

## Architecture Flow

```
agents/ (source) → Build → bin/agents/ → ModeService → Orleans ModeGrain
                                              ↓
                                         ToolingService
```

## 1. Build-Time Process

### Agent Card Files Location
- **Source**: `agents/` directory at repository root
- **Format**: `.agent.md` files with YAML front matter and markdown sections

### Build Configuration (Fixed)
The `server/AIChat.Server.csproj` now includes:
```xml
<ItemGroup>
  <Content Include="..\agents\**\*.agent.md">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>agents\%(RecursiveDir)%(Filename)%(Extension)</Link>
  </Content>
</ItemGroup>
```

This ensures agent files are:
1. Copied to output directory during build
2. Preserved in the same folder structure
3. Available at runtime in `bin/{Configuration}/net9.0/agents/`

## 2. Runtime Loading Process

### Service Registration (Program.cs)
```csharp
builder.Services.AddSingleton<IModeService, ModeService>();
// Orleans ModeGrain integration
builder.Services.AddOrleans(siloBuilder => {
    siloBuilder.AddGrainService<ModeGrain>();
});
builder.Services.AddScoped<IToolingService, ToolingService>();
```

### Agent Loading Flow

1. **ModeService Initialization**
   - Registered as Singleton (loads once at startup)
   - In `LoadSystemModesAsync()`:
     ```csharp
     var agentsPath = Path.Combine(_hostEnvironment.ContentRootPath, "agents");
     var agentFiles = Directory.GetFiles(agentsPath, "*.agent.md", SearchOption.AllDirectories);
     ```
   - ContentRootPath points to the application's root directory
   - Loads all `.agent.md` files (except TEMPLATE)
   - Parses using `AgentCardParser`
   - Caches in memory as `SystemModeConfig` objects

2. **Orleans ModeGrain Integration**
   - Orleans ModeGrain receives mode configurations via grain state
   - Uses modes for:
     - System prompts: `ModeGrain.GetModeSystemPromptAsync()`
     - Default models: `ModeGrain.GetModeDefaultModelAsync()`
     - Dynamic prompt generation with template system and caching

3. **ToolingService Integration**
   - Also receives `IModeService` via DI
   - Filters available tools based on mode:
     ```csharp
     var filterResult = await _modeService.FilterToolsByModeAsync(
         modeId, userId, availableToolNames);
     ```
   - Only tools specified in the agent's `capabilities.tools` are made available

## 3. Mode Application During Chat

### When Creating a Chat
1. User selects a mode (e.g., "coding", "research")
2. Orleans ChatGrain receives mode ID in request and routes to ModeGrain
3. Mode affects via Orleans ModeGrain:
   - **System Prompt**: Generated dynamically with template system and cached
   - **Available Tools**: Filtered based on mode configuration with Orleans caching
   - **Default Model**: Uses mode's preferred model if specified
   - **State Persistence**: Mode configuration stored in Orleans grain state

### Tool Filtering Example
Agent Card defines:
```yaml
capabilities:
  tools: ["web-search", "TaskManager"]
```

Result: Only web-search and TaskManager tools are available in that chat session.

## 4. Deployment Checklist

✅ **Build Configuration**
- Agent files included in `.csproj` as Content items
- CopyToOutputDirectory set to PreserveNewest

✅ **Runtime Requirements**
- `agents/` folder must exist in ContentRootPath
- At least one valid `.agent.md` file present
- YamlDotNet package available for parsing

✅ **Service Dependencies**
- ModeService registered as Singleton
- Orleans ModeGrain configured with grain state persistence
- Orleans ChatGrain integrated with ModeGrain for mode operations
- ToolingService has Orleans routing for mode-based tool filtering
- AgentCardParser available for parsing

## 5. Troubleshooting

### Agents Not Loading
1. Check build output: `ls bin/{Configuration}/net9.0/agents/`
2. Verify ContentRootPath: Check logs for "Loading X agent card files from"
3. Check parsing errors: Look for "Failed to load agent card from file" in logs

### Modes Not Affecting Chat
1. Verify mode ID matches agent ID in YAML front matter
2. Check ModeService is returning correct prompt/tools
3. Verify ToolingService is filtering tools correctly

## 6. Directory Structure (Deployed)

```
bin/Release/net9.0/
├── AIChat.Server.dll
├── appsettings.json
├── agents/
│   ├── development/
│   │   └── senior-developer.agent.md
│   ├── research/
│   │   └── research-assistant.agent.md
│   └── tasks/
│       ├── coding-assistant.agent.md
│       ├── general-assistant.agent.md
│       └── writing-assistant.agent.md
└── ... (other files)
```

## Summary

The key issue that was fixed: Agent Card files were not being included in the build output. The solution was to add them as Content items in the `.csproj` file with appropriate copy instructions. Now agents are:

1. **Included in builds** automatically
2. **Loaded at startup** by ModeService and integrated with Orleans ModeGrain
3. **Applied to chats** through Orleans ChatGrain and ModeGrain integration
4. **Filter tools** through ToolingService with Orleans-based mode routing
5. **Available in deployment** without manual copying
6. **Cached efficiently** through Orleans ModeGrain multi-layer caching system