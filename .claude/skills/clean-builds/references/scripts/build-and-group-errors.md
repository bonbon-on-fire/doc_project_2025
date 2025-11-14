# build_and_group_errors_and_warnings.ps1

## Purpose

Build the solution cleanly and report all errors and warnings grouped by code for easier analysis and fixing.

## Overview

This script performs a clean build of your solution and analyzes the output to extract, group, and report all build errors and warnings. It's essential for achieving and maintaining zero-warning builds.

## What It Does

1. Performs `dotnet clean` to remove build artifacts
2. Performs `dotnet build` with clean environment
3. Parses build output to extract error/warning details
4. Groups issues by type and code for easier analysis
5. Reports summary and detailed listing by file/line

## Requirements

- .NET SDK (for `dotnet clean` and `dotnet build`)
- PowerShell 5.0 or later

## Usage

### Default Console Output

Build and display results in colored, human-readable format:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1
```

### Export as JSON

Export structured data for tooling and automation:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile results.json
```

### Export as CSV

Export for spreadsheet analysis:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Csv -SaveToFile results.csv
```

### Custom Solution Path

Specify a different solution file:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -SolutionPath "path/to/solution.sln"
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-SolutionPath` | String | Auto-detect | Path to .sln file |
| `-OutputFormat` | String | Console | Output format: Console, Json, or Csv |
| `-SaveToFile` | String | None | Path to save output file |

## Output Formats

### Console (Default)

Colored, human-readable summary with grouped warnings:

```
=====================================
Build Summary
=====================================
Total Errors: 0
Total Warnings: 23

Unique Error Codes: 0
Unique Warning Codes: 4

=====================================
Warnings by Code
=====================================

[IDE0005] Remove unnecessary using directive (15 occurrences)
  src/Services/ChatService.cs:5
  src/Services/ChatService.cs:7
  src/Controllers/ChatController.cs:3
  ...

[CA1826] Use property instead of Linq method (5 occurrences)
  src/Services/UserService.cs:42
  src/Services/MessageService.cs:18
  ...

[CA1859] Use concrete types when possible for improved performance (2 occurrences)
  src/Models/ChatContext.cs:15
  ...

[IDE0052] Remove unread private members (1 occurrence)
  src/Services/LegacyService.cs:33
```

### JSON

Structured data for automation:

```json
{
  "summary": {
    "totalErrors": 0,
    "totalWarnings": 23,
    "uniqueErrorCodes": 0,
    "uniqueWarningCodes": 4
  },
  "errors": [],
  "warnings": [
    {
      "code": "IDE0005",
      "message": "Remove unnecessary using directive",
      "count": 15,
      "occurrences": [
        {
          "file": "src/Services/ChatService.cs",
          "line": 5,
          "column": 1
        }
      ]
    }
  ]
}
```

### CSV

Spreadsheet-compatible format:

```
Type,Code,Message,File,Line,Column
Warning,IDE0005,Remove unnecessary using directive,src/Services/ChatService.cs,5,1
Warning,IDE0005,Remove unnecessary using directive,src/Services/ChatService.cs,7,1
Warning,CA1826,Use property instead of Linq method,src/Services/UserService.cs,42,15
```

## Exit Codes

- `0` - Success (build completed, regardless of warnings)
- `1` - Failure (build errors or script failure)

**Note**: The script exits with 0 even if warnings are present. Check the warning count in the output to determine if action is needed.

## Key Data Reported

### Summary Section

- **Total Errors**: Count of compilation errors
- **Total Warnings**: Count of compilation warnings
- **Unique Error Codes**: Number of distinct error types
- **Unique Warning Codes**: Number of distinct warning types

### Grouped Issues

For each unique code (e.g., IDE0005, CA1826):
- **Code**: The diagnostic code
- **Message**: What the issue is
- **Count**: How many times it occurs
- **Occurrences**: List of file:line locations
- **Help URL**: Link to documentation (when available)

## Common Scenarios

### After Formatting

Verify the build is clean after formatting:

```pwsh
# Step 1: Format
pwsh ../scripts/format-code.ps1

# Step 2: Build and check
pwsh ../scripts/build_and_group_errors_and_warnings.ps1
```

### Before Committing

Final validation before creating a commit:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1
```

If warnings are present, fix them before committing.

### CI/CD Pipeline

Add to your pipeline to enforce zero-warning builds:

```yaml
- name: Build and Check Warnings
  run: pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile build-report.json

- name: Fail if Warnings Found
  run: |
    $report = Get-Content build-report.json | ConvertFrom-Json
    if ($report.summary.totalWarnings -gt 0) {
      Write-Error "Build has $($report.summary.totalWarnings) warnings"
      exit 1
    }
```

### Tracking Warning Trends

Export to CSV and track over time:

```pwsh
# Daily build check
pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Csv -SaveToFile "reports/build-$(Get-Date -Format 'yyyy-MM-dd').csv"
```

### Analyzing Specific Warning Types

Use JSON output to filter specific codes:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile results.json

# Then analyze in PowerShell
$report = Get-Content results.json | ConvertFrom-Json
$ide0005 = $report.warnings | Where-Object { $_.code -eq "IDE0005" }
Write-Host "IDE0005 appears in $($ide0005.count) locations"
```

## How It Works

### Step 1: Clean Build Artifacts

```pwsh
dotnet clean <solution>
```

Removes all previous build outputs to ensure a fresh build.

### Step 2: Build Solution

```pwsh
dotnet build <solution>
```

Compiles the solution and captures all output.

### Step 3: Parse Build Output

The script uses regex patterns to extract:
- Error/warning codes (e.g., IDE0005, CA1826)
- Messages
- File paths
- Line numbers
- Column numbers
- Help URLs (when available)

### Step 4: Group by Code

Issues are grouped by diagnostic code for easier batch fixing:
- All IDE0005 warnings together
- All CA1826 warnings together
- etc.

### Step 5: Generate Report

Output is formatted based on `-OutputFormat`:
- Console: Colored, human-readable
- JSON: Structured data
- CSV: Spreadsheet-compatible

## Interpreting Results

### Zero Warnings (Ideal)

```
Total Errors: 0
Total Warnings: 0
```

Your build is clean! Ready to commit.

### Warnings Present

```
Total Warnings: 23
Unique Warning Codes: 4
```

You have work to do. Review the grouped warnings and fix them systematically.

### Build Errors

```
Total Errors: 5
```

Fix errors first before addressing warnings. Errors prevent successful compilation.

## Troubleshooting

### "Build takes very long"

**Problem**: Clean build is slower than expected.

**Explanation**: This is normal. Clean builds compile everything from scratch.

**Solutions**:
- Ensure you have enough disk space
- Close other applications to free resources
- Subsequent builds will be faster (incremental)

### "No solution file found"

**Problem**: Script can't locate the .sln file.

**Solution**: Specify the path explicitly:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -SolutionPath "path/to/solution.sln"
```

### "Output is truncated"

**Problem**: Too many warnings to display in console.

**Solution**: Export to JSON or CSV for full details:

```pwsh
pwsh ../scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile full-report.json
```

### "Warning counts don't match IDE"

**Problem**: IDE shows different warning count than script.

**Possible Causes**:
- IDE is analyzing without building
- IDE has different analyzer settings
- Cache differences

**Solution**:
- Trust the script (it uses actual `dotnet build`)
- Clear IDE caches and rebuild
- Restart IDE

## Integration with Clean-Builds Workflow

This script is **Step 3** in the recommended workflow:

1. Validate package versions
2. Format code
3. **→ Build and check** (this script)
4. Fix warnings
5. Repeat 2-4 until clean

## Best Practices

1. **Run after formatting** - Verify formatting fixed issues
2. **Fix warnings by code** - Use grouping to batch-fix similar issues
3. **Zero warnings before commit** - Never commit code with warnings
4. **Export for tracking** - Keep historical records of build quality
5. **Integrate with CI/CD** - Enforce standards automatically

## Related Documentation

- [Warning Codes Guide](../warning-codes-guide.md) - How to fix specific warnings
- [Format Code Script](format-code.md) - Auto-fix many warnings
- [Best Practices](../best-practices.md) - Zero-warning build strategies
