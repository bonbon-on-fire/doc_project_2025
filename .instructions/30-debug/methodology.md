# Debugging Methodology

6-step systematic approach for debugging any issue.

## 🎯 Before You Start

1. **Create debug session**: `scratchpad/debug-{issue}-{date}/`
2. **Document the problem**: What's expected vs actual behavior
3. **Gather context**: When did it start? What changed?

## 🔍 6-Step Debugging Process

### Step 1: Observe 👁️
**Goal**: Understand what's actually happening

```bash
# Check application logs
tail -f logs/server/app-test.jsonl
tail -f logs/client/app.jsonl

# Query logs with DuckDB
duckdb -c "SELECT * FROM read_json_auto('logs/server/app-test.jsonl') WHERE level = 'Error' ORDER BY timestamp DESC LIMIT 10"
```

**Document findings:**
- Error messages and stack traces
- Timing of issues
- Affected components
- User actions that trigger the problem

### Step 2: Assert 🤔
**Goal**: Form hypotheses about the root cause

Based on observations, create hypotheses:
- "The SignalR connection is dropping"
- "Orleans grain state is not persisting"
- "Frontend is not handling null responses"

**Prioritize hypotheses** by likelihood and impact.

### Step 3: Validate Hypotheses ✅
**Goal**: Test each hypothesis systematically

```bash
# Test SignalR connectivity
# Check browser dev tools Network tab
# Look for failed WebSocket connections

# Test Orleans grain behavior
# Add debug logging to grains
# Check Orleans dashboard at localhost:8081

# Test API responses
curl -X POST http://localhost:5000/api/chat/message -H "Content-Type: application/json" -d '{"message":"test"}'
```

### Step 4: Plan Fix 📋
**Goal**: Design solution based on validated hypothesis

Create `fix-plan.md` in scratchpad:
- Root cause identified
- Solution approach
- Files to modify
- Testing strategy
- Rollback plan

### Step 5: Apply Fix 🔧
**Goal**: Implement the solution

Follow [../20-implement/workflow.md](../20-implement/workflow.md):
- Make minimal changes
- Run validation gates after each change
- Test thoroughly

### Step 6: Validate Fix ✅
**Goal**: Confirm the issue is resolved

```bash
# Run full validation
pwsh scripts/validate-pre-commit.ps1

# Test the original failure scenario
# Monitor logs for the issue
# Verify no regression in other areas
```

## 🛠️ Debugging Tools

### Frontend Debugging
```bash
# Client logs
tail -f logs/client/app.jsonl

# Browser dev tools
# - Console for errors
# - Network for API calls
# - Application for SignalR state
```

### Backend Debugging
```bash
# Server logs
tail -f logs/server/app-test.jsonl

# Orleans dashboard
# http://localhost:8081 (when Orleans enabled)

# Database queries
duckdb -c "SELECT * FROM read_json_auto('logs/server/app-test.jsonl') WHERE component = 'ChatController'"
```

### System-Level Debugging
```bash
# Process monitoring
tasklist | findstr "dotnet"

# Port usage
netstat -an | findstr ":5000\|:30000\|:11111"

# File watching
# Use VS Code or file explorer to monitor changes
```

## 🔍 Common Debug Patterns

### SignalR Issues
```bash
# Check connection state
rg "SignalR" logs/client/app.jsonl

# Verify hub methods
rg "HubConnection" client/src/ -A 5 -B 5
```

### Orleans Issues
```bash
# Check grain activation
rg "Grain.*activat" logs/server/app-test.jsonl

# Verify grain state
rg "UserGrain" logs/server/app-test.jsonl
```

### Database Issues
```bash
# Check EF Core queries
rg "Entity Framework" logs/server/app-test.jsonl

# Database file permissions
ls -la server/chat.db
```

## 🚨 Emergency Procedures

### Build Completely Broken
1. **Stop all processes**: Ctrl+C in terminals
2. **Clean rebuild**:
   ```bash
   dotnet clean
   dotnet restore
   dotnet build
   ```
3. **Check validation**: `pwsh scripts/validate-file-change.ps1`

### Tests All Failing
1. **Check environment**: Are Orleans ports occupied?
2. **Reset test environment**:
   ```bash
   $env:ASPNETCORE_ENVIRONMENT="Test"
   dotnet test --verbosity normal
   ```

### Development Environment Corrupted
1. **Reset to known good state**:
   ```bash
   git stash push -m "Debug session backup"
   git reset --hard HEAD
   pwsh scripts/validate-file-change.ps1
   ```

## 📋 Debug Session Checklist

### Preparation
- [ ] Debug session directory created
- [ ] Problem clearly documented
- [ ] Relevant logs collected
- [ ] Environment state captured

### Investigation
- [ ] Step 1: Observations documented
- [ ] Step 2: Hypotheses formed and prioritized
- [ ] Step 3: Hypotheses tested systematically
- [ ] Root cause identified

### Resolution
- [ ] Step 4: Fix plan created
- [ ] Step 5: Fix implemented following workflow
- [ ] Step 6: Fix validated thoroughly
- [ ] No regressions introduced

### Documentation
- [ ] Findings documented in scratchpad
- [ ] Solution approach recorded
- [ ] Future prevention strategies noted

## 🔄 After Debugging

1. **Document the solution**: Update scratchpad with findings
2. **Consider prevention**: Could this be caught earlier?
3. **Update monitoring**: Add logging if needed
4. **Share learnings**: Update relevant documentation

## 📚 Specific Debug Guides

- **Build failures**: [build-failures.md](build-failures.md)
- **Test failures**: [test-failures.md](test-failures.md)
- **Performance issues**: [performance-debugging.md](performance-debugging.md)
- **Orleans-specific**: [orleans-debugging.md](orleans-debugging.md)