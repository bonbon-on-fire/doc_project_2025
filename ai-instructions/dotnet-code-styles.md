# .NET Code Style Guidelines for DOC_Project_2025

This document outlines the coding standards and automated formatting processes for maintaining consistent code style across the DOC_Project_2025 codebase.

## 🎯 Overview

The project uses a multi-tool approach to ensure consistent, high-quality code formatting:

- **Root Project**: Uses `dotnet format`, Roslynator CLI, and ReSharper Command Line Tools
- **Submodules**: Uses CSharpier for fast, opinionated formatting
- **Configuration**: Centralized through `.editorconfig` with comprehensive C# style rules

## 🚀 Quick Start

### Run Automated Formatting

```powershell
# Format everything (recommended for regular use)
.\format-code.ps1

# Check formatting without making changes
.\format-code.ps1 -CheckOnly

# Format only root project (server + tests)
.\format-code.ps1 -RootOnly  

# Format only submodules
.\format-code.ps1 -SubmodulesOnly

# Show help and tool information
.\format-code.ps1 -Help
```

### Before Your First Use

Install required tools:

```powershell
# Required tools
dotnet tool install -g JetBrains.ReSharper.GlobalTools
dotnet tool install -g csharpier

# Optional (for advanced code analysis)
dotnet tool install -g Roslynator.DotNet.Cli
```

## 📋 Mandatory Practices

### 1. Run Format Script Before Commits

**ALWAYS** run the formatting script before committing code:

```powershell
# Before committing
.\format-code.ps1

# Check what was changed
git diff

# Commit your changes
git add .
git commit -m "Your commit message"
```

### 2. Keep .editorconfig Updated

The `.editorconfig` file is the **single source of truth** for code styling rules. When updating coding standards:

1. **Update `.editorconfig` first** with new rules
2. **Test the changes** with `.\format-code.ps1 -CheckOnly`
3. **Document significant changes** in this file
4. **Communicate changes** to the team

#### Key Areas in .editorconfig

- **File encoding and line endings**
- **Indentation rules** (spaces, tab sizes)
- **C# code style preferences** (var usage, expression bodies, etc.)
- **Formatting rules** (braces, spacing, wrapping)
- **Naming conventions** (PascalCase, interface prefixing)
- **Diagnostic severities** (warnings, suggestions, errors)

### 3. Integration with Development Workflow

#### During Development

- Use your IDE's built-in formatting (follows `.editorconfig`)
- Run formatting checks periodically: `.\format-code.ps1 -CheckOnly`

#### Before Pull Requests

- **Mandatory**: Run `.\format-code.ps1` 
- Verify no unintended changes: `git diff`
- Commit formatting changes separately if needed

#### Code Reviews

- Reviewers should focus on logic, not formatting
- Formatting issues should be caught by automated tools

## 🛠️ Tool Details

### Root Project (Multi-Tool Approach)

The `format-code.ps1` script applies formatting in multiple stages for comprehensive code quality:

#### Stage 1: dotnet format style

- Fixes specific IDE diagnostics (IDE0032, IDE0017, IDE0028, IDE0025)
- Applies auto-property conversions, var usage improvements
- Fast execution, built into .NET SDK

#### Stage 2: Roslynator CLI (Optional)

- Advanced code analysis and fixes
- Requires project compilation
- Covers broader range of code quality issues
- Install: `dotnet tool install -g Roslynator.DotNet.Cli`

#### Stage 3: ReSharper Command Line Tools

- Comprehensive formatting and style application  
- Applies all `.editorconfig` rules consistently
- Industry-standard formatting quality
- **Required**: `dotnet tool install -g JetBrains.ReSharper.GlobalTools`

### Submodules (CSharpier)

- **Fast, opinionated formatting** for `submodules/LmDotnetTools`
- **Minimal configuration** required
- **Consistent results** across different environments
- **Required**: `dotnet tool install -g csharpier`

## 📐 Code Style Standards

### Key Principles from .editorconfig

#### File Structure

- **UTF-8 encoding** with final newlines
- **CRLF line endings** for Windows compatibility
- **Trim trailing whitespace** (except in Markdown)

#### C# Formatting

- **4-space indentation** for C# code
- **2-space indentation** for XML/JSON/YAML
- **Allman braces style** (braces on new lines)
- **var keyword** preferred when type is obvious

#### Naming Conventions

- **PascalCase**: Classes, methods, properties, public fields
- **Interface prefix**: All interfaces must start with 'I'
- **camelCase**: Private fields, parameters, local variables

#### Expression Preferences

- **Auto-properties** over backing fields
- **Object initializers** when appropriate
- **Null propagation** operators (`?.`, `??`)
- **Pattern matching** over traditional casting

#### Code Analysis

- **IDE0055 (Fix formatting)**: Warning severity
- **Unused parameter warnings**: Disabled (IDE0060)
- **CA rules**: Configured for practical development

## 🔍 Troubleshooting

### Common Issues

#### "Tool not found" errors

```powershell
# Verify tool installation
jb --version
csharpier --version
roslynator --version  # Optional

# Reinstall if needed
dotnet tool install -g JetBrains.ReSharper.GlobalTools --force
```

#### Formatting inconsistencies

1. Check if `.editorconfig` was modified
2. Ensure all developers use the same tool versions
3. Run `.\format-code.ps1 -CheckOnly` to identify issues

#### Build errors with Roslynator

- Roslynator requires projects to build successfully
- Fix compilation errors first, then run formatting

### Performance Tips

- **Use `-CheckOnly`** for validation without changes
- **Use `-RootOnly`** when submodules haven't changed
- **Run formatting in CI/CD** for automated enforcement

## 🏗️ Maintenance

### Regular Tasks

#### Monthly

- Review `.editorconfig` for new C# language features
- Update formatting tools: `dotnet tool update -g <tool-name>`
- Test formatting changes on sample code

#### Per Release

- Document any significant style changes
- Ensure all team members have updated tools
- Verify CI/CD formatting checks are working

### Configuration Updates

When modifying `.editorconfig`:

1. **Test locally** first with `.\format-code.ps1`
2. **Check impact** on existing codebase
3. **Update this documentation** if standards change
4. **Communicate to team** before merging

## 📚 Additional Resources

- [EditorConfig Documentation](https://editorconfig.org/)
- [.NET Code Style Rules](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/)
- [ReSharper Code Style](https://www.jetbrains.com/help/resharper/Code_Style_Assistance.html)
- [CSharpier Documentation](https://csharpier.com/)
- [Roslynator CLI](https://josefpihrt.github.io/docs/roslynator/cli/)

---

## ⚠️ Important Reminders

1. **NEVER commit** code without running `.\format-code.ps1`
2. **ALWAYS keep** `.editorconfig` up to date with team standards
3. **MAINTAIN** tool versions across the team
4. **SEPARATE** formatting commits from functional changes when possible
5. **REVIEW** formatting changes before pushing to ensure no unintended modifications

---

*This document should be reviewed and updated whenever coding standards or tooling changes.*
