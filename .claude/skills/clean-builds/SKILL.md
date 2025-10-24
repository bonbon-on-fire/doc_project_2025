---
name: clean-builds
description: This skill guides developers through achieving zero-warning builds, consistent code style, and NuGet package version consistency. It provides a comprehensive workflow combining code formatting (format-code.ps1), build quality checks (build_and_group_errors_and_warnings.ps1), and package version validation (validate-package-versions.ps1). This skill should be used when preparing code for commit, validating build quality, fixing code style issues, ensuring all warnings are addressed, or consolidating package versions before merging.
---

# Clean Builds Skill

## Purpose

This skill enables developers to achieve **zero-warning builds**, **consistent code style**, and **NuGet package version consistency** through a proven three-step workflow:
1. **Format Code** - Automatically fix code style and apply code analysis
2. **Build & Check** - Verify the build is clean with no errors or warnings
3. **Validate Packages** - Ensure NuGet package versions are consistent across projects

Use this skill to:
- Prepare code for commit with confidence
- Fix all build warnings and errors systematically
- Maintain consistent code style across the project
- Identify and fix NuGet package version inconsistencies
- Prevent compatibility issues from version mismatches
- Validate quality before merging pull requests

## When to Use This Skill

Invoke this skill when you need to:
- Format and validate code changes before committing
- Fix build warnings that are blocking progress
- Check for NuGet package version inconsistencies
- Fix package version mismatches that could cause build/runtime failures
- Perform comprehensive pre-commit quality checks
- Achieve zero-warning builds for release preparation
- Understand code quality issues and package compatibility problems
- Prepare for code review with confidence

## Quick Start Workflow

### Option 1: Complete Quality Validation (Recommended)

Execute all steps in sequence before committing:

1. **Validate code style enforcement** to enable IDE0005 detection:
   ```pwsh
   pwsh scripts/validate-code-style-enforcement.ps1 -Enforce
   ```
   - Ensures all projects have `EnforceCodeStyleInBuild` enabled
   - Enables IDE0005 (unused imports) and style rule detection during build
   - Automatically enables the setting if missing

2. **Validate package versions** to ensure no conflicts:
   ```pwsh
   pwsh scripts/validate-package-versions.ps1
   ```
   - If critical issues found, see "Fixing Package Version Issues" below
   - Fix all CRITICAL issues before proceeding

3. **Format the code** to fix style issues and apply code analysis fixes:
   ```pwsh
   pwsh scripts/format-code.ps1
   ```

4. **Build and check** for any remaining errors or warnings:
   ```pwsh
   pwsh scripts/build_and_group_errors_and_warnings.ps1
   ```

5. **Review output** and fix any remaining issues:
   - Warnings: See "Handling Build Warnings" below
   - Package issues: See "Fixing Package Version Issues" below

6. **Repeat** until all validations pass

### Option 2: Check Formatting Only

Verify formatting without making changes:
```pwsh
pwsh scripts/format-code.ps1 -CheckOnly
```

### Option 3: Build Quality Check Only

If you've already formatted, just check build quality:
```pwsh
pwsh scripts/build_and_group_errors_and_warnings.ps1
```

### Option 4: Package Version Check Only

Check for package version inconsistencies:
```pwsh
pwsh scripts/validate-package-versions.ps1
```

## How the Scripts Work

### format-code.ps1

**Purpose:** Automatically fix code style issues and apply code analysis corrections

**Tools used (in order):**
1. `dotnet format style` - Applies IDE code style fixes (IDE0032, IDE0017, etc.)
2. `Roslynator CLI` - Advanced code analysis fixes (optional if installed)
3. `ReSharper CLT` - Comprehensive formatting and cleanup
4. `CSharpier` - Opinionated formatting for submodules

**What it fixes:**
- Code style violations (patterns, null checks, array initialization)
- Using recommended APIs instead of deprecated ones
- Expression form simplifications
- Unnecessary using statements (IDE0005)
- Code organization and structure
- Unused imports and namespace cleanup

**Key flags:**
- `-CheckOnly` - Just report issues without fixing
- `-RootOnly` - Format only the main project (not submodules)
- `-SubmodulesOnly` - Format only external dependencies

### build_and_group_errors_and_warnings.ps1

**Purpose:** Build the solution cleanly and report all errors/warnings grouped by code

**What it does:**
1. Performs `dotnet clean` to remove build artifacts
2. Performs `dotnet build` with clean environment
3. Parses build output to extract error/warning details
4. Groups issues by type and code for easier analysis
5. Reports summary and detailed listing by file/line

**Output formats:**
- `Console` (default) - Colored, human-readable summary
- `Json` - Structured data for tooling
- `Csv` - Spreadsheet format for tracking

**Key data reported:**
- Total error and warning counts
- Unique error/warning codes
- File and line number for each issue
- Help URLs when available
- Count of occurrences per code

### validate-package-versions.ps1

**Purpose:** Scan all projects for NuGet package version inconsistencies and identify critical mismatches

**What it does:**
1. Finds all .csproj files in the solution
2. Extracts package references and versions
3. Compares versions across all projects
4. Identifies critical version mismatches (e.g., Orleans framework)
5. Reports warnings for minor inconsistencies (e.g., patch version variations)

**Output formats:**
- `Console` (default) - Colored report with critical issues highlighted
- `Json` - Structured data for tooling and CI/CD
- `Summary` - Quick statistics-only output

**Key data reported:**
- Total packages analyzed and total projects scanned
- Consistent vs. inconsistent packages
- Critical issues (MUST fix) - incompatible version combinations
- Warnings (should review) - minor version variations
- File paths for each issue
- Recommended fixes for each issue

**Exit codes:**
- `0` - Success (no critical issues)
- `1` - Failure (critical issues found)

**Key flags:**
- `-OutputFormat Console|Json|Summary` - Choose output type
- `-SaveToFile <path>` - Export report to file

## Handling Build Warnings

When the build check finds warnings, they are grouped by code. For each warning code:

1. **Read the message** to understand what needs fixing
2. **Check the help URL** (if provided) for context
3. **Review the files listed** to see all occurrences
4. **Fix the issues** (specific approach depends on warning code)
5. **Re-run the build check** to verify fixes

### Common Warning Types

| Code | Issue | How to Fix |
|------|-------|-----------|
| IDE0005 | Remove unnecessary imports | Delete unused `using` statements (auto-fixed by `dotnet format`) |
| CA1826 | Use property instead of LINQ | Replace `.Where(...).FirstOrDefault()` with `.FirstOrDefault(...)`
| CA1859 | Use concrete types for better perf | Use `List<T>` instead of `IEnumerable<T>` where appropriate
| IDE0052 | Remove unread field | Delete unused private fields or make them static |
| CA1310 | String comparison for culture | Use `StringComparison.Ordinal` or culture-aware options |
| IDE0017 | Inline variable declaration | Combine declaration and assignment on same line |

## Fixing Package Version Issues

When the package validation script finds critical issues, they **must be fixed** before proceeding.

### Understanding Severity Levels

**🔴 CRITICAL Issues (Must Fix):**
- Orleans framework version mismatches (e.g., 9.0.0 vs 9.2.1)
- Major version incompatibilities
- Can cause build failures or runtime crashes

**🟡 WARNING Issues (Should Review):**
- Minor version variations (patch version differences)
- Preview/pre-release version inconsistencies
- Usually compatible but should be consolidated for consistency

### Steps to Fix Critical Issues

1. **Review the validation output**
   - Note which package has the mismatch
   - Identify which projects need updating
   - Check the recommended target version

2. **Find the affected projects**
   - The validation report lists file paths
   - Example: `server/AIChat.LoadTesting/AIChat.LoadTesting.csproj`

3. **Update package references**
   ```xml
   <!-- Before (wrong version) -->
   <PackageReference Include="Microsoft.Orleans.Core" Version="9.0.0" />

   <!-- After (correct version) -->
   <PackageReference Include="Microsoft.Orleans.Core" Version="9.2.1" />
   ```

4. **Update all occurrences**
   - Some packages may appear multiple times in one project
   - Use Find & Replace to ensure consistency

5. **Rebuild and test**
   ```pwsh
   dotnet clean
   dotnet build
   ```

6. **Re-run validation**
   ```pwsh
   pwsh scripts/validate-package-versions.ps1
   ```
   - Confirm the critical issue is resolved
   - Address any new issues that appear

### Handling Multiple Projects

If multiple projects have the same mismatch:

```pwsh
# Find all affected files
Get-ChildItem -Recurse -Filter "*.csproj" |
  Select-String "Microsoft.Orleans.Core" |
  Select-Object -ExpandProperty Path

# Use Find & Replace in your IDE to update all files at once
# Search: Version="9.0.0"
# Replace: Version="9.2.1"
# Replace All
```

## Best Practices for Zero-Warning Builds

### 1. Format After Every Change
Run format-code.ps1 regularly during development, not just before commit.

### 2. Fix Warnings Immediately
Don't accumulate warnings—fix them as you encounter them.

### 3. Understand Each Warning
Read the warning message and URL before dismissing. Warnings usually indicate real issues.

### 4. Use the Grouped Output
The script groups warnings by code, making it easy to batch-fix similar issues.

### 5. Validate Package Versions

Run package validation before committing, especially if you've updated any dependencies:
```pwsh
pwsh scripts/validate-package-versions.ps1
```

Fix critical issues (CRITICAL severity) before proceeding. Warnings can be addressed during the next maintenance window.

### 6. Enable Code Style Enforcement During Build

To catch IDE0005 (unused imports) and other style violations during build, add this to your `.csproj` files:

```xml
<PropertyGroup>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

**Benefits:**
- IDE0005 warnings (unused using statements) are reported during build
- All style rules are enforced consistently
- Violations must be fixed before code can build cleanly
- The `dotnet format` script automatically fixes these issues

**To enable in all projects:**
```pwsh
# Find all test projects that need this setting
Get-ChildItem -Recurse -Filter "*.csproj" |
  Where-Object { $_.FullName -match "\.Tests\." } |
  ForEach-Object {
    $content = Get-Content $_.FullName
    if ($content -notmatch 'EnforceCodeStyleInBuild') {
      Write-Host "Add EnforceCodeStyleInBuild to: $_"
    }
  }
```

**Note:** This is particularly important for test projects where code style often gets neglected.

### 7. Pre-Commit Validation

Always run the full workflow before creating a commit:
```pwsh
# Step 1: Validate and enable code style enforcement
pwsh scripts/validate-code-style-enforcement.ps1 -Enforce
# This enables IDE0005 and style rule detection during build

# Step 2: Validate packages
pwsh scripts/validate-package-versions.ps1
# Fix any CRITICAL issues

# Step 3: Format
pwsh scripts/format-code.ps1

# Step 4: Build & Check
pwsh scripts/build_and_group_errors_and_warnings.ps1

# Only commit if all validations succeed
git add .
git commit -m "message"
```

**Note:** The `-Enforce` flag in step 1 automatically enables `EnforceCodeStyleInBuild` in any projects that are missing it. This ensures IDE0005 warnings are detected during the build check in step 4.

## Bundled Scripts

### `scripts/validate-code-style-enforcement.ps1`

**Purpose:** Validates and enforces code style build settings (`EnforceCodeStyleInBuild`) across all projects to enable IDE0005 and other style violations during build.

**What it does:**
1. Scans all `.csproj` files in the solution
2. Checks if `EnforceCodeStyleInBuild` is set to `true`
3. Reports which projects are missing this setting
4. Optionally enables it automatically for all projects

**Why this matters:**
- IDE0005 (unused imports) is only detected during build if `EnforceCodeStyleInBuild` is enabled
- Ensures consistent code quality enforcement across all projects
- Prevents style violations from being overlooked

**Output formats:**
- `Console` (default) - Colored report with project listing
- `Json` - Structured data for tooling
- `Summary` - Quick statistics-only output

**Key data reported:**
- Total projects scanned
- Projects with enforcement enabled
- Projects missing the setting
- List of affected projects with relative paths
- Number of projects updated (if -Enforce was used)

**Usage:**
```pwsh
# Check which projects need EnforceCodeStyleInBuild
pwsh scripts/validate-code-style-enforcement.ps1

# Automatically enable it in all projects
pwsh scripts/validate-code-style-enforcement.ps1 -Enforce

# Export findings to JSON
pwsh scripts/validate-code-style-enforcement.ps1 -OutputFormat Json -SaveToFile style-report.json

# Check only, don't enforce
pwsh scripts/validate-code-style-enforcement.ps1 -CheckOnly
```

**Exit codes:**
- `0` - Success (all projects have enforcement enabled)
- `1` - Failure (projects missing enforcement and -Enforce not used)

### `scripts/format-code.ps1`

Complete code formatting workflow with multiple tools.

**Requirements:**
- `dotnet format` (comes with .NET SDK)
- `JetBrains.ReSharper.GlobalTools` - Install with: `dotnet tool install -g JetBrains.ReSharper.GlobalTools`
- `csharpier` (optional) - Install with: `dotnet tool install -g csharpier`
- `Roslynator.DotNet.Cli` (optional) - Install with: `dotnet tool install -g Roslynator.DotNet.Cli`

**Usage:**
```pwsh
# Full format (default)
pwsh scripts/format-code.ps1

# Check only
pwsh scripts/format-code.ps1 -CheckOnly

# Format root project only
pwsh scripts/format-code.ps1 -RootOnly

# Format submodules only
pwsh scripts/format-code.ps1 -SubmodulesOnly

# Show help
pwsh scripts/format-code.ps1 -Help
```

### `scripts/build_and_group_errors_and_warnings.ps1`

Clean build with error/warning analysis and grouping.

**Requirements:**
- .NET SDK (for `dotnet clean` and `dotnet build`)

**Usage:**
```pwsh
# Default console output
pwsh scripts/build_and_group_errors_and_warnings.ps1

# Export as JSON
pwsh scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile results.json

# Export as CSV
pwsh scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Csv -SaveToFile results.csv

# Custom solution path
pwsh scripts/build_and_group_errors_and_warnings.ps1 -SolutionPath "path/to/solution.sln"
```

### `scripts/validate-package-versions.ps1`

Validates NuGet package version consistency across all projects.

**Requirements:**
- PowerShell 5+
- .NET SDK with project files (.csproj)

**Usage:**
```pwsh
# Default console output with colored severity levels
pwsh scripts/validate-package-versions.ps1

# Export validation results to JSON
pwsh scripts/validate-package-versions.ps1 -OutputFormat Json -SaveToFile version-report.json

# Quick summary statistics only
pwsh scripts/validate-package-versions.ps1 -OutputFormat Summary

# Export as JSON (alternative syntax)
pwsh scripts/validate-package-versions.ps1 -SaveToFile version-report.json
```

**Exit codes for CI/CD:**
- `0` = Success (no critical issues found)
- `1` = Failure (critical issues found - must fix)

## Troubleshooting

### Script Fails: "Tool not found"
Install the missing tool as indicated in the error message. See script requirements above.

### Warnings Not Disappearing After Fix
- Run format-code.ps1 again to catch any remaining style issues
- Some warnings require manual fixes—ensure you've addressed the specific code
- Run build check again with `-OutputFormat Json` to see exact details

### Build Takes Very Long
- This is normal for first clean build (compilation from scratch)
- Subsequent builds cache results
- Check available disk space

### Can't Modify Submodule Code
Submodule code is external. Focus on fixing issues in the main project (`server/` and `client/`)

### IDE0005 Warnings Not Being Detected
If you're not seeing IDE0005 (unused imports) warnings during build:

1. **Check if `EnforceCodeStyleInBuild` is enabled:**
   ```xml
   <!-- In your .csproj PropertyGroup -->
   <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
   ```

2. **Verify `GenerateDocumentationFile` setting:**
   - `GenerateDocumentationFile` is no longer required with modern .NET SDK
   - Only set it if you actually generate documentation

3. **Rebuild after adding the property:**
   ```pwsh
   dotnet clean
   dotnet build
   ```

4. **Run format to fix unused imports:**
   ```pwsh
   pwsh scripts/format-code.ps1
   ```

5. **Re-run build check to verify:**
   ```pwsh
   pwsh scripts/build_and_group_errors_and_warnings.ps1
   ```

**Note:** This is a build-time enforcement feature, not a runtime issue. Adding `EnforceCodeStyleInBuild` enables static analysis during compilation.

## References

For detailed information:

- **Warning Codes Guide**: [Detailed explanation of build warnings and how to fix them](references/warning-codes-guide.md)
  - 15+ common warning codes with examples
  - Step-by-step fixes for each issue
  - Tips for bulk warning fixes

- **Package Version Management Guide**: [Complete guide to NuGet package version management](references/package-version-management.md)
  - Understanding validation report output
  - How to fix version mismatches
  - Central Package Management (CPM) setup
  - Best practices for version consolidation
  - Troubleshooting version issues

## Next Steps

After achieving a clean build:
1. Run `git diff` to review formatting changes
2. Commit your changes
3. Create a pull request
4. Ensure CI/CD pipeline passes
