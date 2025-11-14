# Roslynator Setup Guide

Complete guide to enabling and configuring Roslynator analyzers for maximum code quality.

## What are Roslynator Analyzers?

Roslynator is a comprehensive collection of 200+ code analyzers, refactorings, and fixes for C#. It integrates directly into your build process to provide continuous code quality enforcement.

### Key Benefits

- **Build-time enforcement** - Issues detected during compilation, not just during formatting
- **Immediate feedback** - Your IDE shows warnings as you type
- **Comprehensive coverage** - 200+ analyzers covering code quality, style, performance, and potential bugs
- **Configurable severity** - Control which rules are errors, warnings, or suggestions
- **Team consistency** - Same rules enforced for all developers through .editorconfig

## Roslynator CLI vs Roslynator.Analyzers

| Feature | Roslynator CLI (format-code.ps1) | Roslynator.Analyzers (NuGet) |
|---------|----------------------------------|------------------------------|
| **When it runs** | On-demand (manual script execution) | Every build (automatic) |
| **What it does** | Fixes issues automatically | Detects and reports issues |
| **Integration** | External tool | Built into compilation |
| **IDE support** | No real-time feedback | Real-time feedback as you type |
| **Team enforcement** | Requires manual runs | Automatic enforcement |

**Recommendation**: Use both together for maximum code quality:
1. Enable Roslynator.Analyzers for continuous enforcement
2. Run format-code.ps1 to automatically fix detected issues

## When to Enable Roslynator Analyzers

Enable when you want:

- **Proactive quality enforcement** - Catch issues during development, not just before commit
- **Consistent team standards** - Ensure all developers see the same warnings
- **Build-time validation** - Prevent low-quality code from being built
- **Comprehensive coverage** - Go beyond basic compiler warnings
- **IDE integration** - Real-time feedback while coding

## Setup Steps

### Step 1: Add Roslynator.Analyzers Package

Add the NuGet package to all projects:

```pwsh
# Recommended: Exclude submodules for faster builds
pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

This adds to each `.csproj`:

```xml
<PackageReference Include="Roslynator.Analyzers" Version="4.14.1">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

### Step 2: Configure .editorconfig

Set rule severities and code style preferences:

```pwsh
# Start with 'warning' severity (recommended)
pwsh <skill_base_path>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
```

### Step 3: Build to See Warnings

```pwsh
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1
```

**Expected**: 100s of warnings initially - this is normal!

### Step 4: Auto-Fix Issues

Let Roslynator fix what it can:

```pwsh
roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format
```

**Expected**: 200-300+ fixes automatically applied.

### Step 5: Address Remaining Issues

Fix unfixable warnings manually or downgrade their severity in `.editorconfig`.

## Expected Impact

### Build Time

Expect **10-30% increase** in build time:
- Small projects (1-5): +10-15%
- Medium projects (10-20): +15-25%
- Large projects (30+): +20-30%

### Initial Warnings

After enabling: **100s of new warnings** initially
- Code style issues
- Performance recommendations
- Modernization suggestions
- Documentation requirements

**Don't panic!** Most can be auto-fixed.

### Long-Term Benefits

- 🎯 200+ code quality rules enforced
- 💡 Real-time IDE feedback
- ⚡ Catch issues early
- 📋 Consistent standards
- 🔒 Build-time validation

## Best Practice Workflow

### Initial Setup (One Time)

```pwsh
# Step 1: Format code first (reduces initial noise)
pwsh <skill_base_path>/scripts/format-code.ps1

# Step 2: Enable analyzers (exclude submodules)
pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 3: Configure with lower severity initially
pwsh <skill_base_path>/scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion

# Step 4: Auto-fix Roslynator issues
roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format

# Step 5: Build and review remaining warnings
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1

# Step 6: Downgrade unfixable rules to suggestion in .editorconfig
# (Review build output to identify which rules)

# Step 7: Verify clean build
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1

# Step 8: Gradually increase severity
pwsh <skill_base_path>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
```

### Regular Workflow (After Setup)

Roslynator runs automatically during every build. Just follow the normal clean-builds workflow:

1. Format code
2. Build and check
3. Fix warnings
4. Repeat

## Handling Unfixable Warnings

Some diagnostics cannot be auto-fixed:

### Documentation Comments

**Rules**: RCS1141, RCS1140, RCS1142

**Problem**: Require human-written descriptions

**Solution**: Downgrade to `suggestion` in `.editorconfig`:

```ini
dotnet_diagnostic.rcs1141.severity = suggestion  # Add param element
dotnet_diagnostic.rcs1140.severity = suggestion  # Add exception element
dotnet_diagnostic.rcs1142.severity = suggestion  # Add type param element
```

### Unused Parameters

**Rule**: RCS1163

**Problem**: May be required by interfaces

**Solution**: Keep as `suggestion` or disable:

```ini
dotnet_diagnostic.rcs1163.severity = suggestion  # Unused parameter
```

### Complex Refactorings

**Problem**: Require semantic understanding

**Solution**: Review case-by-case and fix manually

## Customizing Rules

### Severity Levels

| Severity | Effect | Use When |
|----------|--------|----------|
| `none` | Disabled | Rule doesn't apply to your project |
| `silent` | Runs but no diagnostics | Testing |
| `suggestion` | IDE hints only | Nice-to-have improvements |
| `warning` | Build warnings | Should be fixed |
| `error` | Build fails | Must be fixed |

### Common Customizations

```ini
# .editorconfig

# Disable formatting rules (handled by format-code.ps1)
dotnet_diagnostic.rcs1036.severity = none  # Remove blank line
dotnet_diagnostic.rcs1037.severity = none  # Remove whitespace

# Documentation rules as suggestions
dotnet_diagnostic.rcs1138.severity = suggestion  # Add summary
dotnet_diagnostic.rcs1141.severity = suggestion  # Add param

# Critical quality rules as errors
dotnet_diagnostic.rcs1077.severity = error  # Optimize LINQ
dotnet_diagnostic.rcs1080.severity = error  # Use Count/Length
```

## Troubleshooting

See [Troubleshooting Guide](troubleshooting.md#roslynator-issues) for:
- Too many warnings after enabling
- Build time increased significantly
- Roslynator warnings differ from IDE suggestions

## Related Documentation

- [Enable Roslynator Script](scripts/enable-roslynator-analyzers.md) - Script reference
- [Configure EditorConfig Script](scripts/configure-roslynator-editorconfig.md) - Configuration reference
- [Roslynator Auto-Fix Example](../examples/roslynator-auto-fix.md) - Step-by-step auto-fix guide
- [Troubleshooting](troubleshooting.md) - Common issues
