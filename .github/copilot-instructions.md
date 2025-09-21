---
description: GitHub Copilot instructions for DOC_Project_2025
applyTo: '**'
---

# GitHub Copilot Instructions

## Project Context
Real-time chat application with Orleans grain architecture, SignalR communication, and AI integration.

## Technology Stack
- Frontend: SvelteKit 5.0 + TypeScript + Tailwind CSS
- Backend: ASP.NET 9.0 + Orleans + SignalR + EF Core
- Database: SQLite with EF Core
- AI: LmDotnetTools suite

## Code Standards
- Follow SOLID, DRY, KISS principles
- Use early returns for error conditions
- Write testable, mockable code
- Validate all inputs
- Prefer minimal code changes

## Validation Requirements
After ANY code change, run:
```bash
pwsh scripts/validate-file-change.ps1  # Level 0 validation
```

Before completing work:
```bash
pwsh scripts/quality-check.ps1         # Level 2 validation
pwsh scripts/format-code.ps1           # MANDATORY formatting
```

## Architecture Patterns
- Messages flow through MessageRouter.svelte
- Real-time updates via SignalR
- Orleans grains manage state
- Shared types in shared/types/
- Use .instructions/ for detailed guidance

## Navigation
Start with: CLAUDE.md → .instructions/00-start-here.md

## Key Files
- Frontend: client/src/lib/chat/messageHandlers.ts
- Backend: server/Controllers/ChatController.cs
- Orleans: server/AIChat.Orleans/Grains/UserGrain.cs
- SignalR: server/Hubs/ChatHub.cs

Always follow task-based navigation in .instructions/ for detailed guidance.