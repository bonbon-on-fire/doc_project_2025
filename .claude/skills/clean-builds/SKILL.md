---
name: clean-builds
description: This skill guides developers through achieving zero-warning builds, consistent code style, and NuGet package version consistency. It provides a comprehensive workflow combining Roslynator analyzer integration (200+ code analyzers), code formatting (format-code.ps1), build quality checks (build_and_group_errors_and_warnings.ps1), and package version validation (validate-package-versions.ps1). This skill should be used when preparing code for commit, validating build quality, fixing code style issues, ensuring all warnings are addressed, or consolidating package versions before merging.
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

## Prerequisites and One-Time Setup

Before using the clean-builds workflow for the first time, you must configure the project to enable all code quality tooling. This is a **one-time setup** that ensures:

1. ✅ Code style enforcement is enabled during build
2. ✅ Roslynator analyzers are installed in all projects
3. ✅ EditorConfig is properly configured
4. ✅ Package versions are validated

### Setup Commands (Run Once)

Execute these commands in order to prepare your project:

```pwsh
# Step 1: Enable code style enforcement in all projects
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1 -Enforce

# Step 2: Enable Roslynator analyzers (200+ code quality rules)
# Use -ExcludeSubmodules to skip external dependencies
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 3: Configure .editorconfig with Roslynator severity settings
# Start with 'warning' severity (can escalate to 'error' later)
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning

# Step 4: Validate package versions
pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1
```

### Verification

After setup, verify the configuration worked:

```pwsh
# Check that analyzers are installed
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# Check that code style is enforced
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1

# Preview .editorconfig settings
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -ShowPreview
```

### When to Re-Run Setup

Re-run the setup if:
- ✅ Adding new projects to the solution
- ✅ Updating Roslynator version
- ✅ Changing severity levels (.editorconfig)
- ✅ After cloning the repository on a new machine (some settings are project-file based)

**Important**: The setup phase modifies `.csproj` files and `.editorconfig`. These changes should be committed to version control so all team members benefit.

## Quick Start Workflow

### Option 1: Complete Quality Validation (Recommended)

**Prerequisites**: Ensure you've completed the [one-time setup](#prerequisites-and-one-time-setup) first.

Execute all steps in sequence before committing:

1. **Validate package versions** to ensure no conflicts:
   ```pwsh
   pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1
   ```
   - If critical issues found, see "Fixing Package Version Issues" below
   - Fix all CRITICAL issues before proceeding

2. **Format the code** to fix style issues and apply code analysis fixes:
   ```pwsh
   pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1
   ```

3. **Build and check** for any remaining errors or warnings:
   ```pwsh
   pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1
   ```

4. **Review output** and fix any remaining issues:
   - Warnings: See "Handling Build Warnings" below
   - Package issues: See "Fixing Package Version Issues" below

5. **Repeat** steps 2-4 until all validations pass

### Option 2: Check Formatting Only

Verify formatting without making changes:
```pwsh
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1 -CheckOnly
```

### Option 3: Build Quality Check Only

If you've already formatted, just check build quality:
```pwsh
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1
```

### Option 4: Package Version Check Only

Check for package version inconsistencies:
```pwsh
pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1
```

### Option 5: Enable Roslynator Analyzers (One-Time Setup)

Add 200+ code quality analyzers that run during build to catch issues early:

```pwsh
# Step 1: Add Roslynator.Analyzers NuGet package to all projects
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1

# Step 2: Configure .editorconfig with Roslynator severity settings
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
```

**When to use:**
- Setting up a new project for the first time
- When you want build-time enforcement of code quality rules
- Before starting a major refactoring effort

**Note:** This is a one-time setup. Once enabled, Roslynator analyzers will run automatically during every build, providing immediate feedback on code quality issues.

### Option 6: Auto-Fix Roslynator Issues

After enabling Roslynator analyzers, you may see hundreds of code quality warnings. Many of these can be automatically fixed using the Roslynator CLI:

```pwsh
# Auto-fix Roslynator diagnostics across the entire solution
roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format
```

**What this does:**
- Automatically fixes code quality issues detected by Roslynator analyzers
- Applies code refactorings (simplify expressions, use recommended patterns, etc.)
- Formats the code during fixing

**Expected Results:**
- Can fix 200-300+ diagnostics automatically in large codebases
- Unfixable diagnostics (e.g., missing documentation comments) remain as warnings
- Some manual fixes may still be needed for complex issues

**When to use:**
- After enabling Roslynator analyzers for the first time (initial cleanup)
- After upgrading Roslynator version (new rules may trigger)
- When you have accumulated many Roslynator warnings

**Best Practice Workflow After Auto-Fix:**
```pwsh
# Step 1: Auto-fix what can be fixed
roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format

# Step 2: Review what couldn't be fixed
dotnet build 2>&1 | grep "error RCS" | grep -oP "RCS\d+" | sort | uniq -c

# Step 3: Downgrade unfixable rules to suggestion severity in .editorconfig
# (e.g., RCS1141 for missing documentation comments)

# Step 4: Verify clean build
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1
```

**Important Notes:**
- Some diagnostics cannot be auto-fixed and require manual intervention:
  - **Documentation comments** (RCS1141, RCS1140, RCS1142) - require human-written descriptions
  - **Unused parameters** (RCS1163) - may be required by interfaces
  - **Complex refactorings** - require semantic understanding
- Unfixable rules should be downgraded from `error` to `suggestion` in `.editorconfig` to allow builds to pass while keeping them as IDE hints
- Always review auto-fixes before committing - verify tests still pass and logic is unchanged

## Enabling Roslynator Analyzers (Optional but Recommended)

### What are Roslynator Analyzers?

Roslynator is a comprehensive collection of 200+ code analyzers, refactorings, and fixes for C#. Unlike the Roslynator CLI tool (which runs on-demand during formatting), Roslynator.Analyzers is a NuGet package that integrates directly into your build process.

**Key Benefits:**
- **Build-time enforcement:** Issues are detected during compilation, not just during formatting
- **Immediate feedback:** Your IDE shows warnings as you type
- **Comprehensive coverage:** 200+ analyzers covering code quality, style, performance, and potential bugs
- **Configurable severity:** Control which rules are errors, warnings, or suggestions
- **Team consistency:** Same rules enforced for all developers through .editorconfig

### How They Differ from the CLI Tool

| Feature | Roslynator CLI (format-code.ps1) | Roslynator.Analyzers (NuGet) |
|---------|----------------------------------|------------------------------|
| **When it runs** | On-demand (manual script execution) | Every build (automatic) |
| **What it does** | Fixes issues automatically | Detects and reports issues |
| **Integration** | External tool | Built into compilation |
| **IDE support** | No real-time feedback | Real-time feedback as you type |
| **Team enforcement** | Requires manual runs | Automatic enforcement |

**Recommendation:** Use both together for maximum code quality:
1. Enable Roslynator.Analyzers for continuous enforcement
2. Run format-code.ps1 to automatically fix detected issues

### When to Enable Roslynator Analyzers

Enable Roslynator analyzers when you want:
- **Proactive quality enforcement:** Catch issues during development, not just before commit
- **Consistent team standards:** Ensure all developers see the same warnings
- **Build-time validation:** Prevent low-quality code from being built
- **Comprehensive coverage:** Go beyond basic compiler warnings
- **IDE integration:** Real-time feedback while coding

### Expected Impact

**Build Time:** Expect 10-30% increase in build time as analyzers run on every build. The exact impact depends on:
- Number of projects in your solution
- Size of your codebase
- Number of enabled analyzer rules

**Initial Warnings:** May introduce 100s of new warnings initially, especially if the codebase hasn't been consistently formatted.

**Recommendations:**
1. **Run `format-code.ps1` BEFORE enabling analyzers** to reduce initial noise from fixable issues
2. **Start with `suggestion` severity**, gradually increase to `warning` or `error`:
   ```pwsh
   pwsh scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion
   ```
3. **Use `-ExcludeSubmodules`** if you don't want to analyze external dependencies:
   ```pwsh
   pwsh scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
   ```
4. **Review warnings incrementally** - fix high-priority issues first, then address lower-priority ones
5. **Disable noisy rules** if certain warnings are overwhelming (see Troubleshooting below)

**Best Practice Workflow:**
```pwsh
# Step 1: Format code first to fix auto-fixable issues
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1

# Step 2: Enable analyzers (exclude submodules for faster builds)
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Step 3: Configure with lower severity initially
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion

# Step 4: Auto-fix Roslynator issues
roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format

# Step 5: Build and review remaining warnings
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1

# Step 6: Downgrade unfixable rules to suggestion in .editorconfig
# (Review build output to identify which rules need downgrading)

# Step 7: Verify clean build
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1

# Step 8: Gradually increase severity for critical rules
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
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

### enable-roslynator-analyzers.ps1

**Purpose:** Add the Roslynator.Analyzers NuGet package to all .csproj files in the solution

**What it does:**
1. Scans all .csproj files in the solution
2. Checks which projects already have Roslynator.Analyzers
3. Adds the package reference with proper configuration (PrivateAssets, IncludeAssets)
4. Reports which projects were modified or already had the package

**How it configures the package:**
The script adds a properly configured PackageReference:
```xml
<PackageReference Include="Roslynator.Analyzers" Version="4.14.1">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

This ensures the analyzers run during build but don't propagate to consuming projects.

**Output formats:**
- `Console` (default) - Colored report with modification status for each project
- `Json` - Structured data for automation and tooling
- `Summary` - Quick statistics-only output

**Key data reported:**
- Total projects scanned
- Projects that already have Roslynator.Analyzers
- Projects where the package was added
- Relative path for each project

**Exit codes:**
- `0` - Success (all projects processed successfully)
- `1` - Failure (no projects found or modification failed)

**Key flags:**
- `-RoslynatorVersion <version>` - Specify version to install (default: 4.14.1, latest as of October 2025)
- `-CheckOnly` - Check which projects need the package without making changes
- `-RemoveAnalyzers` - Remove Roslynator.Analyzers from all projects (useful for reverting)
- `-ExcludeSubmodules` - Exclude projects in submodules/ directory from processing (recommended for faster builds)
- `-WhatIf` - Preview changes without actually modifying files (PowerShell ShouldProcess support)
- `-OutputFormat Console|Json|Summary` - Choose output type
- `-SaveToFile <path>` - Export report to file

**Usage examples:**
```pwsh
# Add to all projects (default version 4.14.1)
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1

# Add only to main projects, excluding submodules (recommended)
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules

# Preview what would be changed
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -WhatIf

# Check which projects need it
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# Install specific version
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -RoslynatorVersion "4.12.0"

# Remove from all projects
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers

# Export results to JSON
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -OutputFormat Json -SaveToFile roslynator-status.json
```

### configure-roslynator-editorconfig.ps1

**Purpose:** Create or update .editorconfig file with Roslynator analyzer severity settings and code style preferences

**What it does:**
1. Creates a new .editorconfig file if it doesn't exist (or updates existing one)
2. Adds Roslynator configuration section with:
   - Global severity setting for all Roslynator rules
   - Enable/disable flags for analyzers, refactorings, and compiler fixes
   - Code style preferences (var usage, accessibility modifiers, etc.)
   - Individual rule configurations for common scenarios
3. Preserves existing .editorconfig content (appends Roslynator section)
4. Prevents duplicate configuration (detects existing Roslynator settings)

**Severity Levels Explained:**

| Severity | Effect | When to Use |
|----------|--------|-------------|
| `none` | Rules are disabled | When you want to disable Roslynator entirely |
| `silent` | Rules run but produce no diagnostics | Testing analyzers without affecting builds |
| `suggestion` | IDE shows hints (no build impact) | Non-critical style preferences |
| `warning` | Build produces warnings (recommended) | Code quality rules that should be addressed |
| `error` | Build fails if violations exist | Critical rules that must be enforced |

**Configuration Options:**

The script configures these key Roslynator settings:
- `roslynator_analyzers.enabled_by_default` - Enable analyzers globally
- `dotnet_analyzer_diagnostic.category-roslynator.severity` - Global severity level
- `roslynator_refactorings.enabled` - Enable code refactoring suggestions
- `roslynator_compiler_diagnostic_fixes.enabled` - Enable compiler diagnostic fixes
- Code style options (var usage, field prefixes, accessibility modifiers, etc.)
- Individual rule overrides (e.g., RCS1036, RCS1037, RCS1163)

**Output:**
The script provides console output only (no JSON/Summary formats since it's a one-time configuration).

**Exit codes:**
- `0` - Success (configuration added successfully)
- `1` - Failure (file exists with Roslynator config, or other error)

**Key flags:**
- `-Severity <level>` - Set global severity (none|silent|suggestion|warning|error) - default: warning
- `-ConfigFile <path>` - Path to .editorconfig file - default: .editorconfig in current directory
- `-CreateIfMissing <bool>` - Create new file if doesn't exist - default: true
- `-ShowPreview` - Preview what would be added without making changes
- `-WhatIf` - Preview changes without actually modifying the file (PowerShell ShouldProcess support)
- `-EnableAnalyzers <bool>` - Enable/disable analyzers - default: true

**Usage examples:**
```pwsh
# Set all rules to 'warning' (default, recommended)
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1

# Set to 'error' for strict enforcement (build fails on violations)
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity error

# Preview changes without applying them (two equivalent ways)
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -ShowPreview
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -WhatIf

# Configure a specific .editorconfig file
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -ConfigFile "src\.editorconfig"

# Set to 'suggestion' for non-blocking hints
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion

# Disable analyzers (while keeping configuration for later)
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -EnableAnalyzers $false
```

**Note:** After running this script, you should:
1. Review the generated .editorconfig and customize individual rules as needed
2. Restart your IDE for changes to take effect
3. Run `dotnet build` to see Roslynator warnings/errors
4. Run `pwsh scripts/format-code.ps1` to auto-fix issues detected by analyzers

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

### 1. Complete One-Time Setup First

Before using the clean-builds workflow for the first time, complete the [Prerequisites and One-Time Setup](#prerequisites-and-one-time-setup). This ensures all code quality tools are properly configured.

**Why this matters**:
- Without Roslynator analyzers, you'll miss 200+ code quality issues during build
- Without EnforceCodeStyleInBuild, IDE warnings won't appear during `dotnet build`
- Without proper .editorconfig, team members may have inconsistent settings

**Check if setup is complete**:
```pwsh
# Should report all projects have analyzers
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# Should report all projects have enforcement enabled
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1
```

### 2. Format After Every Change
Run format-code.ps1 regularly during development, not just before commit.

### 3. Fix Warnings Immediately
Don't accumulate warnings—fix them as you encounter them.

### 4. NEVER Suppress Warnings Without Fixing The Root Cause

**🚫 CRITICAL: Avoid `#pragma warning disable`**

Using `#pragma warning disable` to suppress warnings without fixing the underlying issue is considered **code smell** and creates technical debt. It hides problems instead of solving them.

**BAD Example (Don't do this):**
```csharp
// TODO: Remove or re-enable when Orleans LLM integration is fully active
#pragma warning disable IDE0051 // Remove unused private members

private void UnusedMethod()
{
    // This method is no longer needed but we're hiding the warning
}
```

**GOOD Example (Fix the actual issue):**
```csharp
// Method removed entirely since it's no longer needed
// Old Orleans LLM integration is now handled by ChatGrain
```

**When pragma suppression seems necessary:**

1. **First, try to fix the issue properly:**
   - IDE0051 (unused member) → Remove the member entirely
   - IDE0055 (formatting) → Run the formatter to fix formatting
   - CS0618 (obsolete API) → Update to the non-obsolete API
   - IDE0052 (unread field) → Either use the field or remove it

2. **If the warning is truly inappropriate:**
   - Use `.editorconfig` to downgrade severity globally or per-file
   - Document WHY the rule doesn't apply
   - Examples of legitimate cases:
     - Interface members required but not used yet
     - Low-level performance code that needs specific patterns
     - Test mocks that intentionally violate normal rules

3. **Never suppress to avoid fixing formatting or code quality issues**
   - Use auto-fixers: `roslynator fix`, `dotnet format`
   - Let the tools do the work
   - If a fixer creates broken code, report it and fix manually

**Consequences of excessive suppression:**
- ❌ Warnings accumulate and become unmanageable
- ❌ Real issues get hidden among suppressed warnings
- ❌ Code quality degrades over time
- ❌ Team members stop trusting the warning system
- ❌ Technical debt grows exponentially

**The clean-builds philosophy:**
- ✅ Zero warnings through proper fixes, not suppression
- ✅ Auto-fixers do the heavy lifting
- ✅ Manual fixes for cases that can't be automated
- ✅ `.editorconfig` for project-wide policy, not per-file suppression

### 5. Understand Each Warning
Read the warning message and URL before dismissing. Warnings usually indicate real issues.

### 6. Use the Grouped Output
The script groups warnings by code, making it easy to batch-fix similar issues.

### 7. Validate Package Versions

Run package validation before committing, especially if you've updated any dependencies:
```pwsh
pwsh scripts/validate-package-versions.ps1
```

Fix critical issues (CRITICAL severity) before proceeding. Warnings can be addressed during the next maintenance window.

### 8. Enable Code Style Enforcement During Build

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

### 8. Pre-Commit Validation

Always run the full workflow before creating a commit:
```pwsh
# Step 1: Validate and enable code style enforcement
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1 -Enforce
# This enables IDE0005 and style rule detection during build

# Step 2: Validate packages
pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1
# Fix any CRITICAL issues

# Step 3: Format
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1

# Step 4: Build & Check
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1

# Only commit if all validations succeed
git add .
git commit -m "message"
```

**Note:** The `-Enforce` flag in step 1 automatically enables `EnforceCodeStyleInBuild` in any projects that are missing it. This ensures IDE0005 warnings are detected during the build check in step 4.

### 9. Enable Roslynator Analyzers for Maximum Code Quality

For the most comprehensive code quality enforcement, enable Roslynator analyzers in all your projects. This provides build-time analysis with 200+ code quality rules.

**Why enable Roslynator analyzers:**
- **Proactive detection:** Issues are caught during development, not just when you run format-code.ps1
- **IDE integration:** Real-time feedback as you type code
- **Build enforcement:** Prevents poor-quality code from being compiled
- **Comprehensive rules:** Covers areas that standard compiler warnings miss
- **Team consistency:** Everyone sees the same warnings in their IDE

**How to enable (one-time setup):**
```pwsh
# Step 1: Add Roslynator.Analyzers to all projects
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1

# Step 2: Configure severity levels in .editorconfig
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
```

**Recommended severity levels:**
- **Most projects:** Use `warning` (default) - issues show as warnings but don't block builds
- **Strict enforcement:** Use `error` - violations will fail the build
- **Gradual adoption:** Use `suggestion` - issues show as IDE hints only

**Best practice workflow:**
1. Enable analyzers once during initial project setup
2. Configure with `warning` severity for team visibility
3. Run `build_and_group_errors_and_warnings.ps1` to see all Roslynator warnings
4. Run `format-code.ps1` to automatically fix issues
5. Review and customize .editorconfig to adjust specific rule severities

**Benefits of build-time vs. format-time analysis:**

| Aspect | Format-time (format-code.ps1) | Build-time (Roslynator.Analyzers) |
|--------|-------------------------------|-------------------------------------|
| When issues detected | Only when script runs | Every build, real-time in IDE |
| What happens | Automatically fixes issues | Reports issues for you to fix |
| Developer awareness | Only if they run the script | Immediate feedback while coding |
| Team enforcement | Manual script execution | Automatic for everyone |
| Coverage | Runs during formatting only | Continuous throughout development |

**Recommendation:** Enable Roslynator analyzers for continuous enforcement and use format-code.ps1 to batch-fix issues.

## Bundled Scripts

### `<clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1`

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
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1

# Automatically enable it in all projects
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1 -Enforce

# Export findings to JSON
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1 -OutputFormat Json -SaveToFile style-report.json

# Check only, don't enforce
pwsh <clean_builds_skill_base_dir>/scripts/validate-code-style-enforcement.ps1 -CheckOnly
```

**Exit codes:**
- `0` - Success (all projects have enforcement enabled)
- `1` - Failure (projects missing enforcement and -Enforce not used)

### `<clean_builds_skill_base_dir>/scripts/format-code.ps1`

Complete code formatting workflow with multiple tools.

**Requirements:**
- `dotnet format` (comes with .NET SDK)
- `JetBrains.ReSharper.GlobalTools` - Install with: `dotnet tool install -g JetBrains.ReSharper.GlobalTools`
- `csharpier` (optional) - Install with: `dotnet tool install -g csharpier`
- `Roslynator.DotNet.Cli` (optional) - Install with: `dotnet tool install -g Roslynator.DotNet.Cli`

**Usage:**
```pwsh
# Full format (default)
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1

# Check only
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1 -CheckOnly

# Format root project only
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1 -RootOnly

# Format submodules only
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1 -SubmodulesOnly

# Show help
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1 -Help
```

### `<clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1`

Clean build with error/warning analysis and grouping.

**Requirements:**
- .NET SDK (for `dotnet clean` and `dotnet build`)

**Usage:**
```pwsh
# Default console output
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1

# Export as JSON
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile results.json

# Export as CSV
pwsh scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Csv -SaveToFile results.csv

# Custom solution path
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1 -SolutionPath "path/to/solution.sln"
```

### `scripts/validate-package-versions.ps1`

Validates NuGet package version consistency across all projects.

**Requirements:**
- PowerShell 5+
- .NET SDK with project files (.csproj)

**Usage:**
```pwsh
# Default console output with colored severity levels
pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1

# Export validation results to JSON
pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1 -OutputFormat Json -SaveToFile version-report.json

# Quick summary statistics only
pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1 -OutputFormat Summary

# Export as JSON (alternative syntax)
pwsh <clean_builds_skill_base_dir>/scripts/validate-package-versions.ps1 -SaveToFile version-report.json
```

**Exit codes for CI/CD:**
- `0` = Success (no critical issues found)
- `1` = Failure (critical issues found - must fix)

### `<clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1`

Adds Roslynator.Analyzers NuGet package to all .NET projects for build-time code analysis.

**Purpose:** Enable 200+ code quality analyzers to run during every build, providing immediate feedback on code quality issues.

**Requirements:**
- PowerShell 5+
- .NET SDK with project files (.csproj)

**Usage:**
```pwsh
# Add Roslynator.Analyzers to all projects
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1

# Check which projects need it (dry-run)
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -CheckOnly

# Install specific version
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -RoslynatorVersion "4.12.0"

# Remove from all projects
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers

# Export results to JSON
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -OutputFormat Json -SaveToFile analyzers-report.json
```

**Exit codes:**
- `0` = Success (projects processed successfully)
- `1` = Failure (no projects found or modification failed)

**Note:** This is a one-time setup script. After running, the analyzers will be part of your project files and will run during every build.

### `s<clean_builds_skill_base_dir>/cripts/configure-roslynator-editorconfig.ps1`

Creates or updates .editorconfig file with Roslynator analyzer configuration, including severity settings and code style preferences.

**Purpose:** Configure Roslynator rule severities and code style options in a centralized .editorconfig file for team-wide consistency.

**Requirements:**
- PowerShell 5+

**Usage:**
```pwsh
# Set all rules to 'warning' (default, recommended)
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1

# Set to 'error' for strict enforcement
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity error

# Preview changes without applying
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -ShowPreview

# Configure specific file
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -ConfigFile "src\.editorconfig"

# Set to 'suggestion' for IDE hints only
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion
```

**Exit codes:**
- `0` = Success (configuration added)
- `1` = Failure (Roslynator config already exists or other error)

**Note:** After running this script, restart your IDE and run `dotnet build` to see Roslynator warnings/errors.

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
   pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1
   ```

5. **Re-run build check to verify:**
   ```pwsh
   pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1
   ```

**Note:** This is a build-time enforcement feature, not a runtime issue. Adding `EnforceCodeStyleInBuild` enables static analysis during compilation.

### Too Many Warnings After Enabling Roslynator

If you get overwhelmed with warnings after enabling Roslynator analyzers:

**Solution 1: Start with lower severity**
```pwsh
# Set to 'suggestion' so warnings don't block your workflow
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion

# Review suggestions in IDE, fix what makes sense
# Then gradually increase severity
pwsh <clean_builds_skill_base_dir>/scripts/configure-roslynator-editorconfig.ps1 -Severity warning
```

**Solution 2: Format code first to auto-fix issues**
```pwsh
# Run formatter to automatically fix many analyzer warnings
pwsh <clean_builds_skill_base_dir>/scripts/format-code.ps1

# Then rebuild to see remaining warnings
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1
```

**Solution 3: Disable specific noisy rules**

Edit `.editorconfig` to disable rules that are too noisy for your codebase:
```ini
# Disable specific rules that are too noisy
dotnet_diagnostic.rcs1036.severity = none  # Remove unnecessary blank line
dotnet_diagnostic.rcs1138.severity = none  # Add summary to documentation comment
```

To find which rules are producing the most warnings:
```pwsh
# Build and save to JSON to analyze warnings
pwsh <clean_builds_skill_base_dir>/scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile warnings.json

# Review the JSON to see which RCS codes appear most frequently
```

**Solution 4: Exclude submodules from analysis**

If external dependencies are generating warnings:
```pwsh
# Remove Roslynator from submodules
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers

# Re-enable only for main projects
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

### Build Time Increased Significantly After Enabling Roslynator

If builds become too slow after enabling Roslynator analyzers:

**Solution 1: Exclude submodules**
```pwsh
# External code analysis adds overhead without providing value
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers
pwsh <clean_builds_skill_base_dir>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

**Solution 2: Disable analyzers in Debug builds**

Edit your `.csproj` files to enable analyzers only for Release builds:
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <RunAnalyzers>true</RunAnalyzers>
</PropertyGroup>
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <RunAnalyzers>false</RunAnalyzers>
</PropertyGroup>
```

**Solution 3: Review and disable non-essential rules**

Disable entire categories of rules that aren't critical:
```ini
# In .editorconfig
# Disable all documentation-related rules
dotnet_diagnostic.rcs1138.severity = none
dotnet_diagnostic.rcs1139.severity = none

# Disable all formatting rules (handled by format-code.ps1)
dotnet_diagnostic.rcs1036.severity = none
dotnet_diagnostic.rcs1037.severity = none
```

**Solution 4: Use incremental builds**

Ensure your build environment supports incremental compilation:
```pwsh
# Clean only when necessary, not before every build
dotnet build  # Incremental build (fast)

# vs
dotnet clean && dotnet build  # Full rebuild (slow)
```

**Expected build time impact:**
- Small projects (1-5 projects): +10-15%
- Medium projects (10-20 projects): +15-25%
- Large projects (30+ projects): +20-30%

### Roslynator Warnings Differ from IDE Suggestions

If Visual Studio/Rider shows different warnings than the build:

**Cause:** IDE might be using different analyzer versions or .editorconfig settings.

**Solution:**
1. **Restart your IDE** after changing .editorconfig
2. **Clear IDE caches**:
   - Visual Studio: Delete `.vs` folder, restart
   - Rider: File → Invalidate Caches / Restart
3. **Verify .editorconfig is in solution root** and `root = true` is set
4. **Check IDE analyzer settings** match .editorconfig severity levels

## References

For detailed information:

- **One-Time Setup Guide**: [Step-by-step guide to configuring all tools](references/one-time-setup-guide.md)
  - Complete setup checklist with verification steps
  - Troubleshooting for each setup step
  - Understanding what each tool does
  - Commit strategy for setup changes

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
