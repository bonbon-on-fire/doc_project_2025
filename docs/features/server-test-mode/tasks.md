# Tasks: Server Test Mode (Condensed)

- [x] Task 1: Test environment plumbing (server)
  - [x] Add `server/appsettings.Test.json`:
    - [x] `"Urls": "http://localhost:5099"`
    - [x] `"LlmCache": { "EnableCaching": false }`
  - [x] In `Program.cs` when `Environment.IsEnvironment("Test")`:
    - [x] Use EF InMemory: `UseInMemoryDatabase("AIChatTestDb")`
    - [x] On startup: `EnsureDeleted()` then `EnsureCreated()` (clean DB)
    - [x] Skip `app.UseHttpsRedirection()` (HTTP-only)
    - [x] Respect `Urls` from Test settings (bind to 5099)
    - [x] CORS: ensure client origins (5173/4173) work with 5099
  - Requirements:
    - [x] R1.1 (env-based toggle)
    - [x] R7.1 (InMemory + clean)
    - [x] R7.2 (HTTP-only, 5099)
  - Tests:
    - [x] Server starts on `http://localhost:5099` in Test
    - [x] DB is empty at startup

- [x] Task 2: Test SSE engine (no network)
  - [x] Implement `TestSseMessageHandler` (e.g., `server/Services/TestMode/TestSseMessageHandler.cs`):
    - [x] Intercept `POST /v1/chat/completions` with `stream=true`
    - [x] Parse minimal OpenAI-like request to get latest user message and (optional) `model`
    - [x] Detect `"\nReason:"` → enable reasoning-first stream
    - [x] Return `HttpResponseMessage` with `text/event-stream`
  - [x] Implement `SseStreamHttpContent`:
    - [x] Emit OpenAI-compatible `chat.completion.chunk` SSE JSON lines
    - [x] Text: lorem ipsum length in [5, 500] via stable hash of user message
    - [x] Pacing: ~50 words/sec (e.g., ~5 words/100ms chunks)
    - [x] Echo user message verbatim at end of both reasoning and text streams (chunked); echo not counted toward lorem length
    - [x] Send finish chunk (`finish_reason: "stop"`), then `data: [DONE]`
  - Requirements:
    - [x] R2.1–R2.2 (schema + terminator)
    - [x] R3.1–R3.3 (reasoning-trigger + ordering)
    - [x] R4.1–R4.3 (length + pacing + chunking)
    - [x] R5.1–R5.2 (echo behavior)
    - [x] R6.1–R6.2 (termination)
  - Tests:
    - [x] Unit: chunk shape valid; finish chunk + `[DONE]`
    - [x] Unit: lorem count in range; echo excluded from count
    - [x] Unit: reasoning-first when requested
    - [x] Unit: Response headers include `Content-Type: text/event-stream`
    - [x] Unit: SSE formatting uses `data: ...\n\n` per chunk and flushes incrementally (more than one chunk before completion)
    - [x] Unit: Non-targeted requests (wrong path or no `stream=true`) return 404 (simple behavior)
    - [x] Unit: `id` and `created` fields present per chunk; `model` echoed if provided
    - [x] Unit: Request parsing handles simple `messages[{role:"user", content:"..."}]` and ignores complex content arrays (documented simplicity)

- [x] Task 3: Wire handler into existing OpenAI agent (DI)
  - [x] In `Program.cs` when Test env:
    - [x] Build `HttpClient` with `TestSseMessageHandler` (no caching)
    - [x] Bypass API key requirement (no throw in Test)
    - [x] Keep `OpenClientAgent` and `IStreamingAgent` registration unchanged otherwise
  - Requirements:
    - [x] R1.2 (use handler, no outbound HTTP)
  - Tests:
    - [x] Integration: `POST /api/chat/stream-sse` returns streamed chunks parsed by provider

- [ ] Task 4: Client wiring to Test backend (5099)
  - [x] Create `client/.env.test` with `PUBLIC_API_BASE_URL=http://localhost:5099`
  - [x] Ensure `client/src/lib/api/client.ts` and `client/src/lib/api/sse-client.ts` honor `PUBLIC_API_BASE_URL` (avoid hardcoded 5130 in test runs)
  - [ ] Update any remaining hardcoded base URLs (e.g., `client/src/lib/api.ts`) to use env or a single API client
  - [x] Start client in test mode (e.g., `npm run dev -- --mode test`) or set env in Playwright
  - [x] Verify CORS/SSE from client origin to `http://localhost:5099`
  - Requirements:
    - [x] Client can reach Test backend; SSE flows
  - Tests:
    - [x] E2E: client connects to `http://localhost:5099` and receives streaming chunks

- [ ] Task 5: Tests and docs
  - [x] Integration tests (fast):
    - [x] No reasoning → only text stream, `[DONE]` present
    - [x] With reasoning → reasoning-first, then text; both end with echo
    - [x] Pacing tolerance (non-flaky)
    - [ ] Optional: simulate cancellation scenario (document behavior)
  - [ ] Documentation:
    - [x] Link tasks to `requirements.md`
    - [x] Add run instructions for Test mode (env var, ports, HTTP-only)
  - Requirements:
    - [x] R2–R6 (behavior), R8 optional, R1 discoverability

- [ ] Task 6: Instruction-driven responses (server)
  - [x] Update `TestSseMessageHandler` to detect `<|instruction_start|> ... <|instruction_end|>` in latest user message and parse JSON only within the block.
  - [x] Support `id_message`, optional top-level `reasoning.length`, and `messages[]` entries of two kinds:
    - [x] `text_message.length`: stream reasoning-first if configured, then text; wrap both with `id_message` pre/post without counting toward lengths.
    - [x] `tool_call[]`: stream OpenAI-compatible `delta.tool_calls` with indices and progressive `name`/`args`.
  - [x] Emit distinct `choices[0].index` per message entry and a single finish chunk then `[DONE]` at the end.
  - [x] Fallback to legacy behavior when no instruction block or invalid JSON.
  - Requirements:
    - [x] R9.1–R9.6 (instruction-driven mode)
  - Tests:
    - [x] Unit: parses instruction block; ignores outside text.
    - [x] Unit: multiple messages produce distinct indexed streams.
    - [x] Unit: `text_message` lengths honored; `id_message` excluded from counts.
    - [ ] Unit: `tool_call` deltas shape and indexing. (Deferred — test skipped)
    - [x] Unit: fallback path when instruction missing/invalid.

## Reference Samples (format guidance)

- Use these captured responses to match OpenAI-compatible `chat.completion.chunk` shapes and `[DONE]` termination:

  - `server/llm-cache/0bb936a0b9a08c20d5b4f2b2a1090fcfd2dc27d77fd74461513be62c591ebc3e.json`
  - `server/llm-cache/90f3fc0b3811e7cc7ae342bedd2f38a171eaf3031fea2ebc628a6a52b55d8fff.json`
  - `server/llm-cache/5247cf0a156839b39f8a761ad946d42f93d6ecd34ef48363a929642a37854b37.json`
  - `server/llm-cache/641a945d6fa53d09a064b8800992c126534996282a32b7fb4e5ab334d5a9939d.json`
  - `server/llm-cache/80a5c2bc932493fb05727662ba9e1a61e37f29278fef5ae11222437b3f02cc76.json`
