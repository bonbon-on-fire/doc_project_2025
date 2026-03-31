# User Feedback and Learnings: Server Test Mode

- Pending: Capture decisions on toggle mechanism, data sources (cache vs generated), and chunk pacing.

Decisions so far:
- Toggle via environment `ASPNETCORE_ENVIRONMENT=Test` with `appsettings.Test.json`; CLI env override is acceptable.
- Keep `IStreamingAgent`; inject a custom HttpMessageHandler in Test env.
- Rule-based generation only; no cache replays from `server/llm-cache`.
- Reasoning: when user message contains "\nReason: ..." stream reasoning updates (OpenAI-compatible), visibility visible; future: tool call streaming.
- Text: stream lorem ipsum sized by hash of user message (5–500 words total).
- SSE compatibility: emulate OpenAI chat completion chunk schema so the existing provider parses it.
- Pacing: 50 words per second target across streaming.
- Echo: append the incoming user message at the end of both reasoning and text streams, chunked by words.
- Echo does NOT count toward the 5–500 lorem word total.
- Echo content: include the full user message verbatim (including any "Reason:" block) for simplicity.
- Termination: send final finish chunk and then "[DONE]" to end SSE.
- Cancellation: by default, complete the stream and persist content; only simulate mid-stream cancel when explicitly testing that scenario.
- Simplicity principle: prefer the simplest parsing and emission logic that meets requirements.
- Test infra: when running automated tests, use EF Core in-memory database and bind server to dedicated Test ports via `appsettings.Test.json` to avoid conflicts with the dev server.
- Networking: HTTP-only on localhost in Test; disable HTTPS redirection.
- Test ports: HTTP 5099 (no HTTPS) for the API/SSE.
- DB: Ensure clean state on Test startup (e.g., EnsureDeleted/EnsureCreated for InMemory provider).
