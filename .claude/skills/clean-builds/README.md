# Clean Builds Skill

**Purpose:** Guide developers to achieve zero-warning builds, consistent code style, and NuGet package version consistency through automated formatting, comprehensive build quality checks, and package validation.

## Contents

```
clean-builds/
├── SKILL.md                                            # Main skill documentation (load this in Claude)
├── scripts/
│   ├── validate-code-style-enforcement.ps1           # Validate and enable IDE0005 detection
│   ├── format-code.ps1                               # Code formatting with multiple tools
│   ├── build_and_group_errors_and_warnings.ps1       # Clean build with error/warning analysis
│   ├── validate-package-versions.ps1                 # Package version consistency validator
│   ├── enable-roslynator-analyzers.ps1               # Add Roslynator.Analyzers to all projects
│   └── configure-roslynator-editorconfig.ps1         # Configure Roslynator in .editorconfig
├── references/
│   ├── one-time-setup-guide.md                       # Step-by-step first-time configuration guide
│   ├── warning-codes-guide.md                        # Detailed guide to common warning codes
│   └── package-version-management.md                 # Complete package version management guide
└── README.md                                          # This file
```

## Quick Start

1. **In Claude Code**, load the skill:
   ```
   /skill clean-builds
   ```

2. **Or manually reference** `SKILL.md` for detailed instructions

3. **IMPORTANT: One-time setup** (if not already done):
   ```pwsh
   # Enable code style enforcement
   pwsh scripts/validate-code-style-enforcement.ps1 -Enforce

   # Enable Roslynator analyzers (skip submodules)
   pwsh scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

   # Configure .editorconfig
   pwsh scripts/configure-roslynator-editorconfig.ps1 -Severity warning
   ```

4. **Run the complete workflow**:
   ```pwsh
   # Validate package versions
   pwsh scripts/validate-package-versions.ps1

   # Format code
   pwsh scripts/format-code.ps1

   # Build and check quality
   pwsh scripts/build_and_group_errors_and_warnings.ps1
   ```

## Scripts Overview

### validate-code-style-enforcement.ps1
Validates and enables code style build enforcement (`EnforceCodeStyleInBuild`) in all projects to enable IDE0005 and other style violations during build.

**What it does:**
- Scans all .csproj files for `EnforceCodeStyleInBuild` setting
- Reports which projects are missing the setting
- Optionally enables it automatically across all projects
- Supports multiple output formats (Console, Json, Summary)

**Usage:**
```pwsh
# Check which projects need enforcement
pwsh scripts/validate-code-style-enforcement.ps1

# Automatically enable in all projects
pwsh scripts/validate-code-style-enforcement.ps1 -Enforce

# Export findings to JSON
pwsh scripts/validate-code-style-enforcement.ps1 -OutputFormat Json -SaveToFile style-report.json
```

**Note:** Run with `-Enforce` before formatting and building to ensure IDE0005 detection is enabled.

### format-code.ps1
Formats code using ReSharper CLT and CSharpier. Runs multiple tools in sequence:
- `dotnet format style` - IDE code style fixes (including IDE0005: unused imports)
- `Roslynator CLI` - Advanced code analysis (optional)
- `ReSharper CLT` - Comprehensive formatting
- `CSharpier` - Submodule formatting

**Note:** To enable IDE0005 (unused imports) detection during build, add `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` to your project's `.csproj` PropertyGroup.

### build_and_group_errors_and_warnings.ps1
Performs clean build and groups all errors/warnings by code for systematic fixing.

### validate-package-versions.ps1
Scans all projects for NuGet package version inconsistencies and identifies:
- Critical mismatches (e.g., Orleans framework version conflicts)
- Warning-level variations (patch/minor version differences)
- Projects affected by each inconsistency
- Recommended fixes

### enable-roslynator-analyzers.ps1
Adds the Roslynator.Analyzers NuGet package to all .csproj files in the solution, enabling 200+ code analyzers to run during build.

**What it does:**
- Scans all .csproj files
- Adds Roslynator.Analyzers package with proper configuration
- Supports check-only mode and removal mode
- Multiple output formats (Console, Json, Summary)

**Usage:**
```pwsh
# Add to all projects
pwsh scripts/enable-roslynator-analyzers.ps1

# Check which projects need it
pwsh scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# Remove from all projects
pwsh scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers
```

### configure-roslynator-editorconfig.ps1
Creates or updates .editorconfig file with Roslynator analyzer severity settings and code style preferences.

**What it does:**
- Creates/updates .editorconfig at solution root
- Sets global severity for all Roslynator rules
- Configures code style preferences (var usage, accessibility modifiers, etc.)
- Supports preview mode to see changes before applying

**Usage:**
```pwsh
# Set all rules to 'warning' (default)
pwsh scripts/configure-roslynator-editorconfig.ps1

# Set to 'error' for strict enforcement
pwsh scripts/configure-roslynator-editorconfig.ps1 -Severity error

# Preview changes without applying
pwsh scripts/configure-roslynator-editorconfig.ps1 -ShowPreview
```

## When to Use

- **First-time setup**: Configure all code quality tools in a new project
- Setting up a new project for the first time (enable analyzers)
- Before committing code changes
- When you want build-time enforcement of code quality rules
- When package versions need updating or validation
- During code review to ensure quality
- After significant refactoring
- When checking for NuGet package conflicts
- As part of pre-release validation
- When you need to understand build warnings

## Key Features

✓ **One-time setup automation** - Configure all tools with simple commands
✓ **Roslynator analyzer integration** - 200+ analyzers enforced at build time
✓ **EditorConfig management** - Automated .editorconfig setup for team consistency
✓ **Automated formatting** with multiple tools (ReSharper, Roslynator, dotnet format)
✓ **IDE0005 enforcement** - detect and fix unused imports during build
✓ **Code style enforcement** - optional `EnforceCodeStyleInBuild` for compile-time checks
✓ **Comprehensive warning analysis** grouped by code for easy fixing
✓ **Package version validation** - identifies critical conflicts and inconsistencies
✓ **Zero-warning enforced** - scripts fail if warnings remain
✓ **Multiple output formats** (console, JSON, CSV, summary)
✓ **Clear guidance** on how to fix each warning type and version mismatch
✓ **Pre-commit validation** built-in - comprehensive quality checks
✓ **Exit codes** support CI/CD integration

## Reference Materials

**Warning Codes Guide** (`references/warning-codes-guide.md`):
- What each warning code means
- Examples of bad code
- Recommended fixes
- Why the fix matters
- Bulk fixing strategies

**Package Version Management Guide** (`references/package-version-management.md`):
- Understanding validation reports
- How to fix version mismatches
- Central Package Management (CPM) setup
- Best practices for package consolidation
- Troubleshooting version issues

## Requirements

- PowerShell 5+
- .NET SDK with `dotnet format`
- JetBrains.ReSharper.GlobalTools: `dotnet tool install -g JetBrains.ReSharper.GlobalTools`
- CSharpier (for submodules): `dotnet tool install -g csharpier`
- Roslynator (optional): `dotnet tool install -g Roslynator.DotNet.Cli`

## Exit Codes

Both scripts use exit codes for CI/CD integration:
- `0` - Success (no errors, no warnings)
- `1` - Failure (errors or warnings found)

## Integration

These scripts are designed to integrate with your development workflow:
- Run before `git commit`
- Run in CI/CD pipelines
- Run as pre-push hooks
- Run during code review

## Support

For detailed guidance on:
- How to use each script → See SKILL.md
- How to fix specific warnings → See `references/warning-codes-guide.md`
- Best practices → See SKILL.md "Best Practices" section
