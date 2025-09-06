# Runbook: High Memory Usage

## Alert Information
- **Alert Name**: High Memory Usage
- **Severity**: Warning/Critical
- **Threshold**: >1GB working set (Warning), >2GB (Critical)
- **Expected Resolution Time**: 10-20 minutes

## Symptoms
- Memory usage alert triggered
- Application performance degradation
- Potential out-of-memory exceptions
- Increased garbage collection pressure
- Slow response times

## Immediate Response (0-2 minutes)

### Step 1: Verify Current Memory Usage
```bash
# Check current memory consumption
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics."system.memory.working_set_mb"'

# Get system memory overview
free -h

# Check process memory specifically
ps aux --sort=-%mem | head -10
```

### Step 2: Check for Active Memory Pressure
```bash
# Check for out-of-memory errors
grep -i "out of memory\|outofmemory" logs/server/app-*.jsonl | tail -10

# Check GC activity
grep -i "gc\|garbage" logs/server/app-*.jsonl | tail -10
```

## Diagnosis (2-8 minutes)

### Step 3: Identify Memory Usage Patterns
```bash
# Get detailed memory metrics
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics | to_entries | map(select(.key | contains("memory")))'

# Check historical memory trends
curl http://localhost:5000/api/monitoring/metrics/system.memory.working_set_mb/history?hours=6
```

### Step 4: Analyze Grain State Memory
```bash
# Check Orleans grain counts and memory
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics."orleans.grains.active"'

# Look for grain state issues in logs
grep -i "grain.*memory\|state.*large" logs/server/app-*.jsonl | tail -15

# Check for memory leaks in grain activation
grep -i "activation.*memory" logs/server/app-*.jsonl | tail -10
```

### Step 5: Check Background Processing Impact
```bash
# Check background service queue and memory impact
curl http://localhost:5000/api/monitoring/metrics | jq '{
  queue_depth: .metrics."background.queue.depth",
  active_operations: .metrics."background.operations.active"
}'

# Look for background service memory usage
grep -i "background.*memory\|operation.*memory" logs/server/app-*.jsonl | tail -10
```

### Step 6: Examine System Resources
```bash
# Check swap usage
free -h | grep Swap

# Check for memory-intensive processes
top -b -n1 -o %MEM | head -15

# Check disk space (can affect virtual memory)
df -h /tmp /var /
```

## Resolution Steps

### Option A: Trigger Garbage Collection (Immediate Relief)
```bash
# Force GC through monitoring endpoint (if implemented)
curl -X POST http://localhost:5000/api/monitoring/gc

# Or trigger through application restart (safer)
systemctl reload AIChat.Server  # If using systemd
```

### Option B: Orleans State Cleanup
```bash
# Enable aggressive grain deactivation temporarily
# Edit appsettings.json to reduce grain idle timeout:
# "OrleansGrains": { "UserGrain": { "ActivityRetentionHours": 0.5 }}

# Restart application to apply changes
systemctl restart AIChat.Server

# Monitor memory reduction
watch -n 30 'curl -s http://localhost:5000/api/monitoring/metrics | jq .metrics.\"system.memory.working_set_mb\".value'
```

### Option C: Background Processing Optimization
```bash
# Reduce background service concurrency
# Edit appsettings.json:
# "BackgroundProcessing": { "MaxConcurrentOperations": 2 }

# Clear background queue if safe
curl -X POST http://localhost:5000/api/monitoring/background/clear-queue

# Restart background service
systemctl restart AIChat.Server
```

### Option D: Emergency Memory Increase (Temporary)
```bash
# Increase available system memory if on cloud
# AWS: Resize instance
# Azure: Scale up VM
# Docker: Increase memory limits

# Or increase swap space temporarily
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
```

### Option E: Application Restart (Last Resort)
```bash
# Graceful restart to clear memory
systemctl restart AIChat.Server

# Monitor startup memory usage
tail -f logs/server/app-*.jsonl | grep -i "memory\|startup"

# Verify memory levels after restart
sleep 30
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics."system.memory.working_set_mb"'
```

## Verification (2-3 minutes)

### Step 7: Confirm Memory Reduction
```bash
# Check current memory levels
curl http://localhost:5000/api/monitoring/metrics | jq '{
  memory_mb: .metrics."system.memory.working_set_mb".value,
  timestamp: .metrics."system.memory.working_set_mb".timestamp
}'

# Verify memory trend is stable or decreasing
for i in {1..5}; do
  echo "Check $i:"
  curl -s http://localhost:5000/api/monitoring/metrics | jq '.metrics."system.memory.working_set_mb".value'
  sleep 30
done
```

### Step 8: Verify Application Stability
```bash
# Test basic functionality
curl -X POST http://localhost:5000/api/chat/send \
  -H "Content-Type: application/json" \
  -d '{"message":"memory test","chatId":"test-memory"}'

# Check for any new errors
tail -20 logs/server/app-*.jsonl | grep -i error

# Verify Orleans health
curl http://localhost:5000/api/monitoring/health | jq .status
```

## Post-Incident Actions

### Root Cause Analysis
1. **Review Memory Growth Pattern**:
   ```bash
   # Analyze memory growth over time
   curl http://localhost:5000/api/monitoring/metrics/system.memory.working_set_mb/history?hours=24 | jq '.summary'
   ```

2. **Identify Top Memory Consumers**:
   - Grain state storage
   - Background operation buffers  
   - Cached data
   - Connection state

3. **Check for Memory Leaks**:
   - Review Orleans grain lifecycle
   - Examine background service cleanup
   - Analyze event handler cleanup

### Configuration Optimization

Update `appsettings.json` based on findings:

```json
{
  "OrleansGrains": {
    "UserGrain": {
      "MaxActivityBufferSize": 50,          // Reduced from 100
      "ActivityRetentionHours": 0.5,        // Reduced from 1.0
      "CleanupIntervalMinutes": 2,          // Reduced from 5
      "MessageBufferSizePerChat": 50        // Reduced from 100
    }
  },
  "BackgroundProcessing": {
    "MaxConcurrentOperations": 3,           // Reduced from 5
    "QueueCapacity": 500,                   // Reduced from 1000
    "OperationHistoryRetention": "00:30:00" // Reduced from 1 hour
  }
}
```

### Monitoring Enhancement
1. Add more granular memory metrics
2. Set up memory trend alerting
3. Implement proactive grain cleanup
4. Add memory usage to capacity planning

### Prevention Measures
1. **Implement Memory Quotas**:
   - Per-grain memory limits
   - Background service memory bounds
   - Connection memory limits

2. **Enhanced Cleanup**:
   - More aggressive grain deactivation
   - Regular state pruning
   - Buffer size optimization

3. **Monitoring**:
   - Memory usage trends
   - GC frequency monitoring
   - Grain count vs memory correlation

## Escalation Criteria

Escalate if:
- Memory usage exceeds 3GB working set
- Out-of-memory exceptions occur
- System becomes unresponsive
- Memory doesn't stabilize after 30 minutes post-fix
- Issue recurs within 4 hours

## Related Runbooks

- [Grain Activation Failure](grain-activation-failure.md)
- [Performance Degradation](performance-degradation.md)  
- [Background Queue Overload](background-queue-overload.md)
- [Emergency Rollback](emergency-rollback.md)

## Historical Notes

- **2024-01**: Memory issue resolved by reducing grain buffer sizes
- **2024-01**: Added proactive GC triggers for memory pressure
- **Pattern**: Memory spikes correlate with high background operation volume