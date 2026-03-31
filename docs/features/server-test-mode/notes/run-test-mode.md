# Server Test Mode: Run Instructions

- Set environment and run the server directly to honor `appsettings.Test.json` (binds to `http://localhost:5099`) and avoid launchSettings overrides:

```powershell
cd server
$env:ASPNETCORE_ENVIRONMENT='Test'
$env:ASPNETCORE_URLS='http://localhost:5099'
$env:LLM_API_KEY='DUMMY'
dotnet bin\Debug\net9.0\AIChat.Server.dll
```

- Health check:

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:5099/api/health | Select-Object -Expand Content
```

- Notes
  - Do not call `UseHttpsRedirection()` in Test; HTTP-only is required.
  - EF uses InMemory in Test; DB is reset on each run via EnsureDeleted/EnsureCreated.
  - CORS allows client origins on ports 5173 and 4173.

## Client (Test mode)

- Create `client/.env.test` with:

```
PUBLIC_API_BASE_URL=http://localhost:5099
```

- Run client on 5173 in Test mode:

```powershell
cd client
npm run dev:test
```

- Playwright e2e already injects `PUBLIC_API_BASE_URL=http://localhost:5099` via `playwright.config.ts`.
