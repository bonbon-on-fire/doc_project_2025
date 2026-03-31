# Debugging Guide

## Core Debugging Methodology

Debugging follows these systematic steps:

1. **Observe** - Gather information about the issue
2. **Assert** - Form a hypothesis about the root cause
3. **Validate Assertion** - Proof of Assertion through testing
4. **Plan Fix** - Design the solution approach
5. **Apply Fix** - Implement the solution
6. **Validate Fix** - Confirm the issue is resolved

To achieve this methodology (in absence of debugger access), you MUST use Logs and 'duckdb' to debug the system.

## Logging Infrastructure

### Log Configuration
- **JSONL Format**: Structured JSON logs for easy parsing
- **Trace Level**: Verbose logging in development/testing
  - Serilog: Verbose
  - Pino: trace
- **DuckDB Integration**: Query logs with SQL for debugging

### Log Locations
- **Server Logs**:
  - Development: `logs/server/app-dev.jsonl`
  - Test: `logs/server/app-test.jsonl`
  - Build: `logs/server/build.logs`
- **Client Logs**: `logs/client/app.jsonl`

### Querying Logs with DuckDB
```sql
-- Query server test logs
SELECT * FROM read_json_auto('logs/server/app-test.jsonl')
WHERE level = 'Error'
ORDER BY timestamp DESC;

-- Find specific request traces
SELECT * FROM read_json_auto('logs/server/app-dev.jsonl')
WHERE message LIKE '%ChatController%'
LIMIT 100;
```

### Logging Features
- **Server**: Serilog with CompactJsonFormatter
  - Request tracing
  - Performance metrics
  - Structured exception data
- **Client**: Pino with HTTP transport to `/api/logs`
  - Browser console integration
  - Trace level in development

## Step-by-Step Test Debugging Process

When fixing failed tests, follow this systematic process using sequential thinking.

### Essential Practices
You MUST use temporary notebooks created in `scratchpad/` directory to track:
1. Checklist of debugging steps
2. Learnings from investigation
3. All approaches tried to fix
4. Other related tasks

### Step 0: Act & Observe
Take steps to reproduce the bug and observe the behavior and logs of the system.
- Run the failing test in isolation
- Capture all log output
- Note exact error messages and stack traces

### Step 1: Assert
Look at the failure, analyze the code and come up with Root Cause Assertion.
- Document supporting evidence
- Create hypothesis about failure cause
- Note any patterns or correlations

### Step 2: Proof of Assertion
Validate the assertion by adding diagnostic logs to the code and re-running tests.
- Changes should be for diagnostic purposes only
- Add targeted logging at suspected failure points
- Confirm or refute the hypothesis

### Step 3: Design & Plan Fix
If Root Cause is validated, design changes to address it.
- Use supporting evidence to validate design
- Consider edge cases and side effects
- Use sequential thinking for complex problems

### Step 4: Plan Fix Implementation
Break down the fix into discrete steps/tasks.
- Define "definition of done" for each task
- Order tasks by dependency
- Estimate complexity and risk

### Step 5: Apply Fix
Work through the tasks one by one to implement the fix.
- Follow the planned sequence
- Test incrementally
- Document any deviations from plan

### Step 6: Validate Fix
Confirm the fix resolves the issue.
- Run original failing test
- Run related test suite
- Check for regressions
- If still broken, return to Step 1 or Step 3

## Common Debugging Scenarios

### SSE Stream Issues
1. Check `logs/server/app-test.jsonl` for SSE handler logs
2. Verify message sequencing in `MessageSequenceService`
3. Confirm stream completion character (`▋`) handling

### SignalR Connection Problems
1. Check WebSocket upgrade logs
2. Verify CORS configuration
3. Confirm authentication/authorization

### Database Consistency
1. Check EF Core migration logs
2. Verify SQLite file permissions
3. Confirm transaction boundaries

### Test Environment Issues
1. Ensure `ASPNETCORE_ENVIRONMENT=Test`
2. Verify port 5099 availability
3. Check in-memory database initialization

## Tools and Utilities

### Process Monitoring (Windows)
```powershell
# Find hanging test processes
Get-Process | Where-Object {$_.ProcessName -like "*test*"}

# Kill orphaned test runners
Stop-Process -Name "dotnet" -Force
```

### Log Analysis Commands
```bash
# Count errors in logs
duckdb -c "SELECT COUNT(*) FROM read_json_auto('logs/server/app-test.jsonl') WHERE level = 'Error'"

# Find slow requests
duckdb -c "SELECT * FROM read_json_auto('logs/server/app-dev.jsonl') WHERE duration_ms > 1000"
```

## Best Practices

1. **Always use scratchpad** for debugging notes
2. **Create checklists** for complex debugging sessions
3. **Use sequential thinking** for multi-step problems
4. **Document learnings** for future reference
5. **Test fixes incrementally** to avoid introducing new issues
6. **Keep diagnostic code separate** from production fixes