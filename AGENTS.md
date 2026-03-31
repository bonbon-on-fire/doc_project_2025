# Repository Guidelines

This repo uses SvelteKit (client) and ASP.NET 9.0 (server). It aims for readable, well-tested code with minimal diffs and explicit workflows. Use this guide as the source of truth for local dev, testing, and contribution practices.

## Project Structure & Module Organization
- `client/`: SvelteKit app (TypeScript, Tailwind). Source in `client/src` with `lib/` (components, renderers, stores, utils), `routes/`, and tests.
- `server/`: ASP.NET Web API (C#). Core areas include `Controllers/`, `Services/`, `Storage/`, `Models/`, `Hubs/`. Configuration in `appsettings*.json`.
- `server.Tests/`: xUnit + FluentAssertions tests for the server.
- `shared/`: Shared types/utilities.
- `docs/`, `scratchpad/`: Documentation, notes, and per-task checklists.
- LmDotNet internals: see `docs/LmDotNet-doc.md` for middleware, providers, MCP, and HTTP caching.
- `logs/`, `server/logs/`: JSONL logs for client/server.
- `docker-compose.yml`: Local dev orchestration.

## Environment & Configuration
- Client env: create `client/.env` from `.env.example` as needed.
- Server env: use `server/appsettings.Development.json` (local) and `server/appsettings.Test.json` (tests). For secrets, prefer `dotnet user-secrets`.
- Required variables: `LLM_API_KEY` (and optional `LLM_BASE_API_URL`). For test mode, set `ASPNETCORE_ENVIRONMENT=Test` (commonly uses port 5099).

## Development Workflow
- Client (hot reload):
  - `cd client && npm install && npm run dev` (or `npm run dev:test` for test mode)
  - Quality: `npm run check && npm run lint && npm run format`
- Server (hot reload):
  - `cd server && dotnet restore && dotnet watch run`
  - Alternate test mode: set `ASPNETCORE_ENVIRONMENT=Test` before starting
- Docker (optional): `docker-compose up -d`

## Build, Test, and Database
- Client build/preview: `cd client && npm run build && npm run preview`
- Client tests:
  - Unit: `npm run test:unit` (Vitest)
  - E2E: `npm run test:e2e` (Playwright)
  - All: `npm run test`
- Server tests: `dotnet test ./server.Tests`
- Drizzle (client-side SQLite): from `client/` run `npm run db:push | db:generate | db:migrate | db:studio`
- Server storage: SQLite via `Microsoft.Data.Sqlite` with manual CRUD in `server/Storage/` (no EF). DB files like `server/aichat.db` for dev.

## Architecture Overview
- Rendering pipeline: Use `client/src/lib/renderers/` with `MessageRouter.svelte` to select renderers (text, reasoning, tools, etc.). Add new types via the registry, not ad-hoc components.
- Real-time & streaming: SignalR for broadcast updates; Server-Sent Events stream at `/api/chat/stream-sse` for structured incremental output. Use REST endpoints for persistence and state changes.
- State: Client state via Svelte stores (`client/src/lib/stores`); server persists to SQLite. Message sequencing handled by dedicated server services to keep ordering consistent.
- Caching: File-based LLM cache under `server/llm-cache/` (when enabled) for reproducibility.

## Coding Style & Naming Conventions
- Client: Prettier + ESLint (2-space indent, TS strict). Use PascalCase Svelte component files (e.g., `MessageRouter.svelte`). Co-locate tests as `*.test.ts` near sources.
- Server: C# conventions. PascalCase for types/methods; camelCase for locals/fields. Nullable enabled. Prefer async APIs. Keep DI-registered services focused and testable.
- Naming: Use descriptive, domain-specific names. Follow existing patterns in `components/`, `renderers/`, `services/`, and `storage/`.

## Testing Guidelines
- Client: Unit tests with Vitest and component tests colocated in `client/src/lib/**`. E2E tests with Playwright in `client/e2e/`.
- Server: xUnit + FluentAssertions in `server.Tests/` (e.g., `SseHandlerTests.cs`).
- When adding features: include tests for success, error, and edge cases. For UI changes, include screenshots in PRs and/or Playwright assertions.

## Debugging & Logs
- Method: Observe → Assert → Validate → Plan → Apply → Validate (keep a brief log in scratchpad when deep-diving).
- Logs: JSONL logs in `logs/` (client) and `server/logs/` (server). Test mode typically writes to `app-test.jsonl`.
- DuckDB for log analysis:
  - Example: `SELECT * FROM read_json_auto('server/logs/app-test.jsonl') WHERE level IN ('Error','Warning') ORDER BY ts DESC LIMIT 200;`
- Tips: Increase verbosity locally; ensure secrets are redacted before sharing logs.

## Security & Secrets
- Do not commit secrets. Use `client/.env` for frontend values and `dotnet user-secrets` or `appsettings.Development.json` for server.
- Required keys: `LLM_API_KEY` (provider key) and optionally `LLM_BASE_API_URL`.
- Review artifacts: scan `logs/` and `server/logs/` before attaching to issues or PRs.

## Commit, Branch, and PR Guidelines
- Conventional commits (observed in history): `feat:`, `fix:`, `refactor:`, `test:`, `docs:`.
  - Good: `feat: enhance collapsible message rendering`
  - Good: `fix(server): correct SSE error handling`
- Branching: `user/<gh-username>/<short-topic>` or `feature/<topic>`.
- PRs must include: clear description, scope (`client`/`server`), linked issues, test plan (commands + results), and screenshots for UI/UX changes.
- Pre-merge checks: `npm run lint`, `npm test` (client), and `dotnet test` (server) must pass. Keep diffs minimal and focused.

## Chat Modes & Agent Guidance
- Pick modes on the fly to match the task:
  - Implementing scoped tasks → Senior Developer ([.github/chatmodes/senior-developer.chatmode.md](.github/chatmodes/senior-developer.chatmode.md))
  - Exploring/ambiguous or collaborative iteration → Senior Dev Interactive ([.github/chatmodes/senior-dev-interactive.chatmode.md](.github/chatmodes/senior-dev-interactive.chatmode.md))
  - Converting specs to plans → Spec-to-Tasks ([.github/chatmodes/spec-n-design-doc-to-tasks.chatmode.md](.github/chatmodes/spec-n-design-doc-to-tasks.chatmode.md))
- Default to Senior Developer. Announce the selected mode at task start. Switch if the task shifts (e.g., from exploration to implementation) and note the switch.
- Workflow: Research → Develop → Review → Cleanup. Keep notes in `scratchpad/{feature}/{task-id}/notes.md` and track progress in `scratchpad/{feature}/{task-id}/checklist.md`.
- Principles: Minimal diffs, KISS/DRY/SOLID, no duplication, testability-first, avoid blocking commands. Reuse prior notes in `scratchpad/**` and `docs/**`.

## CI Recommendations
- On push/PR run:
  - Client: Node 18+, `npm ci`, `npm run lint`, `npm test` (unit + E2E)
  - Server: .NET 9.0, `dotnet restore`, `dotnet build --configuration Release`, `dotnet test --configuration Release`
- Suggested minimal workflow (excerpt):
  - Checkout; Setup Node; run client checks in `client/`
  - Setup .NET; build and test in `server/`

## Contribution Checklist (pre-PR)
- Code builds locally: `npm run build` (client), `dotnet build` (server)
- Tests pass: `npm test` and `dotnet test`
- Lint/format clean: `npm run lint && npm run format`
- New code covered by tests where meaningful (unit/E2E/server)
- Secrets kept out of code and config samples updated if needed
- PR description includes scope, test plan, and screenshots for UI changes
