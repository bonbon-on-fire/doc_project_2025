# LmDotNet Components and Middleware (Submodules)

This document explains the LmDotnetTools submodule found at `submodules/LmDotnetTools`. It focuses on core abstractions and all middleware in the Core, OpenAIProvider, Misc, and MCP-related projects, plus the key utilities you’ll use to build and compose agents and pipelines. It’s intended to be coding-oriented: after reading, you should know what to instantiate, how to wire it, and when to use each piece.

- Source root: `submodules/LmDotnetTools/src`
  - Core: `LmCore/`
  - OpenAI: `OpenAIProvider/`
  - MCP: `McpMiddleware/`, `McpSampleServer/`, `McpServers/`
  - Misc: `Misc/`
  - Config + HTTP: `LmConfig/`
  - Embeddings: `LmEmbeddings/`
  - Test utils: `LmTestUtils/`


## Architecture Overview

- Core model: LLM “agents” implement either `IAgent` (batched replies) or `IStreamingAgent` (async streams of messages). Middleware wraps an agent to intercept requests/responses.
- Messages: Transport-neutral types in `LmCore/Messages` represent content and tool calls/updates/results, plus usage and reasoning updates.
- Options: `GenerateReplyOptions` carries model selection, sampling params, tools/functions, and provider-specific `ExtraProperties`. Options are immutable records with a `.Merge` method for safe override.
- Middleware: Components that implement `IMiddleware` or `IStreamingMiddleware` (superset) and are composed around an agent using extension methods like `agent.WithMiddleware(...)`.
- Provider adapters: OpenAI-compatible agents (`OpenClientAgent`) and helpers translate core messages/options to provider wire models and back.
- HTTP pipeline: `LmConfig/Http` provides `IHttpHandlerBuilder` and factory helpers; `Misc/Http` contributes wrappers like a caching handler you can attach to the pipeline.
- MCP: Bridges LLM function-calls to external tools via Model Context Protocol clients and server tool registrations, producing `FunctionContract` and runtime function maps.


## Core Project (LmCore)

### Agents and Options
- `IAgent`, `IStreamingAgent`: Core agent contracts. You call `GenerateReplyAsync` or `GenerateReplyStreamingAsync` with `IEnumerable<IMessage>` and optional `GenerateReplyOptions`.
- `GenerateReplyOptions` (file: `LmCore/Core/GenertaeReplyOptions.cs`):
  - Key fields: `ModelId`, `Temperature`, `TopP`, `MaxToken`, `StopSequence`, `Functions` (array of `FunctionContract`), `ResponseFormat`.
  - `ExtraProperties`: `ImmutableDictionary<string, object?>` for provider-specific flags (e.g., OpenRouter routing, inline usage).
  - `Merge(other)`: deep-merges into a new options instance; `other` values win, `ExtraProperties` dictionaries are merged recursively.

### Tooling Contracts
- `FunctionContract`, `FunctionParameterContract`, `FunctionAttribute` (files: `LmCore/Core/*`): declarative function metadata for "tool use". `FunctionContract.GetJsonSchema()` builds a JSON schema from the parameters list. Used by middleware and providers to advertise tools to a model and to validate arguments.
- `FunctionDescriptor` (file: `LmCore/Middleware/FunctionDescriptor.cs`): Record that pairs a `FunctionContract` with its runtime handler (`Func<string, Task<string>>`), plus provider metadata for conflict resolution.

### Messages (selected)
- `TextMessage`, `TextUpdateMessage`: full or incremental text.
- `ReasoningMessage`, `ReasoningUpdateMessage`: for thinking/chain-of-thought visibility (encrypted/plain).
- `ToolsCallMessage`: assistant-proposed tool calls (`ToolCall` list)
- `ToolsCallUpdateMessage`: streaming tool-call chunks (`ToolCallUpdate` list)
- `ToolsCallResultMessage`: tool results to feed back to the model
- `UsageMessage`: unified usage accounting
- Builders: `TextMessageBuilder`, `ToolsCallMessageBuilder`, etc., to aggregate updates into final messages.

### Function Registry and Provider System (LmCore/Middleware)
- `FunctionRegistry`
  - Purpose: Builder for combining functions from multiple sources with declarative conflict resolution when multiple providers offer the same function name.
  - Construction: `new FunctionRegistry()` then chain configuration methods.
  - Key methods:
    - `AddProvider(IFunctionProvider)`: Add functions from a provider (MCP, Natural, etc.) ordered by priority.
    - `AddFunction(contract, handler, providerName?)`: Add a single function explicitly (takes precedence).
    - `WithConflictResolution(ConflictResolution)`: Set strategy for handling naming conflicts.
    - `WithConflictHandler(Func<string, IEnumerable<FunctionDescriptor>, FunctionDescriptor>)`: Custom conflict resolution logic.
    - `Build()`: Returns `(IEnumerable<FunctionContract>, IDictionary<string, Func<string, Task<string>>>)` for use with `FunctionCallMiddleware`.
    - `BuildMiddleware(name?)`: Directly creates configured `FunctionCallMiddleware`.
  - Conflict resolution strategies (`ConflictResolution` enum):
    - `Throw`: Fail fast on conflicts (default).
    - `TakeFirst`/`TakeLast`: First/last registered wins.
    - `PreferMcp`/`PreferNatural`: Prefer specific provider types.
    - `RequireExplicit`: Force use of custom handler.

- `IFunctionProvider`, `IFunctionProviderRegistry`, `IFunctionCallMiddlewareFactory`
  - `IFunctionProvider`: Interface for components that provide functions (name, priority, `GetFunctions()`).
  - `IFunctionProviderRegistry`: Service for discovering and managing providers via DI.
  - `IFunctionCallMiddlewareFactory`: Factory that uses registry to create middleware with all registered providers.
  - `FunctionCallMiddlewareFactory`: Default implementation that builds `FunctionRegistry` from all registered providers.

### Core Middleware (LmCore/Middleware)
All implement `IStreamingMiddleware` unless noted; each can be used with both sync and streaming entry points.

- `FunctionCallMiddleware`
  - Purpose: End-to-end tool calling. Detects pending tool calls (in the last user or assistant message), or triggers model generation if none; executes mapped functions via provided `functionMap` and returns `ToolsCallResultMessage`; aggregates usage information.
  - Construction: `new FunctionCallMiddleware(functions, functionMap, name?, logger?)`
    - `functions`: the contracts advertised to the model; validated against `functionMap` keys. Keys may be `ClassName-Name` if provided that way.
    - `functionMap`: `IDictionary<string, Func<string, Task<string>>>` mapping a function name to an async handler that takes JSON args and returns JSON/string results.
  - Behavior:
    - If the last message exposes tool calls, it bypasses model generation and executes the tool(s), returning results.
    - Otherwise, calls `agent.GenerateReply*(...)` with options extended to include `functions` (unless overridden already), then scans outputs; if tool calls appear in the reply, executes them and emits an aggregated result (`ToolsCallResultMessage` or a `ToolsCallAggregateMessage` pairing calls+results).
    - Usage: collects `UsageMessage` items or `usage` metadata and emits a single consolidated `UsageMessage` at the end.

- `NaturalToolUseParserMiddleware`
  - Purpose: Parse “inline” tool calls emitted in natural text using `<tool_call name="…"> ... </tool_call>` blocks (with optional fenced JSON). Validates against function schemas and can fall back to a parser agent.
  - Construction: `new NaturalToolUseParserMiddleware(functions, schemaValidator?, fallbackParser?, name?)`
  - Behavior:
    - Splits text into chunks using a robust regex+buffer strategy; for tool-call chunks, extracts JSON (fenced or unfenced), validates (if `schemaValidator` provided), and emits `ToolsCallMessage` when valid.
    - Error handling:
      - If no `fallbackParser` and invalid/absent JSON -> throws `ToolUseParsingException`.
      - If `fallbackParser` is provided, it can use structured output (`ResponseFormat.CreateWithSchema(...)`) or a legacy prompt to coerce valid JSON, then re-validates.
    - First invocation injects function documentation into the System message as Markdown; also nulls out `Options.Functions` to prevent redundancy when doing natural tool use.

- `NaturalToolUseMiddleware`
  - Purpose: Convenience composition that chains `NaturalToolUseParserMiddleware` then `FunctionCallMiddleware` so the model can “speak” tool calls in-text and have them executed.
  - Construction: `new NaturalToolUseMiddleware(functions, functionMap, fallbackParser?, name?, schemaValidator?)`
  - Behavior: `WithMiddleware(parser).WithMiddleware(functionCaller)` composition; both sync and streaming paths.

- `MessageUpdateJoinerMiddleware`
  - Purpose: Merge a stream of fine-grained update messages (text/reasoning/tool-call updates) into fewer, larger messages using builders; defers `UsageMessage` until the end.
  - Use when: you want smoother terminal/UI output or need to post-process batched content rather than raw token-level updates.

- `JsonFragmentUpdateMiddleware`
  - Purpose: For streaming tool-call updates, read `FunctionArgs` fragments and emit structured `JsonFragmentUpdate`s (kind, path, partial/complete tokens). This enables advanced UIs to colorize/indent JSON as it streams.
  - Behavior: Internally creates or reuses `JsonFragmentToStructuredUpdateGenerator` per tool call (by id/index/name) and replaces each `ToolCallUpdate` with one that includes `JsonFragmentUpdates` array.
  - Note: Its `Name` property is unimplemented in code; do not rely on it.

- `OptionsOverridingMiddleware`
  - Purpose: Inject/override `GenerateReplyOptions` on the fly (e.g., fix model, add response format, or set ExtraProperties).
  - Construction: `new OptionsOverridingMiddleware(options, name?)`
  - Behavior: Merges overrides with the incoming context’s options and forwards to the wrapped agent.

- `ModelFallbackMiddleware`
  - Purpose: Try multiple agents based on `options.ModelId`, with fallback ordering and optional final fallback to a default agent.
  - Construction: `new ModelFallbackMiddleware(modelAgentMap, defaultAgent, tryDefaultLast: true, name?)`
  - Behavior: On sync/streaming calls, if a mapping exists for `ModelId`, tries each agent in order, catching exceptions and proceeding; if all fail and `tryDefaultLast` is true, tries default; otherwise throws the last error.

- `ToolsCallAggregateTransformer` (static utility)
  - Purpose: Convert a `ToolsCallAggregateMessage` back into natural language with embedded XML-style tool calls and responses; useful for trace/debug output.

- Composition helpers
  - `Extensions.WithMiddleware(...)`: wraps an `IAgent`/`IStreamingAgent` with a middleware instance or two delegates (sync/streaming functions). Allows fluent composition: `agent.WithMiddleware(m1).WithMiddleware(m2)`.

### Core Utilities (LmCore/Utils)
- `JsonFragmentToStructuredUpdateGenerator` + `JsonFragmentUpdate{s}`: Parses incremental JSON text into structured updates. Pairs naturally with `JsonFragmentUpdateMiddleware` and tooling UIs.
- `UsageAccumulator`: Gathers `UsageMessage` or legacy `usage` metadata from messages and produces a single consolidated `UsageMessage` at the end.
- JSON serializers and helpers: `JsonSerializerOptionsFactory`, `ShadowPropertiesJsonConverter` family for metadata, `ImmutableDictionaryJsonConverter*`, schema helpers, etc.
- Performance helpers: `PerformanceLogger`, `RequestMetrics`, etc., used by agents and middleware for structured logging.


## OpenAI Provider (OpenAIProvider)

### Agents
- `OpenClientAgent` (`Agents/OpenAgent.cs`): Implements `IStreamingAgent` on top of an `IOpenClient`.
  - Sync path: Builds `ChatCompletionRequest` from core messages/options, calls `_client.CreateChatCompletionsAsync`, converts back to core messages, enriches/forwards usage; logs per-request metrics.
  - Streaming path: Calls `_client.StreamingChatCompletionsAsync` and yields `TextUpdateMessage`, `ToolsCallUpdateMessage`, and a final `UsageMessage` when provided by provider.
- `OpenClient` (`Agents/OpenClient.cs`): HTTP client wrapper for OpenAI-compatible APIs (OpenAI, OpenRouter, etc.), using `BaseHttpService` and `LmCore/Http` infrastructure. Handles SSE parsing for streaming.

### Request/Response Mapping
- `ChatCompletionRequestFactory`: Inspects `GenerateReplyOptions` and builds a provider-specific `ChatCompletionRequest`.
  - Standard vs OpenRouter mode:
    - Standard: uses `ModelId`, `Temperature`, `MaxToken`, tool list from `Functions`, etc.
    - OpenRouter: additionally maps OpenRouter-specific `ExtraProperties` like `route`, `models`, `transforms`.
  - Converts `ToolsCallMessage` to OpenAI `tool_calls` and back.
- Models under `OpenAIProvider/Models`: typed contracts for request/response, tool definitions, choices, usage, and OpenRouter stats mapping.

### OpenAI Provider Middleware
- `OpenRouterUsageMiddleware`
  - Purpose: Unified usage accounting when using OpenRouter. Injects usage flags into requests, buffers or enriches usage, and produces a single enhanced `UsageMessage` at end (sync and streaming).
  - Construction: `new OpenRouterUsageMiddleware(openRouterApiKey, logger, httpClient?, usageCache?, cacheTtlSeconds?)`
  - Behavior:
    - Injection: Adds `ExtraProperties["usage"] = { include: true }` to options.
    - Inline usage: If responses already include usage (and cost), it converts/forwards as a final consolidated `UsageMessage`.
    - Enhancement: If usage lacks cost or needs OpenRouter-specific cost, calls `GET https://openrouter.ai/api/v1/generation?id={completionId}` with retry + in-memory TTL cache, merging returned stats.
    - Caching: `UsageCache` stores usage by completion id with `is_cached` flag surfaced in `Usage.ExtraProperties`.
    - Timeouts: 3s for streaming, 5s for sync when calling OpenRouter stats.
    - Logging: Structured informational/warning logs for enrichment and retry-exhaustion.
  - Configuration helpers (`Configuration/EnvironmentVariables.cs`):
    - Env vars: `ENABLE_USAGE_MIDDLEWARE`, `ENABLE_INLINE_USAGE`, `USAGE_CACHE_TTL_SEC`, `OPENROUTER_API_KEY`.
    - Validation helper to fail fast if enabled but API key missing.


## MCP Integration (McpMiddleware, McpSampleServer, McpServers)

### McpMiddleware
- Purpose: Bridge tool calls from the model to external MCP tool endpoints through `IMcpClient`s.
- Factory: `await McpMiddleware.CreateAsync(Dictionary<string, IMcpClient> mcpClients, IEnumerable<FunctionContract>? functions = null, name?, logger?, functionCallLogger?, ct)`
  - Discovers tools from each MCP client via `ListToolsAsync` and builds:
    - `functionMap`: delegates that call `client.CallToolAsync(toolName, args)` with JSON-arg parsing/logging and return normalized string results (text blocks joined).
    - `functions`: if not provided, created from each tool’s JSON schema by `ConvertToFunctionContract` (class and method naming is prefixed with the client id for uniqueness, e.g., `clientA-say_hello`).
- Runtime: Internally composes a `FunctionCallMiddleware` with the discovered `functions` + `functionMap`. All sync/streaming calls delegate to it.
- Use with natural tool use: prepend `NaturalToolUseParserMiddleware` if models emit `<tool_call>` blocks.

### McpFunctionCallExtensions
- Purpose: Build `FunctionContract` + `functionMap` from MCP server tool registrations discovered by reflection.
- Entry points:
  - `CreateFunctionCallComponentsFromAssembly(Assembly? toolAssembly = null)` → `(IEnumerable<FunctionContract>, IDictionary<string, Func<string, Task<string>>>)`
  - `CreateFunctionCallComponentsFromTypes(IEnumerable<Type> toolTypes)`
  - `CreateFunctionCallMiddlewareFromAssembly(Assembly? toolAssembly = null, string? name = null)` convenience for wiring into an agent.
- Behavior:
  - Scans for types with `[McpServerToolType]`, methods with `[McpServerTool]` and optional `[Description]`.
  - Builds `FunctionContract` (with `ClassName`, `Namespace`, parameters via `SchemaHelper.CreateJsonSchemaFromType`).
  - Builds function delegates that deserialize JSON args into method params, invoke static/instance or `Task`-returning methods, and serialize results.

### McpSampleServer
- A minimal MCP server exposing tools via stdio transport for local tests.
- Example tools are in `McpSampleServer/Program.cs` with `GreetingTool` and `CalculatorTool` methods annotated using `[McpServerToolType]`/`[McpServerTool]`.
- Pair with an MCP client implementation (e.g., Python MCP client under `McpServers/PythonMCPServer`) to round-trip calls in integration tests.


## Misc Project (Misc)

### Middleware (terminal/UX helpers)
- `ConsolePrinterHelperMiddleware`
  - Purpose: Pretty-print streaming messages to console with colors, including natural JSON coloring for tool calls as they stream.
  - Features: Tracks message continuity; prints horizontal lines between logical messages; when `JsonFragmentUpdateMiddleware` is used earlier in the chain, consumes its structured updates; otherwise falls back to building updates from raw JSON fragments.
  - Construction: `new ConsolePrinterHelperMiddleware(colors?, toolFormatterFactory?, name?)` or the simpler overloads.
- `IToolFormatterFactory`, `DefaultToolFormatterFactory`, `JsonToolFormatter`, `ToolFormatter` delegate
  - Purpose: Pluggable formatting for tool-call JSON using `JsonFragmentUpdate` events (colorize keys/values, commas, brackets). Provide your own `IToolFormatterFactory` to customize output per tool.

### HTTP Caching
- `CachingHttpMessageHandler` and `CachingHttpContent` + `CachingStream`
  - Purpose: Response caching for LLM POST requests based on URL+body SHA256. For streaming, wraps the `HttpContent` to capture bytes as they flow and store as a single cached response.
  - Policy: Caches only successful responses; expiration and limits are set via `LlmCacheOptions`.
  - Integration: attach to pipeline with `StandardWrappers.WithKvCache(IKvStore, LlmCacheOptions)`; see DI helpers below.
- `LlmCacheOptions`
  - Options: directory, enable flag, expiration TTL, max items, max bytes, cleanup-on-startup.
  - Sources: build explicitly; or `FromEnvironment()` reads `LLM_CACHE_*` env vars.

### DI and HTTP Pipeline Helpers
- `ServiceCollectionExtensions` (Misc/Extensions)
  - `AddLlmFileCache(LlmCacheOptions|IConfiguration)`: registers `LlmCacheOptions`, `FileKvStore` as `IKvStore`, and wires `IHttpHandlerBuilder` to include the caching wrapper. Idempotent.
  - `AddLlmFileCacheFromEnvironment()`: shortcut using env.
  - Create clients using configured pipeline:
    - `CreateCachingOpenAIClient(apiKey, baseUrl, timeout?, headers?)`
    - `CreateCachingAnthropicClient(apiKey, baseUrl, timeout?, headers?)`
  - Utility: `GetCacheStatisticsAsync()` and `ClearLlmCacheAsync()`.
- `StandardWrappers.WithKvCache(...)`: returns a pipeline wrapper function to add `CachingHttpMessageHandler`.

### Storage and KV utilities
- `IKvStore`: async KV interface used by caching.
- `FileKvStore`: file-based KV with JSON serialization and atomic writes; also provides `ClearAsync()` and `GetCountAsync()`.
- `SqliteKvStore`: SQLite-backed KV store using `Microsoft.Data.Sqlite`.


## Config and HTTP (LmConfig)

- `IHttpHandlerBuilder` and `HandlerPipeline` (LmConfig/Http): build composed `HttpMessageHandler` chains. `Misc` plugs caching here.
- `HttpClientFactory` (LmConfig/Http): creates configured `HttpClient` instances for providers, taking a pipeline + provider config.
- Provider config models under `LmConfig/Models` for centralizing model pricing, capabilities, etc. (useful adjuncts when you need to compute cost/limits, though orthogonal to middleware).


## Embeddings (LmEmbeddings)

- Embedding and reranking abstractions (`IEmbeddingService`, `IRerankService`) with an OpenAI provider implementation and shared models. This is adjacent to chat, but not middleware-centric. Use when you need vectorization or rerank results with time/cost usage tracking analogous to chat usage.


## Putting It Together: Common Recipes

### 1) Using FunctionRegistry to combine multiple function sources
```csharp
// Create registry and combine functions from different sources
var registry = new FunctionRegistry()
    .AddProvider(mcpProvider)           // Add MCP provider functions
    .AddProvider(naturalToolProvider)   // Add natural tool functions
    .AddFunction(customContract, customHandler, "Custom") // Add explicit function
    .WithConflictResolution(ConflictResolution.PreferMcp); // MCP wins on conflicts

// Build middleware with combined functions
var middleware = registry.BuildMiddleware("CombinedFunctions");

// Or use with DI and factory pattern
var factory = services.GetRequiredService<IFunctionCallMiddlewareFactory>();
var middleware = factory.Create("MyFunctions", registry => 
{
    registry.WithConflictResolution(ConflictResolution.TakeLast)
           .AddFunction(overrideContract, overrideHandler);
});

var agent = baseAgent.WithMiddleware(middleware);
```

### 2) Simple OpenAI agent with usage enrichment and console printing
```csharp
var http = services.CreateCachingOpenAIClient(apiKey, baseUrl);
var client = new OpenClient(http, loggerFactory.CreateLogger<OpenClient>());
var agent = new OpenClientAgent("openai", client, loggerFactory.CreateLogger<OpenClientAgent>());

// Optional: usage enrichment via OpenRouter
var usageMw = new OpenRouterUsageMiddleware(openRouterApiKey, loggerFactory.CreateLogger<OpenRouterUsageMiddleware>());
var printerMw = new ConsolePrinterHelperMiddleware();

var streaming = agent
  .WithMiddleware(usageMw)
  .WithMiddleware(printerMw);

await foreach (var msg in await streaming.GenerateReplyStreamingAsync(messages, new GenerateReplyOptions { ModelId = "openrouter/some-model" }))
{
  // msg is also being printed; collect if needed
}
```

### 3) Natural tool use: parse <tool_call> + execute functions
```csharp
// Define contracts and function map
IEnumerable<FunctionContract> functions = new[] {
  new FunctionContract { Name = "Search", Parameters = new [] {
      new FunctionParameterContract { Name = "query", IsRequired = true, ParameterType = SchemaHelper.CreateJsonSchemaFromType(typeof(string)) }
  }}
};
var functionMap = new Dictionary<string, Func<string, Task<string>>> {
  ["Search"] = async argsJson => { var args = JsonSerializer.Deserialize<Dictionary<string,string>>(argsJson)!; return await MySearch(args["query"]); }
};

var natural = new NaturalToolUseMiddleware(functions, functionMap, fallbackParser: agent /* optional */);

var composed = agent
  .WithMiddleware(natural)
  .WithMiddleware(new MessageUpdateJoinerMiddleware())
  .WithMiddleware(new ConsolePrinterHelperMiddleware());

var replies = await composed.GenerateReplyAsync(messages, new GenerateReplyOptions { ModelId = "gpt-4o" });
```

### 4) MCP-backed tools
```csharp
// Given one or more IMcpClient implementations
var mcpClients = new Dictionary<string, IMcpClient> {
  ["memory"] = memoryClient,
  ["files"] = fileClient,
};

var mcpMw = await McpMiddleware.CreateAsync(mcpClients, name: "MCPTools", logger: loggerFactory.CreateLogger<McpMiddleware>());

var composed = agent
  .WithMiddleware(new NaturalToolUseParserMiddleware(mcpMwFunctions /* optional if you pass functions into CreateAsync */))
  .WithMiddleware(mcpMw);

var stream = await composed.GenerateReplyStreamingAsync(messages, options);
```

### 5) Options override and model fallback
```csharp
var strictJson = new OptionsOverridingMiddleware(new GenerateReplyOptions {
  ResponseFormat = ResponseFormat.CreateWithSchema("my_schema", mySchema, strictValidation: true)
});

var fallback = new ModelFallbackMiddleware(
  new Dictionary<string, IAgent[]> {
    ["gpt-4o"] = new[] { primaryAgent, backupAgent },
  },
  defaultAgent: backupAgent
);

var composed = primaryAgent
  .WithMiddleware(strictJson)
  .WithMiddleware(fallback);
```


## Guidance and Caveats

- Tool naming and maps: For `FunctionCallMiddleware` and MCP middleware, function names must match the keys in your `functionMap`. If you use class-qualified names (e.g., `Class-Method`) ensure your `Functions` contracts and keys align.
- Usage consolidation: Several middlewares delay or transform `UsageMessage`. If you rely on immediate usage updates, place your downstream consumers after those middlewares.
- Natural tool use vs Functions: Do not pass `Options.Functions` when using `NaturalToolUseParserMiddleware` in “inline tool” mode; the middleware injects tool docs into System and clears `Options.Functions` to steer the model toward inline calls instead of JSON tool_calls.
- Json fragment UI: To get colorized JSON tool-call streaming, put `JsonFragmentUpdateMiddleware` before `ConsolePrinterHelperMiddleware`.
- OpenRouter usage: If cost enrichment fails (timeouts/retries exhausted), middleware logs warnings and emits either inline usage or nothing; handle null/missing `UsageMessage` downstream if you depend on it.
- Caching: `CachingHttpMessageHandler` caches successful POST responses keyed by URL+body; remember to include request parameters critical to correctness in body/URL so the key differentiates.


## Quick Index (Middleware)

- Core
  - FunctionRegistry: Builder for combining functions from multiple sources with conflict resolution.
  - FunctionCallMiddleware: execute tool calls + accumulate usage.
  - NaturalToolUseParserMiddleware: parse <tool_call> text blocks into ToolsCallMessage.
  - NaturalToolUseMiddleware: parser + executor combo.
  - MessageUpdateJoinerMiddleware: coalesce streaming updates into larger messages.
  - JsonFragmentUpdateMiddleware: convert raw FunctionArgs stream into JsonFragmentUpdate events.
  - OptionsOverridingMiddleware: overlay GenerateReplyOptions.
  - ModelFallbackMiddleware: agent selection by model with fallback.
- OpenAIProvider
  - OpenRouterUsageMiddleware: inject usage flags and enrich usage/cost via OpenRouter.
- MCP
  - McpMiddleware: discover client tools, build contracts and function map, delegate to FunctionCallMiddleware.
  - McpFunctionCallExtensions: build contracts + map from server tool attributes; create FunctionCallMiddleware.
- Misc
  - ConsolePrinterHelperMiddleware: terminal-friendly streaming printing with JSON colorization.
  - DefaultToolFormatterFactory/JsonToolFormatter: structured JSON formatting.
  - CachingHttpMessageHandler (+ StandardWrappers, ServiceCollectionExtensions): add response caching to HTTP pipeline.


## References (Selected Files)
- Core Middleware: `src/LmCore/Middleware/*.cs`
- OpenAI Usage Middleware: `src/OpenAIProvider/Middleware/OpenRouterUsageMiddleware.cs`
- MCP: `src/McpMiddleware/*.cs`, `src/McpSampleServer/Program.cs`
- Misc Middleware/HTTP/Storage: `src/Misc/**`
- Config HTTP Pipeline: `src/LmConfig/Http/**`

