---
applyTo: '**'
---

Whenever you encounter compiler errors in the code, search the web for solutions. Use the following steps:

1. Identify the compiler/language version
2. Check the error message for specific keywords
3. Use a search engine to look up the error message along with the compiler/language version
4. Review the search results for relevant solutions or discussions

## Running tests

### Goals

- Add a fast, isolated Test environment:
  - HTTP-only on port 5099
  - In-memory EF database reset on each run
  - Disable LLM cache

### Decisions

- Use `builder.Environment.IsEnvironment("Test")` for wiring Test behavior at startup.
- Keep `Urls` in `appsettings.Test.json`; host reads it without extra code.
- Keep CORS policy identical across Dev/Test to allow `http://localhost:5173` and `http://localhost:4173`.

### Gotchas
- Ensure we do not call `UseHttpsRedirection()` in Test, otherwise HTTP-only clients would be redirected to HTTPS (and fail).
- Use `EnsureDeleted()` then `EnsureCreated()` to guarantee a clean in-memory DB even across host restarts.
- We did not change OpenAI agent wiring in this task; API key behavior will be addressed in Task 3.

### Next
- Build and run with `--environment Test` to verify binding and empty DB.
- Add tests in later tasks to assert behavior.

### Backend startup challenges and reliable run steps (PowerShell)
- LaunchSettings overrides: `dotnet run` loads `server/Properties/launchSettings.json` and binds to `http://localhost:5130` in Development, ignoring `ASPNETCORE_URLS`. This caused port conflicts and non-Test env when we expected Test and 5099.
- Solution: run the built DLL directly to bypass launchSettings and honor env vars.
- File lock on rebuild: a previously running `AIChat.Server.exe` locked `bin/Debug/net9.0/AIChat.Server.exe`. Stop it before building.
- Commands:
  - Stop any running server instances:
    ```powershell
    Get-CimInstance Win32_Process | Where-Object {$_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*AIChat.Server*"} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    ```
  - Build server:
    ```powershell
    cd server
    dotnet build -c Debug
    ```
  - Run in Test mode on 5099 (bypasses launchSettings):
    ```powershell
    $env:ASPNETCORE_ENVIRONMENT='Test'
    $env:ASPNETCORE_URLS='http://localhost:5099'
    $env:LLM_API_KEY='DUMMY'
    dotnet bin\Debug\net9.0\AIChat.Server.dll
    ```
  - Health check:
    ```powershell
    Invoke-WebRequest -UseBasicParsing http://localhost:5099/api/health | Select-Object -Expand Content
    ```
- Notes:
  - Alternatively, use `Start-Process` to detach and capture logs.
  - If you must use `dotnet run`, pass `--no-launch-profile` (SDK 9+ supports `DOTNETLAUNCHPROFILE`/env) or ensure an explicit profile for Test, otherwise launchSettings will still bind to 5130 and Development.
