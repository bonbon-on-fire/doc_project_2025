# Orleans Monitoring and Dashboard Documentation

## Overview

The Orleans host includes a comprehensive monitoring and alerting system for tracking grain performance, system health, and operational metrics.

## Dashboard Access

### Development Environment
- **URL**: http://localhost:5100/dashboard
- **Authentication**: None required (development only)
- **Auto-refresh**: Every 30 seconds
- **Features**:
  - Real-time system status
  - Uptime tracking
  - Metrics visualization
  - Manual refresh capability

### Production Environment
- **URL**: http://[host]:8080/dashboard (or configured port)
- **Authentication**: Integrated with host security (future enhancement)
- **Features**: Same as development

## API Endpoints

### Metrics API
- **Endpoint**: `/api/orleans/metrics`
- **Method**: GET
- **Returns**: Complete metrics summary
- **Format**: JSON
- **Update Frequency**: Real-time

### Grain-Specific Metrics
- **Endpoint**: `/api/orleans/metrics/{grainType}`
- **Method**: GET
- **Parameters**: grainType (e.g., "UserGrain")
- **Returns**: Detailed metrics for specific grain type

### Health Check
- **Endpoint**: `/health`
- **Method**: GET
- **Returns**: Simple health status text
- **Purpose**: Load balancer health checks

## Metrics Collected

### Grain Metrics
- **Active Grains**: Current number of active grain instances
- **Activation Time**: Average time to activate grains (milliseconds)
- **Deactivation Events**: Grain deactivation tracking
- **Memory Usage**: Memory consumption by grain states
- **Operation Duration**: Average operation execution time
- **Success Rate**: Percentage of successful operations

### System Metrics
- **Silo Health**: Orleans silo operational status
- **CPU Usage**: System CPU utilization
- **Memory Usage**: System memory consumption
- **Request Rate**: Requests processed per second

## Alert Configuration

### Critical Alerts
1. **Orleans Silo Down**
   - Condition: Silo health check fails
   - Threshold: 2 minutes
   - Notification: Operations + Development teams

2. **Memory Usage High**
   - Condition: Memory usage > 85%
   - Threshold: 3 minutes
   - Notification: Operations team

### Warning Alerts
1. **High Grain Activation Time**
   - Condition: Average activation > 5000ms
   - Threshold: 5 minutes
   - Notification: Development team

2. **Request Processing Errors**
   - Condition: Error rate > 5%
   - Threshold: 5 minutes
   - Notification: Both teams

## Performance Impact

### Metrics Collection Performance
- **Memory Impact**: ~1MB for 1000 grain instances
- **CPU Impact**: <0.1% CPU overhead
- **Storage**: In-memory with 24-hour retention
- **Cleanup**: Automatic hourly cleanup of old metrics

### Dashboard Performance
- **Load Impact**: Minimal - static HTML with AJAX calls
- **Network**: ~1KB per metrics fetch
- **Refresh Rate**: 30 seconds (configurable)
- **Browser Impact**: Negligible client-side processing

## Configuration

### Application Insights Integration
```json
{
  "ConnectionStrings": {
    "ApplicationInsights": "your-app-insights-connection-string"
  }
}
```

### Orleans Configuration
- Dashboard enabled by default
- Port configured via `Orleans:DashboardPort`
- Metrics collection always enabled
- Retention period: 24 hours

### Alert Configuration
Located in `monitoring-alerts.json`:
- Customizable alert thresholds
- Multiple notification channels
- Environment-specific settings

## Troubleshooting

### Dashboard Not Loading
1. Check Orleans host is running
2. Verify port configuration
3. Check firewall settings
4. Review application logs

### Missing Metrics
1. Verify metrics collector registration
2. Check grain instrumentation
3. Review retention settings
4. Validate API endpoints

### Alert Issues
1. Check Application Insights connection
2. Verify alert rule configuration
3. Review notification channel setup
4. Check threshold settings

## Operations Runbook

### Daily Monitoring Tasks
1. Check dashboard for system health
2. Review alert notifications
3. Monitor memory usage trends
4. Validate grain performance metrics

### Weekly Tasks
1. Review historical performance trends
2. Adjust alert thresholds if needed
3. Archive old metrics if required
4. Update documentation

### Emergency Procedures
1. Silo down: Check host status, restart if needed
2. High memory: Review grain states, restart if needed
3. Performance degradation: Check operation duration trends
4. Alert storms: Review threshold configurations

## Future Enhancements

### Planned Improvements
- Authentication for dashboard access
- Historical metrics storage
- Custom metric dashboards
- Integration with external monitoring systems
- Mobile-responsive dashboard interface

### Integration Opportunities
- Prometheus metrics export
- Grafana dashboard templates
- Azure Monitor integration
- Custom alert webhooks