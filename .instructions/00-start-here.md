# Start Here - Task Navigation

This is your primary entry point for any coding task. Choose your path:

## 🚦 What do you need to do?

### 🔍 **Investigate/Understand the Codebase**
→ Read: [10-investigate/start.md](10-investigate/start.md)
- Explore code structure
- Understand architecture
- Find existing implementations

### 🛠️ **Implement a Feature/Fix**
→ Read: [20-implement/workflow.md](20-implement/workflow.md)
- Complete step-by-step implementation process
- Code standards and validation gates
- Testing requirements

### 🐛 **Debug an Issue**
→ Read: [30-debug/methodology.md](30-debug/methodology.md)
- 6-step debugging methodology
- Logging and diagnostic tools
- Common issue patterns

### ✅ **Validate Changes**
→ Read: [40-validate/gates.md](40-validate/gates.md)
- 4-level validation system (mandatory)
- Quality checks and formatting
- Pre-commit requirements

### 📚 **Follow Code Standards**
→ Read: [50-standards/index.md](50-standards/index.md)
- SOLID principles
- Naming conventions
- Framework-specific patterns

## 🚀 Quick Commands

```bash
# Start development
pwsh build-and-start-server.ps1        # Server + Orleans
pwsh build-and-start-client.ps1        # Client

# Validation (run after each change)
pwsh scripts/validate-file-change.ps1   # Level 0 (after save)
pwsh scripts/quality-check.ps1          # Level 2 (before complete)

# Code Quality Sequence (MANDATORY - exact order before commits)
pwsh scripts/format-code.ps1                      # Step 1: Fix formatting
pwsh scripts/build_and_group_errors_and_warnings.ps1  # Step 2: Check ALL warnings
```

**🚨 CRITICAL**: NEVER commit without running format-code.ps1 THEN build_and_group_errors_and_warnings.ps1. The build script captures ALL code style warnings that must be addressed.

## 📋 Emergency Procedures

**Build failing?** → [30-debug/build-failures.md](30-debug/build-failures.md)
**Tests failing?** → [30-debug/test-failures.md](30-debug/test-failures.md)
**Validation blocked?** → [40-validate/troubleshooting.md](40-validate/troubleshooting.md)

---

💡 **Always start with the appropriate numbered directory above. Each contains everything you need for that task type.**