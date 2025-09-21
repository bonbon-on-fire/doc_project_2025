# Code Standards Index

This directory contains all code standards and principles for the project.

## Core Principles
- [core-software-principles.md](core-software-principles.md) - SOLID, DRY, KISS principles
- [code-quality.md](code-quality.md) - Code review standards and quality metrics

## Language & Framework Standards
- [dotnet-code-styles.md](dotnet-code-styles.md) - **MANDATORY** - Formatting and style
- [async-programming.md](async-programming.md) - Async/await patterns
- [data-handling.md](data-handling.md) - Data access patterns
- [exception-handling.md](exception-handling.md) - Error handling
- [linq-collections.md](linq-collections.md) - LINQ and collection usage

## Development Standards
- [naming-types.md](naming-types.md) - Variable naming and type declarations
- [logging.md](logging.md) - Logging standards and practices
- [architecture-patterns.md](architecture-patterns.md) - Architectural design patterns

## Build & Development
- [build-and-test.md](build-and-test.md) - Build processes and test execution
- [development-commands.md](development-commands.md) - Command reference
- [development-workflow.md](development-workflow.md) - Development process
- [debugging-guide.md](debugging-guide.md) - Debugging methodology

## ⚠️ ALWAYS REQUIRED (Run in this exact order)
1. **[dotnet-code-styles.md](dotnet-code-styles.md)** - Code formatting with format-code.ps1
2. **Build Quality Check** - ALWAYS run `pwsh scripts/build_and_group_errors_and_warnings.ps1` after formatting
3. **[debugging-guide.md](debugging-guide.md)** - Systematic problem solving

### 🚨 MANDATORY Code Quality Sequence
```bash
pwsh scripts/format-code.ps1                      # Fix formatting first
pwsh scripts/build_and_group_errors_and_warnings.ps1  # Check ALL warnings after formatting
```
**NEVER commit without running both scripts in this order.**

## Quick Reference
For specific task guidance, return to [00-start-here.md](../00-start-here.md)