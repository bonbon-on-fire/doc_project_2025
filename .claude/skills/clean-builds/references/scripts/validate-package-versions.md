# validate-package-versions.ps1

## Purpose

Scan all projects for NuGet package version inconsistencies and identify critical mismatches that could cause build or runtime failures.

## Overview

This script analyzes all `.csproj` files in your solution to detect NuGet package version discrepancies across projects. It helps prevent compatibility issues, runtime crashes, and build failures caused by version mismatches.

## What It Does

1. Finds all .csproj files in the solution
2. Extracts package references and versions
3. Compares versions across all projects
4. Identifies critical version mismatches (e.g., Orleans framework)
5. Reports warnings for minor inconsistencies (e.g., patch version variations)

## Requirements

- PowerShell 5.0 or later
- .NET SDK with project files (.csproj)

## Usage

### Default Console Output

Display colored report with critical issues highlighted:

```pwsh
pwsh ../scripts/validate-package-versions.ps1
```

### Export to JSON

Export structured data for CI/CD and automation:

```pwsh
pwsh ../scripts/validate-package-versions.ps1 -OutputFormat Json -SaveToFile version-report.json
```

Alternative syntax:

```pwsh
pwsh ../scripts/validate-package-versions.ps1 -SaveToFile version-report.json
```

(When `-SaveToFile` is used without `-OutputFormat`, JSON is assumed)

### Quick Summary Only

Display statistics without detailed listings:

```pwsh
pwsh ../scripts/validate-package-versions.ps1 -OutputFormat Summary
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-OutputFormat` | String | Console | Output format: Console, Json, or Summary |
| `-SaveToFile` | String | None | Path to save output file |

## Output Formats

### Console (Default)

Human-readable colored output:

```
=====================================
Package Version Validation Report
=====================================
Total Packages Analyzed: 152
Total Projects Scanned: 10
Consistent Packages: 140
Inconsistent Packages: 12

=====================================
CRITICAL Issues (Must Fix)
=====================================

🔴 Microsoft.Orleans.Core
   Versions: 9.0.0, 9.2.1
   Impact: Orleans framework version mismatch can cause runtime failures
   Projects:
     - server/AIChat.LoadTesting/AIChat.LoadTesting.csproj (9.0.0)
     - server/AIChat.Server/AIChat.Server.csproj (9.2.1)
   Recommended: Update all to version 9.2.1

=====================================
WARNING Issues (Should Review)
=====================================

🟡 Microsoft.Extensions.DependencyInjection
   Versions: 9.0.1, 9.0.5
   Impact: Minor version differences, usually compatible
   Projects:
     - server/AIChat.Tests/AIChat.Tests.csproj (9.0.1)
     - server/AIChat.Server/AIChat.Server.csproj (9.0.5)
   Recommended: Consider consolidating to version 9.0.5
```

### JSON

Structured data for automation:

```json
{
  "summary": {
    "totalPackages": 152,
    "totalProjects": 10,
    "consistentPackages": 140,
    "inconsistentPackages": 12,
    "criticalIssues": 2,
    "warningIssues": 10
  },
  "critical": [
    {
      "package": "Microsoft.Orleans.Core",
      "versions": ["9.0.0", "9.2.1"],
      "impact": "Orleans framework version mismatch",
      "projects": [
        {
          "path": "server/AIChat.LoadTesting/AIChat.LoadTesting.csproj",
          "version": "9.0.0"
        }
      ],
      "recommendedVersion": "9.2.1"
    }
  ],
  "warnings": [...]
}
```

### Summary

Quick statistics only:

```
Package Version Summary
=======================
Total Packages: 152
Consistent: 140
Inconsistent: 12
Critical Issues: 2
Warnings: 10
```

## Exit Codes

- `0` - Success (no critical issues found)
- `1` - Failure (critical issues found - must fix before proceeding)

**CI/CD Integration**: The exit code makes this script perfect for pipeline validation. Critical issues will fail the build.

## Key Data Reported

### Summary Statistics

- **Total Packages Analyzed**: Number of unique packages found
- **Total Projects Scanned**: Number of .csproj files examined
- **Consistent Packages**: Packages with same version everywhere (✅ good)
- **Inconsistent Packages**: Packages with version variations (needs review)

### Critical Issues (🔴 CRITICAL)

Issues that **must be fixed**:

- Orleans framework version mismatches
- Core framework version differences
- Major version gaps (e.g., 8.x vs 9.x)

**Action**: Fix immediately before proceeding with any commits.

### Warning Issues (🟡 WARNING)

Issues that **should be reviewed**:

- Minor version differences (9.0.1 vs 9.0.5)
- Patch version variations
- Preview/pre-release version inconsistencies

**Action**: Evaluate and consolidate when appropriate.

## Understanding Severity Levels

### When It's CRITICAL

- **Orleans Framework**: All Orleans packages MUST use the same version
  - `Microsoft.Orleans.Core`
  - `Microsoft.Orleans.Runtime`
  - `Microsoft.Orleans.Client`
  - `Microsoft.Orleans.*`

- **Major Version Differences**: Mixing major versions (8.x and 9.x)

- **Known Incompatibilities**: Packages with documented incompatibilities

**Why it matters**: Can cause:
- Serialization incompatibilities
- Protocol handshake failures
- Runtime crashes
- Build failures

### When It's a WARNING

- **Minor Version Differences**: 9.0.1 vs 9.0.5
- **Patch Version Variations**: 1.10.0 vs 1.10.1
- **Preview Versions**: Using preview alongside stable

**Why it matters**:
- Usually compatible but inconsistent
- May cause subtle issues
- Harder to troubleshoot
- Should consolidate for clarity

## Common Scenarios

### Before Committing

Always validate before creating a commit:

```pwsh
pwsh ../scripts/validate-package-versions.ps1
```

If critical issues are found, fix them first.

### After Updating Packages

Verify consistency after `dotnet add package`:

```pwsh
# Update package
dotnet add server/AIChat.Server package Microsoft.Orleans.Core

# Validate
pwsh ../scripts/validate-package-versions.ps1
```

### CI/CD Pipeline

Add to your pipeline to prevent version drift:

```yaml
- name: Validate Package Versions
  run: pwsh ../scripts/validate-package-versions.ps1
  # Exits with code 1 if critical issues found
```

### Regular Audits

Run weekly to catch version drift:

```pwsh
# Schedule this command
pwsh ../scripts/validate-package-versions.ps1 -OutputFormat Json -SaveToFile "audit/versions-$(Get-Date -Format 'yyyy-MM-dd').json"
```

### Pre-Release Validation

Before creating a release, ensure all versions are consistent:

```pwsh
pwsh ../scripts/validate-package-versions.ps1
```

Critical and warning issues should both be addressed for releases.

## How It Works

### Step 1: Find All Projects

Scans the directory tree for all `.csproj` files:

```pwsh
Get-ChildItem -Recurse -Filter "*.csproj"
```

### Step 2: Extract Package References

Parses each .csproj to find `<PackageReference>` elements:

```xml
<PackageReference Include="Microsoft.Orleans.Core" Version="9.2.1" />
```

### Step 3: Group by Package Name

Groups all references by package name across all projects.

### Step 4: Detect Inconsistencies

For each package, checks if all projects use the same version.

### Step 5: Classify Severity

Applies rules to determine if an inconsistency is CRITICAL or WARNING:
- Orleans packages → CRITICAL
- Major version differences → CRITICAL
- Minor/patch differences → WARNING

### Step 6: Generate Report

Outputs in specified format with recommendations.

## Fixing Issues

### Critical Issues - Step by Step

1. **Identify the target version** (usually the newest):
   ```
   Recommended: Update all to version 9.2.1
   ```

2. **Note the affected projects**:
   ```
   - server/AIChat.LoadTesting/AIChat.LoadTesting.csproj (9.0.0)
   ```

3. **Edit the .csproj file**:
   ```xml
   <!-- Before -->
   <PackageReference Include="Microsoft.Orleans.Core" Version="9.0.0" />

   <!-- After -->
   <PackageReference Include="Microsoft.Orleans.Core" Version="9.2.1" />
   ```

4. **Re-validate**:
   ```pwsh
   pwsh ../scripts/validate-package-versions.ps1
   ```

5. **Test the build**:
   ```pwsh
   dotnet clean
   dotnet build
   ```

### Bulk Fixes

For multiple projects with the same issue:

```pwsh
# PowerShell Find & Replace
Get-ChildItem -Recurse -Filter "*.csproj" | ForEach-Object {
    (Get-Content $_.FullName) -replace 'Version="9.0.0"', 'Version="9.2.1"' |
    Set-Content $_.FullName
}

# Then validate
pwsh ../scripts/validate-package-versions.ps1
```

Or use your IDE's Find & Replace across files.

## Troubleshooting

### "No projects found"

**Problem**: Script can't locate .csproj files.

**Solutions**:
- Ensure you're running from the solution root
- Check that .csproj files exist
- Verify you're in the correct directory

### "Different versions but shows consistent"

**Problem**: Script says consistent but you see different versions.

**Explanation**: The script only analyzes `.csproj` files, not `packages.config` (legacy format).

**Solution**: Migrate legacy packages.config to PackageReference format.

### "Critical issue won't go away"

**Problem**: Updated the file but validation still reports the issue.

**Checklist**:
- ✅ Saved the file
- ✅ Updated ALL occurrences in the file
- ✅ Edited the correct file (verify path matches report)
- ✅ No typos in version number
- ✅ Re-ran validation script

## Integration with Clean-Builds Workflow

This script is **Step 1** in the recommended workflow:

1. **→ Validate package versions** (this script)
2. Fix critical issues if found
3. Format code
4. Build and check
5. Repeat until clean

## Best Practices

1. **Validate before committing** - Catch issues early
2. **Fix critical issues immediately** - Don't ignore them
3. **Review warnings regularly** - Consolidate when appropriate
4. **Integrate with CI/CD** - Prevent version drift
5. **Document exceptions** - If you need different versions, document why

## Related Documentation

- [Package Version Management Guide](../package-version-management.md) - Comprehensive guide to package management
- [Troubleshooting Guide](../troubleshooting.md) - Common issues and solutions
- [Best Practices](../best-practices.md) - Zero-warning build strategies
