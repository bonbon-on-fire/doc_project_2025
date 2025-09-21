# Orleans SSE Streaming Configuration Guide

## Overview

This document describes the configuration options for the optimized Orleans SSE streaming infrastructure, including adaptive buffer sizing, overflow strategies, and recovery mechanisms introduced in task ORL-ST-P1-003.

## Configuration Schema

All streaming configuration is located under the `Streaming` section in `appsettings.json`.

### Base Configuration

```json
{
  "Streaming": {
    "BufferSize": 100,
    "BackpressureThreshold": 80.0,
    "WriteTimeoutMs": 30000,
    "BackpressureDelayMs": 100,
    "EnableAdaptiveBackpressure": true,
    "EnableTelemetry": true,
    "MaxChunkSize": 32768,
    "FlushIntervalMs": 100,
    "EnableAutoRetry": true,
    "MaxRetryAttempts": 3
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| BufferSize | int | 100 | Initial buffer size for streaming data |
| BackpressureThreshold | float | 80.0 | Buffer utilization percentage to trigger backpressure |
| WriteTimeoutMs | int | 30000 | Timeout for write operations in milliseconds |
| BackpressureDelayMs | int | 100 | Base delay when backpressure is triggered |
| EnableAdaptiveBackpressure | bool | true | Enable dynamic backpressure adjustment |
| EnableTelemetry | bool | true | Enable detailed telemetry logging |
| MaxChunkSize | int | 32768 | Maximum size for SSE event chunks (bytes) |
| FlushIntervalMs | int | 100 | Interval for flushing HTTP response stream |
| EnableAutoRetry | bool | true | Enable automatic retry on transient errors |
| MaxRetryAttempts | int | 3 | Maximum retry attempts for transient errors |

### Adaptive Buffering Configuration

```json
{
  "Streaming": {
    "AdaptiveBuffering": {
      "Enabled": true,
      "MinSize": 50,
      "MaxSize": 1000,
      "ScaleUpThreshold": 80.0,
      "ScaleDownThreshold": 30.0,
      "WindowSizeMinutes": 5,
      "ScaleFactor": 1.5,
      "ScaleCooldownSeconds": 30
    }
  }
}
```

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| Enabled | bool | - | true | Enable adaptive buffer sizing |
| MinSize | int | 10-1000 | 50 | Minimum buffer size |
| MaxSize | int | 100-10000 | 1000 | Maximum buffer size |
| ScaleUpThreshold | float | 50-95 | 80.0 | Utilization % to trigger scale up |
| ScaleDownThreshold | float | 10-50 | 30.0 | Utilization % to trigger scale down |
| WindowSizeMinutes | int | 1-60 | 5 | Time window for trend analysis |
| ScaleFactor | float | 1.1-3.0 | 1.5 | Multiplier for size adjustments |
| ScaleCooldownSeconds | int | 10-300 | 30 | Minimum time between scaling operations |

### Overflow Strategy Configuration

```json
{
  "Streaming": {
    "OverflowStrategy": {
      "Primary": "Backpressure",
      "Fallback": "DropOldest",
      "DropThreshold": 95.0,
      "EnableMetrics": true,
      "MaxDropBatchSize": 10
    }
  }
}
```

| Property | Type | Options | Default | Description |
|----------|------|---------|---------|-------------|
| Primary | enum | Backpressure, DropOldest, DropNewest, Hybrid | Backpressure | Primary overflow handling strategy |
| Fallback | enum | Backpressure, DropOldest, DropNewest, Hybrid | DropOldest | Fallback strategy when primary fails |
| DropThreshold | float | 80-100 | 95.0 | Buffer utilization % to trigger dropping |
| EnableMetrics | bool | - | true | Track overflow event metrics |
| MaxDropBatchSize | int | 1-100 | 10 | Maximum items to drop in single operation |

#### Strategy Descriptions

- **Backpressure**: Applies delay to slow down producers
- **DropOldest**: Removes oldest items from buffer
- **DropNewest**: Rejects new items
- **Hybrid**: Combines backpressure and drop strategies based on utilization

### Recovery Configuration

```json
{
  "Streaming": {
    "Recovery": {
      "EnableAutoReconnect": true,
      "MaxReconnectAttempts": 5,
      "ReconnectDelayMs": 1000,
      "MaxReconnectDelayMs": 30000,
      "BackoffMultiplier": 2.0,
      "EnableMessageReplay": true,
      "ReplayBufferSize": 100,
      "ReplayMaxAgeSeconds": 300,
      "EnableCheckpoints": true,
      "CheckpointInterval": 50,
      "RecoveryTimeoutMs": 60000
    }
  }
}
```

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| EnableAutoReconnect | bool | - | true | Enable automatic reconnection |
| MaxReconnectAttempts | int | 1-20 | 5 | Maximum reconnection attempts |
| ReconnectDelayMs | int | 100-10000 | 1000 | Initial reconnection delay |
| MaxReconnectDelayMs | int | 1000-60000 | 30000 | Maximum reconnection delay |
| BackoffMultiplier | float | 1.1-5.0 | 2.0 | Exponential backoff multiplier |
| EnableMessageReplay | bool | - | true | Replay messages after recovery |
| ReplayBufferSize | int | 10-1000 | 100 | Number of messages to keep for replay |
| ReplayMaxAgeSeconds | int | 10-3600 | 300 | Maximum age of messages to replay |
| EnableCheckpoints | bool | - | true | Enable checkpoint-based recovery |
| CheckpointInterval | int | 10-500 | 50 | Messages between checkpoints |
| RecoveryTimeoutMs | int | 5000-300000 | 60000 | Timeout for recovery operations |

## Configuration Examples

### High-Throughput Configuration

Optimized for high message volume with aggressive buffering:

```json
{
  "Streaming": {
    "BufferSize": 500,
    "AdaptiveBuffering": {
      "Enabled": true,
      "MinSize": 200,
      "MaxSize": 5000,
      "ScaleUpThreshold": 70.0,
      "ScaleDownThreshold": 20.0,
      "ScaleFactor": 2.0
    },
    "OverflowStrategy": {
      "Primary": "Hybrid",
      "Fallback": "DropOldest",
      "DropThreshold": 90.0,
      "MaxDropBatchSize": 50
    }
  }
}
```

### Low-Latency Configuration

Optimized for minimal latency with smaller buffers:

```json
{
  "Streaming": {
    "BufferSize": 50,
    "FlushIntervalMs": 50,
    "AdaptiveBuffering": {
      "Enabled": true,
      "MinSize": 25,
      "MaxSize": 200,
      "ScaleUpThreshold": 85.0,
      "WindowSizeMinutes": 2
    },
    "OverflowStrategy": {
      "Primary": "DropNewest",
      "EnableMetrics": true
    }
  }
}
```

### Resilient Configuration

Optimized for reliability with aggressive recovery:

```json
{
  "Streaming": {
    "BufferSize": 200,
    "EnableAutoRetry": true,
    "MaxRetryAttempts": 5,
    "Recovery": {
      "EnableAutoReconnect": true,
      "MaxReconnectAttempts": 10,
      "EnableMessageReplay": true,
      "ReplayBufferSize": 200,
      "EnableCheckpoints": true,
      "CheckpointInterval": 25
    },
    "OverflowStrategy": {
      "Primary": "Backpressure",
      "Fallback": "Hybrid"
    }
  }
}
```

## Monitoring and Metrics

The streaming infrastructure provides the following metrics:

### Buffer Metrics
- Current buffer size and capacity
- Buffer utilization percentage
- Scale up/down operations count
- Average processing time per item

### Overflow Metrics
- Total overflow events by strategy
- Items dropped/rejected
- Backpressure delays applied
- Strategy failure counts

### Recovery Metrics
- Recovery attempts and success rate
- Messages replayed
- Checkpoint creation frequency
- Average recovery time

## Troubleshooting

### Common Issues

1. **Buffer constantly scaling up**
   - Check if consumers are keeping up with producers
   - Consider increasing `ScaleUpThreshold`
   - Review `MaxSize` limit

2. **Frequent overflow events**
   - Increase initial `BufferSize`
   - Lower `ScaleUpThreshold` for earlier scaling
   - Consider using `Hybrid` overflow strategy

3. **Recovery failures**
   - Increase `MaxReconnectAttempts`
   - Adjust `BackoffMultiplier` for less aggressive backoff
   - Check network stability

4. **High memory usage**
   - Reduce `MaxSize` in adaptive buffering
   - Lower `ReplayBufferSize`
   - Use more aggressive overflow strategies

## Performance Tuning

### Guidelines

1. **Buffer Sizing**
   - Start with BufferSize = expected messages/second * 0.1
   - Set MaxSize = BufferSize * 10
   - Monitor and adjust based on actual usage

2. **Thresholds**
   - ScaleUpThreshold: 70-80% for proactive scaling
   - ScaleDownThreshold: 20-30% to avoid thrashing
   - DropThreshold: 90-95% as last resort

3. **Recovery Settings**
   - ReplayBufferSize ≈ messages/second * 5
   - CheckpointInterval ≈ ReplayBufferSize / 2
   - RecoveryTimeoutMs ≈ average recovery time * 3

## Migration Notes

When upgrading from previous versions:

1. The default configuration provides backward compatibility
2. Adaptive buffering is opt-in via `AdaptiveBuffering.Enabled`
3. Existing `BufferSize` is used as initial size
4. Recovery features are additive and don't break existing behavior

## References

- [Orleans Streaming Documentation](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/streams-implementation/)
- [Server-Sent Events Specification](https://html.spec.whatwg.org/multipage/server-sent-events.html)
- Task Implementation: ORL-ST-P1-003