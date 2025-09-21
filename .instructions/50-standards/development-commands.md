# Development Commands Reference

## Client (SvelteKit Frontend)

### Basic Commands
```bash
cd client
npm install              # Install dependencies
npm run dev             # Start development server (http://localhost:5173)
npm run dev:test        # Start test server (port 5173, test mode)
npm run build           # Build for production
```

### Code Quality
```bash
npm run check           # Type check with svelte-check
npm run lint            # Run ESLint and Prettier checks
npm run format          # Format code with Prettier
```

### Testing
```bash
npm run test:unit       # Run unit tests (Vitest)
npm run test:e2e        # Run end-to-end tests (Playwright)
npm run test            # Run all tests (unit + e2e)
```

### Database (Client-side SQLite with Drizzle)
```bash
npm run db:push         # Push schema changes to database
npm run db:generate     # Generate migrations
npm run db:migrate      # Run migrations
npm run db:studio       # Open Drizzle Studio
```

## Server (ASP.NET 9.0 Backend)

### Basic Commands
```bash
cd server
dotnet restore          # Install dependencies
dotnet run              # Start development server
dotnet watch run        # Start with hot reload
dotnet build           # Build the project
dotnet test ../server.Tests  # Run unit tests
```

### Test Environment Setup
```bash
# Bypasses launchSettings.json for isolated testing
dotnet build -c Debug
$env:ASPNETCORE_ENVIRONMENT='Test'
$env:ASPNETCORE_URLS='http://localhost:5099'
$env:LLM_API_KEY='DUMMY'
dotnet bin\Debug\net9.0\AIChat.Server.dll
```

## Package Management (LmDotnetTools)

### Quick Reference
```bash
# Publish packages to local feed
cd submodules/LmDotnetTools
powershell -ExecutionPolicy Bypass -File publish-nuget-packages.ps1 -LocalOnly

# Update and consume in server
cd server
# Update versions in AIChat.Server.csproj to match
dotnet restore
dotnet build
```

For detailed package management instructions, see [build-and-test.md](./build-and-test.md)

## Hot Reload Development

### Concurrent Development
```bash
# Terminal 1: Client with hot reload
cd client && npm run dev

# Terminal 2: Server with hot reload  
cd server && dotnet watch run
```

## Background Process Monitoring

### Windows PowerShell
```powershell
# Find running dev processes
Get-CimInstance Win32_Process | Where-Object {$_.Name -eq "node.exe"} | Where-Object {$_.CommandLine -like "* dev"}
Get-CimInstance Win32_Process | Where-Object {$_.Name -eq "dotnet.exe"} | Where-Object {$_.CommandLine -like "*watch*"}
```

## Available Development Tools

- **DuckDB**: v1.3.2 (Ossivalis) - Available for data analysis and SQL operations
- **Query logs**: `SELECT * FROM read_json_auto('logs/server/app-test.jsonl')`