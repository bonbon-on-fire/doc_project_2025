# CLAUDE.md - Claude Code Navigation

This file provides primary navigation for Claude Code when working with this repository.

## 🚦 START HERE

**Primary Navigation**: [.instructions/00-start-here.md](.instructions/00-start-here.md)

Choose your task type to get focused, contextual guidance:

### 🔍 **Investigate/Understand Code**
→ [.instructions/10-investigate/start.md](.instructions/10-investigate/start.md)

### 🛠️ **Implement Feature/Fix**
→ [.instructions/20-implement/workflow.md](.instructions/20-implement/workflow.md)

### 🐛 **Debug Issues**
→ [.instructions/30-debug/methodology.md](.instructions/30-debug/methodology.md)

### ✅ **Validate Changes**
→ [.instructions/40-validate/gates.md](.instructions/40-validate/gates.md)

### 📚 **Code Standards**
→ [.instructions/50-standards/index.md](.instructions/50-standards/index.md)

## 🚀 Essential Commands

```bash
# Quick Start
pwsh build-and-start-server.ps1        # Start server (Test env)
pwsh build-and-start-server.ps1 -UseOrleans  # Start with Orleans
pwsh build-and-start-client.ps1        # Start client

# Validation (MANDATORY after changes)
pwsh scripts/validate-file-change.ps1   # Level 0 (after save)
pwsh scripts/validate-implementation-step.ps1  # Level 1 (after work)
pwsh scripts/quality-check.ps1          # Level 2 (before complete)
pwsh scripts/validate-pre-commit.ps1    # Level 3 (before commit)

# Code Formatting (REQUIRED)
pwsh scripts/format-code.ps1            # NEVER commit without this
```

## 🏗️ Project Overview

- **Frontend**: SvelteKit 2.22, Svelte 5.0, TypeScript 5.0
- **Backend**: ASP.NET 9.0, SignalR, Orleans grains
- **Database**: SQLite + EF Core
- **AI**: LmDotnetTools suite

## 📋 Critical Rules

1. **Use `.instructions/` for detailed guidance** - Don't read everything at once
2. **Always run validation scripts** - Development progression is blocked by failures
3. **Always format code** - Use `format-code.ps1` before commits
4. **Use scratchpad** - Create session directories for complex work
5. **Follow task-based navigation** - Start with the appropriate numbered directory

## 📍 File Organization

```
├── .instructions/     # Primary instruction hub (READ THESE)
├── .claude/          # Claude-specific commands & context
├── .cursor/          # Cursor rules
├── scratchpad/       # Work notes (MANDATORY for complex tasks)
├── scripts/          # Validation & build scripts
└── docs/             # Architecture & documentation
```

## 🆘 Emergency Access

- **Build broken**: [.instructions/30-debug/build-failures.md](.instructions/30-debug/build-failures.md)
- **Tests failing**: [.instructions/30-debug/test-failures.md](.instructions/30-debug/test-failures.md)
- **Validation stuck**: [.instructions/40-validate/troubleshooting.md](.instructions/40-validate/troubleshooting.md)

---

💡 **Remember**: This is just navigation. For detailed instructions, always follow the `.instructions/` links above based on your current task.