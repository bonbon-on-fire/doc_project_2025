# Incremental Tool Call Arguments via JsonFragments

## Summary
- Implement incremental, structured reconstruction of tool-call arguments on the client using server-provided `JsonFragmentUpdates`.
- Continue graceful fallback to raw JSON string accumulation when fragments are unavailable.

## Scope
- Client-only implementation (TypeScript/SvelteKit).
- No server changes beyond ensuring `JsonFragmentUpdateMiddleware` remains in the streaming pipeline.
- Covers streaming rendering, not persistence format changes.

## Current Behavior
- Server streams `tools_call_update` SSE chunks (`ToolsCallUpdateStreamEvent`) with `ToolCallUpdate` objects that may include `function_name`, incremental `function_args`, and optional `tool_call_id`/`index`.
- `JsonFragmentUpdateMiddleware` enriches each `ToolCallUpdate` with `json_update_fragments` derived from incremental parsing of `function_args` by `JsonFragmentToStructuredUpdateGenerator`.
- Client accumulates `function_args` as a string per tool call in `ToolsAggregateMessageHandler` and opportunistically `JSON.parse`s; otherwise renders partial raw JSON text.

## Proposed Behavior
- Prefer `json_update_fragments` for incremental reconstruction of a structured `args` object in the client.
- Maintain raw `function_args` accumulation for fallback and historical completeness.
- Render structured JSON progressively in UI.

### Initial UI Scope (Simple)
- Keep rendering simple at first: show progressively built structured JSON when available; otherwise show raw partial JSON text.
- No special visual indicator for partial strings in the first pass.

### Future Enhancement (Not in first pass)
- Add a subtle “streaming/partial” indicator for values that are still incomplete (e.g., while receiving `PartialString` events). This remains optional and can be implemented later without changing data contracts.

## Data Contract Changes (Client)
- Extend `ToolCallUpdate` (client SSE types) with:
  - `json_update_fragments?: Array<{ path: string; kind: JsonFragmentKind; textValue?: string; value?: unknown }>`
  - `JsonFragmentKind = 'StartObject' | 'EndObject' | 'StartArray' | 'EndArray' | 'StartString' | 'PartialString' | 'CompleteString' | 'Key' | 'CompleteNumber' | 'CompleteBoolean' | 'CompleteNull' | 'JsonComplete'`

## Client Implementation Plan
1. Add `JsonFragmentRebuilder` utility (`client/src/lib/utils/jsonFragmentRebuilder.ts`)
   - Applies `json_update_fragments` in order to a mutable structure.
   - Handles containers, keys, partial strings, primitives, and completion (`JsonComplete`).
2. Update `ToolsAggregateMessageHandler`
   - Maintain a per-message map of `tool_call_id` (or `messageId+index` fallback) -> `JsonFragmentRebuilder`.
   - On each `tools_call_update` chunk, apply fragments (when present) and set `pair.toolCall.args` from the rebuilder.
   - Preserve existing raw `function_args` accumulation and parsing fallback when fragments are absent.
3. Renderer `ToolsCallAggregateRenderer.svelte`
   - If `toolCall.args` is object/array, render structured view; otherwise render raw partial JSON string.
   - Defer special partial-string indicators to a future enhancement.

## Testing Plan
- Utils (`jsonFragmentRebuilder.test.ts`)
  - Flat and nested objects, arrays, and mixed primitives.
  - PartialString accumulation across chunks (including escapes) and final CompleteString.
  - Boundary handling for Start/End of objects/arrays and empty containers.
  - `isComplete()` toggled by `JsonComplete` and final structure matches expected JSON.
- Handler (`toolsAggregateMessageHandler.test.ts`)
  - Fragment-driven evolution of `toolCallPairs` with multiple, interleaved tool calls.
  - Fallback behavior when fragments are missing; mixed streams (some chunks with fragments).
  - Keying by `tool_call_id` with index-based fallback when id is missing.
  - Completion snapshot correctness and map cleanup.

## Risks & Mitigations
- Fragment absence for some providers: keep current string-based fallback.
- Path format drift: path resolution follows submodule generator semantics; add tests to detect regressions.
- UI churn with partial strings: keep minimal indicator and avoid aggressive re-rendering.

## Rollout
1. Land types + utils + unit tests.
2. Integrate handler + extend tests.
3. Light renderer adjustments.
4. Manual validation in Test mode (SSE) and screenshot capture.

## References
- Server middleware: `JsonFragmentUpdateMiddleware`, `JsonFragmentToStructuredUpdateGenerator` (submodule).
- SSE streaming: `ChatService.ProcessStream` tool call updates; `ToolsCallUpdateStreamEvent`.

## Scratchpad Links (for resuming work)
- Notes: ../../scratchpad/incremental-tool-args-json-fragments/notes.md
- Checklist: ../../scratchpad/incremental-tool-args-json-fragments/checklist.md
