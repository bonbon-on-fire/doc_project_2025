# Orleans Deployment Guide

## Overview

This guide covers deployment procedures for the Orleans-based AIChat application, including clustering configuration, persistence setup, monitoring integration, and production deployment considerations.

## Prerequisites

### System Requirements

| Component | Requirement | Notes |
|-----------|-------------|-------|
| .NET Runtime | 9.0 or later | Required for Orleans 9.x |
| Database | SQLite (dev) / SQL Server (prod) | For persistence and clustering |
| Memory | 4GB+ per silo | Grain state and caching |
| Network | Low latency between silos | Inter-silo communication |

### Dependencies

- **Orleans**: 9.0 or later
- **prometheus-net**: For metrics collection
- **Entity Framework Core**: For database operations
- **SignalR**: For real-time communication

## Development Environment Setup

### 1. Basic Orleans Configuration

```csharp
// Program.cs - Development configuration
var builder = Host.CreateDefaultBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering() // Local development only
            .AddSqliteGrainStorage("Default", options =>
            {
                options.ConnectionString = "Data Source=orleans.db";
            })
            .UseDashboard(options =>
            {
                options.Port = 8080; // Orleans dashboard
            })
            .ConfigureLogging(logging => logging.AddConsole());
    });

await builder.Build().RunAsync();
```

### 2. Grain Storage Configuration

```csharp
// SQLite configuration for development
siloBuilder.AddSqliteGrainStorage("ChatStorage", options =>
{
    options.ConnectionString = "Data Source=chat_grains.db";
});

siloBuilder.AddSqliteGrainStorage("UserStorage", options =>
{
    options.ConnectionString = "Data Source=user_grains.db";
});

siloBuilder.AddSqliteGrainStorage("ModeStorage", options =>
{
    options.ConnectionString = "Data Source=mode_grains.db";
});
```

### 3. Enable Monitoring

```csharp
// Add Prometheus metrics
siloBuilder.ConfigureServices(services =>
{
    services.AddSingleton<IOrleansMetricsCollector, PrometheusMetricsCollector>();
});

// Enable health checks
siloBuilder.ConfigureServices(services =>
{
    services.AddHealthChecks()
        .AddSqlite("Data Source=orleans.db", name: "orleans-storage");
});
```

## Production Environment Setup

### 1. Clustering Configuration

#### SQL Server Clustering (Recommended)

```csharp
// Production clustering with SQL Server
var builder = Host.CreateDefaultBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        var connectionString = builder.Configuration.GetConnectionString("Orleans");

        siloBuilder
            .UseSqlServerClustering(options =>
            {
                options.ConnectionString = connectionString;
            })
            .ConfigureClustering(options =>
            {
                options.ClusterId = "aichat-prod";
                options.ServiceId = "aichat";
            });
    });
```

#### Azure Table Storage Clustering

```csharp
// Alternative: Azure Table Storage
siloBuilder
    .UseAzureStorageClustering(options =>
    {
        options.TableName = "OrleansCluster";
        options.ConnectionString = azureStorageConnectionString;
    })
    .ConfigureClustering(options =>
    {
        options.ClusterId = "aichat-prod";
        options.ServiceId = "aichat";
    });
```

### 2. Production Storage Configuration

```csharp
// SQL Server grain storage for production
siloBuilder
    .AddSqlServerGrainStorage("ChatStorage", options =>
    {
        options.ConnectionString = chatStorageConnectionString;
        options.SchemaName = "ChatGrains";
    })
    .AddSqlServerGrainStorage("UserStorage", options =>
    {
        options.ConnectionString = userStorageConnectionString;
        options.SchemaName = "UserGrains";
    })
    .AddSqlServerGrainStorage("ModeStorage", options =>
    {
        options.ConnectionString = modeStorageConnectionString;
        options.SchemaName = "ModeGrains";
    });
```

### 3. Performance Configuration

```csharp
siloBuilder.Configure<SiloOptions>(options =>
{
    options.SiloName = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];
})
.Configure<ClusterOptions>(options =>
{
    options.ClusterId = "aichat-prod";
    options.ServiceId = "aichat";
})
.Configure<GrainCollectionOptions>(options =>
{
    options.CollectionAge = TimeSpan.FromMinutes(10); // Grain deactivation time
    options.DeactivationTimeout = TimeSpan.FromMinutes(1);
})
.Configure<MessagingOptions>(options =>
{
    options.ResponseTimeout = TimeSpan.FromSeconds(30);
    options.MaxResendCount = 10;
});
```

## Environment-Specific Configuration

### Development (appsettings.Development.json)

```json
{
  "Orleans": {
    "ClusterMode": "Localhost",
    "StorageProvider": "SQLite",
    "ConnectionStrings": {
      "Default": "Data Source=orleans_dev.db"
    },
    "Dashboard": {
      "Enabled": true,
      "Port": 8080
    },
    "Monitoring": {
      "Prometheus": {
        "Enabled": true,
        "Port": 9090
      }
    }
  },
  "FeatureFlags": {
    "OrleansEnabled": true,
    "MonitoringEnabled": true
  }
}
```

### Staging (appsettings.Staging.json)

```json
{
  "Orleans": {
    "ClusterMode": "SqlServer",
    "ConnectionStrings": {
      "Clustering": "Server=staging-db;Database=OrleansCluster;Integrated Security=true;",
      "GrainStorage": "Server=staging-db;Database=OrleansStorage;Integrated Security=true;"
    },
    "Dashboard": {
      "Enabled": true,
      "Port": 8080
    },
    "Monitoring": {
      "Prometheus": {
        "Enabled": true,
        "Port": 9090
      },
      "Grafana": {
        "Enabled": true,
        "DashboardsPath": "./monitoring/dashboards"
      }
    }
  },
  "FeatureFlags": {
    "OrleansEnabled": true,
    "MonitoringEnabled": true,
    "AdvancedRecovery": true
  }
}
```

### Production (appsettings.Production.json)

```json
{
  "Orleans": {
    "ClusterMode": "SqlServer",
    "ConnectionStrings": {
      "Clustering": "Server=prod-cluster-db;Database=OrleansCluster;Integrated Security=true;MultipleActiveResultSets=true;",
      "ChatStorage": "Server=prod-chat-db;Database=ChatGrains;Integrated Security=true;",
      "UserStorage": "Server=prod-user-db;Database=UserGrains;Integrated Security=true;",
      "ModeStorage": "Server=prod-mode-db;Database=ModeGrains;Integrated Security=true;"
    },
    "Dashboard": {
      "Enabled": false
    },
    "Monitoring": {
      "Prometheus": {
        "Enabled": true,
        "Port": 9090,
        "MetricsPath": "/metrics"
      },
      "Grafana": {
        "Enabled": true,
        "AlertingRules": "./monitoring/alerts.yml"
      }
    },
    "Performance": {
      "GrainCollectionAge": "00:10:00",
      "DeactivationTimeout": "00:01:00",
      "ResponseTimeout": "00:00:30"
    }
  },
  "FeatureFlags": {
    "OrleansEnabled": true,
    "MonitoringEnabled": true,
    "AdvancedRecovery": true,
    "PerformanceOptimization": true
  }
}
```

## Database Schema Setup

### 1. Orleans Clustering Tables

```sql
-- SQL Server clustering table creation
-- Run this script to set up Orleans clustering

-- Create Orleans clustering database
CREATE DATABASE OrleansCluster;
GO

USE OrleansCluster;
GO

-- Install Orleans clustering tables
-- (Use Orleans SQL scripts from NuGet package)
-- Microsoft.Orleans.Clustering.SqlServer provides these scripts
```

### 2. Grain Storage Tables

```sql
-- Create grain storage database
CREATE DATABASE OrleansStorage;
GO

USE OrleansStorage;
GO

-- Create storage tables for each grain type
CREATE SCHEMA ChatGrains;
CREATE SCHEMA UserGrains;
CREATE SCHEMA ModeGrains;
GO

-- Orleans will auto-create grain storage tables
-- when configured with proper connection strings
```

### 3. Application Database Integration

```csharp
// Ensure Orleans and application databases are coordinated
public class DatabaseSetupService
{
    public async Task SetupDatabasesAsync()
    {
        // 1. Verify Orleans clustering tables exist
        await VerifyOrleansClusteringAsync();

        // 2. Initialize grain storage schemas
        await InitializeGrainStorageAsync();

        // 3. Set up application database with Orleans integration
        await SetupApplicationDatabaseAsync();
    }
}
```

## Monitoring Integration

### 1. Prometheus Configuration

```yaml
# prometheus.yml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'aichat-orleans'
    static_configs:
      - targets: ['localhost:9090']
    scrape_interval: 5s
    metrics_path: /metrics
    scrape_timeout: 10s
```

### 2. Grafana Dashboard Import

```bash
# Import pre-configured Orleans dashboards
curl -X POST \
  http://localhost:3000/api/dashboards/db \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer YOUR_API_KEY' \
  -d @./monitoring/dashboards/orleans-overview.json
```

### 3. Alerting Rules

```yaml
# alerts.yml
groups:
  - name: orleans_alerts
    rules:
      - alert: OrleansGrainActivationFailure
        expr: rate(orleans_grain_activation_failures_total[5m]) > 0.1
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "High Orleans grain activation failure rate"

      - alert: OrleansHighLatency
        expr: histogram_quantile(0.95, orleans_message_processing_duration_seconds) > 0.1
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "Orleans message processing latency too high"
```

## Load Balancer Configuration

### 1. Application Load Balancer (ALB)

```yaml
# AWS ALB configuration for Orleans silos
apiVersion: v1
kind: Service
metadata:
  name: aichat-orleans-service
spec:
  selector:
    app: aichat-orleans
  ports:
    - name: http
      port: 80
      targetPort: 5000
    - name: orleans-gateway
      port: 30000
      targetPort: 30000
  type: LoadBalancer
```

### 2. Health Check Endpoints

```csharp
// Configure health checks for load balancer
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/orleans", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("orleans")
});
```

## Deployment Scripts

### 1. PowerShell Deployment Script

```powershell
# Deploy-Orleans.ps1
param(
    [Parameter(Mandatory=$true)]
    [string]$Environment,

    [Parameter(Mandatory=$true)]
    [string]$Version
)

Write-Host "Deploying AIChat Orleans v$Version to $Environment"

# 1. Build application
dotnet publish -c Release -o ./publish

# 2. Stop existing services
Stop-Service -Name "AIChat.Orleans.Host" -ErrorAction SilentlyContinue

# 3. Deploy new version
Copy-Item -Path "./publish/*" -Destination "C:\AIChat\Orleans\" -Recurse -Force

# 4. Update configuration
Copy-Item -Path "./configs/appsettings.$Environment.json" -Destination "C:\AIChat\Orleans\appsettings.json" -Force

# 5. Start services
Start-Service -Name "AIChat.Orleans.Host"

# 6. Verify deployment
Start-Sleep -Seconds 30
$healthCheck = Invoke-RestMethod -Uri "http://localhost:5000/health"
if ($healthCheck.status -eq "Healthy") {
    Write-Host "Deployment successful" -ForegroundColor Green
} else {
    Write-Host "Deployment failed - health check failed" -ForegroundColor Red
    exit 1
}
```

### 2. Docker Deployment

```dockerfile
# Dockerfile for Orleans silo
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 30000
EXPOSE 9090

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["server/AIChat.Orleans.Host/AIChat.Orleans.Host.csproj", "server/AIChat.Orleans.Host/"]
COPY ["server/AIChat.Orleans/AIChat.Orleans.csproj", "server/AIChat.Orleans/"]
RUN dotnet restore "server/AIChat.Orleans.Host/AIChat.Orleans.Host.csproj"

COPY . .
WORKDIR "/src/server/AIChat.Orleans.Host"
RUN dotnet build "AIChat.Orleans.Host.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AIChat.Orleans.Host.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AIChat.Orleans.Host.dll"]
```

```yaml
# docker-compose.yml
version: '3.8'
services:
  orleans-silo-1:
    build: .
    ports:
      - "5000:80"
      - "30000:30000"
      - "9090:9090"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Orleans__SiloName=Silo1
    depends_on:
      - sql-server

  orleans-silo-2:
    build: .
    ports:
      - "5001:80"
      - "30001:30000"
      - "9091:9090"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Orleans__SiloName=Silo2
    depends_on:
      - sql-server

  sql-server:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong!Passw0rd
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql

volumes:
  sqldata:
```

## Troubleshooting

### Common Issues

#### 1. Silo Won't Start

```bash
# Check Orleans clustering table
SELECT * FROM OrleansCluster.dbo.OrleansMembershipTable;

# Verify connection strings
dotnet user-secrets list

# Check database connectivity
sqlcmd -S server -d database -Q "SELECT 1"
```

#### 2. Grain Activation Failures

```csharp
// Enable detailed logging
builder.ConfigureLogging(logging =>
{
    logging.SetMinimumLevel(LogLevel.Debug);
    logging.AddFilter("Orleans", LogLevel.Debug);
});
```

#### 3. Performance Issues

```bash
# Monitor grain metrics
curl http://localhost:9090/metrics | grep orleans_

# Check memory usage
dotnet-counters monitor --process-id <pid> System.Runtime

# Analyze garbage collection
dotnet-dump collect --process-id <pid>
```

### Health Check Validation

```bash
# Test health endpoints
curl http://localhost:5000/health
curl http://localhost:5000/health/orleans
curl http://localhost:9090/metrics

# Verify Orleans dashboard
curl http://localhost:8080/
```

## Security Considerations

### 1. Network Security

- Use TLS for inter-silo communication in production
- Restrict Orleans port access (30000) to cluster nodes only
- Secure database connections with encryption

### 2. Authentication and Authorization

```csharp
// Configure Orleans authorization
siloBuilder.ConfigureServices(services =>
{
    services.AddAuthentication("Bearer")
        .AddJwtBearer("Bearer", options =>
        {
            options.Authority = "https://your-identity-server";
        });

    services.AddAuthorization();
});
```

### 3. Secret Management

```bash
# Use managed secrets for production
dotnet user-secrets set "Orleans:ConnectionStrings:Clustering" "your-connection-string"

# Or use Azure Key Vault
az keyvault secret set --vault-name "your-keyvault" --name "orleans-clustering" --value "your-connection-string"
```

## Backup and Recovery

### 1. Database Backup

```sql
-- Backup Orleans databases
BACKUP DATABASE OrleansCluster TO DISK = 'C:\Backups\OrleansCluster.bak'
BACKUP DATABASE OrleansStorage TO DISK = 'C:\Backups\OrleansStorage.bak'
```

### 2. Event Store Backup

```bash
# Backup event store data
sqlite3 event_store.db ".backup backup_event_store.db"

# Restore from backup
sqlite3 event_store.db ".restore backup_event_store.db"
```

### 3. Point-in-Time Recovery

```csharp
// Use built-in recovery services
var recoveryService = serviceProvider.GetService<IPointInTimeRecoveryService>();
await recoveryService.RecoverToTimestampAsync(chatId, DateTime.UtcNow.AddHours(-1));
```

---

**Document Version**: 1.0
**Last Updated**: September 2024
**Next Review**: With each major deployment