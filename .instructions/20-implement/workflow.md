# Implementation Workflow

Complete step-by-step process for implementing features or fixes.

## 🎯 Prerequisites

1. **Understand the task**: Read requirements, specs, or issue description
2. **Investigate codebase**: Use [../10-investigate/start.md](../10-investigate/start.md) if needed
3. **Create scratchpad session**: `scratchpad/implement-{feature}-{date}/`

## 🔄 Implementation Process

### Phase 1: Planning (MANDATORY)
```bash
# Create work directory
mkdir scratchpad/implement-{feature}-$(date +%Y%m%d)
cd scratchpad/implement-{feature}-$(date +%Y%m%d)
```

Create `checklist.md` with:
- [ ] Requirements understood
- [ ] Architecture investigated
- [ ] Implementation approach defined
- [ ] Test strategy planned
- [ ] Validation plan created

### Phase 2: Implementation

#### Step 1: Code Changes
Follow standards from [../50-standards/index.md](../50-standards/index.md):
- **SOLID principles**: Single responsibility, open/closed, etc.
- **Minimal changes**: Only modify what's necessary
- **Testable code**: Write with testing in mind
- **Error handling**: Early returns, proper validation

#### Step 2: Validation Gates (MANDATORY)

**After EVERY file save:**
```bash
pwsh scripts/validate-file-change.ps1  # Level 0 (< 30 sec)
```

**After implementation step:**
```bash
pwsh scripts/validate-implementation-step.ps1  # Level 1 (< 5 min)
```

### Phase 3: Testing & Quality

#### Step 3: Testing
```bash
# Run relevant tests
dotnet test  # Backend tests
npm run test:unit  # Frontend unit tests
npm run test:e2e   # E2E tests (if applicable)
```

#### Step 4: Code Quality Check
```bash
pwsh scripts/quality-check.ps1  # Level 2 (< 15 min)
```

#### Step 5: Code Formatting (REQUIRED)
```bash
pwsh scripts/format-code.ps1  # NEVER skip this
```

### Phase 4: Final Validation

#### Step 6: Pre-commit Validation
```bash
pwsh scripts/validate-pre-commit.ps1  # Level 3 (full validation)
```

## 🚦 Validation Rules

### NEVER proceed if validation fails
- **Level 0 failure**: Fix compilation errors immediately
- **Level 1 failure**: Fix build/test failures before continuing
- **Level 2 failure**: Task BLOCKED until fixed
- **Level 3 failure**: Commit BLOCKED

### Rollback Procedures
```bash
# If validation fails and cannot be fixed quickly:
git stash push -m "WIP: validation failure"  # Save work
git reset --hard HEAD                        # Nuclear option
git checkout -- <specific-files>             # Selective revert
```

## 📋 Implementation Checklist

### Code Quality
- [ ] Follows SOLID, DRY, KISS principles
- [ ] Minimal code changes for the task
- [ ] No code duplication
- [ ] No code smells (long functions, large classes)
- [ ] Well-commented complex logic
- [ ] Early returns for error conditions

### Testing
- [ ] Unit tests for new code
- [ ] Existing tests still pass
- [ ] E2E tests pass (if applicable)
- [ ] Manual testing completed

### Validation
- [ ] Level 0: Compilation passes
- [ ] Level 1: Build and basic tests pass
- [ ] Level 2: Quality gates pass
- [ ] Level 3: Full validation passes
- [ ] Code formatted with format-code.ps1

### Documentation
- [ ] Complex work documented in scratchpad
- [ ] Architecture decisions recorded
- [ ] Breaking changes noted

## 🛠️ Common Implementation Patterns

### Frontend (SvelteKit)
- Use existing stores in `client/src/lib/stores/`
- Follow component patterns in `client/src/lib/chat/`
- Update types in `shared/types/` if needed

### Backend (ASP.NET + Orleans)
- Add controllers in `server/Controllers/`
- Orleans grains in `server/AIChat.Orleans/Grains/`
- Update SignalR hub if real-time needed

### Database Changes
- Add migrations: `dotnet ef migrations add {Name}`
- Update entities in `server/Models/`
- Test migration in clean database

## 🆘 Troubleshooting

- **Build errors**: [../30-debug/build-failures.md](../30-debug/build-failures.md)
- **Test failures**: [../30-debug/test-failures.md](../30-debug/test-failures.md)
- **Validation stuck**: [../40-validate/troubleshooting.md](../40-validate/troubleshooting.md)

## 🔄 After Implementation

1. **Validate everything passes**: All 4 levels (0-3)
2. **Document learnings**: Update scratchpad with gotchas
3. **Review checklist**: Ensure all items completed
4. **Consider architecture**: Does this change affect other areas?

## 📚 Related Resources

- **Code standards**: [../50-standards/index.md](../50-standards/index.md)
- **Debugging guide**: [../30-debug/methodology.md](../30-debug/methodology.md)
- **Validation details**: [../40-validate/gates.md](../40-validate/gates.md)