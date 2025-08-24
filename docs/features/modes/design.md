# Chat Modes Feature - Design Document

## Executive Summary

This document outlines the simplest possible implementation for the Chat Modes feature. The approach leverages existing patterns in the codebase, minimizes changes to core components, and provides a clean upgrade path for future enhancements.

**Key Design Principles:**
- Minimal code changes to existing components
- Leverage existing infrastructure (FunctionRegistry, MCP integration, SQLite storage)
- Simple data structures that can be extended later
- No breaking changes to existing APIs
- Progressive enhancement approach

## Architecture Overview

### High-Level Design

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  Frontend       │────▶│  API Layer       │────▶│  Backend        │
│  - ModeSelector │     │  - ChatController│     │  - ChatService  │
│  - ChatWindow   │     │  - Mode param    │     │  - ModeService  │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                │                         │
                                ▼                         ▼
                        ┌──────────────────┐     ┌─────────────────┐
                        │  Shared Types    │     │  Storage        │
                        │  - Mode interface│     │  - JSON files   │
                        └──────────────────┘     │  - SQLite table │
                                                  └─────────────────┘
```

### Storage Strategy (Simplest Approach)

1. **System Modes**: JSON files in `/server/modes/` directory
   - Simple, version-controlled, easy to deploy
   - No database changes needed for system modes
   - Hot-reload in development mode

2. **User Custom Modes**: Single SQLite table `user_modes`
   - Minimal schema addition
   - Leverages existing SQLite infrastructure
   - Simple CRUD operations

## Data Models

### Mode Interface (Shared Type)

```typescript
// shared/types/mode.ts
export interface Mode {
  id: string;                // Unique identifier (e.g., "coding-assistant")
  name: string;              // Display name
  description: string;       // Brief description for UI
  prompt: string;            // System prompt text
  tools: string[];           // Array of tool names to enable
  defaultModel?: string;     // Optional preferred model
  isSystem: boolean;         // System vs user-created flag
  userId?: string;           // Owner for custom modes
  category?: 'task' | 'role' | 'custom';  // For UI grouping
  createdAt?: string;        // ISO timestamp
  updatedAt?: string;        // ISO timestamp
}
```

### System Mode File Format

```json
// server/modes/coding-assistant.json
{
  "id": "coding-assistant",
  "name": "Coding Assistant",
  "description": "Optimized for writing and reviewing code",
  "category": "task",
  "prompt": "You are an expert software developer...",
  "tools": [
    "WeatherFunction",
    "TaskManager",
    "filesystem",
    "web-search"
  ],
  "defaultModel": "openai/gpt-4"
}
```

### Database Schema Addition

```sql
-- Single table for user custom modes
CREATE TABLE IF NOT EXISTS user_modes (
  Id TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  Name TEXT NOT NULL,
  Description TEXT NOT NULL,
  Prompt TEXT NOT NULL,
  Tools TEXT NOT NULL,        -- JSON array stored as text
  DefaultModel TEXT NULL,
  Category TEXT DEFAULT 'custom',
  CreatedAt TEXT NOT NULL,
  UpdatedAt TEXT NOT NULL,
  FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE
);

CREATE INDEX idx_user_modes_user ON user_modes (UserId);
```

## Component Design

### Backend Components

#### 1. ModeService (New - Minimal Implementation)

```csharp
// server/Services/ModeService.cs
public interface IModeService
{
    Task<IEnumerable<Mode>> GetAllModesAsync(string userId);
    Task<Mode?> GetModeByIdAsync(string modeId, string userId);
    Task<Mode> CreateCustomModeAsync(Mode mode, string userId);
    Task<Mode> UpdateCustomModeAsync(string modeId, Mode mode, string userId);
    Task DeleteCustomModeAsync(string modeId, string userId);
    IEnumerable<string> FilterToolsByMode(Mode mode, IEnumerable<string> availableTools);
}
```

**Key Responsibilities:**
- Load system modes from JSON files on startup
- CRUD operations for user custom modes
- Filter tools based on mode configuration
- Cache system modes in memory for performance

#### 2. ChatService Modifications (Minimal Changes)

```csharp
// Modify existing CreateChatSpecificFunctionCallMiddleware method
private async Task<FunctionCallMiddleware?> CreateChatSpecificFunctionCallMiddleware(
    string chatId, 
    string? modeId = null)  // Add optional mode parameter
{
    // ... existing code ...
    
    // NEW: Apply mode-based tool filtering if mode is specified
    if (!string.IsNullOrEmpty(modeId))
    {
        var mode = await _modeService.GetModeByIdAsync(modeId, userId);
        if (mode != null)
        {
            // Filter registry to only include tools specified in mode
            registry = FilterRegistryByMode(registry, mode);
        }
    }
    
    // ... rest of existing code ...
}
```

#### 3. ChatController Modifications (Minimal API Changes)

```csharp
// Add optional modeId to existing request DTOs
public class CreateChatRequest
{
    public string Message { get; set; }
    public string? SystemPrompt { get; set; }
    public string? ModeId { get; set; }  // NEW: Optional mode selection
}

public class ContinueChatRequest
{
    public string ChatId { get; set; }
    public string Message { get; set; }
    public string? ModeId { get; set; }  // NEW: Allow mode switching
}
```

### Frontend Components

#### 1. ModeSelector Component (New - Simple Dropdown)

```svelte
<!-- client/src/lib/components/ModeSelector.svelte -->
<script lang="ts">
  import { onMount } from 'svelte';
  import type { Mode } from '$shared/types/mode';
  
  export let selectedMode: Mode | null = null;
  export let onModeChange: (mode: Mode | null) => void;
  
  let modes: Mode[] = [];
  let isLoading = true;
  
  onMount(async () => {
    // Fetch available modes from API
    const response = await fetch('/api/modes');
    modes = await response.json();
    isLoading = false;
  });
  
  function handleModeChange(event: Event) {
    const modeId = (event.target as HTMLSelectElement).value;
    const mode = modeId ? modes.find(m => m.id === modeId) : null;
    onModeChange(mode);
  }
</script>

<div class="mode-selector">
  <select 
    value={selectedMode?.id || ''} 
    on:change={handleModeChange}
    disabled={isLoading}
  >
    <option value="">General (No specific mode)</option>
    {#each modes as mode}
      <option value={mode.id}>{mode.name}</option>
    {/each}
  </select>
</div>
```

#### 2. ChatWindow Integration (Minimal Changes)

```svelte
<!-- Modify client/src/lib/components/ChatWindow.svelte -->
<script lang="ts">
  // ... existing imports ...
  import ModeSelector from './ModeSelector.svelte';
  import type { Mode } from '$shared/types/mode';
  
  let selectedMode: Mode | null = null;
  
  async function handleSendMessage(message: string) {
    // ... existing code ...
    
    // Include modeId in API request if mode is selected
    const request = {
      message,
      modeId: selectedMode?.id,
      // ... other fields ...
    };
    
    // ... rest of existing code ...
  }
</script>

<!-- Add mode selector above message input -->
<div class="chat-input-area">
  <ModeSelector 
    {selectedMode}
    onModeChange={(mode) => selectedMode = mode}
  />
  <MessageInput onSend={handleSendMessage} />
</div>
```

## Implementation Approach

### Phase 1: Core Infrastructure (2-3 days)
1. Create Mode type definitions in shared types
2. Add user_modes table to SQLite schema
3. Implement ModeService with system mode loading
4. Add mode storage operations

### Phase 2: Backend Integration (2-3 days)
1. Modify ChatService to accept and use modeId
2. Update ChatController with optional mode parameters
3. Add /api/modes endpoints for mode management
4. Implement tool filtering based on mode

### Phase 3: Frontend UI (2-3 days)
1. Create ModeSelector component
2. Integrate selector into ChatWindow
3. Update API calls to include modeId
4. Add mode information display

### Phase 4: Testing & Polish (1-2 days)
1. Add unit tests for ModeService
2. Test mode switching functionality
3. Verify tool filtering works correctly
4. Handle edge cases (missing tools, invalid modes)

## System Modes Configuration

### Default System Modes (Stored as JSON files)

```json
// server/modes/general.json
{
  "id": "general",
  "name": "General Assistant",
  "description": "Default mode with all available tools",
  "category": "task",
  "prompt": "You are a helpful AI assistant...",
  "tools": ["*"],  // Special value meaning all tools
  "defaultModel": null
}

// server/modes/coding.json
{
  "id": "coding",
  "name": "Coding Assistant",
  "description": "Optimized for programming tasks",
  "category": "task",
  "prompt": "You are an expert programmer...",
  "tools": [
    "filesystem",
    "web-search",
    "TaskManager"
  ],
  "defaultModel": "openai/gpt-4"
}

// server/modes/writing.json
{
  "id": "writing",
  "name": "Writing Assistant",
  "description": "Optimized for content creation",
  "category": "task",
  "prompt": "You are a skilled writer and editor...",
  "tools": [
    "web-search",
    "webpage-fetch"
  ],
  "defaultModel": "claude-3"
}
```

## API Endpoints

### Mode Management Endpoints

```
GET    /api/modes              - List all available modes (system + user)
GET    /api/modes/{id}         - Get specific mode details
POST   /api/modes              - Create custom mode
PUT    /api/modes/{id}         - Update custom mode
DELETE /api/modes/{id}         - Delete custom mode
```

### Modified Chat Endpoints

```
POST /api/chat/create
{
  "message": "string",
  "systemPrompt": "string",
  "modeId": "string"  // NEW: Optional
}

POST /api/chat/continue
{
  "chatId": "string",
  "message": "string",
  "modeId": "string"  // NEW: Optional
}
```

## Key Implementation Details

### Tool Filtering Logic

```csharp
public IEnumerable<string> FilterToolsByMode(Mode mode, IEnumerable<string> availableTools)
{
    // Special case: "*" means all tools
    if (mode.Tools.Contains("*"))
        return availableTools;
    
    // Return intersection of mode tools and available tools
    return mode.Tools.Intersect(availableTools, StringComparer.OrdinalIgnoreCase);
}
```

### System Prompt Application

```csharp
// In ChatService.CreateChatAsync or ContinueChatAsync
string effectivePrompt = systemPrompt;

if (!string.IsNullOrEmpty(modeId))
{
    var mode = await _modeService.GetModeByIdAsync(modeId, userId);
    if (mode != null)
    {
        effectivePrompt = mode.Prompt;
        // Apply tool filtering
        // Set model preference if specified
    }
}
```

### Mode Caching Strategy

```csharp
public class ModeService : IModeService
{
    private readonly Dictionary<string, Mode> _systemModes = new();
    private readonly IMemoryCache _cache;
    
    public ModeService(IMemoryCache cache)
    {
        _cache = cache;
        LoadSystemModes();
    }
    
    private void LoadSystemModes()
    {
        var modesPath = Path.Combine(AppContext.BaseDirectory, "modes");
        foreach (var file in Directory.GetFiles(modesPath, "*.json"))
        {
            var json = File.ReadAllText(file);
            var mode = JsonSerializer.Deserialize<Mode>(json);
            mode.IsSystem = true;
            _systemModes[mode.Id] = mode;
        }
    }
}
```

## Benefits of This Design

### Simplicity
- Minimal changes to existing components
- No complex state management
- Straightforward data model
- Easy to understand and maintain

### Flexibility
- Easy to add new system modes (just add JSON files)
- Users can create custom modes through UI
- Tool configuration is declarative
- Can switch modes mid-conversation

### Performance
- System modes cached in memory
- No additional database queries for system modes
- Tool filtering is O(n) operation
- Minimal overhead on chat operations

### Maintainability
- Clear separation of concerns
- Modes are data, not code
- Easy to test in isolation
- No breaking changes to existing APIs

## Migration Path

### Database Migration
```sql
-- Run once to add user_modes table
CREATE TABLE IF NOT EXISTS user_modes (
  Id TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  Name TEXT NOT NULL,
  Description TEXT NOT NULL,
  Prompt TEXT NOT NULL,
  Tools TEXT NOT NULL,
  DefaultModel TEXT NULL,
  Category TEXT DEFAULT 'custom',
  CreatedAt TEXT NOT NULL,
  UpdatedAt TEXT NOT NULL,
  FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE
);

CREATE INDEX idx_user_modes_user ON user_modes (UserId);
```

### Configuration
- Add `server/modes/` directory
- Deploy default mode JSON files
- No changes to appsettings.json required

## Testing Strategy

### Unit Tests
- ModeService CRUD operations
- Tool filtering logic
- System mode loading
- API parameter validation

### Integration Tests
- Mode switching during conversation
- Tool availability with different modes
- Custom mode creation and usage
- Fallback behavior for missing tools

### E2E Tests
- User selects mode and sends message
- Mode switching mid-conversation
- Custom mode creation flow
- Mode persistence across sessions

## Future Enhancements (Not in MVP)

1. **Mode Templates**: Pre-built mode templates users can customize
2. **Mode Sharing**: Share custom modes between users
3. **Mode Analytics**: Track mode usage and effectiveness
4. **Dynamic Tool Loading**: Load tools based on mode at runtime
5. **Mode Versioning**: Track changes to modes over time
6. **Mode Inheritance**: Base modes on other modes

## Conclusion

This design provides the simplest possible implementation of the Chat Modes feature while maintaining clean architecture and extensibility. The approach:

- **Minimizes code changes** to existing components
- **Leverages existing patterns** (JSON config, SQLite storage, FunctionRegistry)
- **Provides clear value** immediately (mode switching, custom modes)
- **Maintains backward compatibility** (all changes are additive)
- **Enables future enhancements** without major refactoring

The total implementation effort is estimated at 8-11 days for a single developer, with the ability to deliver value incrementally starting from Phase 1.