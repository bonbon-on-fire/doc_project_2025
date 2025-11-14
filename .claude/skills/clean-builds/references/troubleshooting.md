# Clean Builds Troubleshooting Guide

This guide provides solutions to common problems encountered when using the clean-builds skill.

## Quick Links

- [Script Errors](#script-errors)
- [Build Issues](#build-issues)
- [Warning Problems](#warning-problems)
- [Roslynator Issues](#roslynator-issues)
- [IDE Integration Problems](#ide-integration-problems)

---

## Script Errors

### Script Fails: "Tool not found"

**Problem**: A required tool is missing.

**Error message**:
```
Error: 'dotnet' is not recognized as an internal or external command
Error: 'jb' command not found
Error: 'roslynator' command not found
```

**Solution**: Install the missing tool based on the error:

```pwsh
# .NET SDK (required)
# Download from: https://dotnet.microsoft.com/download

# ReSharper CLT (required for format-code.ps1)
dotnet tool install -g JetBrains.ReSharper.GlobalTools

# Roslynator CLI (optional but recommended)
dotnet tool install -g Roslynator.DotNet.Cli

# CSharpier (optional for submodule formatting)
dotnet tool install -g csharpier
```

**Verification**:
```pwsh
# Check tool installation
dotnet --version
jb --version
roslynator --version
csharpier --version
```

---

### "No projects found"

**Problem**: Script can't locate .csproj or .sln files.

**Possible causes**:
- Running from wrong directory
- No .csproj/.sln files exist
- Permissions issues

**Solutions**:

1. **Verify current directory**:
   ```pwsh
   Get-Location
   # Should be in solution root
   ```

2. **Check for project files**:
   ```pwsh
   Get-ChildItem -Recurse -Filter "*.csproj"
   Get-ChildItem -Filter "*.sln"
   ```

3. **Explicitly specify solution path**:
   ```pwsh
   pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1 -SolutionPath "path/to/solution.sln"
   ```

---

### "Access denied" or "File is read-only"

**Problem**: Can't modify .csproj or .editorconfig files.

**Solutions**:

1. **Check file permissions**:
   ```pwsh
   Get-ChildItem -Recurse -Filter "*.csproj" | ForEach-Object {
       if ($_.IsReadOnly) {
           Write-Host "Read-only: $($_.FullName)"
       }
   }
   ```

2. **Remove read-only attribute**:
   ```pwsh
   Get-ChildItem -Recurse -Filter "*.csproj" | ForEach-Object {
       $_.IsReadOnly = $false
   }
   ```

3. **Close IDE** (Visual Studio/Rider may lock files)

4. **Run PowerShell as administrator** (if permissions are restricted)

---

## Build Issues

### Build Takes Very Long

**Problem**: Clean build is slower than expected.

**Expected behavior**: This is normal. Clean builds compile everything from scratch.

**Typical build times**:
- Small projects (1-5 projects): 30-60 seconds
- Medium projects (10-20 projects): 2-5 minutes
- Large projects (30+ projects): 5-15 minutes

**Solutions**:

1. **Ensure sufficient disk space**:
   ```pwsh
   Get-PSDrive C | Select-Object Used,Free
   ```

2. **Close other applications** to free resources

3. **Use incremental builds** during development:
   ```pwsh
   # Don't clean before every build
   dotnet build  # Fast (incremental)

   # Only clean when needed
   dotnet clean && dotnet build  # Slow (full rebuild)
   ```

4. **Exclude submodules** from Roslynator analysis:
   ```pwsh
   pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers
   pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
   ```

---

### Build Errors vs Build Warnings

**Problem**: Understanding difference between errors and warnings.

**Errors** (must fix):
- Prevent successful compilation
- Exit code: 1
- Example: `error CS1002: ; expected`

**Warnings** (should fix):
- Build succeeds but issues exist
- Exit code: 0 (build succeeds)
- Example: `warning IDE0005: Using directive is unnecessary`

**Strategy**:
1. Fix all errors first
2. Then address warnings
3. Aim for zero warnings before commit

---

### "Can't modify submodule code"

**Problem**: Build warnings appear in external submodule code.

**Explanation**: Submodules are external dependencies. You shouldn't modify their code directly.

**Solutions**:

1. **Focus on main project** warnings only

2. **Exclude submodules from analysis**:
   ```pwsh
   pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
   ```

3. **Add submodules to .editorconfig exclude list**:
   ```ini
   # .editorconfig
   [submodules/**]
   generated_code = true
   dotnet_analyzer_diagnostic.severity = none
   ```

4. **Report issues to submodule maintainers** if critical

---

## Warning Problems

### IDE0005 Warnings Not Being Detected

**Problem**: IDE0005 (unused imports) not appearing during build.

**Cause**: `EnforceCodeStyleInBuild` is not enabled in .csproj files.

**Solution**:

1. **Enable code style enforcement**:
   ```pwsh
   pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1 -Enforce
   ```

2. **Verify setting is added**:
   ```xml
   <!-- Should be in .csproj -->
   <PropertyGroup>
       <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
   </PropertyGroup>
   ```

3. **Rebuild solution**:
   ```pwsh
   dotnet clean
   dotnet build
   ```

4. **Run build check**:
   ```pwsh
   pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1
   ```

---

### Warnings Not Disappearing After Fix

**Problem**: Fixed code but warnings still appear.

**Solutions**:

1. **Re-run formatter** to catch any remaining issues:
   ```pwsh
   pwsh <skill_base_path>/scripts/format-code.ps1
   ```

2. **Clean and rebuild**:
   ```pwsh
   dotnet clean
   dotnet build
   ```

3. **Verify you fixed the correct file/line**:
   - Check build output for exact file path
   - Ensure changes were saved

4. **Check for multiple occurrences** of the same issue

5. **Run build check with JSON output** for exact details:
   ```pwsh
   pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile report.json
   ```

---

### Different Warning Counts in IDE vs Build

**Problem**: IDE shows different warning count than build script.

**Possible causes**:
- IDE is analyzing without building
- IDE has different analyzer settings
- Cache differences
- Different analyzer versions

**Solutions**:

1. **Trust the build script** (uses actual `dotnet build`)

2. **Clear IDE caches**:
   - Visual Studio: Delete `.vs` folder, restart
   - Rider: File → Invalidate Caches / Restart
   - VS Code: Reload window

3. **Restart IDE** to reload analyzers

4. **Verify analyzer versions match**:
   ```pwsh
   # Check Roslynator version in .csproj
   Get-ChildItem -Recurse -Filter "*.csproj" | Select-String "Roslynator.Analyzers"
   ```

5. **Compare settings**:
   - IDE analyzer settings
   - .editorconfig rules
   - .csproj settings

---

## Roslynator Issues

### Too Many Warnings After Enabling Roslynator

**Problem**: Overwhelmed with 100s of warnings after enabling Roslynator analyzers.

**Expected behavior**: This is normal for first-time setup.

**Solutions (in order of preference)**:

#### Solution 1: Start with Lower Severity

```pwsh
# Set to 'suggestion' for IDE hints only
pwsh <skill_base_path>/scripts/configure-roslynator-editorconfig.ps1 -Severity suggestion
```

Then gradually increase:
```ini
# After fixing issues, edit .editorconfig:
dotnet_analyzer_diagnostic.category-roslynator.severity = warning
```

#### Solution 2: Auto-Fix First

Let Roslynator fix what it can automatically:

```pwsh
# Auto-fix Roslynator issues
roslynator fix DOC_Project_2025.sln --ignore-compiler-errors --format

# Then build to see remaining warnings
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1
```

Expected: 200-300+ warnings fixed automatically.

#### Solution 3: Disable Specific Noisy Rules

Find which rules are generating the most warnings:

```pwsh
# Build and analyze
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile warnings.json

# Review JSON to identify top offenders
```

Then disable noisy rules in .editorconfig:

```ini
# Disable specific rules
dotnet_diagnostic.rcs1036.severity = none  # Remove unnecessary blank line
dotnet_diagnostic.rcs1138.severity = none  # Add summary to documentation comment
dotnet_diagnostic.rcs1141.severity = none  # Add param element to documentation comment
```

#### Solution 4: Disable Documentation Rules

Documentation rules often generate the most noise:

```ini
# Disable all documentation rules
dotnet_diagnostic.rcs1138.severity = none  # Add summary element
dotnet_diagnostic.rcs1139.severity = none  # Add summary to documentation comment
dotnet_diagnostic.rcs1140.severity = none  # Add exception to documentation comment
dotnet_diagnostic.rcs1141.severity = none  # Add param element
dotnet_diagnostic.rcs1142.severity = none  # Add type param element
```

---

### Build Time Increased Significantly After Enabling Roslynator

**Problem**: Builds are 30%+ slower after enabling Roslynator analyzers.

**Expected impact**: 10-30% increase is normal.

**Solutions**:

#### Solution 1: Exclude Submodules

External code analysis adds overhead without value:

```pwsh
pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -RemoveAnalyzers
pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
```

#### Solution 2: Disable Analyzers in Debug Builds

Add to .csproj files:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <RunAnalyzers>true</RunAnalyzers>
</PropertyGroup>
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <RunAnalyzers>false</RunAnalyzers>
</PropertyGroup>
```

#### Solution 3: Disable Non-Essential Rules

Disable entire categories:

```ini
# Disable all formatting rules (handled by format-code.ps1)
dotnet_diagnostic.rcs1036.severity = none
dotnet_diagnostic.rcs1037.severity = none

# Disable all documentation rules
dotnet_diagnostic.rcs1138.severity = none
dotnet_diagnostic.rcs1139.severity = none
```

#### Solution 4: Use Incremental Builds

```pwsh
# Incremental build (fast)
dotnet build

# vs Full rebuild (slow)
dotnet clean && dotnet build
```

Only clean when necessary:
- After changing .csproj files
- After updating NuGet packages
- When debugging build issues

---

### Roslynator Warnings Differ from IDE Suggestions

**Problem**: Visual Studio/Rider shows different warnings than build.

**Cause**: IDE might use different analyzer versions or .editorconfig settings.

**Solutions**:

1. **Restart IDE** after changing .editorconfig

2. **Clear IDE caches**:
   - Visual Studio: Delete `.vs` folder, restart
   - Rider: File → Invalidate Caches / Restart

3. **Verify .editorconfig is in solution root** with `root = true`:
   ```ini
   # .editorconfig
   root = true

   [*.cs]
   # Roslynator settings...
   ```

4. **Check IDE analyzer settings** match .editorconfig severity levels

5. **Verify same Roslynator version**:
   ```pwsh
   # Check all projects use same version
   Get-ChildItem -Recurse -Filter "*.csproj" | Select-String "Roslynator.Analyzers"
   ```

---

## IDE Integration Problems

### IDE Not Respecting .editorconfig Settings

**Problem**: Changes in .editorconfig don't take effect in IDE.

**Solutions**:

1. **Restart IDE** - Always required after .editorconfig changes

2. **Verify .editorconfig location**:
   - Must be in solution root
   - Must have `root = true` at top

3. **Check file is saved** (obvious but often overlooked)

4. **Clear IDE caches**:
   - Visual Studio: Delete `.vs` folder
   - Rider: File → Invalidate Caches / Restart
   - VS Code: Reload window

5. **Verify .editorconfig syntax**:
   ```pwsh
   # Check for syntax errors
   Get-Content .editorconfig | Select-String "="
   ```

6. **Check IDE EditorConfig support**:
   - Visual Studio: Built-in
   - Rider: Built-in
   - VS Code: Requires EditorConfig extension

---

### IDE Shows Warnings but Build Doesn't

**Problem**: IDE shows warnings that don't appear during `dotnet build`.

**Cause**: IDE may run additional analyzers not included in build.

**Solutions**:

1. **Enable `EnforceCodeStyleInBuild`**:
   ```pwsh
   pwsh <skill_base_path>/scripts/validate-code-style-enforcement.ps1 -Enforce
   ```

2. **Add Roslynator analyzers to build**:
   ```pwsh
   pwsh <skill_base_path>/scripts/enable-roslynator-analyzers.ps1 -ExcludeSubmodules
   ```

3. **Check IDE-specific analyzer settings**:
   - Visual Studio: Tools → Options → Text Editor → C# → Code Style
   - Rider: Settings → Editor → Code Style → C#

---

### "Output is truncated"

**Problem**: Too many warnings to display in console.

**Solution**: Export to JSON or CSV for full details:

```pwsh
# Export full report
pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile full-report.json

# Then analyze
$report = Get-Content full-report.json | ConvertFrom-Json
$report.warnings | ForEach-Object {
    Write-Host "$($_.code): $($_.count) occurrences"
}
```

---

## Package Version Issues

### Critical Package Issue Won't Go Away After Fixing

**Problem**: Updated package version but validation still reports the issue.

**Checklist**:

1. ✅ **Saved the file** - Ensure changes were saved

2. ✅ **Updated ALL occurrences** - Some packages appear multiple times:
   ```pwsh
   # Find all occurrences
   Get-ChildItem -Recurse -Filter "*.csproj" | Select-String "Microsoft.Orleans.Core"
   ```

3. ✅ **Edited correct file** - Verify path matches validation report

4. ✅ **No typos in version number** - Check for extra spaces, dots

5. ✅ **Re-ran validation**:
   ```pwsh
   pwsh <skill_base_path>/scripts/validate-package-versions.ps1
   ```

6. ✅ **Rebuild solution**:
   ```pwsh
   dotnet clean
   dotnet build
   ```

---

### "Different versions but validation says consistent"

**Problem**: Seeing different versions but script shows consistent.

**Explanation**: The script only analyzes `.csproj` files (PackageReference format), not `packages.config` (legacy format).

**Solution**: Migrate from packages.config to PackageReference:

1. Visual Studio: Right-click project → Migrate packages.config to PackageReference
2. Or manually convert references

---

## Getting More Help

If you're still stuck:

1. **Review script output carefully** - Error messages often contain the solution

2. **Check related documentation**:
   - [Script Documentation](scripts/) - Detailed script usage
   - [Warning Codes Guide](warning-codes-guide.md) - Understand specific warnings
   - [Best Practices](best-practices.md) - Recommended workflows

3. **Export detailed reports** for analysis:
   ```pwsh
   pwsh <skill_base_path>/scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat Json -SaveToFile debug-report.json
   ```

4. **Test in isolation** - Try on a simple test project first

5. **Check tool versions**:
   ```pwsh
   dotnet --version
   jb --version
   roslynator --version
   ```

6. **Review recent changes** - Did this work before? What changed?

---

## Common Error Patterns

### Pattern: "It worked yesterday"

**Likely causes**:
- NuGet package was updated
- .csproj file was modified
- .editorconfig was changed
- Tool version updated

**Solution**: Use `git diff` to see what changed

---

### Pattern: "Works on my machine"

**Likely causes**:
- Different tool versions
- Missing .editorconfig commit
- Different .csproj settings
- IDE-specific settings

**Solution**: Ensure all team members have same:
- Tool versions
- .editorconfig committed
- .csproj settings committed

---

### Pattern: "Warnings keep coming back"

**Likely causes**:
- Not running format before build
- Team members not using formatters
- Auto-generated code (ignore it)

**Solution**:
1. Add to pre-commit workflow
2. Set up git hooks
3. Add to CI/CD pipeline
