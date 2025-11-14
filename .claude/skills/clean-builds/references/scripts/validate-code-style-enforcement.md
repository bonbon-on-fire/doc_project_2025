# validate-code-style-enforcement.ps1

## Purpose

Validate and enforce code style build settings (`EnforceCodeStyleInBuild`) across all projects to enable IDE warnings (like IDE0005 - unused imports) during `dotnet build`.

## Overview

This script ensures all projects in your solution have `EnforceCodeStyleInBuild` enabled in their `.csproj` files. This setting is critical for detecting IDE code style violations (like IDE0005 - unused using statements) during build time, not just in the IDE.

## What It Does

1. Scans all `.csproj` files in the solution
2. Checks if `EnforceCodeStyleInBuild` is set to `true`
3. Reports which projects are missing this setting
4. Optionally enables it automatically for all projects

## Requirements

- PowerShell 5.0 or later
- .NET SDK with project files (.csproj)
- Write permissions for .csproj files (when using `-Enforce`)

## Usage

### Check Projects (Read-Only)

Verify which projects have enforcement enabled:

```pwsh
pwsh ../scripts/validate-code-style-enforcement.ps1
```

### Enable Enforcement

Automatically enable `EnforceCodeStyleInBuild` in all projects:

```pwsh
pwsh ../scripts/validate-code-style-enforcement.ps1 -Enforce
```

This modifies `.csproj` files to add the required setting.

### Check Only (Explicit)

Same as default, but explicit:

```pwsh
pwsh ../scripts/validate-code-style-enforcement.ps1 -CheckOnly
```

### Export Results

Save the report to a file:

```pwsh
# JSON format
pwsh ../scripts/validate-code-style-enforcement.ps1 -OutputFormat Json -SaveToFile style-report.json

# Summary format
pwsh ../scripts/validate-code-style-enforcement.ps1 -OutputFormat Summary
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Enforce` | Switch | False | Enable EnforceCodeStyleInBuild in all projects |
| `-CheckOnly` | Switch | False | Check without making changes (default behavior) |
| `-OutputFormat` | String | Console | Output format: Console, Json, or Summary |
| `-SaveToFile` | String | None | Path to save output file |

## Why This Matters

### Without EnforceCodeStyleInBuild

IDE warnings (IDE*) are **not reported during build**:

```pwsh
dotnet build
# Build succeeds, but IDE0005 (unused imports) are not reported
# Warnings only appear in your IDE (Visual Studio, Rider, VS Code)
```

### With EnforceCodeStyleInBuild

IDE warnings **are reported during build**:

```pwsh
dotnet build
# Build shows:
# warning IDE0005: Using directive is unnecessary (src/Services/ChatService.cs:5)
# warning IDE0005: Using directive is unnecessary (src/Services/ChatService.cs:7)
```

This ensures:
- ✅ CI/CD catches style violations
- ✅ All developers see same warnings
- ✅ Consistent code quality across team
- ✅ Warnings fixed before commit

## Output Formats

### Console (Default)

Colored report with project listing:

```
=====================================
Code Style Enforcement Validation
=====================================
Total Projects: 9
Projects with enforcement enabled: 6
Projects without enforcement: 3

Projects WITHOUT Enforcement:
  ❌ server/AIChat.Tests/AIChat.Tests.csproj
  ❌ server/AIChat.Integration.Tests/AIChat.Integration.Tests.csproj
  ❌ server/AIChat.LoadTesting/AIChat.LoadTesting.csproj

Recommendation: Run with -Enforce flag to enable in all projects
```

### After Running with -Enforce

```
=====================================
Code Style Enforcement Validation
=====================================
Total Projects: 9
Projects updated: 3
Projects already configured: 6

Updated Projects:
  ✅ server/AIChat.Tests/AIChat.Tests.csproj
  ✅ server/AIChat.Integration.Tests/AIChat.Integration.Tests.csproj
  ✅ server/AIChat.LoadTesting/AIChat.LoadTesting.csproj

All projects now have code style enforcement enabled!
```

### JSON

Structured data for automation:

```json
{
  "summary": {
    "totalProjects": 9,
    "withEnforcement": 6,
    "withoutEnforcement": 3,
    "updated": 0
  },
  "projects": [
    {
      "path": "server/AIChat.Tests/AIChat.Tests.csproj",
      "hasEnforcement": false,
      "relativePath": "server/AIChat.Tests/"
    }
  ]
}
```

### Summary

Quick statistics only:

```
Code Style Enforcement Summary
==============================
Total Projects: 9
Enabled: 6
Not Enabled: 3
```

## Exit Codes

- `0` - Success (all projects have enforcement enabled)
- `1` - Failure (projects missing enforcement and -Enforce not used)

## How It Modifies Project Files

The script adds this property to the first `<PropertyGroup>` in each `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <!-- Script adds this line -->
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

## Common Scenarios

### First-Time Setup

Enable enforcement as part of initial clean-builds setup:

```pwsh
# Step 1: Enable enforcement (this script)
pwsh ../scripts/validate-code-style-enforcement.ps1 -Enforce

# Step 2: Add Roslynator analyzers
pwsh ../scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 3: Build to see all warnings
pwsh ../scripts/build_and_group_errors_and_warnings.ps1
```

### Pre-Commit Validation

Always validate before committing:

```pwsh
# Part of the complete pre-commit workflow
pwsh ../scripts/validate-code-style-enforcement.ps1 -Enforce
pwsh ../scripts/validate-package-versions.ps1
pwsh ../scripts/format-code.ps1
pwsh ../scripts/build_and_group_errors_and_warnings.ps1
```

### CI/CD Pipeline

Verify all projects have enforcement enabled:

```yaml
- name: Validate Code Style Enforcement
  run: pwsh ../scripts/validate-code-style-enforcement.ps1
  # Fails if projects don't have enforcement
```

### After Adding New Projects

Check new projects after adding to solution:

```pwsh
# Check which projects need enforcement
pwsh ../scripts/validate-code-style-enforcement.ps1

# Enable if needed
pwsh ../scripts/validate-code-style-enforcement.ps1 -Enforce
```

## Troubleshooting

### "IDE0005 warnings not appearing during build"

**Problem**: Still not seeing IDE0005 after running with `-Enforce`.

**Solutions**:
1. Verify setting was added:
   ```pwsh
   pwsh ../scripts/validate-code-style-enforcement.ps1
   # Should show all projects with enforcement
   ```

2. Rebuild solution (clean build):
   ```pwsh
   dotnet clean
   dotnet build
   ```

3. Check .editorconfig has rules configured

4. Restart IDE if using incremental builds

### "File is read-only"

**Problem**: Can't modify .csproj files.

**Solutions**:
- Check file permissions
- Remove read-only attribute
- Close Visual Studio/Rider (may lock files)
- Run PowerShell as administrator

### "No projects found"

**Problem**: Script can't locate .csproj files.

**Solutions**:
- Ensure you're in the solution root directory
- Verify .csproj files exist
- Check file paths are correct

### "Changes not persisted"

**Problem**: Run with `-Enforce` but changes didn't save.

**Solutions**:
- Check for script errors in output
- Verify write permissions
- Ensure .csproj files are valid XML
- Try running as administrator

## What Happens After Enabling

### Immediate Effects

1. **Next build will show IDE warnings**:
   ```pwsh
   dotnet build
   # Now shows IDE0005, IDE0017, IDE0052, etc.
   ```

2. **CI/CD will catch style violations**:
   - Builds fail if warnings treated as errors
   - Reports show style issues

3. **Team members see consistent warnings**:
   - Same violations for everyone
   - No "works on my machine" for style issues

### Long-Term Benefits

- ✅ **Consistent code quality** across the team
- ✅ **Automated enforcement** in builds
- ✅ **Fewer style violations** in code reviews
- ✅ **Cleaner codebase** over time

## IDE Code Style Rules Enabled

With `EnforceCodeStyleInBuild=true`, these rules are enforced during build:

- **IDE0001**: Simplify name
- **IDE0002**: Simplify member access
- **IDE0003**: Remove 'this' or 'Me' qualification
- **IDE0004**: Remove unnecessary cast
- **IDE0005**: Remove unnecessary import (most common)
- **IDE0007**: Use 'var' instead of explicit type
- **IDE0008**: Use explicit type instead of 'var'
- **IDE0009**: Add 'this' or 'Me' qualification
- **IDE0010**: Add missing cases to switch statement
- **IDE0011**: Add braces
- And 50+ more IDE rules...

See [Warning Codes Guide](../warning-codes-guide.md) for details on specific rules.

## Integration with Clean-Builds Workflow

This script is part of the **one-time setup** phase:

### Setup Phase (Run Once)

1. **→ Enable code style enforcement** (this script)
2. Enable Roslynator analyzers
3. Configure .editorconfig
4. Validate package versions

### Regular Workflow (After Setup)

Code style enforcement is now active. All builds will report IDE warnings.

## Best Practices

1. **Enable enforcement early** - Part of project setup
2. **Commit .csproj changes** - Share with team
3. **Run before committing** - Ensure consistency
4. **Combine with formatting** - Auto-fix violations
5. **Integrate with CI/CD** - Automated enforcement

## After Running This Script

1. **Verify enforcement is enabled**:
   ```pwsh
   pwsh ../scripts/validate-code-style-enforcement.ps1
   # Should show all projects enabled
   ```

2. **Build to see IDE warnings**:
   ```pwsh
   pwsh ../scripts/build_and_group_errors_and_warnings.ps1
   ```

3. **Auto-fix IDE warnings**:
   ```pwsh
   pwsh ../scripts/format-code.ps1
   ```

4. **Commit the .csproj changes**:
   ```pwsh
   git add .
   git commit -m "Enable code style enforcement in all projects"
   ```

## Related Documentation

- [Format Code Script](format-code.md) - Auto-fix IDE warnings
- [Build and Group Errors Script](build-and-group-errors.md) - See warnings during build
- [Warning Codes Guide](../warning-codes-guide.md) - Understand specific IDE warnings
- [One-Time Setup Guide](../one-time-setup-guide.md) - Complete setup checklist
