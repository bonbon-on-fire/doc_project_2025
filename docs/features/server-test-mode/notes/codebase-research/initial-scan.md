# Codebase Research: Initial Scan for Server Test Mode

Key components touching LLM and streaming:
- `server/Services/ChatService.cs`: Central service orchestrating chats, persistence, and streaming. Uses DI-injected `IStreamingAgent` from LmDotnetTools. Emits events for stream chunk, completion, and reasoning side-channel.
- `server/Controllers/ChatController.cs`: Provides REST and SSE endpoint `POST api/chat/stream-sse` that forwards chunks via SSE and side-channel events.
- `server/Hubs/ChatHub.cs`: SignalR hub wiring to service events (not used for SSE path).
- `server/Models/AiOptions.cs`: Only exposes `ModelId`. No explicit test-mode flag.
- `server/appsettings.Development.json`: No LLM provider config here (likely in user-secrets/ env). No test-mode settings.
- `server/llm-cache/*.json`: Many cached provider responses (SSE-like content strings). Potential source for deterministic test fixtures.

Injection points for Test Mode:
- Swap the DI registration for `IStreamingAgent` with a `TestStreamingAgent` that:
  - Emits text and reasoning chunks deterministically based on the latest user message.
  - For future: emits tool-call messages based on specially formatted inputs.
  - Optionally replay from `server/llm-cache/*.json` when a matching key is available.
- Keep persistence and events untouched to exercise end-to-end streaming and storage.

Streaming contract expectations:
- Only streaming responses are supported for test mode (no non-streaming fallbacks).
- Reasoning side-channel exists via `ReasoningChunkReceived` and `SideChannelReceived`.

Open questions:
- How to toggle test mode (env var, appsettings.Test.json, command-line, or DI profile)?
- Should test mode read from `server/llm-cache` by filename convention or a mapping key?
- Constraints for chunk timings (instant vs paced) for e2e stability.
