# Index to Important Instructions

This index helps AI assistants quickly locate the appropriate instruction files for different scenarios and use cases within the DOC_Project_2025 repository.

## 🚀 Development Workflow & Process

| When You Need To... | Instruction File | Description |
|---------------------|------------------|-------------|
| Start development work, run builds, understand project structure | `.github/instructions/primary.instructions.md` | **PRIMARY REFERENCE** - Core development commands, architecture overview, technology stack, project structure, environment setup |
| Watch for build errors during development | `.github/instructions/hot-reload-based-builds.instructions.md` | Hot reload setup, background process monitoring, build error observation |
| Manage development workflow, use scratchpad, create checklists | `ai-instructions/development-workflow.md` | Essential work practices, organization techniques, task management |
| Handle compiler errors, search web for solutions | `.github/instructions/common-sense.instructions.md` | Common sense debugging, web search for compiler errors, sequential thinking |

## 🎯 Code Quality & Standards

| When You Need To... | Instruction File | Description |
|---------------------|------------------|-------------|
| **Format code, maintain consistent styles** | `ai-instructions/dotnet-code-styles.md` | **MANDATORY** - `format-code.ps1` usage, `.editorconfig` maintenance, multi-tool formatting approach |
| Apply core software engineering principles | `ai-instructions/core-software-principles.md` | SOLID principles, design patterns, software engineering best practices |
| Understand architectural patterns | `ai-instructions/architecture-patterns.md` | Architectural design patterns, system design principles |
| Ensure code quality standards | `ai-instructions/code-quality.md` | Code review standards, quality metrics, maintainability guidelines |

## 🔍 Debugging & Testing

| When You Need To... | Instruction File | Description |
|---------------------|------------------|-------------|
| **Debug issues systematically** | `ai-instructions/debugging-guide.md` | **6-step methodology**: Observe → Assert → Validate → Plan → Apply → Validate Fix |
| Work with logging and diagnostics | `ai-instructions/logging.md` | Logging best practices, structured logging, diagnostic techniques |

## 🛠️ Build & Deployment

/submodu

| When You Need To... | Instruction File | Description |
|---------------------|------------------|-------------|
| Run build and test commands | `ai-instructions/build-and-test.md` | Build processes, test execution, deployment procedures |
| Understand development commands | `ai-instructions/development-commands.md` | Command reference, CLI usage, development tools |

## 🏗️ Architecture & Design

| When You Need To... | Instruction File | Description |
|---------------------|------------------|-------------|
| Handle exceptions properly | `ai-instructions/exception-handling.md` | Exception handling patterns, error management strategies |
| Work with async/await patterns | `ai-instructions/async-programming.md` | Asynchronous programming best practices, async patterns |
| Handle data operations | `ai-instructions/data-handling.md` | Data access patterns, entity management, database operations |

## 📝 Language-Specific Guidelines

| When You Need To... | Instruction File | Description |
|---------------------|------------------|-------------|
| Work with LINQ and collections | `ai-instructions/linq-collections.md` | LINQ best practices, collection operations, functional programming |
| Follow naming conventions | `ai-instructions/naming-types.md` | Naming standards, type naming conventions, identifier guidelines |

## 🔄 Code Review & Self-Assessment

| When You Need To... | Instruction File | Description |
|---------------------|------------------|-------------|
| Follow self-review process | `submodules/LmDotnetTools/.github/instructions/self-review-process.instructions.md` | Code review methodology, self-assessment practices |
| Implement self-review techniques | `submodules/LmDotnetTools/.github/instructions/self-review-implementation.instructions.md` | Implementation of self-review processes |

## 🎯 User-Configured Instructions

| Source | Description |
|--------|-------------|
| `vscode-userdata:/c%3A/Users/Gautam%20Bhakar/AppData/Roaming/Code%20-%20Insiders/User/prompts/always-thinking.instructions.md` | Always use sequential-thinking tool for planning |
| `vscode-userdata:/c%3A/Users/Gautam%20Bhakar/AppData/Roaming/Code%20-%20Insiders/User/prompts/search-web-for-compiler-errors.instructions.md` | Web search for compiler errors, effective thinking methodology |

## 📋 Quick Reference by Scenario

### 🆘 When Starting New Development Work

1. **Read first**: `.github/instructions/primary.instructions.md`
2. **Setup hot reload**: `.github/instructions/hot-reload-based-builds.instructions.md`
3. **Create scratchpad**: `ai-instructions/development-workflow.md`

### 🔧 When Fixing Bugs

1. **Follow debug methodology**: `ai-instructions/debugging-guide.md`
2. **Handle test failures**: `submodules/LmDotnetTools/.github/instructions/test-debugging.instructions.md`
3. **Search compiler errors**: `.github/instructions/common-sense.instructions.md`

### 📝 When Writing Code

1. **Apply formatting**: `ai-instructions/dotnet-code-styles.md` ⚠️ **MANDATORY**
3. **Ensure quality**: `ai-instructions/code-quality.md`

### 🚀 When Deploying/Testing

1. **Build commands**: `ai-instructions/build-and-test.md`
2. **Test standards**: `submodules/LmDotnetTools/.github/instructions/production-test-standards.instructions.md`

## 🎯 Priority Instructions

### ⚠️ ALWAYS REQUIRED

- `ai-instructions/dotnet-code-styles.md` - **NEVER commit without formatting**
- `.github/instructions/primary.instructions.md` - **Primary reference for all development**
- `ai-instructions/debugging-guide.md` - **Systematic problem solving**

### 🔄 FREQUENTLY REFERENCED

- `.github/instructions/hot-reload-based-builds.instructions.md` - **Development workflow**
- `ai-instructions/development-workflow.md` - **Work organization**
- `.github/instructions/common-sense.instructions.md` - **Problem solving approach**

---

## 📖 How to Use This Index

1. **Identify your scenario** from the categories above
2. **Check the priority instructions** for always-required practices
3. **Read the recommended instruction file** for detailed guidance
4. **Follow the sequential steps** outlined in each instruction file

**Remember**: Always use sequential-thinking tool to plan your work and maintain scratchpad notes for complex tasks.
