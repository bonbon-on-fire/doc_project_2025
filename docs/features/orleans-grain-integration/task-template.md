# Task Template

## Standard Format for New Tasks

```markdown
### ORL-PX-XXX: [Task Name] [Status Badge]
**Points**: X | **Priority**: [Critical/High/Medium/Low] | **Depends**: ORL-PX-XXX
**Assignee**: TBD | **Updated**: YYYY-MM-DD

**Requirements**:
- [ ] Specific functional requirement 1
- [ ] Specific functional requirement 2
- [ ] Specific functional requirement 3

**Acceptance Criteria**:
- [ ] Tests: All unit tests pass with >90% coverage
- [ ] Build: Debug and Release configurations build without warnings
- [ ] Performance: [Specific metric, e.g., "Response time < 100ms"]
- [ ] Documentation: API documentation and README updated

**Validation**: Level 2 before completion, Level 3 before commit
**Reference**: [Design Section](design.md#relevant-section)
```

## Status Badges
- 🔴 Not Started
- 🟡 In Progress  
- ✅ Complete
- ❌ Blocked

## Example Task

```markdown
### ORL-P2-001: Add SignalR Dependencies 🔴
**Points**: 2 | **Priority**: Critical | **Depends**: Phase 1 Complete
**Assignee**: TBD | **Updated**: 2024-08-31

**Requirements**:
- [ ] Add Microsoft.AspNetCore.SignalR package
- [ ] Add SignalR client packages to client project
- [ ] Configure SignalR in Program.cs
- [ ] Configure CORS for SignalR connections

**Acceptance Criteria**:
- [ ] Tests: SignalR endpoint responds to connection requests
- [ ] Build: No package version conflicts
- [ ] Performance: WebSocket connection establishes < 500ms
- [ ] Documentation: Configuration documented in README

**Validation**: Level 2 before completion, Level 3 before commit
**Reference**: [SignalR Setup](design.md#signalr-configuration)
```

## Guidelines

1. **Keep it concise**: Each task should fit on one screen
2. **Be specific**: Use measurable acceptance criteria
3. **No duplication**: Reference external docs instead of repeating
4. **Action-focused**: Use checkbox format for easy tracking
5. **Clear dependencies**: Always specify what must complete first

## Validation References

Instead of repeating validation details in each task:
- See [Validation Gates](./validation-gates.md) for complete validation system
- Level 0: After file saves (compilation check)
- Level 1: After implementation steps (build + test)
- Level 2: Before task completion (quality gates)
- Level 3: Before commits (full validation)

## When to Update

- **Status Badge**: Update when work begins/completes/blocks
- **Updated Date**: Change whenever significant progress occurs
- **Assignee**: Add when someone takes ownership
- **Checkboxes**: Mark complete as work progresses