# Build and Test Guidelines

## Overview
This document describes how to build, test, and manage NuGet packages for the DOC_Project_2025 solution, which uses LmDotnetTools from a git submodule.

## Architecture
- **Main Project**: DOC_Project_2025 (AIChat.Server)
- **Submodule**: LmDotnetTools (at `submodules/LmDotnetTools/`)
- **Package Management**: Local NuGet feed for development, NuGet.org for production

## Publishing LmDotnetTools Packages

### 1. Update Package Version
Edit `submodules/LmDotnetTools/Directory.Build.props`:
```xml
<PatchVersion>16</PatchVersion>  <!-- Increment this for patches -->
```

### 2. Publish to Local Feed
```powershell
cd submodules/LmDotnetTools
powershell -ExecutionPolicy Bypass -File publish-nuget-packages.ps1 -LocalOnly
```

This publishes packages to: `%USERPROFILE%\.nuget\local-feed`

### 3. Publish to NuGet.org (Production)
```powershell
cd submodules/LmDotnetTools
powershell -ExecutionPolicy Bypass -File publish-nuget-packages.ps1 -ApiKey "your-api-key"
```

## Consuming Updated Packages

### 1. Update Package References
Edit `server/AIChat.Server.csproj`:
```xml
<PackageReference Include="AchieveAi.LmDotnetTools.LmCore" Version="1.0.16" />
<PackageReference Include="AchieveAi.LmDotnetTools.Misc" Version="1.0.16" />
<PackageReference Include="AchieveAi.LmDotnetTools.McpMiddleware" Version="1.0.16" />
<PackageReference Include="AchieveAi.LmDotnetTools.OpenAiProvider" Version="1.0.16" />
<PackageReference Include="AchieveAi.LmDotnetTools.LmConfig" Version="1.0.16" />
```

### 2. Restore from Local Feed
```bash
cd server
# Option 1: Simple restore (uses configured sources)
dotnet restore

# Option 2: Explicit local feed
dotnet restore --source "%USERPROFILE%\.nuget\local-feed" --source https://api.nuget.org/v3/index.json
```

### 3. Build and Test
```bash
cd server
dotnet build -c Debug
dotnet test ../server.Tests
```

## NuGet Feed Configuration

### Local Feed Setup
The local feed is automatically created at:
- Windows: `C:\Users\[username]\.nuget\local-feed`
- Linux/Mac: `~/.nuget/local-feed`

### Add Local Feed to NuGet Sources (Optional)
```bash
dotnet nuget add source "%USERPROFILE%\.nuget\local-feed" -n LocalFeed
```

## Package Publishing Workflow

### Development Cycle
1. Make changes in `submodules/LmDotnetTools/`
2. Increment version in `Directory.Build.props`
3. Publish to local feed with `-LocalOnly`
4. Update version in consuming projects
5. Test locally
6. Commit changes

### Release Cycle
1. Ensure all tests pass
2. Update version (increment Minor or Major as needed)
3. Publish to NuGet.org with API key
4. Update consuming projects to use new version
5. Create git tag for release

## Important Notes

### Version Management
- **Patch**: Bug fixes, minor improvements (1.0.X)
- **Minor**: New features, backward compatible (1.X.0)
- **Major**: Breaking changes (X.0.0)

### Package Dependencies
The following packages are published together and should maintain version sync:
- AchieveAi.LmDotnetTools.LmCore
- AchieveAi.LmDotnetTools.Misc
- AchieveAi.LmDotnetTools.McpMiddleware
- AchieveAi.LmDotnetTools.OpenAiProvider
- AchieveAi.LmDotnetTools.LmConfig
- AchieveAi.LmDotnetTools.AnthropicProvider
- AchieveAi.LmDotnetTools.LmEmbeddings
- AchieveAi.LmDotnetTools.McpSampleServer

### Troubleshooting

#### Package Not Found
If packages aren't found after publishing:
1. Clear NuGet cache: `dotnet nuget locals all --clear`
2. Verify package exists: `dir "%USERPROFILE%\.nuget\local-feed\*.nupkg"`
3. Check package source: `dotnet nuget list source`

#### Version Conflicts
If you get version conflicts:
1. Ensure all LmDotnetTools packages use the same version
2. Clean solution: `dotnet clean`
3. Delete `bin` and `obj` folders
4. Restore and rebuild

#### Build Errors After Update
1. Ensure submodule is updated: `git submodule update --init --recursive`
2. Verify package version matches between publisher and consumer
3. Check for breaking changes in package changelog

## Testing

### Unit Tests
```bash
# Server tests
cd server.Tests
dotnet test

# Client tests
cd client
npm run test:unit
```

### Integration Tests
```bash
# E2E tests (requires both server and client running)
cd client
npm run test:e2e
```

### Test Environment Setup
For isolated testing:
```bash
cd server
dotnet build -c Debug
$env:ASPNETCORE_ENVIRONMENT='Test'
$env:ASPNETCORE_URLS='http://localhost:5099'
$env:LLM_API_KEY='DUMMY'
dotnet bin\Debug\net9.0\AIChat.Server.dll
```

## Continuous Integration

### Pre-commit Checks
Before committing:
1. Run unit tests: `dotnet test`
2. Check build: `dotnet build`
3. Verify package references are correct
4. Ensure version numbers are synchronized

### GitHub Actions (if configured)
The CI pipeline should:
1. Restore packages from NuGet.org (not local feed)
2. Build all projects
3. Run all tests
4. Publish packages on release tags