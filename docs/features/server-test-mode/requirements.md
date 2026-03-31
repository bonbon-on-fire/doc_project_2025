# Feature Specification: Server Test Mode

## High-Level overview
- Provide a deterministic, offline-friendly LLM test mode for the server that keeps the existing `IStreamingAgent` but swaps in a Test-only `HttpMessageHandler` to synthesize OpenAI-compatible streaming responses. Focus on simplicity and reproducibility for E2E tests.

## High level Requirements
- Toggle via environment: `ASPNETCORE_ENVIRONMENT=Test` with `appsettings.Test.json`.
- Keep `IStreamingAgent`; inject a Test `HttpMessageHandler` that returns OpenAI SSE-like chat completion chunks.
- Only streaming responses; no non-streaming fallbacks.
- Deterministic outputs derived from the incoming request, not from network calls or cache replays.
- Reasoning support: trigger when user message contains "\nReason: ..." and stream reasoning updates before text.
- Text stream: lorem ipsum sized based on hash of user message, 5–500 words.
- Pacing: ~50 words per second across the stream.
- Echo: append the verbatim user message at the end of both reasoning and text streams (chunked by words); the echo does not count toward lorem length.
- Termination: send a final finish chunk then "[DONE]".
- Test infra: use EF Core InMemory DB in Test, clean state on startup; bind HTTP-only on localhost port 5099 and disable HTTPS redirection.

## Existing Solutions
- Current server uses `IStreamingAgent` (LmDotnetTools OpenAI provider) created in `Program.cs` with `HttpClient` and a `CachingHttpMessageHandler` using `server/llm-cache` for local caching.
- SSE endpoint `POST api/chat/stream-sse` already streams assistant chunks and forwards reasoning updates via a side channel.

## Current Implementation
- `ChatService` orchestrates chat persistence and streaming via `IStreamingAgent`.
- `Program.cs` constructs `HttpClient` with a `CachingHttpMessageHandler` pointing at `./llm-cache`.
- No explicit Test environment setup yet for DB or the HTTP handler.

## Detailed Requirements

### Requirement 1: Environment-based Test Mode Toggle
- **User Story**: As a developer, I can start the server in Test mode via `ASPNETCORE_ENVIRONMENT=Test`, causing the app to use Test settings and the Test `HttpMessageHandler`.

#### Acceptance Criteria:
  1. [x] WHEN `ASPNETCORE_ENVIRONMENT=Test` THEN the server loads `appsettings.Test.json`.
  2. [x] WHEN in Test env THEN the constructed `HttpClient` for LLM uses a Test `HttpMessageHandler` (no outbound HTTP).

### Requirement 2: OpenAI-compatible Streaming Synthesis
- **User Story**: As a test runner, I receive OpenAI-compatible SSE chat completion chunks that the existing provider can parse without changes.

#### Acceptance Criteria:
  1. [x] WHEN a streaming request is made THEN chunks use `object: "chat.completion.chunk"` with `choices[0].delta.content` for text and `choices[0].delta.reasoning` for reasoning.
  2. [x] [DONE] is sent as the final line after a finish chunk (`finish_reason: "stop"`).
  3. [x] No non-streaming fallback calls occur in Test mode.

### Requirement 3: Reasoning Trigger and Ordering
- **User Story**: As a tester, I can request reasoning by adding "\nReason: ..." to a user message, and see reasoning streamed before text.

#### Acceptance Criteria:
  1. [x] WHEN the latest user message contains "\nReason:" THEN the handler streams reasoning updates first (via `delta.reasoning`) before any text chunks.
  2. [x] Reasoning visibility is treated as visible (no encryption) for test-mode streams.
  3. [x] The reasoning content reflects the text following "Reason:" (may be chunked by words and paced).

### Requirement 4: Text Generation and Pacing
- **User Story**: As a tester, I see lorem ipsum output with deterministic length and pacing.

#### Acceptance Criteria:
  1. [x] WHEN streaming text THEN total lorem ipsum word count is between 5 and 500 determined by a stable hash of the user message.
  2. [x] Chunks are emitted at ~50 words per second overall, with simple word-based chunking.
  3. [x] Chunk sizes may vary (e.g., 3–10 words per chunk) but maintain the aggregate pacing.

### Requirement 5: Echo of Incoming User Message
- **User Story**: As a tester, the full user message is echoed at the end of both streams for easier validation.

#### Acceptance Criteria:
  1. [x] AFTER lorem/Reasoning streaming completes THEN the verbatim user message is appended to each stream (reasoning and text), chunked by words.
  2. [x] The echoed content does not count toward the lorem length computation.

### Requirement 6: Termination Semantics
- **User Story**: As a client, I can reliably detect stream completion.

#### Acceptance Criteria:
  1. [x] A proper finish chunk with `finish_reason: "stop"` is sent before the terminator.
  2. [x] The stream ends with a line containing only `[DONE]`.

### Requirement 7: Database and Networking in Test
- **User Story**: As a test operator, I want isolation from dev runs.

#### Acceptance Criteria:
  1. [x] In Test env the server uses EF Core InMemory provider and ensures a clean DB on startup.
  2. [x] The server binds to HTTP on localhost port 5099 and disables HTTPS redirection.

### Requirement 8: Cancellation Handling (optional test)
- **User Story**: As a tester, I can simulate cancellation mid-stream when desired.

#### Acceptance Criteria:
  1. [ ] By default, the stream runs to completion and persisted assistant content matches the streamed text.
  2. [ ] A test hook or environment flag can simulate mid-stream cancellation; behavior (persist partial or not) is documented and deterministic.

### Requirement 9: Instruction-driven Response Mode
- **User Story**: As a tester, I can include an instruction block in the user message to deterministically control the sequence and content types the test handler streams.

#### Acceptance Criteria:
  1. [x] WHEN the latest user message contains a block delimited by `<|instruction_start|>` and `<|instruction_end|>` THEN only JSON inside that block is parsed; text outside is ignored for generation.
  2. [x] The JSON structure supports:
   - `id_message` string included at the start and end of both reasoning and text streams for each message, not counted toward any `length`.
   - Optional `reasoning.length` number used as the lorem word count for reasoning per message when present.
   - `messages` array where each item is either a `text_message` with `{ length }` or a `tool_call` array with `name` and `args`.
  3. [x] FOR EACH `messages[i]` an independent stream is emitted with `choices[0].index = i`.
  4. [x] FOR `text_message` items, if `reasoning.length` is present, stream reasoning first, then text; lengths exclude the `id_message` contribution.
  5. [x] FOR `tool_call` items, emit OpenAI-compatible `delta.tool_calls` updates with correct indices and progressive emission of `name` and `args`.
  6. [x] IF the instruction block is absent or invalid JSON, fall back to legacy Test behavior (hash-based lorem with optional `\nReason:` trigger).

## References
- Code: `server/Program.cs`, `server/Services/ChatService.cs`, `server/Controllers/ChatController.cs`, `server/llm-cache`.
- LmDotnetTools message types: `submodules/LmDotnetTools/src/LmCore/Messages`.
