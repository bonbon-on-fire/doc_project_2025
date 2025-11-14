# format-code.ps1

## Purpose

Automatically fix code style issues and apply code analysis corrections across your codebase.

## Overview

The `format-code.ps1` script runs multiple formatting and code analysis tools in sequence to ensure consistent code style and fix common code quality issues. It's designed to be the first step in the clean-builds workflow.

## Tools Used (in order)

1. **dotnet format style** - Applies IDE code style fixes (IDE0032, IDE0017, etc.)
2. **Roslynator CLI** - Advanced code analysis fixes (optional if installed)
3. **ReSharper CLT** - Comprehensive formatting and cleanup
4. **CSharpier** - Opinionated formatting for submodules

## What It Fixes

- Code style violations (patterns, null checks, array initialization)
- Using recommended APIs instead of deprecated ones
- Expression form simplifications
- Unnecessary using statements (IDE0005)
- Code organization and structure
- Unused imports and namespace cleanup

## Requirements

- `dotnet format` (comes with .NET SDK)
- `JetBrains.ReSharper.GlobalTools` - Install with: `dotnet tool install -g JetBrains.ReSharper.GlobalTools`
- `csharpier` (optional) - Install with: `dotnet tool install -g csharpier`
- `Roslynator.DotNet.Cli` (optional) - Install with: `dotnet tool install -g Roslynator.DotNet.Cli`

## Usage

### Full Format (Default)

Formats the entire codebase including root project and submodules:

```pwsh
pwsh ../scripts/format-code.ps1
```

### Check Only (No Changes)

Verify formatting without making changes:

```pwsh
pwsh ../scripts/format-code.ps1 -CheckOnly
```

This is useful for:
- CI/CD validation
- Pre-commit checks
- Verifying if formatting is needed

### Format Root Project Only

Format only the main project, excluding submodules:

```pwsh
pwsh ../scripts/format-code.ps1 -RootOnly
```

Use when:
- You don't want to modify external dependencies
- Faster formatting during development
- Submodules have their own formatting rules

### Format Submodules Only

Format only external dependencies:

```pwsh
pwsh ../scripts/format-code.ps1 -SubmodulesOnly
```

Use when:
- You've updated submodules and need to format them
- Testing submodule formatting separately

### Show Help

Display usage information:

```pwsh
pwsh ../scripts/format-code.ps1 -Help
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-CheckOnly` | Switch | False | Report issues without fixing them |
| `-RootOnly` | Switch | False | Format only the main project |
| `-SubmodulesOnly` | Switch | False | Format only external dependencies |
| `-Help` | Switch | False | Display usage information |

## How It Works

### Step 1: dotnet format style

Applies .editorconfig rules and fixes IDE code style violations:

```
Running: dotnet format style <solution>
```

**Fixes**:
- IDE0005 (unused imports)
- IDE0017 (inline variable declaration)
- IDE0025 (expression body for properties)
- IDE0028 (collection initializers)
- IDE0032 (auto property)
- And many more IDE-prefixed rules

### Step 2: Roslynator CLI (if installed)

Applies advanced code analysis fixes:

```
Running: roslynator fix <solution> --ignore-compiler-errors --format
```

**Fixes**:
- Code simplifications
- Modern C# pattern usage
- Performance improvements
- Redundant code removal

### Step 3: ReSharper CLT

Comprehensive code cleanup using ReSharper's cleanup profile:

```
Running: jb cleanupcode <solution>
```

**Fixes**:
- Code organization
- Member ordering
- Namespace cleanup
- File header standardization

### Step 4: CSharpier (Submodules)

Applies opinionated formatting to submodules:

```
Running: csharpier <submodule-path>
```

**Fixes**:
- Consistent indentation
- Line length enforcement
- Bracket placement
- Whitespace normalization

## Expected Output

### Successful Run

```
[INFO] Formatting codebase...
[INFO] Running dotnet format style...
[SUCCESS] dotnet format completed

[INFO] Running Roslynator fixes...
[SUCCESS] Roslynator completed (150 fixes applied)

[INFO] Running ReSharper cleanup...
[SUCCESS] ReSharper cleanup completed

[INFO] Formatting complete!
```

### Check-Only Mode

```
[INFO] Checking code formatting...
[INFO] Running dotnet format style (check only)...
[WARN] Found 23 formatting issues
  - src/Services/ChatService.cs: 5 issues
  - src/Controllers/ChatController.cs: 3 issues
  ...

[INFO] No changes made (check-only mode)
```

## Exit Codes

- `0` - Success (no issues or all issues fixed)
- `1` - Failure (issues found in check-only mode, or formatting failed)

## Common Scenarios

### Before Committing

Always format before creating a commit:

```pwsh
pwsh ../scripts/format-code.ps1
git diff  # Review changes
git add .
git commit -m "Your message"
```

### CI/CD Pipeline

Verify formatting in your pipeline:

```yaml
- name: Check Code Formatting
  run: pwsh ../scripts/format-code.ps1 -CheckOnly
  # Fails the build if formatting issues exist
```

### During Development

Quick format after making changes:

```pwsh
# Fast: format only root project
pwsh ../scripts/format-code.ps1 -RootOnly
```

### After Enabling Roslynator Analyzers

Use Roslynator's auto-fix first, then format:

```pwsh
# Auto-fix Roslynator diagnostics
roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format

# Then run full format
pwsh ../scripts/format-code.ps1
```

## Troubleshooting

### "Tool not found" Error

**Problem**: A required tool is not installed.

**Solution**: Install the missing tool:

```pwsh
# ReSharper CLT
dotnet tool install -g JetBrains.ReSharper.GlobalTools

# Roslynator CLI
dotnet tool install -g Roslynator.DotNet.Cli

# CSharpier
dotnet tool install -g csharpier
```

### Formatting Takes Too Long

**Problem**: Formatting is slower than expected.

**Solutions**:
- Use `-RootOnly` to skip submodules during development
- Disable Roslynator CLI if it's not critical
- Run on specific projects instead of the entire solution

### Changes Keep Appearing After Format

**Problem**: Running format multiple times produces different results.

**Possible Causes**:
- Different tools have conflicting rules
- .editorconfig settings conflict with tool defaults
- Tool versions differ from team members

**Solution**:
- Review .editorconfig for conflicts
- Ensure all team members use same tool versions
- Run format twice to reach steady state

### Submodules Get Reformatted Unexpectedly

**Problem**: External code gets modified when it shouldn't.

**Solution**:
- Use `-RootOnly` flag
- Add submodule paths to .editorconfig `[exclude]` section
- Configure `.gitignore` for submodule directories

## Integration with Clean-Builds Workflow

This script is **Step 2** in the recommended workflow:

1. ✅ Validate package versions
2. **→ Format code** (this script)
3. Build and check for warnings
4. Fix any remaining issues
5. Repeat 2-4 until clean

## Best Practices

1. **Format frequently** - Don't accumulate style issues
2. **Run before building** - Catch issues early
3. **Use in CI/CD** - Enforce consistent style across the team
4. **Review changes** - Always use `git diff` after formatting
5. **Commit formatted code separately** - Makes reviews easier

## Related Documentation

- [Build and Group Errors Script](build-and-group-errors.md)
- [Warning Codes Guide](../warning-codes-guide.md)
- [Best Practices](../best-practices.md)
