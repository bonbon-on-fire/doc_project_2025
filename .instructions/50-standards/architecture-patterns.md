# Architecture Patterns and Implementation Notes

## Technology Stack

- **Frontend**: SvelteKit 2.22 + Svelte 5.0, TypeScript 5.0, Tailwind CSS 4.0
- **Backend**: ASP.NET 9.0 Web API, Entity Framework Core, SignalR
- **Database**: SQLite (dev), client-side Drizzle ORM + server-side EF Core
- **AI Integration**: AchieveAi.LmDotnetTools suite with OpenAI provider
  - See `docs/LmDotNet-doc.md` for detailed middleware, provider, MCP, and caching reference
- **Real-time**: SignalR + Server-Sent Events (SSE)
- **Testing**: Playwright (E2E), Vitest (unit), xUnit (server)

## Project Structure

```
├── client/          # SvelteKit frontend application
├── server/          # ASP.NET 9.0 Web API backend  
├── server.Tests/    # Backend unit tests
├── shared/          # Shared TypeScript types
├── docs/           # Comprehensive documentation
├── scratchpad/     # Development notes and planning
├── submodules/     # LmDotnetTools AI library
└── .repo-instructions/  # Development guidelines
```

## Key Architectural Patterns

### Message Rendering System
- Extensible renderer registry in `client/src/lib/renderers/`
- Dynamic component loading via `MessageRouter.svelte`
- Support for text, reasoning, and custom message types
- Collapsible message states managed via stores

### Real-time Communication
- SignalR for bidirectional real-time messaging
- Server-Sent Events (SSE) for structured streaming at `/api/chat/stream-sse`
- Unified API endpoints in `ChatController` for state changes
- Message sequencing and ordering via `MessageSequenceService`

### State Management
- Svelte stores for UI state (`messageState.ts`, `chat.ts`)
- EF Core with SQLite for server-side persistence
- File-based LLM response caching in `./llm-cache`

## Implementation Guidelines

### Message Types & Rendering
- All messages flow through `MessageRouter.svelte` for dynamic rendering
- Renderer registry supports extensible message types beyond text/reasoning
- Use `RichMessageDto` interface for message payloads
- Stream completion detection via `▋` character presence

### API Design
- REST endpoints for persistent operations (`ChatController`)
- SignalR (`ChatHub`) for real-time broadcasts only
- SSE provides structured JSON events with proper error handling
- Message sequencing ensures proper ordering across clients

### Database Schema
- **Chat entity**: Id (string), Title, UserId, CreatedAt/UpdatedAt
- **Message entity**: Id, Content, ChatId, IsFromUser, Timestamp
- **User entity**: Id, Username, Email, PasswordHash

### Error Handling
- Comprehensive error boundaries in React-style patterns
- Fallback to `MessageBubble` component on renderer errors
- Structured error events in SSE responses
- Proper logging with structured data

## Environment Configuration

### Required Environment Variables
- `LLM_API_KEY` - API key for LLM provider (OpenRouter/OpenAI)
- `LLM_BASE_API_URL` - Base URL for LLM provider (optional)
- `ASPNETCORE_ENVIRONMENT` - Set to "Test" for test mode

### Key Configuration Files
- `client/.env.local` - Frontend environment variables
- `server/appsettings.Development.json` - Server development settings
- `server/appsettings.Test.json` - Server test configuration

## Development Rules

1. **Type Safety**: Always use shared TypeScript interfaces from `shared/types/`
2. **Hot Reload**: Use `dotnet watch run` and `npm run dev` for development
3. **Testing**: Run both unit and E2E tests before major changes
4. **Database**: Schema changes require both client (Drizzle) and server (EF) updates
5. **Message Rendering**: Add new message types via renderer registry, not hardcoded components
6. **Real-time**: Use REST for persistence, SignalR/SSE for real-time updates only

## Testing Strategy

### End-to-End Tests
- Located in `client/e2e/`
- Run with `npm run test:e2e` 
- Use Playwright for browser automation
- Test SSE flow, chat functionality, message ordering

### Unit Tests
- Client: `client/src/lib/**/*.test.ts` with Vitest
- Server: `server.Tests/` with xUnit and FluentAssertions
- Component tests for renderers and message handling

### Test Environment
- HTTP-only server on port 5099
- In-memory database reset on each run
- Disabled LLM cache for isolated testing