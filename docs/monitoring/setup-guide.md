# Orleans Metrics Monitoring Setup Guide

This guide explains how to set up comprehensive monitoring for Orleans grain metrics using Prometheus and Grafana.

## Overview

The Orleans metrics system provides detailed insights into:
- Grain activation/deactivation rates
- Operation latency and throughput
- Error rates and success metrics
- State size and memory usage
- Active connections (UserGrain)

## Prerequisites

- Prometheus server
- Grafana instance
- Orleans application with metrics enabled

## Metrics Endpoint

The Orleans application exposes metrics at:
```
GET /metrics
```

This endpoint provides Prometheus-format metrics including:
- `orleans_grain_activations_total` - Counter of grain activations by type
- `orleans_grain_deactivations_total` - Counter of grain deactivations by type
- `orleans_grain_activation_duration_seconds` - Histogram of activation times
- `orleans_grain_operation_duration_seconds` - Histogram of operation durations
- `orleans_grain_operation_errors_total` - Counter of operation errors
- `orleans_grain_state_size_bytes` - Gauge of grain state sizes
- `orleans_grain_active_connections` - Gauge of active connections
- `orleans_grain_active_operations` - Gauge of active operations

## Prometheus Configuration

Add the following to your `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: 'orleans-metrics'
    static_configs:
      - targets: ['localhost:5000']  # Adjust to your Orleans app URL
    scrape_interval: 15s
    metrics_path: /metrics
    scrape_timeout: 10s
```

## Alerting Rules Setup

1. Copy `orleans-alerting-rules.yml` to your Prometheus configuration directory
2. Add to your `prometheus.yml`:

```yaml
rule_files:
  - "orleans-alerting-rules.yml"
```

3. Restart Prometheus to load the rules

### Available Alerts

- **HighGrainErrorRate**: >5% error rate for 2 minutes
- **SlowGrainOperations**: P95 latency >1 second for 2 minutes
- **HighGrainStateSize**: Individual grain state >50MB for 5 minutes
- **HighGrainActivationRate**: >2 activations/second for 3 minutes
- **LowOperationSuccessRate**: <95% success rate for 5 minutes
- **GrainMetricsDown**: Metrics endpoint unavailable for 1 minute
- **NoGrainActivity**: No operations for 10 minutes
- **TotalMemoryUsageHigh**: Total grain memory >1GB for 5 minutes
- **MemoryGrowthRate**: >100MB/hour growth for 15 minutes
- **HighUserConnections**: >1000 active connections for 2 minutes
- **ConnectionImbalance**: >100 connection difference between grains

## Grafana Dashboard Setup

1. Import the dashboard JSON:
   - Open Grafana
   - Go to Dashboards → Import
   - Upload `orleans-metrics-dashboard.json`
   - Configure Prometheus data source

2. The dashboard includes panels for:
   - Active grains count by type
   - Activation/deactivation rates
   - Operation latency percentiles
   - Error rates with alerting
   - State size monitoring
   - Operation throughput
   - User connection tracking

## Dashboard Features

### Variables
- `grain_type`: Filter by UserGrain, ChatGrain, or ModeGrain

### Key Visualizations
1. **Active Grains**: Current count of active grains by type
2. **Activation Rate**: Real-time grain activation rate (grains/second)
3. **Deactivation Rate**: Real-time grain deactivation rate
4. **Operation Latency P95**: 95th percentile operation duration
5. **Error Rate**: Percentage of failed operations with alerting
6. **State Size**: Grain state memory usage
7. **Throughput**: Operations per second by grain and operation type
8. **User Connections**: Active connections for UserGrain instances

## Health Check

Monitor the health of the metrics system:
```
GET /api/metrics/health
```

Returns:
```json
{
  "Status": "Healthy",
  "Timestamp": "2024-01-01T12:00:00Z",
  "Service": "PrometheusMetricsEndpoint",
  "Message": "Prometheus metrics endpoint is available at /metrics"
}
```

## Troubleshooting

### No Metrics Data
1. Verify `/metrics` endpoint is accessible
2. Check Prometheus scrape configuration
3. Confirm Orleans application is running with metrics enabled

### Missing Grain Metrics
1. Ensure grains are properly instrumented
2. Check that `IOrleansMetricsCollector` is registered in DI
3. Verify grain operations are being called

### Dashboard Not Loading
1. Confirm Prometheus data source is configured
2. Check that metrics are being scraped successfully
3. Verify time range settings in dashboard

### Alerts Not Firing
1. Check Prometheus rule evaluation logs
2. Verify alert manager configuration
3. Confirm notification channels are set up

## Performance Considerations

- Metrics collection adds minimal overhead (<1ms per operation)
- Prometheus scraping interval should be 15-30 seconds for good resolution
- Consider retention policies for metrics storage
- Monitor the metrics collection system itself

## Example Queries

### Average operation duration by grain type:
```promql
avg by (grain_type) (rate(orleans_grain_operation_duration_seconds_sum[5m]) / rate(orleans_grain_operation_duration_seconds_count[5m]))
```

### Error rate by operation:
```promql
rate(orleans_grain_operation_errors_total[5m]) / rate(orleans_grain_operation_duration_seconds_count[5m]) * 100
```

### Total memory usage:
```promql
sum(orleans_grain_state_size_bytes) / (1024*1024*1024)
```

### Top 10 largest grains:
```promql
topk(10, orleans_grain_state_size_bytes)
```

## Security Considerations

- Restrict access to `/metrics` endpoint in production
- Consider authentication for Prometheus scraping
- Review metrics data for sensitive information exposure
- Use HTTPS for metrics transport in production

## Maintenance

### Regular Tasks
- Review and tune alert thresholds based on operational experience
- Update dashboard queries as new grain types are added
- Monitor metrics retention and storage requirements
- Review and update runbook URLs in alerts

### Capacity Planning
- Monitor Prometheus storage growth
- Plan for increased metrics volume as system scales
- Consider federation for large deployments
- Review scrape intervals based on requirements