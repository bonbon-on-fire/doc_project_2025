# Validation Gates - 4-Level System

**CRITICAL**: These scripts control development progression and are BLOCKING. Exit codes determine whether work can continue.

## 🚦 Validation Levels Overview

| Level | When | Duration | Purpose | Action on Failure |
|-------|------|----------|---------|-------------------|
| **0** | After every file save | < 30 sec | Quick build check | Fix immediately |
| **1** | After implementation steps | < 5 min | Build + test validation | Fix before continuing |
| **2** | Before task completion | < 15 min | Comprehensive quality gates | Task BLOCKED |
| **3** | Before git commits | Full validation | Complete system validation | Commit BLOCKED |

## 📋 Level Details

### Level 0: After Every File Save
```bash
pwsh scripts/validate-file-change.ps1
```

**Purpose**: Quick compilation check
**Duration**: < 30 seconds
**Checks**:
- C# compilation passes
- TypeScript compilation passes
- Basic syntax validation

**Exit Codes**:
- **Exit 0**: Continue working ✅
- **Exit 1**: Fix compilation errors immediately ❌

### Level 1: After Implementation Steps
```bash
pwsh scripts/validate-implementation-step.ps1
```

**Purpose**: Build + test validation
**Duration**: < 5 minutes
**Checks**:
- Full project builds
- Unit tests pass
- Basic integration tests pass
- No critical warnings

**Exit Codes**:
- **Exit 0**: Continue to next step ✅
- **Exit 1**: Fix build/test failures ❌

### Level 2: Before Task Completion
```bash
pwsh scripts/quality-check.ps1
```

**Purpose**: Comprehensive quality gates
**Duration**: < 15 minutes
**Checks**:
- All tests pass (unit + integration)
- Code quality metrics pass
- Security checks pass
- Performance benchmarks met
- Documentation requirements met

**Exit Codes**:
- **Exit 0**: Task can be completed ✅
- **Exit 1**: Task BLOCKED until fixed ❌

### Level 3: Before Git Commits
```bash
pwsh scripts/validate-pre-commit.ps1
```

**Purpose**: Complete system validation
**Duration**: Full validation (10-30 minutes)
**Checks**:
- All previous levels pass
- E2E tests pass
- Orleans integration tests pass
- Code formatted properly
- No TODOs or debug code
- Breaking change analysis

**Exit Codes**:
- **Exit 0**: Commit approved ✅
- **Exit 1**: Commit BLOCKED ❌

## 🛠️ Using Validation Gates

### Basic Workflow
```bash
# After changing a file
pwsh scripts/validate-file-change.ps1

# After completing implementation work
pwsh scripts/validate-implementation-step.ps1

# Before marking task complete
pwsh scripts/quality-check.ps1

# Before committing
pwsh scripts/validate-pre-commit.ps1
```

### Validation with Build Scripts
Build scripts automatically run validation:
```bash
# These scripts include pre-flight validation
pwsh build-and-start-server.ps1   # Runs Level 1 validation first
pwsh build-and-start-client.ps1   # Runs Level 1 validation first
```

## ⚠️ Enforcement Rules

### NEVER Bypass Validation Failures
System integrity depends on adherence to validation gates:

1. **Never bypass validation failures** - Fix issues immediately
2. **Never accumulate validation debt** - Don't work on new features while validation fails
3. **Never commit if Level 3 fails** - No exceptions

### Progression Blocking
- **Level 0 failure**: Cannot save work confidently
- **Level 1 failure**: Cannot continue to next implementation step
- **Level 2 failure**: Cannot mark task as complete
- **Level 3 failure**: Cannot commit to git

## 🔧 Troubleshooting Validation

### Common Failure Patterns

#### Level 0 Failures (Compilation)
```bash
# Check specific errors
pwsh scripts/validate-file-change.ps1 2>&1 | Tee-Object -FilePath validation-errors.log

# Common fixes
dotnet restore
npm install
```

#### Level 1 Failures (Build/Test)
```bash
# Detailed test output
dotnet test --verbosity normal
npm run test:unit -- --verbose

# Check test environment
$env:ASPNETCORE_ENVIRONMENT="Test"
```

#### Level 2 Failures (Quality)
```bash
# Code formatting
pwsh scripts/format-code.ps1

# Manual quality review
# Check for code smells, TODOs, debug statements
```

#### Level 3 Failures (Pre-commit)
```bash
# Full diagnostic
pwsh scripts/validate-pre-commit.ps1 --verbose

# Orleans-specific issues
# Stop any running Orleans processes
taskkill /IM "dotnet.exe" /F
```

### Rollback Procedures
When validation cannot be fixed quickly:

```bash
# Save current work
git stash push -m "WIP: validation failure - $(Get-Date)"

# Nuclear option (lose unsaved work)
git reset --hard HEAD

# Selective revert
git checkout -- <specific-files>

# Verify clean state
pwsh scripts/validate-file-change.ps1
```

## 📊 Validation Monitoring

### Check Validation History
```bash
# Recent validation results
Get-Content logs/validation/validation-*.log | Select-Object -Last 50

# Validation success rate
pwsh scripts/validation-stats.ps1
```

### Performance Tracking
Track validation performance over time:
- Level 0: Should stay < 30 seconds
- Level 1: Should stay < 5 minutes
- Level 2: Should stay < 15 minutes
- Level 3: Monitor for regression

## 🔄 Integration with Development Workflow

### Validation in IDEs
- **VS Code**: Configure tasks to run validation
- **Cursor**: Rules will remind about validation
- **Command line**: Always available

### Validation in CI/CD
Pipeline mirrors local validation:
1. Level 0: Quick smoke tests
2. Level 1: Build and unit tests
3. Level 2: Integration and quality gates
4. Level 3: Full system validation

## 📋 Validation Checklist

### Before Starting Work
- [ ] Clean validation state: All levels pass
- [ ] Environment properly configured
- [ ] Dependencies up to date

### During Development
- [ ] Run Level 0 after each file save
- [ ] Run Level 1 after each implementation step
- [ ] Never proceed with failing validation

### Before Task Completion
- [ ] Level 2 validation passes
- [ ] All tests pass
- [ ] Code formatted with format-code.ps1
- [ ] Documentation updated

### Before Committing
- [ ] Level 3 validation passes
- [ ] No debug code or TODOs
- [ ] Commit message follows conventions
- [ ] Breaking changes documented

## 🚨 Emergency Overrides

**NEVER use emergency overrides unless:**
- Critical production issue
- Approved by team lead
- Documented with justification
- Immediate follow-up task created

```bash
# Emergency commit (RARELY used)
git commit --no-verify -m "EMERGENCY: Critical hotfix - validation bypass approved by [NAME]"
```

## 📚 Related Resources

- **Validation scripts**: `scripts/validate-*.ps1`
- **Build troubleshooting**: [../30-debug/build-failures.md](../30-debug/build-failures.md)
- **Implementation workflow**: [../20-implement/workflow.md](../20-implement/workflow.md)