# Validation Gates System

## 🚨 BLOCKING QUALITY GATES - MANDATORY EXECUTION

### ⚡ CRITICAL: Continuous Validation System (4 Levels)

This system transforms optional quality checks into **BLOCKING REQUIREMENTS**. No task progression without validation success.

### 📋 VALIDATION EXECUTION POINTS

#### 🚫 EXECUTE Level 0: After Every File Save (< 30 seconds)
```powershell
# MANDATORY after any code change
pwsh scripts/validate-file-change.ps1

# Exit code 0 = Continue | Exit code 1 = BLOCKED
# IF FAILS: Fix compilation errors immediately
```
**Blocking Criteria:** Zero compilation errors required to continue

#### 🚫 EXECUTE Level 1: After Implementation Steps (< 5 minutes)  
```powershell
# MANDATORY after completing any implementation work
pwsh scripts/validate-implementation-step.ps1

# Exit code 0 = Continue | Exit code 1 = BLOCKED
# IF FAILS: Fix build/test failures before next step
```
**Blocking Criteria:** Build + Tests must pass to continue

#### 🚫 EXECUTE Level 2: Before Task Completion (< 15 minutes)
```powershell
# MANDATORY before marking any task complete  
pwsh scripts/quality-check.ps1

# Exit code 0 = Complete Task | Exit code 1 = TASK BLOCKED
# IF FAILS: ALL quality gates must pass - NO EXCEPTIONS
```
**Blocking Criteria:** All quality gates PASS required for task completion

#### 🚫 EXECUTE Level 3: Before Commits (Full validation)
```powershell  
# MANDATORY before any git commit
pwsh scripts/validate-pre-commit.ps1

# Exit code 0 = Commit Approved | Exit code 1 = COMMIT BLOCKED
# IF FAILS: Must pass ALL validation levels to commit
```
**Blocking Criteria:** Full system validation PASS required for commits

### 🔄 AUTOMATED ROLLBACK PROCEDURES

#### 🚨 When ANY Validation Level Fails:

**Option 1: Stash and Restart**
```powershell
# Save work in progress
git stash push -m "WIP: validation failure at Level X"
# Fix issues and continue
```

**Option 2: Reset to Known Good State** 
```powershell
# Nuclear option - lose current changes
git reset --hard HEAD
# Start implementation again  
```

**Option 3: Selective Revert**
```powershell  
# Revert specific problematic commits
git revert <commit-hash>
# Fix specific issues
```

### ⚠️ BLOCKING ENFORCEMENT RULES

1. **Level 0 Failure** → Cannot save/continue until compilation fixed
2. **Level 1 Failure** → Cannot proceed to next implementation step  
3. **Level 2 Failure** → Cannot mark task complete - TASK BLOCKED
4. **Level 3 Failure** → Cannot commit - COMMIT BLOCKED

**CRITICAL**: Never override or bypass validation failures. System integrity depends on quality gate adherence.

### 📊 VALIDATION SCRIPT REFERENCE

#### Available Validation Scripts in `scripts/` directory:

| Level | Script | When to Use | Max Time | Purpose |
|-------|--------|-------------|----------|---------|
| **Level 0** | `validate-file-change.ps1` | After every file save | 30s | Quick build check |
| **Level 1** | `validate-implementation-step.ps1` | After implementation steps | 5m | Build + test validation |
| **Level 2** | `quality-check.ps1` | Before task completion | 15m | Comprehensive quality gates |  
| **Level 3** | `validate-pre-commit.ps1` | Before git commits | Full | Complete system validation |

#### Script Exit Codes:
- **0**: PASSED - Continue progression
- **1**: FAILED - BLOCKED until fixed

### 🎯 VALIDATION LEVEL ENFORCEMENT

#### When Each Level Is REQUIRED:

1. **Level 0**: ⚡ After ANY code change/file save
2. **Level 1**: 🔄 After completing ANY implementation work  
3. **Level 2**: 📋 Before marking ANY task complete
4. **Level 3**: 🚫 Before ANY git commit

**NO EXCEPTIONS** - Validation failures prevent progression

### 📋 TASK COMPLETION REQUIREMENTS

#### 🚫 BLOCKING TASK COMPLETION CHECKLIST

**Every task MUST execute and PASS all validation levels before completion:**

**Level 0** ✅ → **Level 1** ✅ → **Level 2** ✅ → **Task Complete**

#### Before Marking ANY Task Complete:

1. **🚫 EXECUTE Level 2**: `pwsh scripts/quality-check.ps1`
   - **Exit code MUST be 0** to proceed
   - **If exit code 1**: TASK BLOCKED until fixed

2. **Verify All Implementation Complete**: 
   - All task checklist items marked [x]
   - All acceptance criteria met
   - Documentation updated (if applicable)

3. **Ready for Commit**:
   - **🚫 EXECUTE Level 3**: `pwsh scripts/validate-pre-commit.ps1`
   - **Exit code MUST be 0** to commit
   - **If exit code 1**: COMMIT BLOCKED until fixed

### 🛡️ Validation Enforcement Rules

#### Absolute Requirements (No Exceptions)
1. **Every code change** must pass Level 0 validation (< 30s)
2. **Every implementation step** must pass Level 1 validation (< 5m)
3. **Every task completion** must pass Level 2 validation (< 15m)
4. **Every commit** must pass Level 3 validation
5. **All formatting** must use `format-code.ps1`, not `dotnet format`

#### Progression Blocking Rules
- **Level 0 Failure** → Cannot continue coding until compilation fixed
- **Level 1 Failure** → Cannot proceed to next implementation step
- **Level 2 Failure** → Cannot mark task complete  
- **Level 3 Failure** → Cannot commit to repository

#### Developer Workflow Commands

**Quick Quality Check**: `pwsh scripts/quality-check.ps1`
**Format and Validate**: `pwsh format-code.ps1 && pwsh scripts/validate-implementation-step.ps1`
**Complete Task Check**: `pwsh scripts/quality-check.ps1` (must return exit code 0)
**Pre-Commit Check**: `pwsh scripts/validate-pre-commit.ps1` (must return exit code 0)

#### Recovery Commands (When Validation Fails)

**Quick Fix Workflow**:
```powershell
# Fix compilation issues
pwsh format-code.ps1
pwsh scripts/validate-file-change.ps1

# Fix build/test issues  
dotnet build && dotnet test
pwsh scripts/validate-implementation-step.ps1

# Fix quality issues
pwsh scripts/quality-check.ps1
```

**Emergency Rollback**:
```powershell
# Save current work
git stash push -m "WIP: validation failure recovery"

# Reset to clean state  
git reset --hard HEAD

# Restart from known good state
pwsh scripts/validate-implementation-step.ps1  # Verify system health
```