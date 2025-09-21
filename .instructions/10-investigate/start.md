# Investigate/Understand Codebase

When you need to explore, understand, or investigate the codebase.

## 🎯 Start Here

1. **Create scratchpad session**: `scratchpad/investigation-{date}/`
2. **Review project structure**: See [CLAUDE.md](../../CLAUDE.md) overview
3. **Use search tools systematically**

## 🔍 Investigation Methodology

### Step 1: High-Level Understanding
```bash
# Get project structure overview
tree -L 3 -I node_modules

# Find key architectural files
find . -name "*.md" -path "*/docs/*" | head -10
```

### Step 2: Code Exploration
```bash
# Search for specific patterns
rg "pattern" --type cs
rg "class.*Component" client/src/

# Find related implementations
rg "function.*handleMessage" --type ts
```

### Step 3: Architecture Analysis
- **Client**: `client/src/lib/` - Core logic and components
- **Server**: `server/` - ASP.NET backend with Orleans
- **Shared**: `shared/types/` - TypeScript interfaces
- **Messages**: Flow through `MessageRouter.svelte`
- **Real-time**: SignalR hub at `server/Hubs/ChatHub.cs`

### Step 4: State Management
- **Orleans**: `server/AIChat.Orleans/Grains/` - Distributed state
- **Client**: `client/src/lib/stores/` - Svelte stores
- **Persistence**: EF Core entities in `server/Models/`

## 🗂️ Key Files to Understand

### Frontend Architecture
- `client/src/lib/chat/messageHandlers.ts` - Message processing
- `client/src/lib/chat/signalrOrchestrator.ts` - Real-time communication
- `client/src/routes/MessageRouter.svelte` - Message rendering

### Backend Architecture
- `server/Controllers/ChatController.cs` - REST API
- `server/Hubs/ChatHub.cs` - SignalR real-time hub
- `server/AIChat.Orleans/Grains/UserGrain.cs` - Orleans state management

### Shared Contracts
- `shared/types/chat.ts` - Message and state types
- `server/AIChat.Orleans/Contracts/` - Orleans interfaces

## 🧭 Investigation Patterns

### Finding Related Code
```bash
# Find all files related to a feature
rg -l "chatId" --type ts
rg -l "UserGrain" --type cs

# Understand data flow
rg "interface.*Message" shared/
rg "class.*Handler" client/src/
```

### Understanding Integration Points
- **SignalR**: Search for `HubConnection` and `invoke`
- **Orleans**: Search for `IGrain` and `GetGrain`
- **Database**: Search for `DbContext` and `Entity`

## 📋 Investigation Checklist

- [ ] Understand overall architecture
- [ ] Identify key components and their responsibilities
- [ ] Trace data/message flow
- [ ] Understand state management approach
- [ ] Identify integration points
- [ ] Note any patterns or conventions
- [ ] Document findings in scratchpad

## 🔄 Next Steps

After investigation:
- **Implement changes**: [../20-implement/workflow.md](../20-implement/workflow.md)
- **Debug issues**: [../30-debug/methodology.md](../30-debug/methodology.md)
- **Review standards**: [../50-standards/index.md](../50-standards/index.md)

## 📚 Related Resources

- **Architecture docs**: `docs/architecture/`
- **Previous investigations**: `scratchpad/*/analysis.md`
- **Decision records**: `docs/decisions/`