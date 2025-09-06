# Runbook: Background Queue Overload

## Alert Information
- **Alert Name**: Background Queue Overload
- **Severity**: Warning/Critical  
- **Threshold**: >100 queued operations (Warning), >500 (Critical)
- **Expected Resolution Time**: 5-15 minutes

## Symptoms
- Background queue depth alert triggered
- Slow response times for chat operations
- Users experiencing delays in message processing
- Background operations timing out
- Increasing memory usage due to queued operations

## Immediate Response (0-2 minutes)

### Step 1: Check Current Queue Status
```bash
# Get current queue depth
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics."background.queue.depth"'

# Check active operations
curl http://localhost:5000/api/monitoring/metrics | jq '.metrics."background.operations.active"'

# Get queue trend over last hour
curl http://localhost:5000/api/monitoring/metrics/background.queue.depth/history?hours=1
```

### Step 2: Check System Load
```bash
# Check overall system health
curl http://localhost:5000/api/monitoring/health

# Check CPU and memory pressure
curl http://localhost:5000/api/monitoring/metrics | jq '{
  cpu: .metrics."system.cpu.usage_percent",
  memory: .metrics."system.memory.working_set_mb"
}'
```

## Diagnosis (2-5 minutes)

### Step 3: Analyze Background Service Status
```bash
# Check for background service errors
grep -i "background.*error\|operation.*failed" logs/server/app-*.jsonl | tail -20

# Look for timeout issues
grep -i "timeout.*background\|operation.*timeout" logs/server/app-*.jsonl | tail -15

# Check for LLM API issues (common cause)
grep -i "llm.*error\|api.*timeout\|rate.*limit" logs/server/app-*.jsonl | tail -15
```

### Step 4: Check Operation Types and Patterns
```bash
# Look for operation distribution in logs
grep "EnqueueOperationAsync" logs/server/app-*.jsonl | tail -20 | \
  sed -E 's/.*"operationType":"([^"]*).*/\1/' | sort | uniq -c

# Check for stuck or long-running operations
grep -i "operation.*duration\|processing.*time" logs/server/app-*.jsonl | tail -15

# Look for Orleans grain connectivity issues
grep -i "grain.*failed\|orleans.*error" logs/server/app-*.jsonl | tail -10
```

### Step 5: Identify Resource Bottlenecks
```bash
# Check if LLM API is responding
curl -w "Time: %{time_total}s\n" -o /dev/null -s http://localhost:5000/api/health

# Check disk I/O (database operations)
iostat -x 1 3

# Check network connectivity if using external services
ping -c 3 api.openai.com  # or your LLM provider
```

## Resolution Steps

### Option A: Increase Processing Capacity (Quick Fix)
```bash
# Temporarily increase concurrent operations
# Edit appsettings.json:
# "BackgroundProcessing": { "MaxConcurrentOperations": 8 }

# Restart the application to apply changes
systemctl restart AIChat.Server

# Monitor queue reduction
watch -n 15 'curl -s http://localhost:5000/api/monitoring/metrics | jq .metrics.\"background.queue.depth\".value'
```

### Option B: Clear Stuck Operations (If Safe)
```bash
# Check for very old operations
grep "EnqueueOperationAsync" logs/server/app-*.jsonl | \
  awk -F'T' '{print $1}' | sort | uniq -c | tail -10

# If operations are stuck (>30 minutes), consider restart
age_minutes=$(( ($(date +%s) - $(date -d "$(grep 'EnqueueOperationAsync' logs/server/app-*.jsonl | head -1 | cut -d'"' -f4 | cut -dT -f1,2 | tr T ' ')" +%s)) / 60 ))
echo "Oldest operation age: $age_minutes minutes"

# Restart if operations are very old (>30 min)
if [ $age_minutes -gt 30 ]; then
  systemctl restart AIChat.Server
fi
```

### Option C: Address LLM API Issues
```bash
# Check LLM API status and rate limits
curl -H "Authorization: Bearer $LLM_API_KEY" \
  https://api.openai.com/v1/models | jq '.error // "API OK"'

# Temporarily reduce request rate if rate limited
# Edit appsettings.json to add delays or reduce concurrency
# "BackgroundProcessing": { "MaxConcurrentOperations": 2 }

# Restart application
systemctl restart AIChat.Server
```

### Option D: Emergency Queue Drain (Last Resort)
```bash
# Stop new operations from being queued (disable background processing)
# Edit appsettings.json:
# "FeatureManagement": { "BackgroundProcessing": { "EnabledFor": [{"Name": "Percentage", "Parameters": {"Value": 0}}]}}

# Restart to apply feature flag
systemctl restart AIChat.Server

# Wait for existing operations to complete (up to 5 minutes)
timeout 300 bash -c 'while [[ $(curl -s http://localhost:5000/api/monitoring/metrics | jq -r .metrics.\"background.operations.active\".value) -gt 0 ]]; do echo "Waiting for operations to complete..."; sleep 10; done'

# Re-enable background processing
# Edit appsettings.json to restore:
# "FeatureManagement": { "BackgroundProcessing": { "EnabledFor": [{"Name": "Percentage", "Parameters": {"Value": 100}}]}}

# Restart again
systemctl restart AIChat.Server
```

### Option E: Scale Infrastructure (For Persistent Overload)
```bash
# If running on cloud infrastructure, scale up
# AWS:
aws ecs update-service --cluster your-cluster --service your-service --desired-count 2

# Docker Compose:
docker-compose up --scale aichat-server=2

# Kubernetes:
kubectl scale deployment aichat-server --replicas=2
```

## Verification (2-3 minutes)

### Step 6: Confirm Queue Recovery
```bash
# Check queue depth is decreasing
for i in {1..6}; do
  echo "Check $i ($(date)):"
  curl -s http://localhost:5000/api/monitoring/metrics | jq '{
    queue_depth: .metrics."background.queue.depth".value,
    active_ops: .metrics."background.operations.active".value
  }'
  sleep 30
done
```

### Step 7: Verify Processing Performance
```bash
# Test a new operation end-to-end
start_time=$(date +%s)
curl -X POST http://localhost:5000/api/chat/send \
  -H "Content-Type: application/json" \
  -d '{"message":"test queue recovery","chatId":"test-queue"}'
end_time=$(date +%s)
echo "Request took $((end_time - start_time)) seconds"

# Check for new errors
tail -20 logs/server/app-*.jsonl | grep -i error

# Verify Orleans integration is working
curl http://localhost:5000/api/monitoring/health | jq .components.orleans
```

## Post-Incident Actions

### Root Cause Analysis
1. **Identify Queue Growth Pattern**:
   ```bash
   # Analyze queue depth over time
   curl http://localhost:5000/api/monitoring/metrics/background.queue.depth/history?hours=6 | jq '.summary'
   ```

2. **Check Operation Success Rates**:
   ```bash
   # Look for failed operations pattern
   grep -c "operation.*failed\|background.*error" logs/server/app-*.jsonl
   grep -c "operation.*completed\|background.*success" logs/server/app-*.jsonl
   ```

3. **Examine External Dependencies**:
   - LLM API response times
   - Database query performance  
   - Network latency
   - Orleans grain activation times

### Performance Optimization

Update configuration based on findings:

```json
{
  "BackgroundProcessing": {
    "MaxConcurrentOperations": 6,     // Increased from 5
    "QueueCapacity": 2000,            // Increased from 1000  
    "OperationTimeout": "00:08:00",   // Increased from 5 minutes
    "RetryPolicy": {
      "MaxRetries": 2,                // Reduced from 3
      "RetryDelayMilliseconds": 2000, // Increased delay
      "BackoffMultiplier": 1.5        // Reduced from 2.0
    }
  }
}
```

### Monitoring Enhancement
1. Add queue growth rate alerts
2. Monitor operation completion times
3. Track LLM API response times
4. Set up capacity planning alerts

### Prevention Measures
1. **Load Balancing**:
   - Implement operation priority queues
   - Add circuit breakers for external APIs
   - Implement backpressure mechanisms

2. **Capacity Planning**:
   - Monitor peak usage patterns  
   - Implement auto-scaling rules
   - Add resource reservation

3. **Fallback Mechanisms**:
   - Direct processing fallback
   - Operation batching
   - Smart retry logic

## Escalation Criteria

Escalate if:
- Queue depth exceeds 1000 operations
- Operations are failing with >20% error rate
- Queue doesn't drain after 20 minutes of intervention
- System resources become critically low
- LLM API is completely unavailable

## Related Runbooks

- [High Memory Usage](high-memory-usage.md)
- [Performance Degradation](performance-degradation.md)
- [Grain Activation Failure](grain-activation-failure.md)
- [Emergency Rollback](emergency-rollback.md)

## Common Patterns

- **Peak Hours**: Queue overload typically occurs during business hours
- **LLM API Issues**: Often correlated with OpenAI service incidents  
- **Memory Pressure**: Queue overload can lead to memory issues
- **Grain Activation**: Queue buildup when Orleans grains are slow to activate