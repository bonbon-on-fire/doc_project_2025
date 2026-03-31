# Feature Specification: Manual CRUD Storage (Sqlite v1)

## High-Level overview
- Replace Entity Framework with manual SQL CRUD over Sqlite.
- Store evolving/non-critical data in JSON columns while keeping essential metadata in dedicated columns.
- Maintain current API surface (`IChatService` and controllers) while swapping the persistence layer.

## High level Requirements
- Big-bang removal of EF usage from runtime paths (no migrations/DbContext at runtime).
- Implement manual CRUD for `chats` and `messages`; keep `users` seeded/static.
- Preserve per-chat monotonic `SequenceNumber` and ordering semantics.
- Store full `MessageDto` as JSON; drop the `Content` column.
- Add `ChatJson` to store flexible chat metadata/config.
- Preserve cascade delete behavior (deleting a chat deletes its messages).
- Keep API responses and behavior stable for the client.

## Existing Solutions
- Current EF-based models and services: `AIChatDbContext`, `ChatService`, `MessageSequenceService`, migrations.
- Controller (`ChatController`) consumes `IChatService`, so the persistence swap can be isolated under the service layer.

## Current Implementation
- Entities: `User`, `Chat`, `Message` with EF annotations.
- Key behaviors:
  - `Messages` ordered and indexed by `(ChatId, SequenceNumber)`; unique per chat.
  - `(ChatId, Timestamp)` index used for history queries.
  - Sequence numbers computed via `MessageSequenceService` with transactional safety for relational providers.

## Detailed Requirements

### Requirement 1: Sqlite schema (manual)
- User Story: As a developer, I want a simple, explicit schema to support fast iteration and JSON-based extensibility.

Schema (Sqlite):
- `chats`
  - `Id` TEXT PRIMARY KEY
  - `UserId` TEXT NOT NULL
  - `Title` TEXT NOT NULL
  - `CreatedAtUtc` TEXT NOT NULL  // ISO8601
  - `UpdatedAtUtc` TEXT NOT NULL  // ISO8601
  - `ChatJson` TEXT NULL          // JSON extension field
  - Index: `idx_chats_user_updated` on (`UserId`, `UpdatedAtUtc` DESC)
- `messages`
  - `Id` TEXT PRIMARY KEY
  - `ChatId` TEXT NOT NULL REFERENCES `chats`(`Id`) ON DELETE CASCADE
  - `Role` TEXT NOT NULL          // user|assistant|system
  - `Kind` TEXT NOT NULL          // e.g., text|reasoning
  - `TimestampUtc` TEXT NOT NULL  // ISO8601
  - `SequenceNumber` INTEGER NOT NULL
  - `MessageJson` TEXT NOT NULL   // full DTO including derived types
  - Unique Index: `ux_messages_chat_sequence` on (`ChatId`, `SequenceNumber`)
  - Index: `idx_messages_chat_ts` on (`ChatId`, `TimestampUtc`)
  - Note: No additional indexes in v1 beyond the above.

Allowed values (extensible):
- Role (current): `user`, `assistant`, `tool`, `system`
- Kind (current): `text`, `reasoning`; near-future: `toolcall`, `toolresult`

Acceptance Criteria:
  1. [ ] Tables and indexes are created on startup if missing.
  2. [ ] PRAGMA `foreign_keys=ON` and cascade delete enforced for messages.
  3. [ ] JSON fields are valid JSON for inserted records.
  4. [ ] `Kind` column is populated consistently with message discriminator and validated on insert against the current set, while permitting forward-compatible values.
  5. [ ] `Role` values used by the service are one of the current set; additional roles are not blocked by schema.

### Requirement 2: Storage provider (Sqlite)
- User Story: As a developer, I want a clear provider abstraction so we can add other databases later.

Acceptance Criteria:
  1. [ ] Define `IChatStorage` with methods to manage chats, messages, pagination, and sequence allocation.
  2. [ ] Provide `SqliteChatStorage` using `Microsoft.Data.Sqlite` with parameterized SQL.
  3. [ ] All queries avoid ORM; use transactions where needed (sequence allocation, multi-insert flows).

### Requirement 3: Sequence number allocation
- User Story: As a client, I need messages to maintain strict order per chat.

Acceptance Criteria:
  1. [ ] Next sequence = `SELECT IFNULL(MAX(SequenceNumber), -1) + 1 FROM messages WHERE ChatId=?` within a transaction.
  2. [ ] Unique constraint on (`ChatId`, `SequenceNumber`) guarantees no duplicates.
  3. [ ] Concurrent requests on the same chat yield distinct sequence numbers; one succeeds, others retry once on conflict.

### Requirement 4: Service layer adaptation
- User Story: As a developer, I want to keep `IChatService` unchanged while replacing EF with the storage provider.

Acceptance Criteria:
  1. [ ] `ChatService` depends on `IChatStorage` and no longer references EF or `AIChatDbContext`.
  2. [ ] `MessageSequenceService` logic is replaced/absorbed into storage provider transaction.
  3. [ ] All existing endpoints continue to function and return identical shapes.

### Requirement 5: JSON persistence for message DTOs
- User Story: As a developer, I want to store full message payloads including derived types without schema churn.

Acceptance Criteria:
  1. [ ] `MessageJson` contains serialized `MessageDto` with polymorphic discriminator.
  2. [ ] Retrieval deserializes JSON to correct derived type (`TextMessageDto`/`ReasoningMessageDto`).
  3. [ ] No `Content` column exists; text is read from JSON.

### Requirement 6: Startup and configuration
- User Story: As an operator, I want reliable initialization with clear configuration.

Acceptance Criteria:
  1. [ ] Connection string configurable via appsettings and env vars.
  2. [ ] On startup, ensure schema and indexes exist (idempotent). If schema version mismatch, drop-and-recreate is acceptable for this new project.
  3. [ ] `users` table seeded with system/demo users similar to current behavior.
  4. [ ] Use existing defaults: in Development, `ConnectionStrings:DefaultConnection = Data Source=aichat_dev.db`; otherwise default to `Data Source=aichat.db` from `appsettings.json`.
  5. [ ] Environment override via `ConnectionStrings__DefaultConnection` is respected when provided.

### Requirement 7: Test Mode (in-memory Sqlite)
- User Story: As a tester, I want a fast, isolated, resettable database for automated tests.

Acceptance Criteria:
  1. [ ] When `ASPNETCORE_ENVIRONMENT=Test`, use `Data Source=:memory:;Cache=Shared` as the connection string by default (overridable via env/appsettings.Test.json).
  2. [ ] Maintain a single root connection open for the app lifetime to keep the in-memory database alive; reuse pooled connections against the shared cache.
  3. [ ] On host start in Test, drop-and-recreate schema and indexes to guarantee a clean state; reseed `users`.
  4. [ ] Do not call HTTPS redirection in Test; HTTP-only endpoints function on the configured port.
  5. [ ] No reset endpoint in v1; restarting the server resets the in-memory database.

### Requirement 8: Testing and performance
- User Story: As a maintainer, I need fast tests and no regressions.

Acceptance Criteria:
  1. [ ] Unit/integration tests pass; client e2e critical paths remain green.
  2. [ ] Queries are indexed properly; history and chat loads execute in milliseconds on sample datasets.
  3. [ ] No warnings in build output; clean builds.
