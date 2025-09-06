# Runbook: Grain Activation Failure

## Alert Information
- **Alert Name**: Orleans Silo Down / Grain Activation Failure
- **Severity**: Critical
- **Expected Resolution Time**: 5-15 minutes

## Symptoms
- Users unable to connect or interact with the system
- "Grain activation failed" errors in application logs
- Orleans dashboard shows activation timeouts or failures
- Health check endpoint `/api/monitoring/health` returns unhealthy status
- High response times or timeouts on API endpoints

## Immediate Response (0-2 minutes)

### Step 1: Verify the Alert
```bash
# Check system health
curl http://localhost:5000/api/monitoring/health

# Check Orleans-specific health
curl http://localhost:5000/api/health
```

### Step 2: Check Application Status
```bash
# Check if the application is running
netstat -tlnp | grep :5000

# Check process status
ps aux | grep AIChat.Server
```

## Diagnosis (2-5 minutes)

### Step 3: Examine Application Logs
```bash
# Check recent error logs
tail -n 100 logs/server/app-*.jsonl | grep -i "error\|exception\|grain"

# Look for specific grain activation errors
grep "grain activation" logs/server/app-*.jsonl | tail -20
```

### Step 4: Check System Resources
```bash
# Check memory usage
free -h
cat /proc/meminfo | head -10

# Check CPU usage
top -b -n1 | head -20

# Check disk space
df -h
```

### Step 5: Verify Orleans Cluster Health
```bash
# Check Orleans cluster status through monitoring API
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics."orleans.silo.healthy"'

# Check for Orleans-specific errors in logs
grep -i "orleans\|silo\|cluster" logs/server/app-*.jsonl | tail -20
```

## Resolution Steps

### Option A: Service Restart (Most Common)
```bash
# Stop the application gracefully
kill -TERM $(pgrep -f AIChat.Server)

# Wait for graceful shutdown (max 30 seconds)
sleep 10

# Force kill if still running
pkill -9 -f AIChat.Server

# Restart the application
cd /path/to/application
dotnet run --project server/AIChat.Server.csproj
```

### Option B: Memory Pressure Resolution
If memory pressure is detected:

```bash
# Clear system cache (Linux)
echo 3 > /proc/sys/vm/drop_caches

# Increase application memory limits in appsettings.json
# Restart with increased heap size
export DOTNET_GCHeapHardLimit=2048MB
dotnet run --project server/AIChat.Server.csproj
```

### Option C: Configuration Issues
If configuration is suspected:

```bash
# Verify Orleans configuration
cat server/appsettings.json | jq '.Orleans'

# Check for required environment variables
echo "LLM_API_KEY: ${LLM_API_KEY:-(not set)}"
echo "ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-(not set)}"

# Reset to known good configuration
git checkout HEAD -- server/appsettings.json
```

### Option D: Emergency Fallback Mode
If Orleans continues to fail:

```bash
# Enable SSE fallback mode by disabling Orleans
# Edit appsettings.json:
# "FeatureManagement": { "OrleansIntegration": { "EnabledFor": [{ "Name": "Percentage", "Parameters": { "Value": 0 }}]}}

# Restart application in fallback mode
dotnet run --project server/AIChat.Server.csproj
```

## Verification (1-2 minutes)

### Step 6: Confirm Resolution
```bash
# Test health endpoint
curl http://localhost:5000/api/monitoring/health
# Expected: {"status":"Healthy"}

# Test grain activation
curl -X POST http://localhost:5000/api/chat/send \
  -H "Content-Type: application/json" \
  -d '{"message":"test","chatId":"test"}'
# Expected: Success response

# Monitor for 2-3 minutes to ensure stability
watch -n 30 'curl -s http://localhost:5000/api/monitoring/health | jq .status'
```

### Step 7: Check Metrics Recovery
```bash
# Verify Orleans metrics are updating
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics."orleans.silo.healthy"'
# Expected: {"value": 1, "timestamp": "recent"}

# Check for new errors
tail -f logs/server/app-*.jsonl | grep -i error
# Expected: No new errors
```

## Post-Incident Actions

### Documentation
1. Record the incident in the operations log
2. Note the root cause and resolution method used
3. Update this runbook if new information was discovered

### Prevention
1. **If memory pressure was the cause**: 
   - Consider increasing server memory
   - Review grain state storage patterns
   - Implement more aggressive state cleanup

2. **If configuration issues were found**:
   - Add configuration validation at startup
   - Document required environment variables
   - Implement configuration monitoring

3. **If Orleans cluster issues occurred**:
   - Review clustering configuration
   - Consider implementing redundant silos
   - Add Orleans-specific monitoring alerts

### Monitoring
- Monitor system for 30 minutes after resolution
- Set up additional alerts if patterns are identified
- Review capacity planning metrics for scaling needs

## Escalation Criteria

Escalate to senior engineering if:
- Resolution steps don't work after 15 minutes
- Issue recurs multiple times in 24 hours
- System performance doesn't return to baseline after 30 minutes
- Memory or CPU usage remains abnormally high

## Contact Information

- **Primary Oncall**: [Engineering Team]
- **Secondary Escalation**: [Senior Engineering Team]
- **Business Escalation**: [Product Team]

## Related Runbooks

- [High Memory Usage](high-memory-usage.md)
- [Background Queue Overload](background-queue-overload.md)
- [Performance Degradation](performance-degradation.md)
- [Emergency Rollback Procedure](emergency-rollback.md)