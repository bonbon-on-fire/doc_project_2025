# SSE Monitoring API Documentation

## Overview

The SSE (Server-Sent Events) Monitoring API provides comprehensive metrics, health indicators, and alerting capabilities for Orleans-based streaming infrastructure. This API is essential for monitoring stream performance, detecting issues, and ensuring reliable message delivery.

## Base URL

```
/api/monitoring/sse
```

## Authentication

All endpoints require appropriate authentication based on your deployment configuration.

## Endpoints

### 1. Get SSE Metrics

**GET** `/api/monitoring/sse/metrics`

Returns current SSE streaming metrics summary.

#### Response

```json
{
  "timestamp": "2025-01-21T10:30:00Z",
  "connections": {
    "active": 42,
    "establishedLastHour": 156,
    "closedLastHour": 114
  },
  "streaming": {
    "averageDuration": 3456.78,
    "chunksProcessedLastHour": 12580,
    "bytesTransmittedLastHour": 52428800,
    "averageChunkProcessingTime": 23.45,
    "successRate": 98.5
  },
  "buffers": {
    "averageUtilization": 67.8,
    "overflowsLastHour": 0
  },
  "failures": {
    "streamsFailedLastHour": 2,
    "failureRate": 1.5
  },
  "chunkTypes": [
    {
      "type": "text",
      "metrics": {
        "count": 8500,
        "averageSize": 256,
        "averageProcessingTime": 15.2,
        "successRate": 99.8,
        "totalBytes": 2176000
      }
    }
  ]
}
```

### 2. Get SSE Health Status

**GET** `/api/monitoring/sse/health`

Returns health indicators and active alerts for SSE streaming.

#### Response

```json
{
  "timestamp": "2025-01-21T10:30:00Z",
  "overallHealth": "Healthy",
  "indicators": {
    "connectionStability": {
      "status": "healthy",
      "value": 3,
      "warningThreshold": 10,
      "criticalThreshold": 50,
      "description": "3 connection drops in the last hour",
      "trend": "stable"
    },
    "streamThroughput": {
      "status": "healthy",
      "value": 209.67,
      "description": "Processing 209.67 chunks per minute",
      "trend": "increasing"
    },
    "bufferHealth": {
      "status": "healthy",
      "value": 67.8,
      "warningThreshold": 75,
      "criticalThreshold": 90,
      "description": "Buffer utilization at 67.8%",
      "trend": "stable"
    },
    "errorRate": {
      "status": "healthy",
      "value": 1.5,
      "warningThreshold": 5,
      "criticalThreshold": 10,
      "description": "Stream failure rate: 1.5%",
      "trend": "decreasing"
    },
    "latency": {
      "status": "healthy",
      "value": 23.45,
      "warningThreshold": 500,
      "criticalThreshold": 1000,
      "description": "Average chunk processing: 23ms",
      "trend": "stable"
    },
    "recoveryPerformance": {
      "status": "healthy",
      "value": 95.3,
      "description": "Recovery success rate: 95.3%",
      "trend": "stable"
    }
  },
  "activeAlerts": [],
  "checkedAt": "2025-01-21T10:30:00Z"
}
```

### 3. Get Historical Metrics

**GET** `/api/monitoring/sse/metrics/history`

Returns historical data for specific SSE metrics.

#### Parameters

- `metricType` (optional): Type of metric to retrieve
  - `ActiveConnections` (default)
  - `ChunkThroughput`
  - `ChunkLatency`
  - `BufferUtilization`
  - `FailureRate`
  - `ByteThroughput`
  - `ConnectionDropRate`
  - `RecoveryTime`
- `hours` (optional): Number of hours of history (1-24, default: 1)

#### Response

```json
{
  "metricType": "ChunkThroughput",
  "timeRange": {
    "hours": 1,
    "start": "2025-01-21T09:30:00Z",
    "end": "2025-01-21T10:30:00Z"
  },
  "dataPoints": [
    {
      "timestamp": "2025-01-21T09:30:00Z",
      "value": 185.5,
      "tags": {}
    },
    {
      "timestamp": "2025-01-21T09:35:00Z",
      "value": 192.3,
      "tags": {}
    }
  ],
  "summary": {
    "count": 12,
    "average": 195.8,
    "min": 185.5,
    "max": 215.2,
    "latest": 209.67,
    "trend": "increasing"
  }
}
```

### 4. Get Alert Rules Configuration

**GET** `/api/monitoring/sse/alerts/rules`

Returns configured alerting rules for SSE monitoring.

#### Response

```json
{
  "timestamp": "2025-01-21T10:30:00Z",
  "rules": [
    {
      "id": "sse-connection-drops",
      "name": "SSE Connection Drops",
      "metric": "ConnectionDrops",
      "condition": "greater_than",
      "warningThreshold": 10,
      "criticalThreshold": 50,
      "evaluationWindow": "1 hour",
      "enabled": true,
      "description": "Monitors unexpected SSE connection terminations"
    },
    {
      "id": "sse-stream-failure-rate",
      "name": "SSE Stream Failure Rate",
      "metric": "FailureRate",
      "condition": "greater_than",
      "warningThreshold": 5.0,
      "criticalThreshold": 10.0,
      "evaluationWindow": "1 hour",
      "enabled": true,
      "description": "Tracks percentage of failed SSE streams"
    },
    {
      "id": "sse-buffer-utilization",
      "name": "SSE Buffer Utilization",
      "metric": "BufferUtilization",
      "condition": "greater_than",
      "warningThreshold": 75.0,
      "criticalThreshold": 90.0,
      "evaluationWindow": "5 minutes",
      "enabled": true,
      "description": "Monitors SSE buffer capacity usage"
    }
  ]
}
```

### 5. Get Active Alerts

**GET** `/api/monitoring/sse/alerts/active`

Returns currently active SSE-related alerts.

#### Response

```json
{
  "timestamp": "2025-01-21T10:30:00Z",
  "totalActive": 1,
  "bySeverity": {
    "critical": 0,
    "warning": 1,
    "info": 0
  },
  "alerts": [
    {
      "id": "alert-123",
      "severity": "warning",
      "message": "SSE buffer utilization is high",
      "triggeredAt": "2025-01-21T10:25:00Z",
      "duration": "5m",
      "metric": "BufferUtilization",
      "currentValue": 78.5,
      "threshold": 75.0,
      "action": "Consider increasing buffer capacity or optimizing chunk processing"
    }
  ]
}
```

### 6. Get Dashboard Configuration

**GET** `/api/monitoring/sse/dashboard/config`

Returns dashboard panel configuration for UI rendering.

#### Response

```json
{
  "title": "SSE Streaming Monitor",
  "refreshInterval": 30,
  "version": "1.0.0",
  "panels": [
    {
      "id": "sse-connections",
      "title": "Active SSE Connections",
      "type": "gauge",
      "metric": "ActiveConnections",
      "order": 1,
      "size": "small",
      "thresholds": {
        "warning": 100,
        "critical": 500
      }
    },
    {
      "id": "sse-throughput",
      "title": "Stream Throughput",
      "type": "line-chart",
      "metric": "ChunkThroughput",
      "order": 2,
      "size": "medium",
      "timeWindow": "1h"
    }
  ],
  "metrics": [
    {
      "name": "ActiveConnections",
      "displayName": "Active Connections",
      "unit": "count"
    },
    {
      "name": "ChunkLatency",
      "displayName": "Processing Latency",
      "unit": "ms"
    }
  ]
}
```

## Integration with Orleans Dashboard

To integrate SSE monitoring with the Orleans dashboard:

### 1. Service Registration

Add SSE metrics collection in `Program.cs`:

```csharp
// Add Orleans with SSE monitoring
builder.Services.AddOrleans(siloBuilder =>
{
    // Orleans configuration...
});

// Add SSE metrics collection
builder.Services.AddSseMetricsCollection(options =>
{
    options.MaxRecentEvents = 10000;
    options.MetricsRetentionHours = 24;
    options.EnableAutoAlerts = true;
});
```

### 2. Metric Collection Points

Integrate metric collection in grain operations:

```csharp
public class UserGrain : Grain, IUserGrain
{
    private readonly ISseMetricsCollector _sseMetrics;

    public async Task ProcessStreamChunkAsync(StreamChunk chunk)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Process chunk...

            await _sseMetrics.RecordSseStreamChunkAsync(
                this.GetPrimaryKeyString(),
                chunk.Type,
                chunk.Size,
                sw.ElapsedMilliseconds,
                success: true
            );
        }
        catch
        {
            await _sseMetrics.RecordSseStreamChunkAsync(
                this.GetPrimaryKeyString(),
                chunk.Type,
                chunk.Size,
                sw.ElapsedMilliseconds,
                success: false
            );
            throw;
        }
    }
}
```

### 3. Dashboard Integration

The Orleans dashboard automatically displays SSE metrics when configured:

1. Navigate to the Orleans Dashboard
2. Select "SSE Streaming" from the menu
3. View real-time metrics and health indicators
4. Configure alerts and thresholds as needed

## Alert Configuration

### Alert Severity Levels

- **Critical**: Requires immediate attention (e.g., >10% failure rate)
- **Warning**: Needs investigation (e.g., >75% buffer utilization)
- **Info**: Informational alerts (e.g., configuration changes)

### Custom Alert Rules

Create custom alert rules by posting to the configuration endpoint:

```json
{
  "name": "Custom SSE Alert",
  "metric": "CustomMetric",
  "condition": "greater_than",
  "threshold": 100,
  "severity": "warning",
  "evaluationWindow": "5 minutes"
}
```

## Monitoring Best Practices

### 1. Key Metrics to Monitor

- **Connection Stability**: Track connection drops and recovery rates
- **Stream Throughput**: Monitor chunks processed per minute
- **Latency**: Watch chunk processing times
- **Buffer Health**: Monitor utilization and overflow events
- **Error Rates**: Track stream failures and chunk processing errors

### 2. Alert Thresholds

Recommended initial thresholds:

- Connection Drops: Warning at 10/hour, Critical at 50/hour
- Failure Rate: Warning at 5%, Critical at 10%
- Buffer Utilization: Warning at 75%, Critical at 90%
- Latency: Warning at 500ms, Critical at 1000ms

### 3. Troubleshooting Guide

#### High Latency
1. Check chunk processing logic
2. Review grain activation patterns
3. Analyze network conditions
4. Consider scaling resources

#### Buffer Overflows
1. Increase buffer capacity
2. Implement back-pressure
3. Optimize chunk processing
4. Review client consumption rates

#### Connection Drops
1. Check network stability
2. Review client reconnection logic
3. Analyze grain lifecycle
4. Monitor resource utilization

## Performance Considerations

- Metrics collection adds minimal overhead (<1ms per operation)
- Historical data is retained for 24 hours by default
- Health checks are cached for 30 seconds
- Dashboard refresh interval: 30 seconds (configurable)

## API Versioning

Current API version: 1.0.0

Future versions will maintain backward compatibility or provide migration paths.

## Support and Feedback

For issues or feature requests related to SSE monitoring:
1. Check the troubleshooting guide
2. Review alert recommendations
3. Contact the platform team
4. Submit feedback through the dashboard

---

Last Updated: January 2025
Version: 1.0.0