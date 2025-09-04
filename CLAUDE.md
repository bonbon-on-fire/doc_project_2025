# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 📚 Quick Reference Guide

### Development Instructions

All detailed instructions are organized in `.repo-instructions/` directory:

| Category | File | Description |
|----------|------|-------------|
| 🛠️ **Commands** | [development-commands.md](.repo-instructions/development-commands.md) | All CLI commands for client, server, and tools |
| 🏗️ **Architecture** | [architecture-patterns.md](.repo-instructions/architecture-patterns.md) | System design, patterns, and tech stack |
| 🐛 **Debugging** | [debugging-guide.md](.repo-instructions/debugging-guide.md) | Systematic debugging methodology and tools |
| 🔄 **Workflow** | [development-workflow.md](.repo-instructions/development-workflow.md) | Development process and best practices |
| 📦 **Build & Test** | [build-and-test.md](.repo-instructions/build-and-test.md) | Package publishing and testing procedures |

### Code Standards

| Standard | File | Focus Area |
|----------|------|------------|
| 📝 **Naming** | [naming-types.md](.repo-instructions/naming-types.md) | Variable naming and type declarations |
| ✨ **Quality** | [code-quality.md](.repo-instructions/code-quality.md) | Code style and quality guidelines |
| 🎯 **Principles** | [core-software-principles.md](.repo-instructions/core-software-principles.md) | SOLID, DRY, KISS principles |
| ⚡ **Async** | [async-programming.md](.repo-instructions/async-programming.md) | Async/await patterns |
| 📊 **Data** | [data-handling.md](.repo-instructions/data-handling.md) | Data manipulation standards |
| ⚠️ **Errors** | [exception-handling.md](.repo-instructions/exception-handling.md) | Error handling patterns |
| 🔍 **LINQ** | [linq-collections.md](.repo-instructions/linq-collections.md) | LINQ and collection usage |
| 📋 **Logging** | [logging.md](.repo-instructions/logging.md) | Logging standards and practices |

## 🚀 Quick Start

### Essential Commands

```bash
# Frontend
cd client && npm install && npm run dev

# Backend  
cd server && dotnet restore && dotnet watch run

# Tests
npm run test:e2e      # Client E2E tests
dotnet test           # Server unit tests
```

### Package Management (LmDotnetTools)

```bash
# Quick publish to local feed
cd submodules/LmDotnetTools
powershell -ExecutionPolicy Bypass -File publish-nuget-packages.ps1 -LocalOnly
```
See [build-and-test.md](.repo-instructions/build-and-test.md) for detailed instructions.

## 🏛️ Architecture Overview

### Tech Stack

- **Frontend**: SvelteKit 2.22, Svelte 5.0, TypeScript 5.0, Tailwind CSS 4.0
- **Backend**: ASP.NET 9.0, SignalR, Entity Framework Core
- **Database**: SQLite with Drizzle ORM (client) + EF Core (server)
- **AI**: LmDotnetTools suite - see `docs/LmDotNet-doc.md`

### Project Structure

```
├── client/              # SvelteKit frontend
├── server/              # ASP.NET backend
├── server.Tests/        # Backend tests
├── shared/              # Shared TypeScript types
├── submodules/          # LmDotnetTools
├── .repo-instructions/  # Development guides
├── scratchpad/          # Work notes (MUST USE)
└── docs/                # Documentation
```

## 🧠 Critical Development Rules

### MUST DO

1. **Use Scratchpad**: Create session directories in `scratchpad/` for ALL work
2. **Sequential Thinking**: Break complex problems into documented steps
3. **Use Checklists**: Track progress systematically
4. **EXECUTE VALIDATION SCRIPTS**: Run appropriate validation level after each change
5. **Never Bypass Validation**: Fix failures immediately - progression is blocked otherwise
6. **Follow Standards**: Reference instruction files for code standards

### Key Patterns

- **Messages**: Flow through `MessageRouter.svelte` for rendering
- **Real-time**: REST for persistence, SignalR/SSE for updates
- **Types**: Use shared interfaces from `shared/types/`
- **Debugging**: Use logs + DuckDB (see [debugging-guide.md](.repo-instructions/debugging-guide.md))

## 🎭 Development Modes

Use specialized modes in `.github/chatmodes/` for different tasks:

1. **📝 Spec Writer** → Requirements gathering
2. **🏗️ Spec to Tasks** → Design and planning  
3. **👨‍💻 Senior Developer** → Implementation
4. **🔄 Interactive Dev** → Research and experimentation

See [development-workflow.md](.repo-instructions/development-workflow.md#development-modes) for details.

## 🔧 Environment Configuration

### Required Variables

- `LLM_API_KEY` - LLM provider API key
- `LLM_BASE_API_URL` - Provider base URL (optional)
- `ASPNETCORE_ENVIRONMENT` - Set to "Test" for testing

### Config Files

- `client/.env.local` - Frontend environment
- `server/appsettings.{Environment}.json` - Server settings

## Start server and client

**These scripts will stop any ongoing process, build and restart the server**
The following scripts will redirect build logs to respective locations for checking offline
Application logs are also redirected to respective locations to be queried later.

Powershell:
  MUST use `pwsh build-and-start-server.ps1` for starting / restarting server
  MUST use `pwsh build-and-start-client.ps1` for starting / restarting client
Bash:
  MUST use `bash build-and-start-server.sh` for starting / restarting server
  MUST use `bash build-and-start-client.sh` for starting / restarting client

## 🚨 Continuous Validation Commands

### 4-Level Validation System (MANDATORY)

**CRITICAL**: These scripts control development progression and are BLOCKING - exit codes determine whether work can continue.

#### Level 0: After Every File Save (< 30 seconds)

```powershell
pwsh scripts/validate-file-change.ps1
```
- **Purpose**: Quick build check  
- **When**: After any code file save
- **Exit 0**: Continue | **Exit 1**: Fix compilation errors immediately

#### Level 1: After Implementation Steps (< 5 minutes)  

```powershell
pwsh scripts/validate-implementation-step.ps1
```
- **Purpose**: Build + test validation
- **When**: After completing any implementation work
- **Exit 0**: Continue to next step | **Exit 1**: Fix build/test failures

#### Level 2: Before Task Completion (< 15 minutes)

```powershell
pwsh scripts/quality-check.ps1
```
- **Purpose**: Comprehensive quality gates
- **When**: Before marking any task complete
- **Exit 0**: Task can be completed | **Exit 1**: Task BLOCKED until fixed

#### Level 3: Before Git Commits (Full validation)

```powershell
pwsh scripts/validate-pre-commit.ps1
```
- **Purpose**: Complete system validation
- **When**: Before any git commit  
- **Exit 0**: Commit approved | **Exit 1**: Commit BLOCKED

### Validation Enforcement Rules

1. **Never bypass validation failures** - system integrity depends on adherence
2. **Always fix issues immediately** - don't accumulate validation debt
3. **Use rollback procedures** if validation cannot be fixed quickly:

```powershell
# Quick rollback options
git stash push -m "WIP: validation failure"  # Save work
git reset --hard HEAD                        # Nuclear option  
git checkout -- <specific-files>             # Selective revert
```

### Enhanced Build Scripts

**build-and-start-server.ps1** and **build-and-start-client.ps1** now include pre-flight validation:
- Automatically run Level 1 validation before starting
- Block startup if validation fails  
- Seamless integration with existing workflow

## 📊 Logging & Debugging

### Log Locations

- Server: `logs/server/app-{env}.jsonl`
- Client: `logs/client/app.jsonl`

### Query with DuckDB

```sql
SELECT * FROM read_json_auto('logs/server/app-test.jsonl')
WHERE level = 'Error' ORDER BY timestamp DESC;
```

## 🎯 Important Notes

**Remember**: 
- ALWAYS use `scratchpad/` for notes and learning capture
- Follow the debugging methodology in [debugging-guide.md](.repo-instructions/debugging-guide.md)
- Reference instruction files for specific standards
- Use sequential thinking for complex problems
- Test thoroughly before committing changes

For any specific topic, refer to the appropriate file in `.repo-instructions/` for detailed guidance.
- Use `format-code.ps1` for making sure the code styles is good and fix any build errors post re-formatting. You MUST prefer `format-code.ps1` over `dotnet format`.
