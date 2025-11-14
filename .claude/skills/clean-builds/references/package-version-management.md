# Package Version Management Guide

This guide explains how to manage NuGet package versions in your project and fix version inconsistencies.

## Quick Start

**Check for version issues:**
```pwsh
pwsh scripts/validate-package-versions.ps1
```

**Fix critical issues:**
1. Read the report (critical vs. warnings)
2. For each critical issue, update the file to use the recommended version
3. Re-run validation to confirm

## Understanding the Validation Report

### Summary Section

```
Total Packages Analyzed: 152
Total Projects Scanned: 10
Consistent Packages: 140
Inconsistent Packages: 12
```

- **Consistent Packages**: Same version used everywhere (good)
- **Inconsistent Packages**: Different versions in different projects (needs investigation)

### Severity Levels

#### 🔴 CRITICAL

These issues **must be fixed** before proceeding:

- **Orleans Framework Mismatches**: Different Orleans versions can cause build failures
- **Core Framework Differences**: .NET runtime incompatibilities
- **Major Version Gaps**: e.g., 8.x vs 9.x

**Action**: Fix immediately by updating all references to the recommended version.

#### 🟡 WARNING

These issues should be **reviewed and considered for consolidation**:

- **Minor version differences**: 9.0.1 vs 9.0.5 (usually compatible)
- **Patch version variations**: Small differences that typically don't break compatibility
- **Preview/Pre-release packages**: Different pre-release versions

**Action**: Evaluate if consolidation is needed based on your project's requirements.

## Common Version Discrepancies

### Orleans Framework (CRITICAL if mismatched)

**Expected behavior**: All Orleans packages should use the **same version**.

| Scenario | Status | Action |
|----------|--------|--------|
| All projects: 9.2.1 | ✅ Good | No action needed |
| Most: 9.2.1, Some: 9.0.0 | ❌ Critical | Update 9.0.0 → 9.2.1 |
| Mix of 9.0.0, 9.1.0, 9.2.1 | ❌ Critical | Choose one version and update all |

**Why this matters**: Orleans is a distributed actor framework. Version mismatches between client and host can cause:
- Serialization incompatibilities
- Protocol handshake failures
- Runtime crashes

**How to fix**:
1. Identify the target Orleans version (usually the newest)
2. Find all projects using different versions
3. Update their .csproj files
4. Run validation again to confirm

### Microsoft.Extensions Packages

**Expected behavior**: Generally compatible across minor versions, but consistency is better.

| Scenario | Status | Action |
|----------|--------|--------|
| All 9.0.5 | ✅ Perfect | No action needed |
| Mix of 9.0.1, 9.0.5, 9.0.7 | ⚠️ Warning | Consider consolidating to 9.0.7 |
| Mix of 9.x and preview (9.5.1) | ⚠️ Warning | Clarify if preview is intentional |

**Why variations exist**: Different projects may reference extensions at different times during development.

**How to fix**:
1. Determine baseline version (usually the newest stable)
2. Update older projects to match
3. Remove preview versions unless explicitly needed

### OpenTelemetry Packages

**Expected behavior**: Patch versions (1.10.0 vs 1.10.1) are usually fine.

**When to consolidate**: If mixing major versions (1.x vs 2.x).

## How to Fix Version Mismatches

### Manual Method (for small fixes)

1. **Find the affected projects**
   - From validation report, note the file paths
   - Examples: `server/AIChat.LoadTesting/AIChat.LoadTesting.csproj`

2. **Edit the .csproj file**
   ```xml
   <!-- Before -->
   <PackageReference Include="Microsoft.Orleans.Core" Version="9.0.0" />

   <!-- After -->
   <PackageReference Include="Microsoft.Orleans.Core" Version="9.2.1" />
   ```

3. **Update all occurrences** in the same file

4. **Validate**
   ```pwsh
   pwsh <clean_builds_base_directory>/scripts/validate-package-versions.ps1
   ```

5. **Rebuild** to ensure compatibility
   ```pwsh
   dotnet clean
   dotnet build
   ```

### Bulk Method (for many packages)

If you have many inconsistencies, consider implementing **Central Package Management (CPM)**:

**Create `Directory.Packages.props` at root:**
```xml
<Project>
  <ItemGroup>
    <!-- Core Framework -->
    <PackageVersion Include="Microsoft.Orleans.Core" Version="9.2.1" />
    <PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.2.1" />
    <PackageVersion Include="Microsoft.Orleans.Client" Version="9.2.1" />

    <!-- Microsoft Extensions -->
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.0.5" />
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="9.0.5" />

    <!-- Other packages -->
    <PackageVersion Include="Serilog" Version="4.0.0" />
    <!-- ... more packages ... -->
  </ItemGroup>
</Project>
```

**Update each .csproj to use it:**
```xml
<!-- Before -->
<ItemGroup>
  <PackageReference Include="Microsoft.Orleans.Core" Version="9.2.1" />
</ItemGroup>

<!-- After (version comes from Directory.Packages.props) -->
<ItemGroup>
  <PackageReference Include="Microsoft.Orleans.Core" />
</ItemGroup>
```

**Benefits:**
- Single source of truth for all versions
- Easy to update versions globally
- Prevents accidental mismatches
- Scales well with many projects

## Validation Output Formats

### Console Output (Default)

Human-readable colored output showing:
- Summary statistics
- Critical issues (must fix)
- Warnings (should review)
- File paths for each issue

Use this for:
- Interactive troubleshooting
- Reading reports on screen
- Understanding what needs fixing

### JSON Output

Structured data suitable for:
- CI/CD pipeline processing
- Automation and scripting
- Archiving reports
- Tool integration

**Export to JSON:**
```pwsh
pwsh <clean_builds_base_directory>/scripts/validate-package-versions.ps1 -OutputFormat Json -SaveToFile version-report.json
```

### Summary Output

Compact statistics-only output for:
- Quick status checks
- Scripted monitoring
- Dashboard integration

```pwsh
pwsh <clean_builds_base_directory>/scripts/validate-package-versions.ps1 -OutputFormat Summary
```

## Integration with CI/CD

The validation script exits with codes suitable for CI/CD:
- `0` = Success (no critical issues)
- `1` = Failure (critical issues found)

**Add to CI/CD pipeline:**
```yaml
- name: Validate Package Versions
  run: pwsh <clean_builds_base_directory>/scripts/validate-package-versions.ps1
  # If validation fails, the pipeline stops here
```

## Best Practices

### 1. Validate Regularly

Run validation:
- Before committing package changes
- As part of your build process
- Weekly to catch drifts
- Before releases

### 2. Update Systematically

When updating package versions:
1. Validate before making changes
2. Update one package at a time (for complex changes)
3. Run the full build after each change
4. Test thoroughly
5. Validate again to confirm no new issues

### 3. Document Decisions

If you intentionally keep different versions:
```xml
<!-- Intentionally using preview version for experimental feature -->
<PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="9.5.1" />
```

Add a comment explaining why.

### 4. Use Central Package Management

As your project grows, switch to `Directory.Packages.props`:
- Easier to maintain
- Prevents accidental mismatches
- Clearer version strategy
- Scales to many projects

### 5. Review Validation Reports

Don't ignore warnings:
- Warnings today can become critical issues tomorrow
- Consolidate when possible
- Document exceptions

## Troubleshooting

### "No projects found"

**Problem**: The script didn't locate your projects.

**Solution**:
- Ensure you're running from the project root
- Check that .csproj files exist
- Verify file extensions are correct

### Different versions but validation says "consistent"

**Problem**: Seeing package.config with different versions, but script shows consistent.

**Explanation**: This script only analyzes .csproj files, not packages.config files. If your project mixes these formats, you may need manual review.

### Critical issue won't go away after fixing

**Problem**: Updated the file but validation still reports the issue.

**Solutions**:
- Ensure you saved the file
- Check you updated ALL occurrences (use Find & Replace)
- Verify the file path in the report matches what you edited
- Run the validation again (caching might be an issue)

### How do I update many packages at once?

If using Central Package Management, one-file update handles all projects:

```xml
<!-- Before -->
<PackageVersion Include="Microsoft.Orleans.Core" Version="9.0.0" />

<!-- After (applies everywhere) -->
<PackageVersion Include="Microsoft.Orleans.Core" Version="9.2.1" />
```

If not using CPM yet, use Find & Replace in your IDE:
1. Find: `<PackageReference Include="Microsoft.Orleans.Core" Version="9.0.0" />`
2. Replace: `<PackageReference Include="Microsoft.Orleans.Core" Version="9.2.1" />`
3. Replace All

Then validate to confirm.

## Reference: Current Known Issues

As of last validation run:

| Package | Issue | Status |
|---------|-------|--------|
| Microsoft.Orleans.Core | 9.0.0 in some projects | CRITICAL - Fix to 9.2.1 |
| Microsoft.Orleans.Runtime | 9.0.0 in some projects | CRITICAL - Fix to 9.2.1 |
| Microsoft.Extensions.* | Minor variations | WARNING - Consider consolidating |

## Links and Resources

- [Central Package Management (CPM)](https://learn.microsoft.com/en-us/nuget/consume/central-package-management)
- [Orleans Framework](https://learn.microsoft.com/en-us/dotnet/orleans/)
- [Microsoft.Extensions packages](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions)
- [NuGet Package Versioning](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning)

## Next Steps

1. **Run validation**: `pwsh <clean_builds_base_directory>/scripts/validate-package-versions.ps1`
2. **Review output**: Understand critical vs. warning issues
3. **Fix critical issues**: Update package versions as recommended
4. **Re-validate**: Confirm fixes were successful
5. **Consider CPM**: For long-term maintainability
