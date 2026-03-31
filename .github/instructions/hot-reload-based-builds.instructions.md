---
description: Instructions for how to observe file changes and build errors when apps are running with hot reload
applyTo: '**'
---

# Watching for build errors

## Client project

Run `npm run dev` to start the client project and also watch for changes and rebuild/run them
When executing in background, you should `cd client; npm run dev:test` to change directory into client and run the client project
Run `Get-CimInstance Win32_Process | Where-Object {$_.Name -eq "node.exe"} | Where-Object {$_.CommandLine -like "* dev:test"}` to find the processes that are running in the background

Run in test mode: `npm run dev:test`. Note this will want to connect to test mode backend that mocks the responses.
Run in dev mode: `npm run dev`. Note this will want to connect to dev mode backend that connects to real services (and it COSTS money).

## Server project

Run `dotnet watch run` to start the server project and also watch for changes and rebuild/run them
When executing in background, you should `cd client; dotnet watch run` to change directory into client and run the client project
Run `Get-CimInstance Win32_Process | Where-Object {$_.Name -eq "dotnet.exe"} | Where-Object {$_.CommandLine -like "*watch*"}` to find the processes that are running in the background

Run in test mode: `$env:ASPNETCORE_ENVIRONMENT='Test'; $env:ASPNETCORE_URLS='http://localhost:5099'; dotnet watch run`. 
Run in dev mode: `$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5130'; dotnet watch run`. 

## Observe for Errors

When making changes keep peeking at the background processes (terminal) for any errors. This way you don't have to constantly keep rebuilding. But to observe these errors, use `Start-Sleep 3` to sleep a bit before checking for errors

## Debugging

Use logs for debugging, which will also appear in above consoles. Use them to make sure you're abe to debug

Use `playwright` to use browser and debug the application

## Notes:

1. ALWAYS check current directory before running commands.
2. ALWAYS verify if the background task has succeeded before making further changes, many times, because of PORT CONFLICTS, it may not start.
3. When running background processes, REMEMBER to also switch into the project directory e.g. `cd client` or `cd server`.
4. You MUST use `;` as a separator in PowerShell to run multiple commands in one line.
5. You SHOULD use `netstat -ano | findstr :<port>` to check if a specific port is in use, and kill the process if needed using `Stop-Process -Id <PID>` where `<PID>` is the process ID you find from the previous command.