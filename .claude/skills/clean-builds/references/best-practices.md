# Clean Builds Best Practices

Best practices for achieving and maintaining zero-warning builds with consistent code quality.

## Core Principles

1. **Zero Warnings Before Commit** - Never commit code with warnings
2. **Format Frequently** - Don't accumulate style issues
3. **Fix Warnings Immediately** - Don't let them pile up
4. **Understand Each Warning** - Learn from the messages
5. **Use Tools Properly** - Let automation do the heavy lifting
6. **Team Consistency** - Same standards for everyone

---

## 1. Complete One-Time Setup First

Before using clean-builds for the first time, complete the full setup.

### Why This Matters

Without proper setup:
- ❌ Miss 200+ code quality issues during build
- ❌ IDE warnings don't appear during `dotnet build`
- ❌ Team members have inconsistent settings
- ❌ Can't enforce standards automatically

### Setup Checklist

```pwsh
# Step 1: Enable code style enforcement
pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1 -Enforce

# Step 2: Add Roslynator analyzers
pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 3: Configure .editorconfig
pwsh <skill_base_path>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning

# Step 4: Validate package versions
pwsh <skill_base_path>/scripts/validate-package-versions.ps1
```

### Verify Setup

```pwsh
# Check code style enforcement
pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1

# Check Roslynator analyzers
pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# Check .editorconfig
pwsh <skill_base_path>/scripts/configure-roslynator-editorconfig.ps1 -ShowPreview
```

---

## 2. Format After Every Change

Run `format-code.ps1` regularly during development, not just before commit.

### When to Format

- ✅ After adding new code
- ✅ After refactoring
- ✅ After merging branches
- ✅ Before committing
- ✅ After code review feedback

### Quick Format

```pwsh
# Fast: format only root project during development
pwsh <skill_base_path>/scripts/format-code.ps1 -RootOnly

# Full: before committing
pwsh <skill_base_path>/scripts/format-code.ps1
```

### Benefits

- ✅ Catches issues early
- ✅ Smaller diffs in commits
- ✅ Easier code reviews
- ✅ Less merge conflicts

---

## 3. Fix Warnings Immediately

Don't accumulate warnings—fix them as you encounter them.

### Why This Matters

**Accumulating warnings**:
- ❌ Hard to track which are new vs old
- ❌ Overwhelms developers
- ❌ Hides new issues among noise
- ❌ Technical debt grows

**Fixing immediately**:
- ✅ Always know current status
- ✅ Manageable workload
- ✅ New issues stand out
- ✅ Prevents debt accumulation

### Strategy

1. **Build and check**:
   ```pwsh
   pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1
   ```

2. **Review grouped warnings** - Warnings are grouped by code for batch fixing

3. **Fix one code at a time** - Tackle similar warnings together

4. **Rebuild and verify** - Confirm warnings are gone

5. **Repeat** - Until zero warnings

---

## 4. NEVER Suppress Warnings Without Fixing The Root Cause

### ❌ BAD: Hiding Problems

Using `#pragma warning disable` without fixing the issue:

```csharp
// BAD: Suppressing instead of fixing
#pragma warning disable IDE0051 // Remove unused private members

private void UnusedMethod()
{
    // This method is not needed but we're hiding the warning
}
```

### ✅ GOOD: Fixing Properly

```csharp
// GOOD: Method removed entirely since it's not needed
// (No code needed - just delete the unused method)
```

### When Suppression Seems Necessary

1. **First, try to fix properly**:
   - IDE0051 (unused member) → Remove the member
   - IDE0055 (formatting) → Run the formatter
   - CS0618 (obsolete API) → Update to non-obsolete API
   - IDE0052 (unread field) → Use it or remove it

2. **If warning is truly inappropriate**:
   - Use `.editorconfig` to downgrade severity (not `#pragma`)
   - Document WHY the rule doesn't apply
   - Examples of legitimate cases:
     - Interface members required but not used yet
     - Low-level performance code
     - Test mocks

3. **Never suppress to avoid formatting or code quality fixes**:
   - Use auto-fixers: `roslynator fix`, `dotnet format`
   - Let the tools do the work

### Consequences of Excessive Suppression

- ❌ Warnings accumulate and become unmanageable
- ❌ Real issues hidden among suppressed warnings
- ❌ Code quality degrades
- ❌ Team stops trusting warning system
- ❌ Technical debt grows exponentially

### The Clean-Builds Philosophy

- ✅ Zero warnings through proper fixes, not suppression
- ✅ Auto-fixers do the heavy lifting
- ✅ Manual fixes for non-automatable cases
- ✅ `.editorconfig` for project-wide policy

---

## 5. Understand Each Warning

Read the warning message and documentation before dismissing.

### How to Learn From Warnings

1. **Note the code** (e.g., CA1826, IDE0052)

2. **Read the message** for context

3. **Check help URL** (if provided)

4. **Look up in documentation**:
   - [Warning Codes Guide](warning-codes-guide.md)
   - [Microsoft Docs](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/)

5. **Understand the "why"** - Why is this a problem?

6. **Apply the fix** - Make the change

7. **Remember for next time** - Pattern recognition

### Example: CA1826

```
warning CA1826: Use property instead of Linq method
   at: src/Services/UserService.cs(42)
```

**Lookup**: [Warning Codes Guide](warning-codes-guide.md#ca1826)

**Learn**: `.Where().FirstOrDefault()` creates unnecessary intermediate collections

**Fix**: Use `.FirstOrDefault(predicate)` directly

**Remember**: Always prefer direct methods over chained LINQ

---

## 6. Use Grouped Output

The build script groups warnings by code - use this for batch fixing.

### Example Output

```
[IDE0005] Remove unnecessary using directive (15 occurrences)
  src/Services/ChatService.cs:5
  src/Services/ChatService.cs:7
  src/Controllers/ChatController.cs:3
  ...

[CA1826] Use property instead of Linq method (5 occurrences)
  src/Services/UserService.cs:42
  src/Services/MessageService.cs:18
  ...
```

### Batch Fixing Strategy

1. **Pick one warning code** - Start with the most frequent

2. **Understand the pattern** - Read first occurrence

3. **Fix all similar issues** - Use IDE find/replace if possible

4. **Verify fixes** - Rebuild and check

5. **Move to next code** - Repeat until done

### Benefits

- ✅ Fix similar issues together
- ✅ Learn patterns faster
- ✅ More efficient workflow
- ✅ Clear progress tracking

---

## 7. Validate Package Versions

Run package validation before committing, especially after dependency updates.

### When to Validate

- ✅ Before every commit
- ✅ After `dotnet add package`
- ✅ After updating dependencies
- ✅ Weekly (catch drift)
- ✅ Before releases

### Validation Command

```pwsh
pwsh <skill_base_path>/scripts/validate-package-versions.ps1
```

### Fix Critical Issues Immediately

🔴 **CRITICAL** issues **must be fixed** before proceeding:
- Orleans framework version mismatches
- Major version gaps
- Core framework differences

🟡 **WARNING** issues should be addressed during maintenance:
- Minor version differences
- Patch version variations

### See Also

- [Package Version Management Guide](package-version-management.md)

---

## 8. Pre-Commit Validation

Always run the full workflow before creating a commit.

### Complete Pre-Commit Workflow

```pwsh
# Step 1: Validate and enable code style enforcement
pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1 -Enforce

# Step 2: Validate packages
pwsh <skill_base_path>/scripts/validate-package-versions.ps1
# Fix any CRITICAL issues before proceeding

# Step 3: Format
pwsh <skill_base_path>/scripts/format-code.ps1

# Step 4: Build & Check
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1

# Step 5: Fix any warnings and repeat 3-4 until clean

# Only commit if all validations succeed
git add .
git commit -m "Your message"
```

### Automate With Git Hooks

Create `.git/hooks/pre-commit`:

```bash
#!/bin/bash

# Run validation
pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1 -Enforce || exit 1
pwsh <skill_base_path>/scripts/validate-package-versions.ps1 || exit 1
pwsh <skill_base_path>/scripts/format-code.ps1 || exit 1
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1 || exit 1

echo "✅ All validations passed!"
```

---

## 9. Enable Roslynator Analyzers for Maximum Quality

For comprehensive code quality enforcement, enable Roslynator analyzers in all projects.

### Why Enable

- 🎯 **Proactive detection** - Issues caught during development
- 💡 **IDE integration** - Real-time feedback as you type
- 🔒 **Build enforcement** - Prevents poor-quality code
- 📋 **Comprehensive rules** - 200+ analyzers
- 👥 **Team consistency** - Everyone sees same warnings

### How to Enable (One-Time)

```pwsh
# Step 1: Add analyzers
pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 2: Configure severity
pwsh <skill_base_path>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
```

### Recommended Severity Levels

- **Most projects**: `warning` - Visible but doesn't block builds
- **Strict enforcement**: `error` - Violations fail the build
- **Gradual adoption**: `suggestion` - IDE hints only

### Build-Time vs Format-Time Analysis

| Aspect | Format-time | Build-time (Roslynator) |
|--------|-------------|-------------------------|
| When detected | Only when script runs | Every build + real-time in IDE |
| What happens | Automatically fixes | Reports for you to fix |
| Developer awareness | Only if they run script | Immediate feedback |
| Team enforcement | Manual | Automatic |

**Recommendation**: Enable Roslynator for continuous enforcement + use `format-code.ps1` to batch-fix.

---

## 10. Team Best Practices

Ensure the entire team follows the same standards.

### Commit Configuration

All these files should be in version control:

```
✅ .editorconfig
✅ */.csproj files (with EnforceCodeStyleInBuild and Roslynator)
✅ Directory.Build.props (if using)
✅ .gitignore (build artifacts)
```

### Team Onboarding

New team members should:

1. Clone the repository
2. Run one-time setup scripts
3. Install required tools
4. Verify setup with check scripts
5. Run a test build

### Document Team Standards

Create `docs/coding-standards.md`:

```markdown
# Coding Standards

## Quality Gates

- ✅ Zero warnings before commit
- ✅ All tests pass
- ✅ Code formatted with format-code.ps1
- ✅ Package versions validated

## Tools

- Roslynator analyzers: Enabled
- EnforceCodeStyleInBuild: Enabled
- Severity level: Warning

## Workflow

See `.claude/skills/clean-builds/SKILL.md`
```

### CI/CD Integration

```yaml
# .github/workflows/build.yml

- name: Validate Code Style Enforcement
  run: pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1

- name: Validate Package Versions
  run: pwsh <skill_base_path>/scripts/validate-package-versions.ps1

- name: Format Code
  run: pwsh <skill_base_path>/scripts/format-code.ps1

- name: Build and Check Warnings
  run: pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1
```

---

## Quick Reference

### Daily Workflow

```pwsh
# 1. Format
pwsh <skill_base_path>/scripts/format-code.ps1 -RootOnly

# 2. Build & check
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1

# 3. Fix warnings
# (Use grouped output to batch-fix)

# 4. Repeat until clean
```

### Before Commit

```pwsh
# Full validation
pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1 -Enforce
pwsh <skill_base_path>/scripts/validate-package-versions.ps1
pwsh <skill_base_path>/scripts/format-code.ps1
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1
```

### Weekly Maintenance

```pwsh
# Update Roslynator
dotnet tool update -g Roslynator.DotNet.Cli

# Check for package drift
pwsh <skill_base_path>/scripts/validate-package-versions.ps1

# Review .editorconfig rules
# (Adjust severities based on team feedback)
```

---

## Related Documentation

- [One-Time Setup Guide](one-time-setup-guide.md) - Initial configuration
- [Warning Codes Guide](warning-codes-guide.md) - Understanding specific warnings
- [Roslynator Setup](roslynator-setup.md) - Comprehensive Roslynator guide
- [Troubleshooting](troubleshooting.md) - Common issues
- [Complete Workflow Example](../examples/complete-workflow.md) - Step-by-step walkthrough
