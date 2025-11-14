# enable-roslynator-analyzers.ps1

## Purpose

Add the Roslynator.Analyzers NuGet package to all .csproj files in the solution to enable 200+ code quality analyzers that run during every build.

## Overview

This script automates the process of adding Roslynator.Analyzers to all projects in your solution. Roslynator provides comprehensive code analysis that catches issues during compilation, giving developers immediate feedback on code quality.

## What It Does

1. Scans all .csproj files in the solution
2. Checks which projects already have Roslynator.Analyzers
3. Adds the package reference with proper configuration (PrivateAssets, IncludeAssets)
4. Reports which projects were modified or already had the package

## Requirements

- PowerShell 5.0 or later
- .NET SDK with project files (.csproj)

## Usage

### Add to All Projects

Install Roslynator.Analyzers in all projects with default version (4.14.1):

```pwsh
pwsh ../scripts/enable-roslynator-analyzers.ps1
```

### Add to Main Projects Only (Recommended)

Exclude submodules to avoid conflicts with external dependencies:

```pwsh
pwsh ../scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

**Why recommended**: External submodules may have their own analyzer configuration. Adding Roslynator could conflict with upstream settings and slow builds.

### Check Which Projects Need It

Dry-run to see which projects would be updated:

```pwsh
pwsh ../scripts/enable-roslynator-analyzers.ps1 -CheckOnly
```

### Preview Changes

Use PowerShell's `-WhatIf` to preview without making changes:

```pwsh
pwsh ../scripts/enable-roslynator-analyzers.ps1 -WhatIf
```

### Install Specific Version

Specify a different Roslynator version:

```pwsh
pwsh ../scripts/enable-roslynator-analyzers.ps1 -RoslynatorVersion "4.12.0"
```

### Remove from All Projects

Uninstall Roslynator.Analyzers from all projects:

```pwsh
pwsh ../scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers
```

Useful for:
- Testing analyzer impact on build time
- Reverting to baseline
- Troubleshooting issues

### Export Results

Save the report to a file:

```pwsh
# JSON format
pwsh ../scripts/enable-roslynator-analyzers.ps1 -OutputFormat Json -SaveToFile roslynator-status.json

# Summary format
pwsh ../scripts/enable-roslynator-analyzers.ps1 -OutputFormat Summary -SaveToFile status.txt
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-RoslynatorVersion` | String | 4.14.1 | Version to install |
| `-CheckOnly` | Switch | False | Check without making changes |
| `-RemoveAnalyzers` | Switch | False | Remove Roslynator from all projects |
| `-ExcludeSubmodules` | Switch | False | Skip projects in submodules/ directory |
| `-WhatIf` | Switch | False | Preview changes (PowerShell ShouldProcess) |
| `-OutputFormat` | String | Console | Output format: Console, Json, or Summary |
| `-SaveToFile` | String | None | Path to save output file |

## How It Configures The Package

The script adds a properly configured PackageReference to each project:

```xml
<ItemGroup>
  <PackageReference Include="Roslynator.Analyzers" Version="4.14.1">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

### Configuration Explained

- **PrivateAssets=all**: Analyzers run during build but don't propagate to consuming projects
- **IncludeAssets**: Specifies which assets to include from the package
- **Version**: Locked to specific version for consistency

## Output Formats

### Console (Default)

Colored report with modification status:

```
=====================================
Roslynator Analyzers Installation
=====================================
Total Projects: 9
Already had analyzers: 2 project(s)
Added to: 7 project(s)

Projects Modified:
  ✅ server/AIChat.Server/AIChat.Server.csproj
  ✅ server/AIChat.Tests/AIChat.Tests.csproj
  ✅ server/AIChat.Orleans/AIChat.Orleans.csproj
  ...

Projects Already Configured:
  ⏭️  server/AIChat.Integration.Tests/AIChat.Integration.Tests.csproj
  ⏭️  server/AIChat.Models/AIChat.Models.csproj
```

### JSON

Structured data for automation:

```json
{
  "summary": {
    "totalProjects": 9,
    "alreadyHadAnalyzers": 2,
    "added": 7,
    "skipped": 0
  },
  "projects": [
    {
      "path": "server/AIChat.Server/AIChat.Server.csproj",
      "status": "added",
      "version": "4.14.1"
    }
  ]
}
```

### Summary

Quick statistics only:

```
Roslynator Analyzers Summary
============================
Total Projects: 9
Already Configured: 2
Newly Added: 7
```

## Exit Codes

- `0` - Success (all projects processed successfully)
- `1` - Failure (no projects found or modification failed)

## Common Scenarios

### First-Time Setup

Add Roslynator to your project for the first time:

```pwsh
# Step 1: Add analyzers (excluding submodules)
pwsh ../scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 2: Configure .editorconfig
pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity warning

# Step 3: Build to see warnings
pwsh ../scripts/build_and_group_errors_and_warnings.ps1
```

### Adding New Projects

After adding new projects to the solution:

```pwsh
# Check which projects need analyzers
pwsh ../scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# Add to projects that need it
pwsh ../scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

### Updating Roslynator Version

Update to a newer version across all projects:

```pwsh
# Remove old version
pwsh ../scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers

# Add new version
pwsh ../scripts/enable-roslynator-analyzers.ps1 -RoslynatorVersion "4.15.0" -ExcludeSubmodules
```

### CI/CD Verification

Verify all projects have analyzers in CI/CD:

```yaml
- name: Verify Roslynator Analyzers
  run: pwsh ../scripts/enable-roslynator-analyzers.ps1 -CheckOnly
```

### Troubleshooting Build Time

If builds are too slow, remove analyzers temporarily:

```pwsh
# Remove analyzers
pwsh ../scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers

# Test build time
pwsh ../scripts/build_and_group_errors_and_warnings.ps1

# Re-add if acceptable
pwsh ../scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

## Expected Impact

### Build Time

Expect **10-30% increase** in build time:
- Small projects (1-5 projects): +10-15%
- Medium projects (10-20 projects): +15-25%
- Large projects (30+ projects): +20-30%

### Initial Warnings

After enabling, expect **100s of new warnings** initially:
- Code style issues
- Performance recommendations
- Modernization suggestions
- Documentation requirements

**Don't panic!** Many can be auto-fixed. See [examples/roslynator-auto-fix.md](../../examples/roslynator-auto-fix.md).

### Benefits

- 🎯 **200+ code quality rules** enforced automatically
- 💡 **Real-time IDE feedback** as you type
- ⚡ **Catch issues early** during development
- 📋 **Consistent standards** across the team
- 🔒 **Build-time validation** prevents poor-quality code

## Troubleshooting

### "No projects found"

**Problem**: Script can't locate .csproj files.

**Solutions**:
- Ensure you're in the solution root directory
- Check that .csproj files exist
- Verify you have read permissions

### "Failed to modify project file"

**Problem**: Can't write to .csproj file.

**Solutions**:
- Check file is not read-only
- Ensure you have write permissions
- Close Visual Studio/Rider (may lock files)
- Check file is not in use by another process

### "Build time increased significantly"

**Problem**: Builds are much slower after adding analyzers.

**Solutions**:
- Use `-ExcludeSubmodules` flag
- Disable analyzers in Debug builds (see [Roslynator Setup](../roslynator-setup.md))
- Review and disable noisy rules in .editorconfig
- Consider incremental builds instead of clean builds

### "Too many warnings"

**Problem**: Overwhelmed with warnings after enabling.

**Solutions**:
1. Run auto-fix first:
   ```pwsh
   roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format
   ```

2. Lower severity in .editorconfig:
   ```pwsh
   pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion
   ```

3. See [Troubleshooting Guide](../troubleshooting.md#too-many-warnings-after-enabling-roslynator)

### "Package restore failed"

**Problem**: NuGet can't download Roslynator.Analyzers.

**Solutions**:
- Check internet connection
- Verify NuGet package source is configured
- Try manual restore: `dotnet restore`
- Check firewall/proxy settings

## Integration with Clean-Builds Workflow

This script is part of the **one-time setup** phase:

### Setup Phase (Run Once)

1. Enable code style enforcement
2. **→ Enable Roslynator analyzers** (this script)
3. Configure .editorconfig
4. Validate package versions

### Regular Workflow (After Setup)

1. Validate package versions
2. Format code
3. Build and check (Roslynator warnings appear here)
4. Fix warnings
5. Repeat until clean

## Best Practices

1. **Use `-ExcludeSubmodules`** - Avoid analyzing external code
2. **Start with `suggestion` severity** - Gradually increase strictness
3. **Run auto-fix first** - Let tools do the heavy lifting
4. **Disable noisy rules** - Customize .editorconfig for your project
5. **Update regularly** - Keep Roslynator version current
6. **Commit .csproj changes** - Share configuration with team

## What Happens After Running This Script

1. **All projects have Roslynator.Analyzers** package reference
2. **Next build will show warnings** - Expect many initially
3. **IDE will show real-time hints** - As you type code
4. **CI/CD builds will enforce rules** - Automatically
5. **Team members will see same warnings** - Consistency

## Next Steps

After running this script:

1. **Configure .editorconfig**:
   ```pwsh
   pwsh ../scripts/configure-roslynator-editorconfig.ps1 -Severity warning
   ```

2. **Build to see warnings**:
   ```pwsh
   pwsh ../scripts/build_and_group_errors_and_warnings.ps1
   ```

3. **Auto-fix issues**:
   ```pwsh
   roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format
   ```

4. **Commit the changes**:
   ```pwsh
   git add .
   git commit -m "Enable Roslynator analyzers for code quality enforcement"
   ```

## Related Documentation

- [Roslynator Setup Guide](../roslynator-setup.md) - Comprehensive setup guide
- [Configure EditorConfig Script](configure-roslynator-editorconfig.md) - Next step after this script
- [Roslynator Auto-Fix Example](../../examples/roslynator-auto-fix.md) - How to auto-fix warnings
- [Troubleshooting Guide](../troubleshooting.md) - Common issues
