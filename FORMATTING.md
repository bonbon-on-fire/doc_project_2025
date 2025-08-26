# Code Formatting Setup for DOC_Project_2025

This repository uses **ReSharper Command Line Tools** for consistent C# code formatting across the codebase. ReSharper provides configurable, high-quality formatting with proper closing parenthesis placement.

## 🎯 Quick Start

### Installation
```bash
# Install ReSharper Command Line Tools as global dotnet tool (one-time setup)
dotnet tool install -g JetBrains.ReSharper.GlobalTools

# Install Roslynator CLI for advanced code analysis (optional)
dotnet tool install -g Roslynator.DotNet.Cli
```

### Format All Code
```bash
# Use the integrated script (recommended)
.\format-code.ps1

# Or run tools individually:

# 1. Apply code style fixes
dotnet format style --diagnostics "IDE0032 IDE0017 IDE0028 IDE0025"

# 2. Apply Roslynator fixes (optional)
roslynator fix server/AIChat.Server.csproj --severity-level info

# 3. Format with ReSharper CLT
jb cleanupcode server/AIChat.Server.csproj
jb cleanupcode server.Tests/server.Tests.csproj

# Check available options
jb cleanupcode --help
```

## ⚙️ Configuration

### ReSharper Settings (`.editorconfig`)
ReSharper uses `.editorconfig` for formatting configuration:
```ini
# C# files: 4 spaces
[*.{cs,csx}]
indent_size = 4

# XML/Project files: 2 spaces  
[*.{csproj,props,targets,xml,config,xaml}]
indent_size = 2

# JSON files: 2 spaces
[*.{json,json5}]
indent_size = 2

# Web files (TypeScript/JavaScript): 2 spaces
[*.{js,ts,tsx,css,sass,scss,less,svg}]
indent_size = 2
```

### Key Features
- **Closing parentheses**: Stay on same line as last parameter (preferred behavior)
- **Indentation**: Per-file-type (C#: 4 spaces, XML/JSON/Web: 2 spaces)
- **Line Endings**: CRLF (Windows standard)  
- **Highly configurable**: Custom profiles and settings
- **Uses .editorconfig**: Respects existing configuration standards

### What ReSharper Formats
- **C# files**: Complete formatting with configurable rules
- **Project files**: XML indentation and structure  
- **Configuration files**: Proper indentation and structure
- **Import ordering**: Configurable using statement organization
- **Code style**: Comprehensive C# style enforcement

### Excluded Areas
ReSharper automatically respects standard exclusions:
- **Submodules**: Have their own formatting (using CSharpier)
- **Generated code**: bin/, obj/, node_modules/
- **Build artifacts**: test-results/, logs/, cache files

## 🔧 IDE Integration

### JetBrains Rider
ReSharper formatting is built into Rider:
1. **Settings** → **Editor** → **Code Style** → **C#**
2. Configure formatting rules and enable format on save
3. Use **Ctrl+Alt+F** to format current file

### Visual Studio
1. Install **ReSharper** extension (commercial)
2. Use **Ctrl+E, Ctrl+C** for cleanup
3. Configure profiles in **ReSharper** → **Options** → **Code Editing**

### Command Line (Recommended)
Use the global ReSharper CLT for consistent formatting:
```bash
# Format specific projects
jb cleanupcode server/AIChat.Server.csproj
jb cleanupcode server.Tests/server.Tests.csproj
```

## 🚀 CI/CD Integration

### GitHub Actions
```yaml
name: Code Formatting Check

on: [push, pull_request]

jobs:
  format-check:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v4
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
    - name: Install ReSharper CLT
      run: dotnet tool install -g JetBrains.ReSharper.GlobalTools
    - name: Format server code
      run: jb cleanupcode server/AIChat.Server.csproj
    - name: Format test code  
      run: jb cleanupcode server.Tests/server.Tests.csproj
```

### Azure DevOps
```yaml
- task: DotNetCoreCLI@2
  displayName: 'Install ReSharper CLT'
  inputs:
    command: 'custom'
    custom: 'tool'
    arguments: 'install -g JetBrains.ReSharper.GlobalTools'

- script: jb cleanupcode server/AIChat.Server.csproj
  displayName: 'Format server code'

- script: jb cleanupcode server.Tests/server.Tests.csproj  
  displayName: 'Format test code'
```

## 🛠️ Troubleshooting

### Common Issues

**CSharpier not installed**
```bash
# Install globally
dotnet tool install csharpier --global

# Or install locally per project
dotnet new tool-manifest  # if no manifest exists
dotnet tool install csharpier --local
```

**Files not formatting**
- Ensure you're in the right directory
- Check `.csharpierrc.json` configuration is valid JSON
- Use `csharpier check .` to see what needs formatting

**IDE integration not working**
- Restart your IDE after installing extensions
- Check extension settings and enable format-on-save
- Verify CSharpier is installed globally

## 📁 File Coverage

CSharpier formats:
- **C# files** (*.cs): Complete formatting including line wrapping
- **Project files** (*.csproj, *.props): XML indentation and structure  
- **Configuration files**: Proper indentation and structure

## 🎯 Why CSharpier?

### Advantages over `dotnet format`
- **No crashes**: CSharpier is stable and reliable
- **Hard line wrapping**: Enforces 100-character limit
- **Faster**: Typically 3x faster than `dotnet format`
- **Deterministic**: Same result every time
- **Less configuration**: Opinionated choices reduce bikeshedding

### Performance
- **High-quality formatting** with configurable rules
- **Respects .editorconfig** standards
- **Project-based** formatting (server + tests)

## 🔬 Roslynator: Advanced Code Analysis

### What is Roslynator?
Roslynator is a comprehensive set of 500+ analyzers, refactorings, and fixes for C#, powered by Roslyn. It goes beyond basic formatting to improve code quality, performance, and maintainability.

### Installation
```bash
# Install Roslynator CLI as global dotnet tool
dotnet tool install -g Roslynator.DotNet.Cli
```

### Usage
```bash
# Build project first (required for Roslynator)
dotnet build server/AIChat.Server.csproj

# Analyze and fix issues in project
roslynator fix server/AIChat.Server.csproj --severity-level info --fix-scope project

# Analyze without fixing (check only)
roslynator analyze server/AIChat.Server.csproj --severity-level info --verbosity normal

# List available diagnostics
roslynator --help
```

### Important Notes
- **Build Required**: Projects must be successfully built before running Roslynator fixes
- **External Analyzers**: Roslynator requires external analyzer assemblies (not included in CLI)
- **Documentation**: See [Roslynator CLI Fix Command](https://josefpihrt.github.io/docs/roslynator/cli/commands/fix/) for full options

### Key Features
- **500+ Analyzers**: Detects code issues, performance problems, and style violations
- **Automatic Fixes**: Applies safe code transformations and improvements
- **Configurable**: Works with .editorconfig and MSBuild properties
- **Build Integration**: Can run during build process

### What Roslynator Fixes
- **Code Simplification**: Unnecessary code, redundant expressions
- **Performance**: Inefficient LINQ usage, string operations
- **Readability**: Complex conditional logic, naming improvements  
- **Maintainability**: Long methods, complex expressions
- **Best Practices**: Modern C# features, pattern usage

### Integration Order
The format script runs tools in this order for maximum effectiveness:
1. **dotnet format style** → Basic code style fixes (IDE0032, etc.)
2. **Roslynator** → Advanced analysis and refactoring
3. **ReSharper CLT** → Final formatting and cleanup

## 🔄 Alternative: CSharpier (Used in Submodules)

### Installation
```bash
# Install CSharpier as global dotnet tool
dotnet tool install csharpier --global
```

### Usage
```bash
# Format submodules (they use CSharpier)
cd submodules/LmDotnetTools && csharpier format .

# Check submodule formatting
cd submodules/LmDotnetTools && csharpier check .
```

### Key Differences from ReSharper CLT
- **Closing parentheses**: Put on new line (different behavior)
- **Faster**: ~3x faster than ReSharper
- **Less configurable**: Opinionated formatting choices
- **Deterministic**: Same result every time

### When CSharpier is Used
- **Submodules**: LmDotnetTools uses CSharpier for speed
- **Large codebases**: When formatting speed is critical
- **Teams preferring opinionated tools**: Minimal configuration debates

## 📚 References

- [CSharpier Official Documentation](https://csharpier.com/)
- [CSharpier GitHub Repository](https://github.com/belav/csharpier)
- [CSharpier Configuration Options](https://csharpier.com/docs/Configuration)
- [CSharpier IDE Extensions](https://csharpier.com/docs/Editors)
- [ReSharper Command Line Tools](https://www.jetbrains.com/help/resharper/ReSharper_Command_Line_Tools.html)
- [JetBrains CleanupCode Documentation](https://www.jetbrains.com/help/resharper/CleanupCode.html)