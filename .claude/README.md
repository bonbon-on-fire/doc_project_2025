# Claude-Specific Configuration

This directory contains Claude Code specific enhancements.

## Structure

- `commands/` - Custom commands accessible via `/project:` prefix
- `context/` - Additional context files for Claude

## Usage

Commands in `commands/` become available as:
- `/project:fix-issue` - Debug and fix issues
- `/project:implement-feature` - Implementation workflow
- `/project:validate` - Run validation gates

## Context Priority

1. CLAUDE.md (primary navigation)
2. .claude/context/ (specific context)
3. .instructions/ (detailed workflows)