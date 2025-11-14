# One-Time Setup Guide for Clean Builds

This guide walks you through the initial setup required before using the clean-builds skill.

## Overview

The clean-builds skill requires four tools to be properly configured:

1. **EnforceCodeStyleInBuild** - Enables IDE warnings during build
2. **Roslynator.Analyzers** - 200+ code quality analyzers
3. **EditorConfig** - Team-wide code style settings
4. **Package Version Validation** - Ensures NuGet consistency

## Setup Checklist

### ☐ Step 1: Enable Code Style Enforcement

**What it does**: Enables `EnforceCodeStyleInBuild` property in all `.csproj` files, allowing IDE warnings (like IDE0005 - unused imports) to appear during `dotnet build`.

**Command**:
```pwsh
pwsh <clean_builds_base_directory>/scripts/validate-code-style-enforcement.ps1 -Enforce
```

**Expected output**:
```
Total Projects: 9
Projects with enforcement enabled: 9
Projects without enforcement: 0
```

**Troubleshooting**:
- If some projects still missing: Run with `-Enforce` again
- If errors occur: Check that .csproj files are not read-only

### ☐ Step 2: Enable Roslynator Analyzers

**What it does**: Adds `Roslynator.Analyzers` NuGet package to all projects, enabling 200+ code quality analyzers to run during build.

**Command**:
```pwsh
pwsh <clean_builds_base_directory>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

**Why `-ExcludeSubmodules`**: External submodule code has its own analyzer configuration. Adding Roslynator to submodules may conflict with upstream settings.

**Expected output**:
```
Total Projects: 9
Already had analyzers: 0 project(s)
Added to: 9 project(s)
```

**Verification**:
```pwsh
# Check all projects have Roslynator
pwsh <clean_builds_base_directory>/scripts/enable-roslynator-analyzers.ps1 -CheckOnly
```

**Troubleshooting**:
- If NuGet restore fails: Run `dotnet restore` manually
- If version conflicts: Check existing analyzer packages

### ☐ Step 3: Configure EditorConfig

**What it does**: Creates or updates `.editorconfig` at solution root with Roslynator severity settings and code style preferences.

**Command**:
```pwsh
pwsh <clean_builds_base_directory>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
```

**Severity options**:
- `suggestion` - IDE hints only (no build warnings)
- `warning` - Build warnings (recommended for most projects)
- `error` - Build errors (strict enforcement)

**Preview first**:
```pwsh
pwsh <clean_builds_base_directory>/scripts/configure-roslynator-editorconfig.ps1 -ShowPreview
```

**Expected output**:
```
[SUCCESS] Roslynator configuration added to .editorconfig
```

**Troubleshooting**:
- If ".editorconfig already has Roslynator": Remove old config or skip this step
- If IDE not respecting settings: Restart IDE

### ☐ Step 4: Validate Package Versions

**What it does**: Scans all projects for NuGet package version inconsistencies.

**Command**:
```pwsh
pwsh <clean_builds_base_directory>/scripts/validate-package-versions.ps1
```

**Expected output** (ideal):
```
Consistent Packages: 140
Inconsistent Packages: 0
CRITICAL issues: 0
```

**If issues found**: See `references/package-version-management.md` for fixing guide.

## Verification

After completing all setup steps, verify everything is configured:

```pwsh
# 1. Check code style enforcement
pwsh <clean_builds_base_directory>/scripts/validate-code-style-enforcement.ps1

# 2. Check Roslynator analyzers
pwsh <clean_builds_base_directory>/scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# 3. Check .editorconfig
pwsh <clean_builds_base_directory>/scripts/configure-roslynator-editorconfig.ps1 -ShowPreview

# 4. Check package versions
pwsh <clean_builds_base_directory>/scripts/validate-package-versions.ps1
```

## Commit the Changes

The setup modifies project files and creates/updates configuration:

```pwsh
git status
# Should show:
#   modified: server/**/*.csproj (9 projects)
#   modified: .editorconfig (or new file)

git add .
git commit -m "Configure clean-builds tooling: Roslynator, code style enforcement, EditorConfig"
```

## First Build After Setup

**Expect many warnings** on the first build after enabling Roslynator:

```pwsh
pwsh <clean_builds_base_directory>/scripts/build_and_group_errors_and_warnings.ps1
```

This is normal! You're now detecting issues that were previously hidden. Follow the clean-builds workflow to fix them systematically.

## Next Steps

With setup complete, proceed to the regular clean-builds workflow:
1. Validate package versions
2. Format code
3. Build and fix warnings
4. Repeat until clean

See `SKILL.md` for the complete workflow.
