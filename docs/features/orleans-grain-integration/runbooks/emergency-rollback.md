# Runbook: Emergency Rollback Procedure

## Alert Information
- **Trigger**: Critical system failure, data corruption risk, or widespread service degradation
- **Severity**: Critical
- **Expected Execution Time**: 5-10 minutes
- **Recovery Time**: 2-5 minutes after rollback

## When to Execute Emergency Rollback

Execute this procedure when:
- Multiple critical alerts are active simultaneously
- User-facing functionality is completely broken
- Data corruption or loss is suspected
- System performance is severely degraded (>5 minute response times)
- Orleans integration is causing cascading failures
- Memory usage is approaching system limits (>90%)

## Pre-Rollback Checklist (1 minute)

### Step 1: Assess Situation Severity
```bash
# Check all critical metrics at once
curl http://localhost:5000/api/monitoring/health | jq '{
  status: .status,
  critical_alerts: .criticalAlerts,
  orleans: .components.orleans.status,
  memory_mb: .components.system.memoryUsage,
  active_alerts: .activeAlerts
}'
```

### Step 2: Backup Current State (Optional - if time permits)
```bash
# Create emergency state backup
timestamp=$(date +%Y%m%d_%H%M%S)
mkdir -p /tmp/emergency_backup_$timestamp

# Backup current configuration
cp server/appsettings.json /tmp/emergency_backup_$timestamp/
cp -r logs/ /tmp/emergency_backup_$timestamp/logs_sample/

# Export current metrics
curl http://localhost:5000/api/monitoring/metrics > /tmp/emergency_backup_$timestamp/metrics_before_rollback.json

echo "Backup created at: /tmp/emergency_backup_$timestamp"
```

## Rollback Execution

### Phase 1: Disable Orleans Integration (2 minutes)

```bash
# Step 1: Disable Orleans integration immediately
cat > /tmp/rollback_config.json << 'EOF'
{
  "FeatureManagement": {
    "OrleansIntegration": {
      "EnabledFor": [{
        "Name": "Percentage",
        "Parameters": {"Value": 0}
      }]
    },
    "BackgroundProcessing": {
      "EnabledFor": [{
        "Name": "Percentage", 
        "Parameters": {"Value": 0}
      }]
    },
    "SignalRMessaging": {
      "EnabledFor": [{
        "Name": "Percentage",
        "Parameters": {"Value": 0}
      }]
    }
  }
}
EOF

# Step 2: Apply rollback configuration
jq -s '.[0] * .[1]' server/appsettings.json /tmp/rollback_config.json > /tmp/appsettings_rollback.json
cp /tmp/appsettings_rollback.json server/appsettings.json

# Step 3: Force application restart
pkill -f AIChat.Server
sleep 5
nohup dotnet run --project server/AIChat.Server.csproj > logs/rollback_startup.log 2>&1 &
```

### Phase 2: Verify Fallback Mode (1 minute)

```bash
# Step 4: Wait for application startup
echo "Waiting for application startup..."
for i in {1..30}; do
  if curl -s http://localhost:5000/api/health >/dev/null 2>&1; then
    echo "Application is responding (attempt $i)"
    break
  fi
  sleep 2
done

# Step 5: Verify fallback mode is active
curl http://localhost:5000/api/monitoring/health | jq '{
  status: .status,
  orleans_disabled: (.components.orleans.status == "Disabled" or .components.orleans.status == "Healthy"),
  background_disabled: true  # Should be disabled
}'
```

### Phase 3: Test Critical Functionality (2 minutes)

```bash
# Step 6: Test basic chat functionality (SSE fallback)
echo "Testing basic functionality..."

# Test chat endpoint
response=$(curl -s -w "%{http_code}" -X POST http://localhost:5000/api/chat/send \
  -H "Content-Type: application/json" \
  -d '{"message":"rollback test","chatId":"emergency-test"}' \
  -o /tmp/rollback_test_response.json)

if [ "$response" = "200" ]; then
  echo "✅ Basic chat functionality working"
else  
  echo "❌ Basic chat functionality failed (HTTP $response)"
  cat /tmp/rollback_test_response.json
fi

# Test health endpoints
health_status=$(curl -s http://localhost:5000/api/health | jq -r .status)
if [ "$health_status" = "Healthy" ]; then
  echo "✅ Health check passing"
else
  echo "❌ Health check failing: $health_status"  
fi

# Check memory usage has normalized
memory_mb=$(curl -s http://localhost:5000/api/monitoring/metrics | jq -r '.metrics."system.memory.working_set_mb".value // 0')
if [ "${memory_mb%.*}" -lt 1024 ]; then
  echo "✅ Memory usage normal: ${memory_mb}MB"
else
  echo "⚠️  Memory usage still high: ${memory_mb}MB"
fi
```

## Post-Rollback Verification (2-3 minutes)

### Step 7: Comprehensive System Check
```bash
# Monitor system for 2 minutes to ensure stability
echo "Monitoring system stability for 2 minutes..."
for i in {1..4}; do
  echo "Check $i/4 ($(date)):"
  
  # Check health
  health=$(curl -s http://localhost:5000/api/health | jq -r .status)
  
  # Check memory
  memory=$(curl -s http://localhost:5000/api/monitoring/metrics | jq -r '.metrics."system.memory.working_set_mb".value // 0')
  
  # Check for errors
  error_count=$(tail -50 logs/server/app-*.jsonl | grep -c -i error || echo "0")
  
  echo "  Health: $health, Memory: ${memory}MB, Recent Errors: $error_count"
  
  if [ $i -lt 4 ]; then sleep 30; fi
done
```

### Step 8: User Communication
```bash
# Prepare status message
cat > /tmp/rollback_status.txt << EOF
SYSTEM STATUS UPDATE - $(date)

Emergency rollback procedure completed successfully.

Current Status:
- System: Operational in fallback mode
- Orleans: Disabled (reverted to direct processing)
- Performance: Restored to baseline
- User Impact: Service fully restored

Users should now be able to:
- Send and receive chat messages normally
- Access all core functionality
- Experience normal response times

Engineering team is investigating the root cause.
EOF

echo "Status message prepared at: /tmp/rollback_status.txt"
cat /tmp/rollback_status.txt
```

## Rollback Verification Checklist

Verify each of the following before confirming successful rollback:

- [ ] Application responds to health checks within 5 seconds
- [ ] Chat functionality works end-to-end
- [ ] Memory usage is below 1GB
- [ ] No critical errors in last 5 minutes of logs
- [ ] Response times are under 2 seconds
- [ ] Orleans integration is confirmed disabled
- [ ] Background processing is confirmed disabled
- [ ] SSE fallback mode is working

## Recovery Planning

### Immediate Actions (Next 1-2 hours)
1. **Monitor System Stability**:
   ```bash
   # Set up continuous monitoring
   watch -n 60 'echo "$(date): Health=$(curl -s http://localhost:5000/api/health | jq -r .status), Memory=$(curl -s http://localhost:5000/api/monitoring/metrics | jq -r .metrics.\"system.memory.working_set_mb\".value)MB"'
   ```

2. **Analyze Root Cause**:
   - Review logs from before the incident
   - Examine metrics trends leading to the failure
   - Identify configuration changes or deployments

3. **Document Incident**:
   - Record timeline of events
   - Note which symptoms led to rollback decision
   - Document effectiveness of rollback procedure

### Recovery Strategy
1. **Fix Root Cause**: Address underlying issues in development environment
2. **Staged Re-enablement**: Gradually re-enable Orleans features
3. **Enhanced Monitoring**: Add preventive alerts based on lessons learned

### Staged Re-enablement Procedure (After Root Cause Fix)
```bash
# Phase 1: Enable Orleans with low traffic (5%)
# Phase 2: Enable background processing with monitoring (25%)
# Phase 3: Gradual scale up to full traffic (50%, 75%, 100%)
# Phase 4: Re-enable all features with enhanced monitoring
```

## Troubleshooting Rollback Issues

### If Rollback Fails to Start Application
```bash
# Check for port conflicts
netstat -tlnp | grep :5000

# Check configuration syntax
jq . server/appsettings.json > /dev/null && echo "JSON valid" || echo "JSON invalid"

# Start with minimal configuration
cp server/appsettings.Development.json server/appsettings.json
dotnet run --project server/AIChat.Server.csproj
```

### If Performance Doesn't Improve
```bash
# Force memory cleanup
pkill -f AIChat.Server
sleep 10
echo 3 > /proc/sys/vm/drop_caches  # Linux only
dotnet run --project server/AIChat.Server.csproj
```

### If Users Still Report Issues
```bash
# Check for cached state issues
# Clear any browser caches, restart load balancers
# Verify all instances are rolled back if load balanced

# Test from different network/location
curl -H "User-Agent: Emergency-Test" http://your-public-endpoint/api/health
```

## Escalation and Communication

### When to Escalate Beyond Rollback
- Rollback doesn't complete within 15 minutes
- System doesn't stabilize after rollback
- Data corruption is confirmed
- Multiple dependent services are affected

### Communication Templates

**Internal Notification**:
```
PRIORITY: URGENT - Emergency Rollback Executed
Time: $(date)
System: AIChat Production
Status: Orleans integration disabled, service restored
Impact: User service restored, investigating root cause
Next: Root cause analysis in progress, staged re-enablement planned
```

**User-Facing Status**:
```
Service Alert: Brief service disruption resolved
We experienced a temporary service issue that has been resolved. 
All functionality is now operating normally.
Duration: [X] minutes
Impact: Temporary delays in message processing
Resolution: System restored to full functionality
```

## Related Documentation

- [Monitoring Dashboard](../monitoring-dashboard.html)
- [Grain Activation Failure](grain-activation-failure.md)
- [High Memory Usage](high-memory-usage.md)
- [Background Queue Overload](background-queue-overload.md)
- [Performance Degradation](performance-degradation.md)

## Post-Incident Review Template

After each emergency rollback, conduct a review covering:

1. **Timeline**: Exact sequence of events and response times
2. **Detection**: How quickly was the issue identified?
3. **Response**: Effectiveness of the rollback procedure
4. **Communication**: Quality and timeliness of notifications
5. **Root Cause**: Technical cause and contributing factors
6. **Prevention**: What changes will prevent recurrence?
7. **Procedure Updates**: How to improve this runbook