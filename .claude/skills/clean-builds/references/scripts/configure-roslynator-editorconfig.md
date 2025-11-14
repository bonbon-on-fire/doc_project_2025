# configure-roslynator-editorconfig.ps1

## Purpose

Create or update `.editorconfig` file with Roslynator analyzer severity settings and code style preferences for team-wide consistency.

## Overview

This script automates the configuration of Roslynator analyzers in your `.editorconfig` file. It ensures all team members see the same code quality warnings and follow the same style guidelines.

## What It Does

1. Creates a new `.editorconfig` file if it doesn't exist (or updates existing one)
2. Adds Roslynator configuration section with:
   - Global severity setting for all Roslynator rules
   - Enable/disable flags for analyzers, refactorings, and compiler fixes
   - Code style preferences (var usage, accessibility modifiers, etc.)
   - Individual rule configurations for common scenarios
3. Preserves existing `.editorconfig` content (appends Roslynator section)
4. Prevents duplicate configuration (detects existing Roslynator settings)

## Requirements

- PowerShell 5.0 or later
- Write permissions in the target directory

## Usage

### Default Configuration (Warning Severity)

Add Roslynator configuration with `warning` severity (recommended):

```pwsh
pwsh ../scripts/configure-roslynator-editorconfig.ps1
```

This is the recommended starting point for most projects.

### Strict Enforcement (Error Severity)

Set all rules to `error` so violations fail the build:

```pwsh
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity error
```

Use for:
- Production code with high quality standards
- Final cleanup before release
- Projects that enforce zero violations

### Gradual Adoption (Suggestion Severity)

Set rules to `suggestion` for IDE hints only (no build warnings):

```pwsh
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion
```

Use for:
- Initial adoption without disrupting workflow
- Learning Roslynator rules
- Large codebases with many existing issues

### Preview Changes

See what would be added without making changes:

```pwsh
# Using -ShowPreview
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -ShowPreview

# Or using PowerShell -WhatIf
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -WhatIf
```

### Configure Specific File

Target a different .editorconfig location:

```pwsh
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -ConfigFile "src\.editorconfig"
```

### Disable Analyzers

Add configuration but disable analyzers (keep for later):

```pwsh
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -EnableAnalyzers $false
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Severity` | String | warning | Global severity: none, silent, suggestion, warning, error |
| `-ConfigFile` | String | .editorconfig | Path to .editorconfig file |
| `-CreateIfMissing` | Boolean | true | Create new file if doesn't exist |
| `-ShowPreview` | Switch | False | Preview without making changes |
| `-WhatIf` | Switch | False | PowerShell ShouldProcess support |
| `-EnableAnalyzers` | Boolean | true | Enable/disable analyzers globally |

## Severity Levels Explained

| Severity | Effect | Build Impact | When to Use |
|----------|--------|--------------|-------------|
| **none** | Rules are disabled | None | Disable Roslynator entirely |
| **silent** | Rules run but produce no diagnostics | None | Testing analyzers |
| **suggestion** | IDE shows hints | None | Non-critical style preferences |
| **warning** | Build produces warnings | Warnings shown, build succeeds | Recommended: Code quality rules |
| **error** | Build fails if violations exist | Build fails | Critical rules that must be enforced |

### Severity Recommendations

**Start with `warning`**:
- Visible to developers
- Doesn't block builds
- Can be addressed over time

**Move to `error`** for:
- Critical quality gates
- Pre-release validation
- Established projects with clean codebase

**Use `suggestion`** when:
- Learning Roslynator
- Large existing codebase with many violations
- Want hints without build noise

## Configuration Added

The script adds this section to `.editorconfig`:

```ini
#####################
# Roslynator Settings
#####################

[*.cs]

# Enable Roslynator analyzers
roslynator_analyzers.enabled_by_default = true

# Global severity for all Roslynator rules
dotnet_analyzer_diagnostic.category-roslynator.severity = warning

# Enable refactorings
roslynator_refactorings.enabled = true

# Enable compiler diagnostic fixes
roslynator_compiler_diagnostic_fixes.enabled = true

# Code style preferences
csharp_prefer_var = true:suggestion
csharp_prefer_explicit_type = false:suggestion
dotnet_style_require_accessibility_modifiers = always:warning

# Individual rule overrides
dotnet_diagnostic.rcs1036.severity = none      # Remove unnecessary blank line
dotnet_diagnostic.rcs1037.severity = none      # Remove trailing white-space
dotnet_diagnostic.rcs1163.severity = suggestion # Unused parameter
```

## Output

The script provides console output only (no JSON/Summary formats since it's a one-time configuration).

### Successful Configuration

```
[SUCCESS] Roslynator configuration added to .editorconfig
Location: C:\Projects\MyApp\.editorconfig
Severity: warning
```

### Preview Mode

```
[PREVIEW] Would add the following to .editorconfig:

#####################
# Roslynator Settings
#####################

[*.cs]
roslynator_analyzers.enabled_by_default = true
dotnet_analyzer_diagnostic.category-roslynator.severity = warning
...

[INFO] No changes made (preview mode)
```

### Already Configured

```
[ERROR] .editorconfig already contains Roslynator configuration
To reconfigure, manually edit or remove the existing Roslynator section
```

## Exit Codes

- `0` - Success (configuration added successfully)
- `1` - Failure (file exists with Roslynator config, or other error)

## Common Scenarios

### First-Time Setup

Configure Roslynator after adding analyzers:

```pwsh
# Step 1: Add analyzers to projects
pwsh ../scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 2: Configure .editorconfig (this script)
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity warning

# Step 3: Restart IDE for changes to take effect
```

### Changing Severity Levels

Start lenient, then increase strictness:

```pwsh
# Week 1: Start with suggestions
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion

# Week 2: Escalate to warnings (after fixing issues)
# Manually edit .editorconfig:
# Change: dotnet_analyzer_diagnostic.category-roslynator.severity = warning

# Week 3: Selected rules to error
# Manually edit .editorconfig:
# Add: dotnet_diagnostic.rcs1002.severity = error  # Remove unnecessary braces
```

### Project-Specific Configuration

Create separate .editorconfig for a subdirectory:

```pwsh
# Root configuration (strict)
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity error

# Test project configuration (lenient)
pwsh ../scripts/configure-roslynator-editorconfig.ps1 `
     -ConfigFile "tests\.editorconfig" `
     -Severity suggestion
```

### Disabling Noisy Rules

After building, disable rules generating too many warnings:

```ini
# Manually add to .editorconfig after running script

# Disable documentation rules (too noisy initially)
dotnet_diagnostic.rcs1138.severity = none  # Add summary element
dotnet_diagnostic.rcs1139.severity = none  # Add summary to documentation comment
dotnet_diagnostic.rcs1141.severity = none  # Add 'param' element

# Disable formatting rules (handled by format-code.ps1)
dotnet_diagnostic.rcs1036.severity = none  # Remove unnecessary blank line
dotnet_diagnostic.rcs1037.severity = none  # Remove trailing white-space
```

## Configuration Options Explained

### roslynator_analyzers.enabled_by_default

Enables Roslynator analyzers globally. Set to `false` to disable all analyzers.

### dotnet_analyzer_diagnostic.category-roslynator.severity

Sets global severity for all Roslynator rules (RCS*). Individual rules can override this.

### roslynator_refactorings.enabled

Enables code refactoring suggestions in the IDE. These are code improvements that don't produce diagnostics but offer quick fixes.

### roslynator_compiler_diagnostic_fixes.enabled

Enables Roslynator's fixes for compiler diagnostics (CS*). Provides additional quick fixes for compiler warnings.

### Code Style Preferences

- `csharp_prefer_var`: Prefer `var` over explicit types
- `dotnet_style_require_accessibility_modifiers`: Require `public`, `private`, etc.
- And more...

### Individual Rule Overrides

Fine-tune specific rules:

```ini
dotnet_diagnostic.rcs1036.severity = none      # Disable blank line rule
dotnet_diagnostic.rcs1163.severity = suggestion # Unused parameter as hint only
```

## Troubleshooting

### ".editorconfig already has Roslynator"

**Problem**: Roslynator configuration already exists.

**Solutions**:
- Manually edit .editorconfig to update settings
- Remove existing Roslynator section and re-run script
- Use `-ShowPreview` to see what would be added

### "IDE not respecting settings"

**Problem**: Changes in .editorconfig don't take effect.

**Solutions**:
- Restart your IDE (Visual Studio, Rider, VS Code)
- Clear IDE caches:
  - Visual Studio: Delete `.vs` folder
  - Rider: File → Invalidate Caches / Restart
- Verify `.editorconfig` has `root = true` at the top
- Check file is in the solution root

### "Different warnings in IDE vs Build"

**Problem**: IDE shows different warnings than `dotnet build`.

**Solutions**:
- Ensure IDE is using same analyzer version
- Restart IDE to reload .editorconfig
- Verify .editorconfig is committed to version control
- Check IDE analyzer settings match .editorconfig

### "Too many warnings after configuration"

**Problem**: Overwhelmed with warnings.

**Solutions**:
1. Lower severity to `suggestion`:
   ```ini
   dotnet_analyzer_diagnostic.category-roslynator.severity = suggestion
   ```

2. Disable specific noisy rules
3. Run auto-fix:
   ```pwsh
   roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format
   ```

## After Running This Script

1. **Restart your IDE** - Changes won't take effect until restart
2. **Build to see warnings**:
   ```pwsh
   pwsh ../scripts/build_and_group_errors_and_warnings.ps1
   ```
3. **Auto-fix issues**:
   ```pwsh
   roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format
   ```
4. **Review and customize** - Edit .editorconfig for project-specific rules
5. **Commit .editorconfig** - Share configuration with team

## Integration with Clean-Builds Workflow

This script is part of the **one-time setup** phase:

### Setup Phase (Run Once)

1. Enable code style enforcement
2. Enable Roslynator analyzers
3. **→ Configure .editorconfig** (this script)
4. Validate package versions

### Regular Workflow (After Setup)

.editorconfig is now active. All builds will respect these settings.

## Best Practices

1. **Start with `warning` severity** - Balance between visibility and productivity
2. **Customize for your project** - Edit .editorconfig to match your standards
3. **Restart IDE after changes** - Always restart for configuration to take effect
4. **Commit to version control** - Ensure team consistency
5. **Review and adjust rules** - Disable noisy rules, escalate critical ones
6. **Document exceptions** - Comment why specific rules are disabled

## Related Documentation

- [Roslynator Setup Guide](../roslynator-setup.md) - Comprehensive setup guide
- [Enable Roslynator Script](enable-roslynator-analyzers.md) - Previous step
- [Troubleshooting Guide](../troubleshooting.md) - Common issues
- [Best Practices](../best-practices.md) - Zero-warning build strategies
