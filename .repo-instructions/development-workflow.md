# Development Workflow Guidelines

## Essential Work Practices

### Scratchpad Usage
**You MUST use scratchpad to keep your notes.** If scratchpad is not used, you tend to forget the learnings.

- Create a directory in `scratchpad/` for each session
- Name directories relevant to current work
- Store checklists, learnings, and experiments

### Organization Techniques

#### Use Checklists
- Create and maintain checklists in scratchpad
- Track work progress systematically
- Define "done" criteria for each item

#### Per Session Directory Structure
```
scratchpad/
├── session-2025-01-15-taskmanager/
│   ├── checklist.md
│   ├── learnings.md
│   ├── test-scripts/
│   └── debug-notes.md
```

#### Sequential Thinking
**You MUST use sequential thinking to organize thoughts.** It increases accuracy significantly.
- Break complex problems into steps
- Document reasoning at each step
- Validate assumptions before proceeding

## Development Modes

This project includes specialized chat mode instructions in `.github/chatmodes/` for different development workflows.

### 📝 Specification Writer Mode
**File**: `.github/chatmodes/spec-writer.chatmode.md`

**Use for**: Creating new feature specifications and requirements
- Gathering requirements through iterative questioning
- Researching existing solutions
- Creating structured specs in `docs/features/`
- Understanding current implementation

### 🏗️ Spec to Design & Tasks Mode
**File**: `.github/chatmodes/spec-n-design-doc-to-tasks.chatmode.md`

**Use for**: Converting requirements into actionable tasks
- Creating design documents from specifications
- Breaking down features into manageable tasks
- Planning implementation approach
- Generating task lists with acceptance criteria

### 👨‍💻 Senior Developer Mode
**File**: `.github/chatmodes/senior-developer.chatmode.md`

**Use for**: Implementing with high code quality
- Working on clearly specified tasks
- Following SOLID, KISS, and DRY principles
- Writing unit tests
- Performing code reviews
- Creating checklists in `scratchpad/{feature-name}/{task-id}/`

### 🔄 Senior Developer Interactive Mode
**File**: `.github/chatmodes/senior-dev-interactive.chatmode.md`

**Use for**: Interactive development with research
- Researching codebase patterns
- Iterative development cycles
- Maintaining development checklists
- Following Research → Develop → Review → Cleanup workflow

## Recommended Feature Development Flow

1. **New Feature**: Start with **Spec Writer Mode**
   - Create requirements document
   - Research existing patterns
   - Document constraints

2. **Planning**: Use **Spec to Design & Tasks Mode**
   - Create design documents
   - Break into tasks
   - Define acceptance criteria

3. **Implementation**: Use **Senior Developer Mode**
   - Execute tasks systematically
   - Write tests
   - Maintain quality standards

4. **Complex Tasks**: Use **Senior Developer Interactive Mode**
   - When extensive research needed
   - For experimental features
   - When refactoring large sections

## Code Quality Standards

Reference these instruction files for specific standards:

- **[naming-types.md](./naming-types.md)** - Variable naming and type declarations
- **[code-quality.md](./code-quality.md)** - Code quality and style guidelines
- **[core-software-principles.md](./core-software-principles.md)** - SOLID, DRY, KISS principles
- **[async-programming.md](./async-programming.md)** - Async/await patterns
- **[data-handling.md](./data-handling.md)** - Data manipulation standards
- **[exception-handling.md](./exception-handling.md)** - Error handling patterns
- **[linq-collections.md](./linq-collections.md)** - LINQ and collection usage
- **[logging.md](./logging.md)** - Logging standards and practices

## Git Workflow

### Branch Naming
- Feature: `user/{username}/feat/{feature-name}`
- Bug fix: `user/{username}/fix/{issue-description}`
- Refactor: `user/{username}/refactor/{component}`

### Commit Messages
- Use conventional commits format
- Include ticket/issue number if applicable
- Keep subject line under 50 characters
- Add body for complex changes

### Before Committing
1. Run unit tests: `dotnet test` / `npm test`
2. Check linting: `npm run lint`
3. Verify build: `dotnet build` / `npm run build`
4. Update documentation if needed

## Continuous Improvement

### Learning Capture
- Document new patterns discovered
- Update scratchpad with insights
- Share learnings in team notes

### Code Review Checklist
- [ ] Tests pass
- [ ] Code follows style guidelines
- [ ] Documentation updated
- [ ] No security issues
- [ ] Performance considered
- [ ] Error handling complete

### Refactoring Approach
1. Identify code smells
2. Write tests for current behavior
3. Refactor incrementally
4. Validate with tests
5. Update documentation