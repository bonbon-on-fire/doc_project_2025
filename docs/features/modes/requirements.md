# Feature Specification: Chat Modes

## High-Level Overview

The Chat Modes feature provides users with the ability to switch between different AI personalities and tool configurations to optimize their chat experience for specific tasks or workflows. Each mode consists of a system prompt and a curated set of tools, allowing users to quickly adapt the AI assistant's behavior and capabilities to their current needs.

## High-Level Requirements

### Core Functionality
- Users can select from predefined system modes or create their own custom modes
- Each mode defines a specific prompt/instructions and a set of available tools
- Modes can be switched mid-conversation without losing conversation history
- System provides both task-specific modes (e.g., Coding Assistant) and role-based modes (e.g., Developer, Product Manager)

### Architecture Requirements
- Clean, loosely coupled architecture separating mode management from chat logic
- Hybrid storage approach: system modes in configuration files, user modes in database
- Integration with existing MCP tool system and function registry
- Support for mode-specific model selection (each mode can have a default LLM model)

## Existing Solutions

### Current System Components
- **MCP Integration**: McpClientManager handles external tool providers
- **Function Registry**: Dynamic tool registration system in ChatService
- **Chat Architecture**: REST API for persistence, SignalR/SSE for real-time updates
- **Agent System**: Existing `.claude/agents/` structure with YAML frontmatter (name, description, model, color)

### Related Patterns in Codebase
- Developer-facing chat modes in `.github/chatmodes/` (not exposed to users)
- LmDotnetTools middleware system for message processing
- Task-specific function providers (WeatherFunction, TaskManager)

## Current Implementation

### Components Involved
1. **Backend Services**
   - `ChatService`: Handles chat operations and tool registration
   - `McpClientManager`: Manages MCP tool connections
   - `ChatController`: REST API endpoints for chat operations
   
2. **Frontend Components**
   - `ChatInterface.svelte`: Main chat UI container
   - `ChatWindow.svelte`: Message display and input
   - `ChatSidebar.svelte`: Chat history navigation

3. **Data Models**
   - Chat and Message types in `shared/types/chat.ts`
   - No current mode-specific data structures

## Detailed Requirements

### Requirement 1: Mode Definition and Storage
**User Story**: As a system administrator, I want to define reusable chat modes with specific prompts and tool sets.

#### Acceptance Criteria:
1. [ ] WHEN defining a system mode THEN it SHALL be stored as a YAML/JSON file in a designated directory
2. [ ] WHEN a mode is defined THEN it SHALL include: name, description, prompt, tools array, and default model
3. [ ] WHEN loading system modes THEN they SHALL be read from the filesystem at application startup
4. [ ] WHEN a user creates a custom mode THEN it SHALL be stored in the database

### Requirement 2: Mode Selection UI
**User Story**: As a user, I want to easily select and switch between different chat modes during my conversation.

#### Acceptance Criteria:
1. [ ] WHEN using the chat interface THEN a mode selector dropdown SHALL appear below the text input box
2. [ ] WHEN clicking the dropdown THEN it SHALL display all available modes (system + user-created)
3. [ ] WHEN selecting a mode THEN it SHALL immediately apply to subsequent messages
4. [ ] WHEN a mode has unavailable tools THEN a warning SHALL be displayed but selection allowed

### Requirement 3: Mode Switching Behavior
**User Story**: As a user, I want to switch modes mid-conversation to adapt the AI's behavior to my changing needs.

#### Acceptance Criteria:
1. [ ] WHEN switching modes mid-conversation THEN the conversation history SHALL remain visible
2. [ ] WHEN a new message is sent THEN it SHALL use the currently selected mode's prompt and tools
3. [ ] WHEN viewing conversation history THEN all messages SHALL be displayed regardless of mode used
4. [ ] WHEN a mode's required tools are unavailable THEN the system SHALL gracefully degrade functionality

### Requirement 4: Custom Mode Creation
**User Story**: As a power user, I want to create and manage my own custom modes tailored to my specific workflows.

#### Acceptance Criteria:
1. [ ] WHEN creating a custom mode THEN I SHALL have full control over the prompt text
2. [ ] WHEN selecting tools THEN I SHALL be able to choose from all available tools
3. [ ] WHEN editing a mode THEN a simple form-based editor SHALL be provided
4. [ ] WHEN saving a custom mode THEN it SHALL be stored in the database with user association

### Requirement 5: Mode-Tool Integration
**User Story**: As a user, I want modes to automatically configure the appropriate tools for my task.

#### Acceptance Criteria:
1. [ ] WHEN a mode specifies tools THEN only those tools SHALL be available during chat
2. [ ] WHEN tools are unavailable THEN they SHALL be filtered from the mode's active tool set
3. [ ] WHEN MCP clients are connected THEN their tools SHALL be available for mode selection
4. [ ] WHEN tool availability changes THEN modes SHALL adapt without breaking

### Requirement 6: API Integration
**User Story**: As a developer, I want to integrate mode selection into the chat API seamlessly.

#### Acceptance Criteria:
1. [ ] WHEN creating a chat THEN the request SHALL accept an optional modeId parameter
2. [ ] WHEN continuing a chat THEN the request SHALL accept an optional modeId to switch modes
3. [ ] WHEN a mode is selected THEN it SHALL be included in the chat session state
4. [ ] WHEN retrieving chat history THEN mode information SHALL be included if relevant

### Requirement 7: Default Modes
**User Story**: As a user, I want access to useful predefined modes without configuration.

#### Acceptance Criteria:
1. [ ] WHEN the system starts THEN it SHALL load predefined task-specific modes (Coding, Writing, Research, etc.)
2. [ ] WHEN the system starts THEN it SHALL load predefined role-based modes (Developer, Product Manager, etc.)
3. [ ] WHEN no mode is selected THEN a default "General" mode SHALL be used
4. [ ] WHEN system modes are updated THEN they SHALL be reloaded without restart (development mode)

### Requirement 8: Mode Metadata
**User Story**: As a user, I want to understand what each mode does before selecting it.

#### Acceptance Criteria:
1. [ ] WHEN viewing modes THEN each SHALL display: name, description, available tools list
2. [ ] WHEN a mode has a default model THEN it SHALL be indicated in the mode information
3. [ ] WHEN hovering over a mode THEN additional details SHALL be displayed in a tooltip
4. [ ] WHEN modes are listed THEN they SHALL be organized by category (task/role/custom)

## Technical Design Considerations

### Data Model
```typescript
interface Mode {
  id: string;
  name: string;
  description: string;
  prompt: string;
  tools: string[];
  defaultModel?: string;
  isSystem: boolean;
  userId?: string; // For custom modes
  createdAt: Date;
  updatedAt: Date;
}
```

### Storage Structure
- System modes: `/server/modes/*.yaml` or `/config/modes/*.yaml`
- User modes: Database table `UserModes`
- Mode format compatible with existing `.claude/agents/` structure

### API Changes
```typescript
// Update existing interfaces
interface CreateChatRequest {
  message: string;
  systemPrompt?: string;
  modeId?: string; // New field
}

interface ContinueChatRequest {
  chatId: string;
  message: string;
  modeId?: string; // New field for mode switching
}
```

### UI Components
- New `ModeSelector.svelte` component
- Integration point: Below message input in `ChatWindow.svelte`
- Simple form-based `ModeEditor.svelte` for custom mode management

## Future Considerations

### Phase 2 Enhancements
- Mode sharing between users
- Mode versioning and history
- Mode templates and inheritance (if requirements change)
- Analytics on mode usage
- Mode-specific conversation branching

### Performance Optimizations
- Lazy loading of mode configurations
- Caching of frequently used modes
- Tool availability checking optimization

## Success Metrics
- Users can switch between modes seamlessly
- Custom mode creation is intuitive and reliable
- System maintains backward compatibility
- Mode switching does not impact chat performance
- Tool integration works reliably with modes