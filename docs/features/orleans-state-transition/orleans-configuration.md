# Orleans Configuration Reference

## Overview

This document provides a comprehensive reference for configuring Orleans in the AI Chat application. Orleans is the backbone of the distributed state management system, and proper configuration is essential for optimal performance, scalability, and reliability.

## Base Orleans Configuration

### Program.cs Configuration

```csharp
// Orleans Host Configuration (Production)
builder.Services.AddOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering() // Development
        .ConfigureApplicationParts(parts =>
        {
            parts.AddApplicationPart(typeof(ChatGrain).Assembly)
                 .AddApplicationPart(typeof(UserGrain).Assembly)
                 .AddApplicationPart(typeof(ModeGrain).Assembly)
                 .WithReferences();
        })
        .AddMemoryGrainStorage("Default")
        .UseSqliteGrainStorage("sqliteStorage", options =>
        {
            options.ConnectionString = connectionString;
        })
        .ConfigureServices(services =>
        {
            services.AddSingleton<IOrleansMetricsCollector, OrleansMetricsCollector>();
            services.AddScoped<IChatTelemetry, ChatTelemetry>();
        });
});

// Orleans Client Configuration
builder.Services.AddOrleansClient(clientBuilder =>
{
    clientBuilder.UseLocalhostClustering();
});
```

## Environment-Specific Configuration

### Development Environment

```json
{
  "Orleans": {
    "ClusterConfiguration": {
      "ClusterId": "aichat-dev",
      "ServiceId": "AIChat",
      "AdvertisedIP": "127.0.0.1",
      "SiloPort": 11111,
      "GatewayPort": 30000
    },
    "Storage": {
      "DefaultProvider": "memory",
      "SqliteConnectionString": "Data Source=orleans-dev.db",
      "EnableCompression": false
    },
    "Grain": {
      "DefaultActivationTimeout": "00:02:00",
      "DefaultDeactivationTimeout": "00:02:00",
      "CollectionInterval": "00:01:00"
    }
  }
}
```

### Production Environment

```json
{
  "Orleans": {
    "ClusterConfiguration": {
      "ClusterId": "aichat-prod",
      "ServiceId": "AIChat",
      "Clustering": {
        "Provider": "SqlServer",
        "ConnectionString": "Server=prod-cluster;Database=OrleansCluster;Trusted_Connection=true;"
      }
    },
    "Storage": {
      "DefaultProvider": "sqliteStorage",
      "SqliteConnectionString": "Data Source=orleans-prod.db;Cache=Shared;",
      "EnableCompression": true,
      "CompressionLevel": "Optimal"
    },
    "Grain": {
      "DefaultActivationTimeout": "00:10:00",
      "DefaultDeactivationTimeout": "00:30:00",
      "CollectionInterval": "00:05:00",
      "MaxWarmedUpActivations": 1000,
      "MaxIdleActivations": 500
    },
    "Monitoring": {
      "EnableTelemetry": true,
      "MetricsPrefix": "aichat_orleans",
      "CollectPerformanceCounters": true
    }
  }
}
```

## Grain-Specific Configuration

### ChatGrain Configuration

```json
{
  "Orleans": {
    "Grains": {
      "ChatGrain": {
        "Placement": "ActivationCountBased",
        "StateTimeout": "00:15:00",
        "MaxConcurrency": 100,
        "BufferConfiguration": {
          "MessageBufferSize": 1000,
          "SequenceGapTimeout": "00:00:30",
          "OutOfOrderQueueLimit": 50
        },
        "LLMIntegration": {
          "HttpTimeoutSeconds": 60,
          "MaxRetries": 3,
          "CacheResponsesInState": true
        }
      }
    }
  }
}
```

### UserGrain Configuration

```json
{
  "Orleans": {
    "Grains": {
      "UserGrain": {
        "Placement": "HashBased",
        "StateTimeout": "01:00:00",
        "SessionConfiguration": {
          "MaxSessions": 10,
          "SessionTimeoutMinutes": 30,
          "TrackActivity": true
        },
        "Privacy": {
          "EnablePIIDetection": true,
          "AnonymizeData": true,
          "DataRetentionDays": 90
        }
      }
    }
  }
}
```

### ModeGrain Configuration

```json
{
  "Orleans": {
    "Grains": {
      "ModeGrain": {
        "Placement": "ActivationCountBased",
        "StateTimeout": "02:00:00",
        "CacheConfiguration": {
          "ConfigurationCacheTTL": "00:00:30",
          "PromptCacheTTL": "00:15:00",
          "ValidationCacheTTL": "01:00:00",
          "MaxCacheEntries": 10000
        },
        "Templates": {
          "MaxParameterCount": 50,
          "MaxTemplateSize": 8192,
          "EnableValidation": true
        }
      }
    }
  }
}
```

## Persistence Configuration

### SQLite Storage Provider

```csharp
// In Program.cs
siloBuilder.UseSqliteGrainStorage("sqliteStorage", options =>
{
    options.ConnectionString = "Data Source=orleans.db;Cache=Shared;";
    options.UseJsonFormat = true; // Better debugging
    options.GrainStorageSerializer = new JsonGrainStorageSerializer();
});

siloBuilder.UseSqliteReminders("sqliteReminders", options =>
{
    options.ConnectionString = "Data Source=orleans-reminders.db;Cache=Shared;";
});
```

### Event Store Configuration

```json
{
  "Orleans": {
    "EventStore": {
      "Provider": "SQLite",
      "ConnectionString": "Data Source=orleans-events.db;Cache=Shared;",
      "RetentionPolicy": {
        "MaxEvents": 1000000,
        "MaxAgeDays": 365
      },
      "Compression": {
        "Enabled": true,
        "MinimumEventSize": 1024,
        "CompressionLevel": "Optimal"
      }
    },
    "Snapshots": {
      "Enabled": true,
      "Frequency": 100,
      "CompressionEnabled": true,
      "MaxSnapshots": 10000
    }
  }
}
```

## Performance Tuning Configuration

### Grain Placement Configuration

```csharp
// Grain placement attributes in grain classes
[ActivationCountBasedPlacement] // For ChatGrain and ModeGrain
public class ChatGrain : Grain<ChatGrainState>, IChatGrain
{
    // Implementation
}

[HashBasedPlacement] // For UserGrain (session stickiness)
public class UserGrain : Grain<UserGrainState>, IUserGrain
{
    // Implementation
}
```

### Silo Configuration for Performance

```json
{
  "Orleans": {
    "Silo": {
      "LoadSheddingEnabled": true,
      "LoadSheddingLimit": 95,
      "MessagingConfiguration": {
        "ResponseTimeout": "00:00:30",
        "MaxResendCount": 3,
        "ResendOnTimeout": true,
        "DropExpiredMessages": true
      },
      "Threading": {
        "DefaultMaxActiveThreads": 0, // Use system default
        "IOQueueLimitInKB": 16384,
        "WorkItemQueueLimitInKB": 4096
      }
    }
  }
}
```

## Monitoring and Metrics Configuration

### Prometheus Integration

```csharp
// In Program.cs - Prometheus metrics
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<PrometheusAspNetCoreOptions>(options =>
{
    options.MapPath = "/metrics";
});

// Add Orleans metrics collection
builder.Services.AddSingleton<IOrleansMetricsCollector>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<OrleansMetricsCollector>>();
    return new OrleansMetricsCollector(logger, "aichat_orleans");
});

// Add Prometheus middleware
app.UseHttpMetrics(); // Must be before MapControllers
```

### Metrics Configuration

```json
{
  "Orleans": {
    "Metrics": {
      "Prometheus": {
        "Enabled": true,
        "Endpoint": "/metrics",
        "GrainMetrics": {
          "TrackActivations": true,
          "TrackDeactivations": true,
          "TrackMethodCalls": true,
          "TrackStateSize": true,
          "TrackExceptions": true
        },
        "Labels": {
          "Environment": "production",
          "Service": "aichat-orleans",
          "Version": "1.0"
        }
      }
    }
  }
}
```

## Security Configuration

### Authentication Integration

```csharp
// Orleans authentication configuration
siloBuilder.ConfigureServices(services =>
{
    services.AddAuthentication("Bearer")
        .AddJwtBearer("Bearer", options =>
        {
            options.Authority = Configuration["Auth:Authority"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false
            };
        });
});
```

### Network Security

```json
{
  "Orleans": {
    "Security": {
      "TLS": {
        "Enabled": true,
        "CertificatePath": "/certs/orleans.pfx",
        "CertificatePassword": "encrypted_password"
      },
      "Authentication": {
        "Enabled": true,
        "Provider": "JWT",
        "Authority": "https://auth.example.com"
      }
    }
  }
}
```

## Debugging and Development Configuration

### Orleans Dashboard

```csharp
// Add Orleans Dashboard for development
siloBuilder.UseDashboard(options =>
{
    options.HostSelf = true;
    options.Port = 8080;
    options.ScriptPath = "/dashboard/index.js";
});
```

### Logging Configuration

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Orleans": "Warning",
      "Orleans.Runtime.GrainDirectory": "Debug",
      "Orleans.Runtime.Catalog": "Debug",
      "AIChat.Orleans.Grains": "Information"
    },
    "Console": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-dd HH:mm:ss.fff "
    }
  }
}
```

## Error Handling Configuration

### Grain Error Policies

```csharp
// Configure grain error handling
siloBuilder.ConfigureServices(services =>
{
    services.Configure<GrainOptions>(options =>
    {
        options.CollectionAgeLimit = TimeSpan.FromMinutes(5);
        options.DeactivationTimeout = TimeSpan.FromMinutes(2);
        options.MaxWarmedUpActivations = 1000;
    });
});
```

### Circuit Breaker Configuration

```json
{
  "Orleans": {
    "CircuitBreaker": {
      "ChatGrain": {
        "FailureThreshold": 5,
        "RecoveryTimeout": "00:01:00",
        "MinimumThroughput": 10
      },
      "LLMIntegration": {
        "FailureThreshold": 3,
        "RecoveryTimeout": "00:00:30",
        "MinimumThroughput": 5
      }
    }
  }
}
```

## Health Check Configuration

### Orleans Health Checks

```csharp
// Add Orleans health checks
builder.Services.AddHealthChecks()
    .AddCheck("orleans-cluster", () =>
    {
        var client = serviceProvider.GetService<IClusterClient>();
        return client?.IsInitialized == true
            ? HealthCheckResult.Healthy("Orleans cluster is running")
            : HealthCheckResult.Unhealthy("Orleans cluster is not available");
    })
    .AddCheck("orleans-grains", async () =>
    {
        try
        {
            var healthGrain = client.GetGrain<IHealthCheckGrain>(Guid.NewGuid());
            await healthGrain.CheckHealthAsync();
            return HealthCheckResult.Healthy("Grains are responsive");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Grain health check failed: {ex.Message}");
        }
    });
```

## Configuration Validation

### Startup Validation

```csharp
// Validate Orleans configuration on startup
public class OrleansConfigurationValidator : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Validate clustering configuration
        if (string.IsNullOrEmpty(Configuration["Orleans:ClusterConfiguration:ClusterId"]))
        {
            throw new InvalidOperationException("Orleans ClusterId must be configured");
        }

        // Validate storage configuration
        var storageConfig = Configuration.GetSection("Orleans:Storage");
        if (!storageConfig.Exists())
        {
            throw new InvalidOperationException("Orleans storage configuration is required");
        }

        // Validate grain placement configuration
        await ValidateGrainPlacement(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

## Best Practices

### Configuration Management

1. **Environment Separation**: Use separate configuration files for each environment
2. **Secret Management**: Store sensitive configuration in Azure Key Vault or similar
3. **Validation**: Validate configuration on startup
4. **Monitoring**: Monitor configuration changes and their impact

### Performance Optimization

1. **Grain Placement**: Choose appropriate placement strategies for each grain type
2. **State Management**: Configure appropriate timeouts for grain state persistence
3. **Caching**: Configure multi-layer caching appropriately
4. **Monitoring**: Use Prometheus metrics to identify performance bottlenecks

### Security

1. **TLS**: Always use TLS in production deployments
2. **Authentication**: Integrate with your authentication system
3. **Network**: Secure Orleans clustering communication
4. **Secrets**: Never store secrets in configuration files

This configuration reference provides the foundation for properly configuring Orleans in all environments while maintaining performance, security, and reliability.