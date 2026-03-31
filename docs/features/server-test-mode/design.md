# Design: Server Test Mode (Minimal)

## Goals
- Deterministic, offline-friendly LLM behavior in Test env.
- Minimal changes to existing architecture.
- Keep `IStreamingAgent` and OpenAI provider intact; synthesize SSE via a Test-only `HttpMessageHandler`.
- Simple, readable code paths; few moving parts.

## Non-goals
- No provider replacement or deep refactors.
- No non-streaming completions in Test mode.
- No cache replays from `server/llm-cache`.

## Toggle and Config
- Trigger via `ASPNETCORE_ENVIRONMENT=Test`.
- Add `appsettings.Test.json`:
  - `Urls`: `http://localhost:5099`
  - `LlmCache:EnableCaching`: `false`
  - Optional logging overrides.
- Disable HTTPS redirection in Test.

## Minimal Changes Overview
1) DB provider:
   - In Test env use EF Core InMemory database.
   - Ensure clean DB at startup (EnsureDeleted + EnsureCreated).
2) HTTP pipeline for LLM:
   - In Test env, inject `TestSseMessageHandler` into the existing `HttpClient` stack used by `OpenClientAgent`.
   - Keep the rest of DI and `IStreamingAgent` wiring unchanged.
3) Networking:
   - Bind server to `http://localhost:5099` in Test.

## Components

### TestSseMessageHandler (new)
- Type: `HttpMessageHandler` (or `DelegatingHandler`).
- Behavior:
  - Intercept OpenAI-style chat completion streaming requests (POST `/v1/chat/completions` with stream=true).
  - Parse request JSON minimally to extract:
    - `model` (optional; default if absent)
    - latest user message content (string); detect `"\nReason:"` tail to enable reasoning.
  - Return `HttpResponseMessage` with `SseStreamHttpContent` that writes OpenAI-compatible SSE chunks.
  - For any non-targeted requests, either delegate to inner handler (if present) or return a minimal 404-style response (keeps code simple). In Test we won’t hit those.

### SseStreamHttpContent (new)
- Subclass of `HttpContent` that overrides `SerializeToStreamAsync` to:
  - Emit SSE lines with `data: {...}\n\n` using UTF-8.
  - Chunking by words; flush after each chunk.
  - Pacing: ~50 words/second by emitting 5-word chunks every ~100ms.
  - Ordering: if `Reason:` present, stream reasoning deltas first, then text deltas.
  - Echo: after each stream (reasoning, then text), append the full user message verbatim (including Reason block) as additional deltas, chunked by words.
  - Termination: write a finish chunk (`finish_reason: "stop"`), then `data: [DONE]\n\n`.

## OpenAI-compatible SSE Schema
- Each chunk:
  - `{"id":"gen-<ts>-<rand>","object":"chat.completion.chunk","model":"<model>","created":<unix>,"choices":[{"index":0,"delta":{"content":"<words>"}}]}` for text
  - `{"...","choices":[{"index":0,"delta":{"reasoning":"<words>"}}]}` for reasoning
- Finish chunk:
  - `{"...","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}`
- Terminator:
  - `data: [DONE]`

## Request Parsing (minimal)
- Use `System.Text.Json` to parse body.
- Find latest user message text; if not strictly OpenAI schema (e.g., content array), handle only the simple string `content` case for Test simplicity.
- Extract `Reason:` tail via string search for `"\nReason:"`.

## Instruction-driven Response Mode (new)
- Detection: If the user message contains a block delimited by `<|instruction_start|>` and `<|instruction_end|>`, only parse JSON inside that block. Ignore text outside.
- JSON structure:
  - `id_message` (string): included verbatim at the start and end of both reasoning and text streams for each message. Its length does not count toward configured lengths.
  - `reasoning.length` (number): target lorem word count for reasoning stream per message when present.
  - `messages[]` (array): each entry is either:
    - `{ "text_message": { "length": number } }` to stream text; OR
    - `{ "tool_call": [{ "name": string, "args": object }, ...] }` to stream tool calls.
- Behavior when instruction is present and valid:
  - For each `messages[i]` in order, emit a distinct choice stream identified by `choices[0].index = i`.
  - Text message:
    - If top-level `reasoning.length` is provided, stream reasoning first (lorem of that many words), then text (lorem of `text_message.length`).
    - Prepend and append `id_message` around both reasoning and text substreams. Do not include `id_message` in length calculations.
  - Tool call message:
    - Stream OpenAI-compatible `delta.tool_calls` updates with proper per-item indices, progressively emitting `name` and serialized `args` fields.
  - After all messages complete, emit a single finish chunk (`finish_reason: "stop"`) and then `data: [DONE]`.
- Fallback: If no instruction block is present or JSON is invalid, use the existing minimal behavior (hash-based lorem length, optional reasoning via "\nReason:").

Implementation notes:
- Parsing is implemented in `TestSseMessageHandler` via `TryParseInstructionPlan`, which safely extracts `id_message`, optional `reasoning.length`, and `messages[]` entries.
- Streaming is implemented in `SseStreamHttpContent.SerializeInstructionPlanAsync`:
  - Emits `id_message` before/after both reasoning and text; these markers do not count toward configured lengths.
  - Emits `delta.tool_calls` entries with per-call `index`, progressive `function.name` and `function.arguments` updates.
  - Uses `choices[0].index = i` to segregate parallel message streams.

## Lorem Generation (deterministic)
- Source words: fixed lorem list repeated as needed.
- Compute target word count: `5 + (abs(stableHash(userMessage)) % 496)`.
- Chunk sizes: 5 words each; last chunk smaller if needed.
- Pacing: delay ~100ms per chunk to target 50 wps.

## Program.cs Changes (minimal)
- Test env DB:
  - `builder.Services.AddDbContext<AIChatDbContext>(o => o.UseInMemoryDatabase("AIChatTestDb"));`
  - After `app.Build()`, in Test: `EnsureDeleted()` then `EnsureCreated()`.
- Test env HTTP:
  - Skip `app.UseHttpsRedirection()`.
  - Respect `Urls` from `appsettings.Test.json`.
- Test env LLM HttpClient:
  - Construct `TestSseMessageHandler`.
  - If caching is disabled (from Test settings), pass the test handler directly to `HttpClient`.
  - Otherwise, place it as the inner-most handler under the cache handler.

## Error Handling & Logging
- Minimal: log one-line diagnostics when the test handler intercepts a request and which mode (reasoning/text) is produced.
- Avoid complex retries; Test mode should never reach network.

## Edge Cases
- No `Reason:` in message: skip reasoning stream; only text + echo.
- Very short messages: still produce at least 5 lorem words; echo remains verbatim.
- Cancellation: default to full completion; if upstream cancels token, stop writing immediately.

## Reference Data Sources
- OpenAI-compatible chunk examples (SSE packed into JSON strings) under `server/llm-cache/`, e.g.:
  - `server/llm-cache/0bb936a0b9a08c20d5b4f2b2a1090fcfd2dc27d77fd74461513be62c591ebc3e.json`
  - `server/llm-cache/90f3fc0b3811e7cc7ae342bedd2f38a171eaf3031fea2ebc628a6a52b55d8fff.json`
  - `server/llm-cache/5247cf0a156839b39f8a761ad946d42f93d6ecd34ef48363a929642a37854b37.json`
- SSE streaming infra and examples in submodule docs:
  - `submodules/LmDotnetTools/src/docs/provider-modernization-tracking.md` (SSE file support, event format)

## Streaming Implementation References (code to emulate)
- Submodule test utilities showing SSE streaming constructs:
  - `submodules/LmDotnetTools/src/LmTestUtils/MockHttpHandlerBuilder.cs`:
    - `SseFileStream` (streaming SSE implementation)
    - `StreamingSequenceResponseProvider` (programmatic SSE sequence)
    - `StreamingFileResponseProvider` (file-based SSE)
- Submodule tests demonstrating integration with OpenAI endpoints:
  - `tests/OpenAIProvider.Tests/Agents/*` using `GenerateReplyStreamingAsync`
  - `tests/LmTestUtils.Tests/MockHttpHandlerBuilderTests.cs` (RespondWithStreamingFile)
- These give patterns for chunk writing, headers (`text/event-stream`), flushing, and delays.

## Future Extension
- General tool-call detection outside of instruction blocks (e.g., via free-form markers) remains out of scope.
- Instruction-driven tool calls are supported as described above; broader natural-language extraction is a possible future enhancement.

## Acceptance Alignment
- Matches `docs/features/server-test-mode/requirements.md` on: env toggle, HTTP 5099, InMemory DB clean start, SSE shape, pacing, ordering, echo, and terminator.
