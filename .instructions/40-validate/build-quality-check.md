# Build Quality Check - Critical Script

## 🚨 MANDATORY: build_and_group_errors_and_warnings.ps1

This script is **ESSENTIAL** for maintaining code quality. It captures **ALL** code style warnings and build warnings that need attention before committing.

## ⚡ When to Use

**ALWAYS run this script after format-code.ps1 and before committing any code.**

```bash
# CORRECT ORDER (NEVER change this sequence):
pwsh scripts/format-code.ps1                      # Step 1: Auto-fix formatting
pwsh scripts/build_and_group_errors_and_warnings.ps1  # Step 2: Check ALL warnings
pwsh scripts/quality-check.ps1                    # Step 3: Validate quality gates
```

## 🎯 What This Script Does

1. **Performs clean build** of entire solution
2. **Captures ALL errors and warnings** from build output
3. **Groups warnings by type** (code style, build, etc.)
4. **Shows summary** of all code violations
5. **Provides organized output** for easy review

## 📋 Script Usage

```bash
# Basic usage (console output)
pwsh scripts/build_and_group_errors_and_warnings.ps1

# Save results to file for review
pwsh scripts/build_and_group_errors_and_warnings.ps1 -SaveToFile "build-warnings.txt"

# JSON output for tooling integration
pwsh scripts/build_and_group_errors_and_warnings.ps1 -OutputFormat "Json"
```

## 🔍 What It Catches

- **Code Style Warnings**: CA1305, IDE0005, etc.
- **Build Warnings**: Missing references, deprecated APIs
- **Security Warnings**: Potential vulnerabilities
- **Performance Warnings**: Inefficient patterns
- **Documentation Warnings**: Missing XML docs

## ⚠️ Why Order Matters

1. **format-code.ps1 first**: Fixes auto-correctable formatting issues
2. **build_and_group_errors_and_warnings.ps1 second**: Shows remaining warnings that need manual attention
3. **quality-check.ps1 last**: Validates overall quality standards

**CRITICAL**: Running format-code.ps1 AFTER this script could hide warnings that were just revealed!

## 📊 Reading the Output

The script groups warnings like this:
```
=== Code Style Warnings ===
CA1305 (3 occurrences): Use IFormatProvider
  - server/Program.cs(254): int.ToString()
  - server/Utils.cs(42): decimal.ToString()

IDE0005 (1 occurrence): Remove unnecessary usings
  - client/src/utils.ts(1): unused import
```

## 🛠️ Acting on Results

### Code Style Warnings (CA*, IDE*)
- **High Priority**: Fix before committing
- **Review**: Check if auto-fixable in IDE
- **Document**: If suppression needed, add justification

### Build Warnings
- **Critical**: All build warnings must be addressed
- **Dependencies**: Check for missing packages
- **Deprecation**: Update to current APIs

### Security Warnings
- **Immediate**: Address all security warnings
- **Review**: Validate with security best practices
- **Test**: Ensure fixes don't break functionality

## 🚫 Common Mistakes to Avoid

1. **NEVER skip this script** - It catches issues format-code.ps1 cannot fix
2. **NEVER run format-code.ps1 after this** - It could hide newly revealed warnings
3. **NEVER ignore warnings** - They indicate code quality issues
4. **NEVER commit with unresolved warnings** - They accumulate as technical debt

## 🔄 Integration with IDE

Many warnings can be auto-fixed in your IDE:
- **VS Code**: Use "Fix All" in Problems panel
- **Visual Studio**: Use "Fix All in Document/Solution"
- **Rider**: Use bulk fix suggestions

Run the script again after IDE fixes to verify all warnings are resolved.

## 📋 Pre-Commit Checklist

Before committing, ensure:
- [ ] format-code.ps1 executed successfully
- [ ] build_and_group_errors_and_warnings.ps1 shows 0 warnings/errors
- [ ] All code style violations addressed
- [ ] All build warnings resolved
- [ ] Security warnings fixed
- [ ] Documentation requirements met

## 🆘 Troubleshooting

### Script Fails to Run
```bash
# Check PowerShell execution policy
Get-ExecutionPolicy
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Too Many Warnings
```bash
# Focus on high-priority warnings first
pwsh scripts/build_and_group_errors_and_warnings.ps1 | Where-Object { $_ -match "Error|CA1" }
```

### Build Fails During Script
```bash
# Fix compilation errors first
pwsh scripts/validate-file-change.ps1
dotnet build --verbosity normal
```

## 📚 Related Resources

- **Validation workflow**: [gates.md](gates.md)
- **Implementation process**: [../20-implement/workflow.md](../20-implement/workflow.md)
- **Code standards**: [../50-standards/index.md](../50-standards/index.md)

---

**REMEMBER**: This script is not optional. It's a critical part of maintaining code quality and must be run after every formatting session and before every commit.