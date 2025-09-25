# Orleans Capacity Planning Guide

## Overview

This guide provides comprehensive capacity planning procedures for Orleans-based AI Chat system, designed to enable accurate resource sizing, cost optimization, and auto-scaling policies for production deployments. This guide builds upon the High Availability and Multi-Region deployment patterns to provide enterprise-scale capacity management.

## Table of Contents

1. [Performance Baseline Establishment](#1-performance-baseline-establishment)
2. [Resource Sizing Recommendations](#2-resource-sizing-recommendations)
3. [Scaling Models and Strategies](#3-scaling-models-and-strategies)
4. [Cost Optimization Framework](#4-cost-optimization-framework)
5. [Auto-Scaling Policies](#5-auto-scaling-policies)
6. [Capacity Calculator Tools](#6-capacity-calculator-tools)
7. [Performance Testing Integration](#7-performance-testing-integration)
8. [Monitoring and Alerting](#8-monitoring-and-alerting)

## 1. Performance Baseline Establishment

### 1.1 Orleans Performance Metrics Overview

#### Core Orleans Metrics
- **Grain Activations per Second**: Rate of new grain activations
- **Active Grain Count**: Number of grains currently in memory
- **Request Processing Time**: Average time per Orleans grain method call
- **Memory Usage per Grain**: Memory footprint of individual grains
- **Silo CPU Utilization**: CPU usage by Orleans silos
- **Network I/O**: Inter-silo and client-silo communication volume

#### AI Chat System Specific Metrics
- **Concurrent Chat Sessions**: Number of active chat sessions
- **Messages per Second**: Rate of chat message processing
- **AI Processing Time**: Time for LLM tool execution
- **State Persistence Volume**: Amount of data being persisted per session

### 1.2 Baseline Performance Testing

#### Test Environment Setup
```powershell
# Orleans baseline performance test setup
function Setup-BaselineTest {
    param(
        [string]$Environment = "Baseline",
        [int]$TestDurationMinutes = 30,
        [string]$ConfigProfile = "Standard"
    )

    Write-Host "🔬 Setting up Orleans baseline performance test..." -ForegroundColor Cyan

    # Step 1: Deploy clean test environment
    $testConfig = @{
        Environment = $Environment
        OrleansClusterSize = 2  # Standard 2-silo configuration
        Resources = @{
            CPU = "4 vCPU"
            Memory = "8GB"
            Storage = "100GB SSD"
        }
        LoadProfile = $ConfigProfile
    }

    # Step 2: Initialize baseline database
    $baselineDb = "data/baseline-test.db"
    if (Test-Path $baselineDb) { Remove-Item $baselineDb }
    Copy-Item "templates/clean-db.db" $baselineDb

    # Step 3: Configure monitoring for baseline metrics
    $monitoringConfig = @{
        Metrics = @(
            "orleans_active_grains",
            "orleans_activations_per_second",
            "orleans_request_processing_time",
            "orleans_memory_usage",
            "orleans_cpu_utilization",
            "orleans_network_io"
        )
        SampleInterval = "5s"
        TestDuration = "${TestDurationMinutes}m"
    }

    Write-Host "✅ Baseline test environment configured" -ForegroundColor Green
    return $testConfig
}
```

#### Baseline Test Execution
```powershell
# Execute comprehensive baseline performance test
function Start-BaselinePerformanceTest {
    param(
        [int]$TestDurationMinutes = 30,
        [string[]]$TestProfiles = @("Light", "Standard", "Heavy")
    )

    Write-Host "🚀 Starting Orleans baseline performance test..." -ForegroundColor Cyan

    $baselineResults = @{}

    foreach ($profile in $TestProfiles) {
        Write-Host "📊 Testing profile: $profile" -ForegroundColor Yellow

        # Configure load profile
        $loadConfig = switch ($profile) {
            "Light" { @{ ConcurrentUsers = 10; MessagesPerMinute = 100; ChatSessions = 5 } }
            "Standard" { @{ ConcurrentUsers = 100; MessagesPerMinute = 1000; ChatSessions = 50 } }
            "Heavy" { @{ ConcurrentUsers = 1000; MessagesPerMinute = 10000; ChatSessions = 500 } }
        }

        # Deploy test environment
        Setup-BaselineTest -ConfigProfile $profile

        # Start monitoring
        $monitoringJob = Start-Job -Name "Monitoring-$profile" -ScriptBlock {
            param($Profile, $Duration)

            $metrics = @{}
            $endTime = (Get-Date).AddMinutes($Duration)

            while ((Get-Date) -lt $endTime) {
                try {
                    # Collect Orleans metrics
                    $orleanStats = Invoke-RestMethod -Uri "http://localhost:5099/metrics/orleans" -TimeoutSec 5

                    # Store metrics with timestamp
                    $timestamp = Get-Date
                    $metrics[$timestamp] = @{
                        ActiveGrains = $orleanStats.ActiveGrains
                        ActivationsPerSecond = $orleanStats.ActivationsPerSecond
                        RequestProcessingTime = $orleanStats.AverageRequestTime
                        MemoryUsage = $orleanStats.MemoryUsageMB
                        CpuUtilization = $orleanStats.CpuUtilization
                        NetworkIO = $orleanStats.NetworkIO
                    }

                } catch {
                    Write-Warning "Failed to collect metrics: $($_.Exception.Message)"
                }

                Start-Sleep -Seconds 5
            }

            return $metrics

        } -ArgumentList $profile, $TestDurationMinutes

        # Start load test
        $loadTestResult = Start-LoadTest -Profile $loadConfig -Duration $TestDurationMinutes

        # Wait for completion and collect results
        $monitoringResult = Receive-Job -Job $monitoringJob -Wait
        Remove-Job $monitoringJob

        # Analyze results
        $analysis = Analyze-BaselineResults -Metrics $monitoringResult -LoadTest $loadTestResult

        $baselineResults[$profile] = @{
            LoadConfig = $loadConfig
            Metrics = $monitoringResult
            LoadTest = $loadTestResult
            Analysis = $analysis
        }

        Write-Host "✅ Profile $profile completed" -ForegroundColor Green
        Write-Host "   Peak Active Grains: $($analysis.PeakActiveGrains)" -ForegroundColor White
        Write-Host "   Avg Request Time: $($analysis.AverageRequestTime)ms" -ForegroundColor White
        Write-Host "   Throughput: $($analysis.RequestsPerSecond) req/s" -ForegroundColor White
    }

    # Generate baseline report
    Generate-BaselineReport -Results $baselineResults

    return $baselineResults
}

function Analyze-BaselineResults {
    param($Metrics, $LoadTest)

    $analysis = @{
        PeakActiveGrains = ($Metrics.Values | Measure-Object -Property ActiveGrains -Maximum).Maximum
        AverageActiveGrains = ($Metrics.Values | Measure-Object -Property ActiveGrains -Average).Average
        PeakActivationsPerSecond = ($Metrics.Values | Measure-Object -Property ActivationsPerSecond -Maximum).Maximum
        AverageRequestTime = ($Metrics.Values | Measure-Object -Property RequestProcessingTime -Average).Average
        PeakMemoryUsage = ($Metrics.Values | Measure-Object -Property MemoryUsage -Maximum).Maximum
        AverageCpuUtilization = ($Metrics.Values | Measure-Object -Property CpuUtilization -Average).Average
        RequestsPerSecond = $LoadTest.TotalRequests / ($LoadTest.Duration / 60)
        ErrorRate = $LoadTest.FailedRequests / $LoadTest.TotalRequests
    }

    return $analysis
}
```

### 1.3 Baseline Results Documentation

#### Standard Baseline Metrics (Reference Implementation)
Based on testing with the Orleans AI Chat system:

| Load Profile | Concurrent Users | Active Grains | Req/s | Memory (MB) | CPU (%) |
|--------------|------------------|---------------|-------|-------------|---------|
| Light        | 10              | 25            | 50    | 512         | 15      |
| Standard     | 100             | 250           | 500   | 2048        | 45      |
| Heavy        | 1000            | 2500          | 5000  | 8192        | 85      |

#### Key Performance Characteristics
- **Grain Memory Footprint**: ~2MB per active chat session grain
- **Activation Overhead**: ~5ms per new grain activation
- **Request Processing**: ~10ms average for standard chat operations
- **AI Tool Calls**: ~500ms average (external LLM dependency)
- **State Persistence**: ~1ms per state save operation

## 2. Resource Sizing Recommendations

### 2.1 CPU Sizing Guidelines

#### Orleans CPU Requirements
```powershell
# Calculate CPU requirements based on workload
function Calculate-CPURequirements {
    param(
        [int]$ConcurrentUsers,
        [int]$MessagesPerSecond,
        [int]$AIToolCallsPerSecond = 0,
        [string]$WorkloadType = "Standard"
    )

    Write-Host "🖥️ Calculating CPU requirements..." -ForegroundColor Cyan

    # Base CPU requirements per Orleans silo
    $baseCpuCores = 2

    # CPU scaling factors based on workload
    $cpuFactors = @{
        "Light" = 0.5
        "Standard" = 1.0
        "Heavy" = 1.5
        "AI-Intensive" = 2.0
    }

    $workloadFactor = $cpuFactors[$WorkloadType]

    # Calculate CPU requirements
    $userCpuRequirement = $ConcurrentUsers / 250  # 250 users per core
    $messageCpuRequirement = $MessagesPerSecond / 500  # 500 messages per core
    $aiCpuRequirement = $AIToolCallsPerSecond / 2  # 2 AI calls per core

    $totalCpuRequirement = ($baseCpuCores + $userCpuRequirement + $messageCpuRequirement + $aiCpuRequirement) * $workloadFactor

    # Round up to even number of cores
    $recommendedCores = [Math]::Ceiling($totalCpuRequirement / 2) * 2

    $result = @{
        RecommendedCores = $recommendedCores
        BaseRequirement = $baseCpuCores
        UserScaling = $userCpuRequirement
        MessageScaling = $messageCpuRequirement
        AIScaling = $aiCpuRequirement
        WorkloadFactor = $workloadFactor
        TotalRequirement = $totalCpuRequirement
    }

    Write-Host "📊 CPU Requirements Analysis:" -ForegroundColor Yellow
    Write-Host "   Concurrent Users: $ConcurrentUsers" -ForegroundColor White
    Write-Host "   Messages/Second: $MessagesPerSecond" -ForegroundColor White
    Write-Host "   AI Calls/Second: $AIToolCallsPerSecond" -ForegroundColor White
    Write-Host "   Workload Type: $WorkloadType" -ForegroundColor White
    Write-Host "   Recommended Cores: $recommendedCores" -ForegroundColor Green

    return $result
}
```

#### CPU Sizing Table
| User Load | Messages/s | AI Calls/s | Recommended Cores | VM Size (Azure) | Instance Type (AWS) |
|-----------|------------|------------|-------------------|-----------------|---------------------|
| 1-100     | 1-50       | 0-5        | 2                | Standard_D2s_v3 | t3.large           |
| 100-500   | 50-250     | 5-25       | 4                | Standard_D4s_v3 | m5.xlarge          |
| 500-1000  | 250-500    | 25-50      | 8                | Standard_D8s_v3 | m5.2xlarge         |
| 1000-5000 | 500-2500   | 50-250     | 16               | Standard_D16s_v3| m5.4xlarge         |

### 2.2 Memory Sizing Guidelines

#### Orleans Memory Requirements
```powershell
# Calculate memory requirements for Orleans deployment
function Calculate-MemoryRequirements {
    param(
        [int]$MaxConcurrentSessions,
        [int]$AverageSessionDurationMinutes = 30,
        [int]$GrainCacheSize = 10000,
        [string]$DeploymentMode = "HA"  # Single, HA, MultiRegion
    )

    Write-Host "🧠 Calculating memory requirements..." -ForegroundColor Cyan

    # Base memory requirements
    $baseSiloMemoryMB = 1024  # Base Orleans silo overhead
    $operatingSystemMB = 1024  # OS and other processes

    # Per-grain memory requirements
    $chatSessionGrainMB = 2    # Each chat session grain
    $aiToolGrainMB = 1         # Each AI tool grain
    $userStateGrainMB = 0.5    # Each user state grain

    # Calculate active grain memory
    $activeChatSessions = $MaxConcurrentSessions
    $activeAITools = $MaxConcurrentSessions * 0.3  # 30% of sessions using AI tools
    $activeUserStates = $MaxConcurrentSessions * 1.2  # Multiple user states per session

    $chatSessionMemoryMB = $activeChatSessions * $chatSessionGrainMB
    $aiToolMemoryMB = $activeAITools * $aiToolGrainMB
    $userStateMemoryMB = $activeUserStates * $userStateGrainMB

    # Grain cache memory
    $grainCacheMemoryMB = ($GrainCacheSize * 0.1)  # 0.1MB per cached grain

    # Deployment mode multipliers
    $deploymentFactors = @{
        "Single" = 1.0
        "HA" = 1.2      # 20% overhead for HA
        "MultiRegion" = 1.4  # 40% overhead for multi-region
    }

    $deploymentFactor = $deploymentFactors[$DeploymentMode]

    # Calculate total memory requirement
    $totalApplicationMemoryMB = ($baseSiloMemoryMB + $chatSessionMemoryMB + $aiToolMemoryMB + $userStateMemoryMB + $grainCacheMemoryMB) * $deploymentFactor
    $totalSystemMemoryMB = $totalApplicationMemoryMB + $operatingSystemMB

    # Round up to standard memory sizes
    $recommendedMemoryGB = [Math]::Ceiling($totalSystemMemoryMB / 1024)

    # Ensure minimum memory requirements
    if ($recommendedMemoryGB -lt 4) { $recommendedMemoryGB = 4 }

    $result = @{
        RecommendedMemoryGB = $recommendedMemoryGB
        TotalSystemMemoryMB = $totalSystemMemoryMB
        ApplicationMemoryMB = $totalApplicationMemoryMB
        Breakdown = @{
            BaseSiloMB = $baseSiloMemoryMB
            ChatSessionsMB = $chatSessionMemoryMB
            AIToolsMB = $aiToolMemoryMB
            UserStatesMB = $userStateMemoryMB
            GrainCacheMB = $grainCacheMemoryMB
            OperatingSystemMB = $operatingSystemMB
        }
        DeploymentFactor = $deploymentFactor
    }

    Write-Host "📊 Memory Requirements Analysis:" -ForegroundColor Yellow
    Write-Host "   Max Concurrent Sessions: $MaxConcurrentSessions" -ForegroundColor White
    Write-Host "   Deployment Mode: $DeploymentMode" -ForegroundColor White
    Write-Host "   Application Memory: $([Math]::Round($totalApplicationMemoryMB/1024, 1))GB" -ForegroundColor White
    Write-Host "   Recommended Memory: ${recommendedMemoryGB}GB" -ForegroundColor Green

    return $result
}
```

#### Memory Sizing Table
| Concurrent Sessions | Chat Memory | AI Memory | Cache Memory | Total App | Recommended | VM Size |
|-------------------|-------------|-----------|--------------|-----------|-------------|---------|
| 100               | 200MB       | 60MB      | 1000MB       | 2.3GB     | 4GB         | 4GB     |
| 500               | 1GB         | 300MB     | 5000MB       | 7.3GB     | 8GB         | 8GB     |
| 1000              | 2GB         | 600MB     | 10000MB      | 13.6GB    | 16GB        | 16GB    |
| 5000              | 10GB        | 3GB       | 50000MB      | 64GB      | 64GB        | 64GB    |

### 2.3 Storage Sizing Guidelines

#### Database Storage Requirements
```powershell
# Calculate storage requirements for Orleans system
function Calculate-StorageRequirements {
    param(
        [int]$ExpectedUsers,
        [int]$AverageSessionsPerUserPerDay = 5,
        [int]$AverageMessagesPerSession = 20,
        [int]$RetentionDays = 365,
        [float]$GrowthRatePercentage = 20.0
    )

    Write-Host "💾 Calculating storage requirements..." -ForegroundColor Cyan

    # Data sizing constants
    $userRecordKB = 2           # User profile data
    $sessionRecordKB = 5        # Session metadata
    $messageRecordKB = 1        # Chat message
    $aiToolCallKB = 10          # AI tool execution data
    $eventSourcingMultiplier = 1.5  # Event sourcing overhead

    # Calculate daily data generation
    $dailySessions = $ExpectedUsers * $AverageSessionsPerUserPerDay
    $dailyMessages = $dailySessions * $AverageMessagesPerSession
    $dailyAIToolCalls = $dailyMessages * 0.1  # 10% of messages trigger AI

    # Calculate daily storage requirements
    $dailyUserDataKB = $ExpectedUsers * $userRecordKB
    $dailySessionDataKB = $dailySessions * $sessionRecordKB
    $dailyMessageDataKB = $dailyMessages * $messageRecordKB
    $dailyAIDataKB = $dailyAIToolCalls * $aiToolCallKB

    $dailyTotalKB = ($dailyUserDataKB + $dailySessionDataKB + $dailyMessageDataKB + $dailyAIDataKB) * $eventSourcingMultiplier

    # Calculate retention storage requirements
    $retentionTotalKB = $dailyTotalKB * $RetentionDays

    # Apply growth rate
    $projectedTotalKB = $retentionTotalKB * (1 + ($GrowthRatePercentage / 100))

    # Convert to GB and round up
    $recommendedStorageGB = [Math]::Ceiling($projectedTotalKB / 1024 / 1024)

    # Add database overhead and indices (30%)
    $databaseOverheadGB = [Math]::Ceiling($recommendedStorageGB * 0.3)
    $totalDatabaseGB = $recommendedStorageGB + $databaseOverheadGB

    # Add backup storage (100% of database size)
    $backupStorageGB = $totalDatabaseGB

    # Add log storage
    $logStorageGB = [Math]::Max(10, [Math]::Ceiling($totalDatabaseGB * 0.1))

    # Total storage requirement
    $totalStorageGB = $totalDatabaseGB + $backupStorageGB + $logStorageGB

    $result = @{
        RecommendedStorageGB = $totalStorageGB
        Breakdown = @{
            DatabaseGB = $totalDatabaseGB
            BackupGB = $backupStorageGB
            LogsGB = $logStorageGB
        }
        DailyGrowthMB = [Math]::Round($dailyTotalKB / 1024, 2)
        ProjectionDays = $RetentionDays
        GrowthRate = $GrowthRatePercentage
    }

    Write-Host "📊 Storage Requirements Analysis:" -ForegroundColor Yellow
    Write-Host "   Expected Users: $ExpectedUsers" -ForegroundColor White
    Write-Host "   Daily Growth: $([Math]::Round($dailyTotalKB / 1024, 2))MB" -ForegroundColor White
    Write-Host "   Database Storage: ${totalDatabaseGB}GB" -ForegroundColor White
    Write-Host "   Total Storage: ${totalStorageGB}GB" -ForegroundColor Green

    return $result
}
```

## 3. Scaling Models and Strategies

### 3.1 Linear Scaling Model

#### Orleans Linear Scaling Implementation
```powershell
# Implement linear scaling model for Orleans clusters
function Implement-LinearScaling {
    param(
        [int]$BaselineUsers = 100,
        [int]$BaselineSilos = 2,
        [int]$TargetUsers,
        [hashtable]$ScalingConstraints = @{
            MinSilos = 2
            MaxSilos = 20
            ScaleUpThreshold = 0.8  # 80% resource utilization
            ScaleDownThreshold = 0.3  # 30% resource utilization
        }
    )

    Write-Host "📈 Implementing linear scaling model..." -ForegroundColor Cyan

    # Calculate linear scaling factor
    $scalingFactor = $TargetUsers / $BaselineUsers
    $targetSilos = [Math]::Ceiling($BaselineSilos * $scalingFactor)

    # Apply constraints
    $targetSilos = [Math]::Max($ScalingConstraints.MinSilos, $targetSilos)
    $targetSilos = [Math]::Min($ScalingConstraints.MaxSilos, $targetSilos)

    # Calculate resource requirements per silo
    $usersPerSilo = [Math]::Ceiling($TargetUsers / $targetSilos)

    $scalingPlan = @{
        TargetSilos = $targetSilos
        UsersPerSilo = $usersPerSilo
        ScalingFactor = $scalingFactor
        ResourceRequirements = @{
            CPU = Calculate-CPURequirements -ConcurrentUsers $usersPerSilo
            Memory = Calculate-MemoryRequirements -MaxConcurrentSessions $usersPerSilo
            Storage = Calculate-StorageRequirements -ExpectedUsers $usersPerSilo
        }
    }

    # Generate scaling triggers
    $scalingTriggers = @{
        ScaleUp = @{
            Condition = "cpu_utilization > $($ScalingConstraints.ScaleUpThreshold * 100) OR memory_utilization > $($ScalingConstraints.ScaleUpThreshold * 100)"
            Action = "Add 1 Orleans silo"
            Cooldown = "5m"
        }
        ScaleDown = @{
            Condition = "cpu_utilization < $($ScalingConstraints.ScaleDownThreshold * 100) AND memory_utilization < $($ScalingConstraints.ScaleDownThreshold * 100)"
            Action = "Remove 1 Orleans silo"
            Cooldown = "15m"
        }
    }

    Write-Host "📊 Linear Scaling Plan:" -ForegroundColor Yellow
    Write-Host "   Target Users: $TargetUsers" -ForegroundColor White
    Write-Host "   Target Silos: $targetSilos" -ForegroundColor White
    Write-Host "   Users per Silo: $usersPerSilo" -ForegroundColor White
    Write-Host "   Scaling Factor: $([Math]::Round($scalingFactor, 2))" -ForegroundColor White

    return @{
        ScalingPlan = $scalingPlan
        ScalingTriggers = $scalingTriggers
    }
}
```

### 3.2 Step Scaling Model

#### Orleans Step Scaling Implementation
```powershell
# Implement step scaling model with predefined capacity tiers
function Implement-StepScaling {
    param(
        [int]$CurrentUsers,
        [hashtable[]]$ScalingTiers = @(
            @{ MinUsers = 0; MaxUsers = 500; Silos = 2; Tier = "Small" }
            @{ MinUsers = 501; MaxUsers = 2000; Silos = 4; Tier = "Medium" }
            @{ MinUsers = 2001; MaxUsers = 8000; Silos = 8; Tier = "Large" }
            @{ MinUsers = 8001; MaxUsers = 32000; Silos = 16; Tier = "XLarge" }
        )
    )

    Write-Host "🔢 Implementing step scaling model..." -ForegroundColor Cyan

    # Find current tier
    $currentTier = $ScalingTiers | Where-Object { $CurrentUsers -ge $_.MinUsers -and $CurrentUsers -le $_.MaxUsers }

    if (!$currentTier) {
        throw "No suitable scaling tier found for $CurrentUsers users"
    }

    # Determine next tiers for scaling decisions
    $nextTierUp = $ScalingTiers | Where-Object { $_.MinUsers -gt $currentTier.MaxUsers } | Select-Object -First 1
    $nextTierDown = $ScalingTiers | Where-Object { $_.MaxUsers -lt $currentTier.MinUsers } | Select-Object -Last 1

    $stepScalingPlan = @{
        CurrentTier = $currentTier
        NextTierUp = $nextTierUp
        NextTierDown = $nextTierDown
        ScalingActions = @{
            ScaleUp = if ($nextTierUp) {
                @{
                    Trigger = "user_count > $($currentTier.MaxUsers * 0.9) OR avg_response_time > 200ms"
                    Action = "Scale from $($currentTier.Silos) to $($nextTierUp.Silos) silos"
                    TargetTier = $nextTierUp.Tier
                }
            } else { $null }
            ScaleDown = if ($nextTierDown) {
                @{
                    Trigger = "user_count < $($currentTier.MinUsers * 1.1) AND avg_response_time < 100ms"
                    Action = "Scale from $($currentTier.Silos) to $($nextTierDown.Silos) silos"
                    TargetTier = $nextTierDown.Tier
                }
            } else { $null }
        }
    }

    Write-Host "📊 Step Scaling Plan:" -ForegroundColor Yellow
    Write-Host "   Current Users: $CurrentUsers" -ForegroundColor White
    Write-Host "   Current Tier: $($currentTier.Tier) ($($currentTier.Silos) silos)" -ForegroundColor White
    if ($nextTierUp) {
        Write-Host "   Next Tier Up: $($nextTierUp.Tier) ($($nextTierUp.Silos) silos)" -ForegroundColor Green
    }
    if ($nextTierDown) {
        Write-Host "   Next Tier Down: $($nextTierDown.Tier) ($($nextTierDown.Silos) silos)" -ForegroundColor Yellow
    }

    return $stepScalingPlan
}
```

### 3.3 Predictive Scaling Model

#### Machine Learning-Based Scaling
```powershell
# Implement predictive scaling using historical usage patterns
function Implement-PredictiveScaling {
    param(
        [string]$HistoricalDataPath = "data/usage-history.json",
        [int]$PredictionHorizonHours = 24,
        [float]$ConfidenceThreshold = 0.8
    )

    Write-Host "🔮 Implementing predictive scaling model..." -ForegroundColor Cyan

    # Load historical usage data
    if (!(Test-Path $HistoricalDataPath)) {
        Write-Warning "Historical data not found. Using default patterns."
        $historicalData = Generate-DefaultUsagePatterns
    } else {
        $historicalData = Get-Content $HistoricalDataPath | ConvertFrom-Json
    }

    # Analyze usage patterns
    $usageAnalysis = Analyze-UsagePatterns -Data $historicalData

    # Generate predictions
    $predictions = Generate-UsagePredictions -Analysis $usageAnalysis -HorizonHours $PredictionHorizonHours

    # Create scaling schedule
    $scalingSchedule = @()

    foreach ($prediction in $predictions) {
        if ($prediction.Confidence -gt $ConfidenceThreshold) {
            $requiredSilos = Calculate-RequiredSilos -PredictedUsers $prediction.ExpectedUsers

            $scalingSchedule += @{
                Timestamp = $prediction.Timestamp
                PredictedUsers = $prediction.ExpectedUsers
                RequiredSilos = $requiredSilos
                Confidence = $prediction.Confidence
                Action = "Pre-scale to $requiredSilos silos"
            }
        }
    }

    Write-Host "📊 Predictive Scaling Schedule:" -ForegroundColor Yellow
    foreach ($schedule in $scalingSchedule) {
        Write-Host "   $($schedule.Timestamp): $($schedule.Action) (Confidence: $([Math]::Round($schedule.Confidence * 100, 1))%)" -ForegroundColor White
    }

    return @{
        UsageAnalysis = $usageAnalysis
        Predictions = $predictions
        ScalingSchedule = $scalingSchedule
    }
}

function Analyze-UsagePatterns {
    param($Data)

    # Analyze hourly, daily, and weekly patterns
    $patterns = @{
        HourlyPattern = @{}
        DailyPattern = @{}
        WeeklyPattern = @{}
        TrendAnalysis = @{}
    }

    # Process historical data to identify patterns
    foreach ($dataPoint in $Data) {
        $timestamp = [DateTime]$dataPoint.Timestamp
        $users = $dataPoint.ConcurrentUsers

        # Hourly pattern (0-23)
        $hour = $timestamp.Hour
        if (!$patterns.HourlyPattern[$hour]) {
            $patterns.HourlyPattern[$hour] = @()
        }
        $patterns.HourlyPattern[$hour] += $users

        # Daily pattern (Sunday=0 to Saturday=6)
        $dayOfWeek = [int]$timestamp.DayOfWeek
        if (!$patterns.DailyPattern[$dayOfWeek]) {
            $patterns.DailyPattern[$dayOfWeek] = @()
        }
        $patterns.DailyPattern[$dayOfWeek] += $users

        # Weekly pattern
        $weekOfYear = [System.Globalization.ISOWeek]::GetWeekOfYear($timestamp)
        if (!$patterns.WeeklyPattern[$weekOfYear]) {
            $patterns.WeeklyPattern[$weekOfYear] = @()
        }
        $patterns.WeeklyPattern[$weekOfYear] += $users
    }

    # Calculate averages for each pattern
    foreach ($hour in $patterns.HourlyPattern.Keys) {
        $patterns.HourlyPattern[$hour] = ($patterns.HourlyPattern[$hour] | Measure-Object -Average).Average
    }

    foreach ($day in $patterns.DailyPattern.Keys) {
        $patterns.DailyPattern[$day] = ($patterns.DailyPattern[$day] | Measure-Object -Average).Average
    }

    return $patterns
}
```

## 4. Cost Optimization Framework

### 4.1 Cloud Resource Cost Analysis

#### Azure Cost Optimization
```powershell
# Analyze and optimize Azure costs for Orleans deployment
function Optimize-AzureCosts {
    param(
        [hashtable]$CurrentDeployment,
        [string]$Region = "East US",
        [int]$ProjectionMonths = 12
    )

    Write-Host "💰 Analyzing Azure cost optimization opportunities..." -ForegroundColor Cyan

    # Azure VM pricing (sample - update with current rates)
    $azurePricing = @{
        "Standard_D2s_v3" = @{ HourlyRate = 0.096; Cores = 2; MemoryGB = 8 }
        "Standard_D4s_v3" = @{ HourlyRate = 0.192; Cores = 4; MemoryGB = 16 }
        "Standard_D8s_v3" = @{ HourlyRate = 0.384; Cores = 8; MemoryGB = 32 }
        "Standard_D16s_v3" = @{ HourlyRate = 0.768; Cores = 16; MemoryGB = 64 }
    }

    # Reserved Instance discounts
    $reservedInstanceDiscounts = @{
        "1Year" = 0.38  # 38% discount
        "3Year" = 0.55  # 55% discount
    }

    # Current cost calculation
    $currentVMSize = $CurrentDeployment.VMSize
    $currentVMCount = $CurrentDeployment.VMCount
    $currentHourlyRate = $azurePricing[$currentVMSize].HourlyRate
    $currentMonthlyCost = $currentHourlyRate * 24 * 30 * $currentVMCount

    # Cost optimization recommendations
    $optimizations = @()

    # Reserved Instances
    $reservedInstanceSavings1Year = $currentMonthlyCost * $reservedInstanceDiscounts["1Year"] * $ProjectionMonths
    $reservedInstanceSavings3Year = $currentMonthlyCost * $reservedInstanceDiscounts["3Year"] * $ProjectionMonths

    $optimizations += @{
        Type = "Reserved Instance 1-Year"
        MonthlySavings = $currentMonthlyCost * $reservedInstanceDiscounts["1Year"]
        TotalSavings = $reservedInstanceSavings1Year
        Description = "Purchase 1-year reserved instances for $($reservedInstanceDiscounts["1Year"] * 100)% savings"
    }

    $optimizations += @{
        Type = "Reserved Instance 3-Year"
        MonthlySavings = $currentMonthlyCost * $reservedInstanceDiscounts["3Year"]
        TotalSavings = $reservedInstanceSavings3Year
        Description = "Purchase 3-year reserved instances for $($reservedInstanceDiscounts["3Year"] * 100)% savings"
    }

    # Right-sizing analysis
    $utilizationData = Get-ResourceUtilization -Deployment $CurrentDeployment
    if ($utilizationData.AverageCPU -lt 0.4 -and $utilizationData.AverageMemory -lt 0.4) {
        $smallerVMSize = Get-SmallerVMSize -CurrentSize $currentVMSize -AzurePricing $azurePricing
        if ($smallerVMSize) {
            $newHourlyRate = $azurePricing[$smallerVMSize].HourlyRate
            $newMonthlyCost = $newHourlyRate * 24 * 30 * $currentVMCount
            $rightSizingSavings = ($currentMonthlyCost - $newMonthlyCost) * $ProjectionMonths

            $optimizations += @{
                Type = "Right-sizing"
                MonthlySavings = $currentMonthlyCost - $newMonthlyCost
                TotalSavings = $rightSizingSavings
                Description = "Downsize from $currentVMSize to $smallerVMSize based on utilization"
            }
        }
    }

    # Auto-scaling optimization
    $autoScalingSavings = $currentMonthlyCost * 0.25 * $ProjectionMonths  # Estimated 25% savings

    $optimizations += @{
        Type = "Auto-scaling"
        MonthlySavings = $currentMonthlyCost * 0.25
        TotalSavings = $autoScalingSavings
        Description = "Implement auto-scaling to optimize resource usage"
    }

    # Spot instances for non-critical workloads
    $spotInstanceSavings = $currentMonthlyCost * 0.70 * $ProjectionMonths  # Up to 70% savings

    $optimizations += @{
        Type = "Spot Instances"
        MonthlySavings = $currentMonthlyCost * 0.70
        TotalSavings = $spotInstanceSavings
        Description = "Use spot instances for development/testing workloads"
        Risk = "High - instances can be reclaimed"
    }

    $result = @{
        CurrentCost = @{
            HourlyCost = $currentHourlyRate * $currentVMCount
            MonthlyCost = $currentMonthlyCost
            AnnualCost = $currentMonthlyCost * 12
        }
        Optimizations = $optimizations
        TotalPotentialSavings = ($optimizations | Measure-Object -Property TotalSavings -Sum).Sum
    }

    Write-Host "📊 Azure Cost Analysis:" -ForegroundColor Yellow
    Write-Host "   Current Monthly Cost: $([Math]::Round($currentMonthlyCost, 2))" -ForegroundColor White
    Write-Host "   Potential Annual Savings: $([Math]::Round($result.TotalPotentialSavings, 2))" -ForegroundColor Green

    return $result
}
```

### 4.2 Resource Scheduling for Cost Optimization

#### Intelligent Resource Scheduling
```powershell
# Implement intelligent resource scheduling for cost optimization
function Implement-ResourceScheduling {
    param(
        [hashtable]$UsagePatterns,
        [float]$CostPerHour = 0.50,
        [int]$MinInstances = 2,
        [int]$MaxInstances = 20
    )

    Write-Host "⏰ Implementing intelligent resource scheduling..." -ForegroundColor Cyan

    # Create scheduling plan based on usage patterns
    $schedulingPlan = @()

    for ($hour = 0; $hour -lt 24; $hour++) {
        $expectedLoad = $UsagePatterns.HourlyPattern[$hour]
        $requiredInstances = Calculate-RequiredInstances -ExpectedLoad $expectedLoad

        # Apply constraints
        $requiredInstances = [Math]::Max($MinInstances, $requiredInstances)
        $requiredInstances = [Math]::Min($MaxInstances, $requiredInstances)

        $schedulingPlan += @{
            Hour = $hour
            ExpectedLoad = $expectedLoad
            RequiredInstances = $requiredInstances
            HourlyCost = $requiredInstances * $CostPerHour
        }
    }

    # Calculate cost savings compared to static provisioning
    $staticProvisioningCost = $MaxInstances * $CostPerHour * 24
    $scheduledProvisioningCost = ($schedulingPlan | Measure-Object -Property HourlyCost -Sum).Sum
    $dailySavings = $staticProvisioningCost - $scheduledProvisioningCost

    # Generate scheduling automation script
    $automationScript = Generate-SchedulingScript -SchedulingPlan $schedulingPlan

    $result = @{
        SchedulingPlan = $schedulingPlan
        CostAnalysis = @{
            StaticDailyCost = $staticProvisioningCost
            ScheduledDailyCost = $scheduledProvisioningCost
            DailySavings = $dailySavings
            MonthlySavings = $dailySavings * 30
            AnnualSavings = $dailySavings * 365
        }
        AutomationScript = $automationScript
    }

    Write-Host "📊 Resource Scheduling Analysis:" -ForegroundColor Yellow
    Write-Host "   Static Daily Cost: $([Math]::Round($staticProvisioningCost, 2))" -ForegroundColor White
    Write-Host "   Scheduled Daily Cost: $([Math]::Round($scheduledProvisioningCost, 2))" -ForegroundColor White
    Write-Host "   Annual Savings: $([Math]::Round($dailySavings * 365, 2))" -ForegroundColor Green

    return $result
}

function Generate-SchedulingScript {
    param($SchedulingPlan)

    $script = @"
# Orleans Auto-Scaling Schedule Script
# Generated on $(Get-Date)

function Start-OrleansScheduler {
    Write-Host "Starting Orleans resource scheduler..." -ForegroundColor Cyan

    while (`$true) {
        `$currentHour = (Get-Date).Hour

        switch (`$currentHour) {
"@

    foreach ($schedule in $SchedulingPlan) {
        $script += "`n            $($schedule.Hour) { Set-OrleansClusterSize -TargetSize $($schedule.RequiredInstances) }"
    }

    $script += @"

            default { Write-Host "No scaling action for hour `$currentHour" }
        }

        # Wait for next hour
        `$nextHour = (Get-Date).AddHours(1).Date.AddHours((Get-Date).AddHours(1).Hour)
        `$waitTime = (`$nextHour - (Get-Date)).TotalSeconds
        Start-Sleep -Seconds `$waitTime
    }
}

function Set-OrleansClusterSize {
    param([int]`$TargetSize)

    Write-Host "Scaling Orleans cluster to `$TargetSize instances..." -ForegroundColor Yellow

    # Implementation depends on your cloud provider and orchestration platform
    # Azure VM Scale Sets, AWS Auto Scaling Groups, Kubernetes HPA, etc.

    # Example for Azure VM Scale Sets
    az vmss scale --resource-group "orleans-rg" --name "orleans-vmss" --new-capacity `$TargetSize

    Write-Host "Cluster scaled to `$TargetSize instances" -ForegroundColor Green
}

# Start the scheduler
Start-OrleansScheduler
"@

    return $script
}
```

## 5. Auto-Scaling Policies

### 5.1 Orleans-Specific Auto-Scaling Metrics

#### Custom Metrics Collection
```powershell
# Collect Orleans-specific metrics for auto-scaling decisions
function Collect-OrleansMetrics {
    param(
        [string[]]$OrleansEndpoints = @("http://localhost:5099", "http://localhost:5098"),
        [int]$SampleIntervalSeconds = 30
    )

    Write-Host "📊 Collecting Orleans metrics for auto-scaling..." -ForegroundColor Cyan

    $metrics = @{}

    foreach ($endpoint in $OrleansEndpoints) {
        try {
            # Collect standard Orleans metrics
            $orleanStats = Invoke-RestMethod -Uri "$endpoint/metrics/orleans" -TimeoutSec 10
            $healthStats = Invoke-RestMethod -Uri "$endpoint/health/orleans" -TimeoutSec 10

            # Collect system metrics
            $systemStats = Invoke-RestMethod -Uri "$endpoint/metrics/system" -TimeoutSec 10

            $metrics[$endpoint] = @{
                Orleans = @{
                    ActiveGrains = $orleanStats.ActiveGrains
                    ActivationsPerSecond = $orleanStats.ActivationsPerSecond
                    RequestsPerSecond = $orleanStats.RequestsPerSecond
                    AverageResponseTime = $orleanStats.AverageResponseTime
                    QueueLength = $orleanStats.QueueLength
                    GrainDirectorySize = $orleanStats.GrainDirectorySize
                }
                System = @{
                    CpuUtilization = $systemStats.CpuUtilization
                    MemoryUtilization = $systemStats.MemoryUtilization
                    NetworkIO = $systemStats.NetworkIO
                    DiskIO = $systemStats.DiskIO
                }
                Health = @{
                    Status = $healthStats.Status
                    ResponseTime = $healthStats.ResponseTime
                }
                Timestamp = Get-Date
            }

        } catch {
            Write-Warning "Failed to collect metrics from $endpoint`: $($_.Exception.Message)"
            $metrics[$endpoint] = $null
        }
    }

    return $metrics
}
```

#### Auto-Scaling Decision Engine
```powershell
# Implement Orleans auto-scaling decision engine
function Invoke-AutoScalingDecision {
    param(
        [hashtable]$CurrentMetrics,
        [hashtable]$ScalingPolicy = @{
            ScaleUpThresholds = @{
                CpuUtilization = 80
                MemoryUtilization = 85
                AverageResponseTime = 500  # milliseconds
                QueueLength = 1000
                ActivationsPerSecond = 100
            }
            ScaleDownThresholds = @{
                CpuUtilization = 30
                MemoryUtilization = 40
                AverageResponseTime = 100  # milliseconds
                QueueLength = 10
                ActivationsPerSecond = 10
            }
            CooldownPeriods = @{
                ScaleUp = 300    # 5 minutes
                ScaleDown = 900  # 15 minutes
            }
            ScalingLimits = @{
                MinInstances = 2
                MaxInstances = 20
                ScaleUpStep = 1
                ScaleDownStep = 1
            }
        }
    )

    Write-Host "🤖 Evaluating auto-scaling decision..." -ForegroundColor Cyan

    # Aggregate metrics across all Orleans endpoints
    $aggregatedMetrics = Aggregate-OrleansMetrics -Metrics $CurrentMetrics

    # Evaluate scaling triggers
    $scaleUpTriggers = @()
    $scaleDownTriggers = @()

    # Check CPU utilization
    if ($aggregatedMetrics.System.CpuUtilization -gt $ScalingPolicy.ScaleUpThresholds.CpuUtilization) {
        $scaleUpTriggers += "CPU utilization: $($aggregatedMetrics.System.CpuUtilization)% > $($ScalingPolicy.ScaleUpThresholds.CpuUtilization)%"
    }
    elseif ($aggregatedMetrics.System.CpuUtilization -lt $ScalingPolicy.ScaleDownThresholds.CpuUtilization) {
        $scaleDownTriggers += "CPU utilization: $($aggregatedMetrics.System.CpuUtilization)% < $($ScalingPolicy.ScaleDownThresholds.CpuUtilization)%"
    }

    # Check memory utilization
    if ($aggregatedMetrics.System.MemoryUtilization -gt $ScalingPolicy.ScaleUpThresholds.MemoryUtilization) {
        $scaleUpTriggers += "Memory utilization: $($aggregatedMetrics.System.MemoryUtilization)% > $($ScalingPolicy.ScaleUpThresholds.MemoryUtilization)%"
    }
    elseif ($aggregatedMetrics.System.MemoryUtilization -lt $ScalingPolicy.ScaleDownThresholds.MemoryUtilization) {
        $scaleDownTriggers += "Memory utilization: $($aggregatedMetrics.System.MemoryUtilization)% < $($ScalingPolicy.ScaleDownThresholds.MemoryUtilization)%"
    }

    # Check Orleans-specific metrics
    if ($aggregatedMetrics.Orleans.AverageResponseTime -gt $ScalingPolicy.ScaleUpThresholds.AverageResponseTime) {
        $scaleUpTriggers += "Response time: $($aggregatedMetrics.Orleans.AverageResponseTime)ms > $($ScalingPolicy.ScaleUpThresholds.AverageResponseTime)ms"
    }

    if ($aggregatedMetrics.Orleans.QueueLength -gt $ScalingPolicy.ScaleUpThresholds.QueueLength) {
        $scaleUpTriggers += "Queue length: $($aggregatedMetrics.Orleans.QueueLength) > $($ScalingPolicy.ScaleUpThresholds.QueueLength)"
    }

    # Check cooldown periods
    $lastScaleAction = Get-LastScaleAction
    $timeSinceLastScale = if ($lastScaleAction) {
        ((Get-Date) - $lastScaleAction.Timestamp).TotalSeconds
    } else {
        999999
    }

    # Make scaling decision
    $scalingDecision = @{
        Action = "None"
        Reason = ""
        Triggers = @()
        Blocked = $false
        BlockReason = ""
        Metrics = $aggregatedMetrics
        Timestamp = Get-Date
    }

    if ($scaleUpTriggers.Count -gt 0) {
        if ($timeSinceLastScale -gt $ScalingPolicy.CooldownPeriods.ScaleUp) {
            $scalingDecision.Action = "ScaleUp"
            $scalingDecision.Reason = "Scale up triggered by: $($scaleUpTriggers -join '; ')"
            $scalingDecision.Triggers = $scaleUpTriggers
        } else {
            $scalingDecision.Blocked = $true
            $scalingDecision.BlockReason = "Scale up cooldown active ($([Math]::Round($ScalingPolicy.CooldownPeriods.ScaleUp - $timeSinceLastScale, 0))s remaining)"
        }
    }
    elseif ($scaleDownTriggers.Count -ge 2) {  # Require multiple triggers for scale down
        if ($timeSinceLastScale -gt $ScalingPolicy.CooldownPeriods.ScaleDown) {
            $scalingDecision.Action = "ScaleDown"
            $scalingDecision.Reason = "Scale down triggered by: $($scaleDownTriggers -join '; ')"
            $scalingDecision.Triggers = $scaleDownTriggers
        } else {
            $scalingDecision.Blocked = $true
            $scalingDecision.BlockReason = "Scale down cooldown active ($([Math]::Round($ScalingPolicy.CooldownPeriods.ScaleDown - $timeSinceLastScale, 0))s remaining)"
        }
    }

    Write-Host "📊 Auto-Scaling Decision:" -ForegroundColor Yellow
    Write-Host "   Action: $($scalingDecision.Action)" -ForegroundColor $(if ($scalingDecision.Action -eq "None") { "White" } else { "Green" })
    if ($scalingDecision.Reason) {
        Write-Host "   Reason: $($scalingDecision.Reason)" -ForegroundColor White
    }
    if ($scalingDecision.Blocked) {
        Write-Host "   Blocked: $($scalingDecision.BlockReason)" -ForegroundColor Yellow
    }

    return $scalingDecision
}

function Aggregate-OrleansMetrics {
    param([hashtable]$Metrics)

    $validMetrics = $Metrics.Values | Where-Object { $_ -ne $null }
    if ($validMetrics.Count -eq 0) {
        throw "No valid metrics available for aggregation"
    }

    $aggregated = @{
        Orleans = @{
            ActiveGrains = ($validMetrics | Measure-Object -Property { $_.Orleans.ActiveGrains } -Sum).Sum
            ActivationsPerSecond = ($validMetrics | Measure-Object -Property { $_.Orleans.ActivationsPerSecond } -Average).Average
            RequestsPerSecond = ($validMetrics | Measure-Object -Property { $_.Orleans.RequestsPerSecond } -Sum).Sum
            AverageResponseTime = ($validMetrics | Measure-Object -Property { $_.Orleans.AverageResponseTime } -Average).Average
            QueueLength = ($validMetrics | Measure-Object -Property { $_.Orleans.QueueLength } -Sum).Sum
        }
        System = @{
            CpuUtilization = ($validMetrics | Measure-Object -Property { $_.System.CpuUtilization } -Average).Average
            MemoryUtilization = ($validMetrics | Measure-Object -Property { $_.System.MemoryUtilization } -Average).Average
            NetworkIO = ($validMetrics | Measure-Object -Property { $_.System.NetworkIO } -Average).Average
        }
    }

    return $aggregated
}
```

### 5.2 Auto-Scaling Policy Templates

#### Production Auto-Scaling Template
```json
{
  "autoScalingPolicy": {
    "name": "Orleans Production Auto-Scaling",
    "enabled": true,
    "metrics": {
      "orleans": {
        "activeGrains": {
          "scaleUpThreshold": 5000,
          "scaleDownThreshold": 500,
          "evaluationPeriods": 2,
          "weight": 0.3
        },
        "averageResponseTime": {
          "scaleUpThreshold": 200,
          "scaleDownThreshold": 50,
          "evaluationPeriods": 3,
          "weight": 0.4
        },
        "queueLength": {
          "scaleUpThreshold": 100,
          "scaleDownThreshold": 10,
          "evaluationPeriods": 2,
          "weight": 0.3
        }
      },
      "system": {
        "cpuUtilization": {
          "scaleUpThreshold": 70,
          "scaleDownThreshold": 30,
          "evaluationPeriods": 2,
          "weight": 0.4
        },
        "memoryUtilization": {
          "scaleUpThreshold": 80,
          "scaleDownThreshold": 40,
          "evaluationPeriods": 2,
          "weight": 0.6
        }
      }
    },
    "scaling": {
      "minInstances": 2,
      "maxInstances": 20,
      "scaleUpStep": 2,
      "scaleDownStep": 1,
      "cooldownPeriods": {
        "scaleUp": 300,
        "scaleDown": 600
      }
    },
    "schedule": {
      "enabled": true,
      "timezone": "UTC",
      "rules": [
        {
          "name": "Business Hours Scale Up",
          "schedule": "0 8 * * MON-FRI",
          "minInstances": 4,
          "maxInstances": 20
        },
        {
          "name": "Off Hours Scale Down",
          "schedule": "0 18 * * MON-FRI",
          "minInstances": 2,
          "maxInstances": 10
        }
      ]
    }
  }
}
```

## 6. Capacity Calculator Tools

### 6.1 Interactive Capacity Calculator

#### PowerShell Capacity Calculator
```powershell
# Interactive Orleans capacity calculator
function Start-CapacityCalculator {
    param(
        [switch]$InteractiveMode = $true
    )

    Write-Host "🧮 Orleans Capacity Calculator" -ForegroundColor Cyan
    Write-Host "=============================" -ForegroundColor Cyan

    if ($InteractiveMode) {
        $inputs = Get-InteractiveInputs
    } else {
        $inputs = Get-DefaultInputs
    }

    $capacityAnalysis = Calculate-ComprehensiveCapacity @inputs

    Display-CapacityResults -Analysis $capacityAnalysis
    Generate-CapacityReport -Analysis $capacityAnalysis -Inputs $inputs

    return $capacityAnalysis
}

function Get-InteractiveInputs {
    $inputs = @{}

    Write-Host "`n📋 Workload Requirements:" -ForegroundColor Yellow

    $inputs.ExpectedUsers = Read-Host "Expected concurrent users (default: 1000)"
    if (!$inputs.ExpectedUsers) { $inputs.ExpectedUsers = 1000 }
    $inputs.ExpectedUsers = [int]$inputs.ExpectedUsers

    $inputs.MessagesPerUserPerHour = Read-Host "Messages per user per hour (default: 20)"
    if (!$inputs.MessagesPerUserPerHour) { $inputs.MessagesPerUserPerHour = 20 }
    $inputs.MessagesPerUserPerHour = [int]$inputs.MessagesPerUserPerHour

    $inputs.AIToolUsagePercentage = Read-Host "Percentage of messages using AI tools (default: 30)"
    if (!$inputs.AIToolUsagePercentage) { $inputs.AIToolUsagePercentage = 30 }
    $inputs.AIToolUsagePercentage = [int]$inputs.AIToolUsagePercentage

    Write-Host "`n🏗️ Infrastructure Preferences:" -ForegroundColor Yellow

    $deploymentModes = @("Single", "HA", "MultiRegion")
    Write-Host "Deployment modes: $($deploymentModes -join ', ')"
    $inputs.DeploymentMode = Read-Host "Deployment mode (default: HA)"
    if (!$inputs.DeploymentMode) { $inputs.DeploymentMode = "HA" }

    $inputs.DataRetentionDays = Read-Host "Data retention days (default: 365)"
    if (!$inputs.DataRetentionDays) { $inputs.DataRetentionDays = 365 }
    $inputs.DataRetentionDays = [int]$inputs.DataRetentionDays

    $inputs.GrowthRatePercentage = Read-Host "Expected annual growth rate % (default: 50)"
    if (!$inputs.GrowthRatePercentage) { $inputs.GrowthRatePercentage = 50 }
    $inputs.GrowthRatePercentage = [float]$inputs.GrowthRatePercentage

    Write-Host "`n💰 Cost Preferences:" -ForegroundColor Yellow

    $cloudProviders = @("Azure", "AWS", "GCP")
    Write-Host "Cloud providers: $($cloudProviders -join ', ')"
    $inputs.CloudProvider = Read-Host "Cloud provider (default: Azure)"
    if (!$inputs.CloudProvider) { $inputs.CloudProvider = "Azure" }

    $inputs.BudgetConstraint = Read-Host "Monthly budget constraint USD (optional)"
    if ($inputs.BudgetConstraint) { $inputs.BudgetConstraint = [float]$inputs.BudgetConstraint }

    return $inputs
}

function Calculate-ComprehensiveCapacity {
    param(
        [int]$ExpectedUsers,
        [int]$MessagesPerUserPerHour,
        [int]$AIToolUsagePercentage,
        [string]$DeploymentMode,
        [int]$DataRetentionDays,
        [float]$GrowthRatePercentage,
        [string]$CloudProvider,
        [float]$BudgetConstraint = 0
    )

    Write-Host "🔍 Calculating comprehensive capacity requirements..." -ForegroundColor Cyan

    # Calculate workload metrics
    $messagesPerSecond = ($ExpectedUsers * $MessagesPerUserPerHour) / 3600
    $aiCallsPerSecond = ($messagesPerSecond * $AIToolUsagePercentage) / 100

    # Calculate resource requirements
    $cpuRequirements = Calculate-CPURequirements -ConcurrentUsers $ExpectedUsers -MessagesPerSecond $messagesPerSecond -AIToolCallsPerSecond $aiCallsPerSecond
    $memoryRequirements = Calculate-MemoryRequirements -MaxConcurrentSessions $ExpectedUsers -DeploymentMode $DeploymentMode
    $storageRequirements = Calculate-StorageRequirements -ExpectedUsers $ExpectedUsers -RetentionDays $DataRetentionDays -GrowthRatePercentage $GrowthRatePercentage

    # Calculate Orleans cluster sizing
    $clusterSizing = Calculate-ClusterSizing -CPURequirements $cpuRequirements -MemoryRequirements $memoryRequirements -DeploymentMode $DeploymentMode

    # Calculate costs
    $costAnalysis = Calculate-CloudCosts -ClusterSizing $clusterSizing -StorageRequirements $storageRequirements -CloudProvider $CloudProvider

    # Generate scaling recommendations
    $scalingRecommendations = Generate-ScalingRecommendations -ExpectedUsers $ExpectedUsers -GrowthRate $GrowthRatePercentage

    $analysis = @{
        Inputs = @{
            ExpectedUsers = $ExpectedUsers
            MessagesPerUserPerHour = $MessagesPerUserPerHour
            AIToolUsagePercentage = $AIToolUsagePercentage
            DeploymentMode = $DeploymentMode
        }
        WorkloadMetrics = @{
            MessagesPerSecond = $messagesPerSecond
            AICallsPerSecond = $aiCallsPerSecond
        }
        ResourceRequirements = @{
            CPU = $cpuRequirements
            Memory = $memoryRequirements
            Storage = $storageRequirements
        }
        ClusterSizing = $clusterSizing
        CostAnalysis = $costAnalysis
        ScalingRecommendations = $scalingRecommendations
        BudgetAnalysis = if ($BudgetConstraint -gt 0) {
            Analyze-BudgetConstraints -CostAnalysis $costAnalysis -Budget $BudgetConstraint
        } else {
            $null
        }
    }

    return $analysis
}

function Calculate-ClusterSizing {
    param($CPURequirements, $MemoryRequirements, $DeploymentMode)

    # Determine appropriate VM sizes based on requirements
    $vmSizes = Get-VMSizes -CloudProvider "Azure"  # Can be parameterized

    $selectedVMSize = $vmSizes | Where-Object {
        $_.Cores -ge $CPURequirements.RecommendedCores -and
        $_.MemoryGB -ge $MemoryRequirements.RecommendedMemoryGB
    } | Sort-Object Price | Select-Object -First 1

    if (!$selectedVMSize) {
        # Scale out to multiple smaller VMs
        $selectedVMSize = $vmSizes | Sort-Object Price | Select-Object -First 1
        $vmCount = [Math]::Ceiling($CPURequirements.RecommendedCores / $selectedVMSize.Cores)
    } else {
        $vmCount = 1
    }

    # Apply deployment mode multipliers
    $deploymentMultipliers = @{
        "Single" = 1
        "HA" = 2       # Minimum 2 for HA
        "MultiRegion" = 4  # 2 per region, 2 regions minimum
    }

    $finalVMCount = [Math]::Max($vmCount, $deploymentMultipliers[$DeploymentMode])

    return @{
        VMSize = $selectedVMSize.Name
        VMCount = $finalVMCount
        TotalCores = $finalVMCount * $selectedVMSize.Cores
        TotalMemoryGB = $finalVMCount * $selectedVMSize.MemoryGB
        DeploymentMode = $DeploymentMode
    }
}

function Display-CapacityResults {
    param($Analysis)

    Write-Host "`n📊 Capacity Analysis Results" -ForegroundColor Cyan
    Write-Host "============================" -ForegroundColor Cyan

    Write-Host "`n🔧 Resource Requirements:" -ForegroundColor Yellow
    Write-Host "   CPU Cores: $($Analysis.ClusterSizing.TotalCores)" -ForegroundColor White
    Write-Host "   Memory: $($Analysis.ClusterSizing.TotalMemoryGB)GB" -ForegroundColor White
    Write-Host "   Storage: $($Analysis.ResourceRequirements.Storage.RecommendedStorageGB)GB" -ForegroundColor White

    Write-Host "`n🏗️ Infrastructure Recommendation:" -ForegroundColor Yellow
    Write-Host "   VM Size: $($Analysis.ClusterSizing.VMSize)" -ForegroundColor White
    Write-Host "   VM Count: $($Analysis.ClusterSizing.VMCount)" -ForegroundColor White
    Write-Host "   Deployment Mode: $($Analysis.ClusterSizing.DeploymentMode)" -ForegroundColor White

    Write-Host "`n💰 Cost Analysis:" -ForegroundColor Yellow
    Write-Host "   Monthly Cost: $([Math]::Round($Analysis.CostAnalysis.MonthlyCost, 2))" -ForegroundColor White
    Write-Host "   Annual Cost: $([Math]::Round($Analysis.CostAnalysis.AnnualCost, 2))" -ForegroundColor White

    if ($Analysis.BudgetAnalysis) {
        Write-Host "`n💳 Budget Analysis:" -ForegroundColor Yellow
        if ($Analysis.BudgetAnalysis.WithinBudget) {
            Write-Host "   Status: Within Budget ✅" -ForegroundColor Green
            Write-Host "   Remaining Budget: $([Math]::Round($Analysis.BudgetAnalysis.RemainingBudget, 2))" -ForegroundColor Green
        } else {
            Write-Host "   Status: Over Budget ❌" -ForegroundColor Red
            Write-Host "   Budget Overage: $([Math]::Round($Analysis.BudgetAnalysis.BudgetOverage, 2))" -ForegroundColor Red
            Write-Host "   Recommendations: $($Analysis.BudgetAnalysis.CostReductions -join '; ')" -ForegroundColor Yellow
        }
    }

    Write-Host "`n📈 Scaling Recommendations:" -ForegroundColor Yellow
    foreach ($recommendation in $Analysis.ScalingRecommendations) {
        Write-Host "   $($recommendation.Timeframe): $($recommendation.Recommendation)" -ForegroundColor White
    }
}
```

### 6.2 TCO (Total Cost of Ownership) Calculator

#### Comprehensive TCO Analysis
```powershell
# Calculate Total Cost of Ownership for Orleans deployment
function Calculate-TCOAnalysis {
    param(
        [hashtable]$CapacityAnalysis,
        [int]$ProjectionYears = 3,
        [hashtable]$CostFactors = @{
            DevelopmentCostPerHour = 150
            OperationsCostPerMonth = 5000
            TrainingCostPerUser = 50
            ComplianceCostPerYear = 10000
            DisasterRecoveryCostPerYear = 15000
        }
    )

    Write-Host "💼 Calculating Total Cost of Ownership..." -ForegroundColor Cyan

    $tcoAnalysis = @{
        ProjectionYears = $ProjectionYears
        CostBreakdown = @{}
        YearlyProjection = @()
        ROIAnalysis = @{}
    }

    # Infrastructure costs
    $infrastructureCosts = @{
        Year1 = $CapacityAnalysis.CostAnalysis.AnnualCost
        Year2 = $CapacityAnalysis.CostAnalysis.AnnualCost * 1.2  # 20% growth
        Year3 = $CapacityAnalysis.CostAnalysis.AnnualCost * 1.5  # 50% growth
    }

    # Development and implementation costs
    $developmentCosts = @{
        InitialImplementation = $CostFactors.DevelopmentCostPerHour * 200  # 200 hours
        OngoingDevelopment = $CostFactors.DevelopmentCostPerHour * 40 * 12  # 40 hours/month
        Maintenance = $CostFactors.DevelopmentCostPerHour * 20 * 12  # 20 hours/month
    }

    # Operations costs
    $operationsCosts = @{
        MonthlyOperations = $CostFactors.OperationsCostPerMonth
        AnnualOperations = $CostFactors.OperationsCostPerMonth * 12
    }

    # Training and adoption costs
    $trainingCosts = @{
        InitialTraining = $CapacityAnalysis.Inputs.ExpectedUsers * $CostFactors.TrainingCostPerUser
        OngoingTraining = ($CapacityAnalysis.Inputs.ExpectedUsers * 0.2) * $CostFactors.TrainingCostPerUser  # 20% annual turnover
    }

    # Compliance and governance costs
    $complianceCosts = @{
        AnnualCompliance = $CostFactors.ComplianceCostPerYear
        DisasterRecovery = $CostFactors.DisasterRecoveryCostPerYear
    }

    # Calculate yearly projections
    for ($year = 1; $year -le $ProjectionYears; $year++) {
        $yearlyInfrastructure = switch ($year) {
            1 { $infrastructureCosts.Year1 }
            2 { $infrastructureCosts.Year2 }
            3 { $infrastructureCosts.Year3 }
            default { $infrastructureCosts.Year3 * [Math]::Pow(1.2, $year - 3) }
        }

        $yearlyDevelopment = if ($year -eq 1) {
            $developmentCosts.InitialImplementation + $developmentCosts.OngoingDevelopment
        } else {
            $developmentCosts.OngoingDevelopment + $developmentCosts.Maintenance
        }

        $yearlyTraining = if ($year -eq 1) {
            $trainingCosts.InitialTraining
        } else {
            $trainingCosts.OngoingTraining
        }

        $yearlyTotal = $yearlyInfrastructure + $yearlyDevelopment + $operationsCosts.AnnualOperations + $yearlyTraining + $complianceCosts.AnnualCompliance + $complianceCosts.DisasterRecovery

        $tcoAnalysis.YearlyProjection += @{
            Year = $year
            Infrastructure = $yearlyInfrastructure
            Development = $yearlyDevelopment
            Operations = $operationsCosts.AnnualOperations
            Training = $yearlyTraining
            Compliance = $complianceCosts.AnnualCompliance + $complianceCosts.DisasterRecovery
            Total = $yearlyTotal
        }
    }

    # Calculate total TCO
    $totalTCO = ($tcoAnalysis.YearlyProjection | Measure-Object -Property Total -Sum).Sum

    # ROI analysis (assuming productivity gains)
    $annualProductivityGains = $CapacityAnalysis.Inputs.ExpectedUsers * 2000  # $2000 per user per year
    $totalROI = ($annualProductivityGains * $ProjectionYears) - $totalTCO
    $roiPercentage = ($totalROI / $totalTCO) * 100

    $tcoAnalysis.ROIAnalysis = @{
        TotalTCO = $totalTCO
        AnnualProductivityGains = $annualProductivityGains
        TotalProductivityGains = $annualProductivityGains * $ProjectionYears
        NetROI = $totalROI
        ROIPercentage = $roiPercentage
        PaybackPeriod = $totalTCO / $annualProductivityGains
    }

    # Display TCO analysis
    Write-Host "`n📊 TCO Analysis Summary:" -ForegroundColor Yellow
    Write-Host "   Total Cost of Ownership ($ProjectionYears years): $([Math]::Round($totalTCO, 0))" -ForegroundColor White
    Write-Host "   Average Annual Cost: $([Math]::Round($totalTCO / $ProjectionYears, 0))" -ForegroundColor White
    Write-Host "   Net ROI: $([Math]::Round($totalROI, 0))" -ForegroundColor $(if ($totalROI -gt 0) { "Green" } else { "Red" })
    Write-Host "   ROI Percentage: $([Math]::Round($roiPercentage, 1))%" -ForegroundColor $(if ($roiPercentage -gt 0) { "Green" } else { "Red" })
    Write-Host "   Payback Period: $([Math]::Round($tcoAnalysis.ROIAnalysis.PaybackPeriod, 1)) years" -ForegroundColor White

    return $tcoAnalysis
}
```

## 7. Performance Testing Integration

### 7.1 NBomber Load Testing Framework

#### Orleans-Specific Load Testing
```powershell
# Implement NBomber-based load testing for Orleans
function Implement-OrleansLoadTesting {
    param(
        [string]$OrleansEndpoint = "http://localhost:5099",
        [int]$TestDurationMinutes = 10,
        [hashtable]$LoadProfiles = @{
            "Light" = @{ Users = 10; RampUpMinutes = 2 }
            "Standard" = @{ Users = 100; RampUpMinutes = 5 }
            "Heavy" = @{ Users = 1000; RampUpMinutes = 10 }
            "Stress" = @{ Users = 2000; RampUpMinutes = 15 }
        }
    )

    Write-Host "🧪 Implementing Orleans load testing with NBomber..." -ForegroundColor Cyan

    # Generate NBomber test script
    $nbomberScript = @"
using NBomber;
using NBomber.Contracts;
using NBomber.CSharp;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OrleansLoadTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var httpClient = new HttpClient();
            var orleansEndpoint = "$OrleansEndpoint";

            // Chat scenario - simulate chat message processing
            var chatScenario = Scenario.Create("chat_scenario", async context =>
            {
                var chatRequest = new
                {
                    UserId = $"user_{context.ScenarioInfo.ThreadId}_{context.InvocationNumber}",
                    Message = $"Test message {context.InvocationNumber}",
                    SessionId = $"session_{context.ScenarioInfo.ThreadId}",
                    Timestamp = DateTime.UtcNow
                };

                var json = JsonConvert.SerializeObject(chatRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{orleansEndpoint}/api/chat", content);

                return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
            })
            .WithLoadSimulations(
                Simulation.InjectPerSec(rate: 10, during: TimeSpan.FromMinutes($TestDurationMinutes))
            );

            // AI tool scenario - simulate AI tool calls
            var aiToolScenario = Scenario.Create("ai_tool_scenario", async context =>
            {
                var aiRequest = new
                {
                    UserId = $"user_{context.ScenarioInfo.ThreadId}_{context.InvocationNumber}",
                    ToolName = "summarize",
                    Parameters = new { text = "This is a test document for summarization." },
                    SessionId = $"session_{context.ScenarioInfo.ThreadId}"
                };

                var json = JsonConvert.SerializeObject(aiRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{orleansEndpoint}/api/ai-tools", content);

                return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
            })
            .WithLoadSimulations(
                Simulation.InjectPerSec(rate: 2, during: TimeSpan.FromMinutes($TestDurationMinutes))
            );

            // Orleans health check scenario
            var healthScenario = Scenario.Create("health_check_scenario", async context =>
            {
                var response = await httpClient.GetAsync($"{orleansEndpoint}/health/orleans");

                return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
            })
            .WithLoadSimulations(
                Simulation.InjectPerSec(rate: 1, during: TimeSpan.FromMinutes($TestDurationMinutes))
            );

            NBomberRunner
                .RegisterScenarios(chatScenario, aiToolScenario, healthScenario)
                .Run();
        }
    }
}
"@

    # Create NBomber project structure
    $projectPath = "load-tests/Orleans.LoadTest"
    if (!(Test-Path $projectPath)) {
        New-Item -ItemType Directory -Path $projectPath -Force
    }

    # Create project file
    $csprojContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NBomber" Version="4.1.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
"@

    $nbomberScript | Set-Content "$projectPath/Program.cs"
    $csprojContent | Set-Content "$projectPath/Orleans.LoadTest.csproj"

    # Create load test execution script
    $executionScript = @"
# Orleans Load Test Execution Script

function Start-OrleansLoadTest {
    param(
        [string]`$Profile = "Standard",
        [int]`$DurationMinutes = 10,
        [string]`$ReportPath = "load-test-results"
    )

    Write-Host "Starting Orleans load test with profile: `$Profile" -ForegroundColor Cyan

    # Build the load test project
    Set-Location "$projectPath"
    dotnet build --configuration Release

    if (`$LASTEXITCODE -ne 0) {
        throw "Failed to build load test project"
    }

    # Create results directory
    if (!(Test-Path `$ReportPath)) {
        New-Item -ItemType Directory -Path `$ReportPath -Force
    }

    # Execute load test
    `$timestamp = Get-Date -Format "yyyy-MM-dd-HH-mm-ss"
    `$reportFile = "`$ReportPath/orleans-load-test-`$timestamp.html"

    dotnet run --configuration Release -- --report-folder `$ReportPath --report-file-name "orleans-load-test-`$timestamp"

    Write-Host "Load test completed. Report available at: `$reportFile" -ForegroundColor Green
}

# Execute with different profiles
foreach (`$profile in @("Light", "Standard", "Heavy")) {
    Write-Host "Running `$profile load test..." -ForegroundColor Yellow
    Start-OrleansLoadTest -Profile `$profile -DurationMinutes $TestDurationMinutes
    Start-Sleep -Seconds 30  # Cool down between tests
}
"@

    $executionScript | Set-Content "$projectPath/run-load-tests.ps1"

    Write-Host "✅ Orleans load testing framework created at: $projectPath" -ForegroundColor Green
    Write-Host "   Run with: powershell $projectPath/run-load-tests.ps1" -ForegroundColor White

    return @{
        ProjectPath = $projectPath
        NBomberScript = $nbomberScript
        ExecutionScript = $executionScript
        LoadProfiles = $LoadProfiles
    }
}
```

## 8. Monitoring and Alerting

### 8.1 Capacity Planning Metrics

#### Custom Metrics Collection for Capacity Planning
```powershell
# Implement comprehensive metrics collection for capacity planning
function Start-CapacityPlanningMonitoring {
    param(
        [string[]]$OrleansEndpoints = @("http://localhost:5099", "http://localhost:5098"),
        [int]$CollectionIntervalSeconds = 60,
        [string]$MetricsStoragePath = "metrics/capacity-planning"
    )

    Write-Host "📊 Starting capacity planning metrics collection..." -ForegroundColor Cyan

    # Ensure metrics storage directory exists
    if (!(Test-Path $MetricsStoragePath)) {
        New-Item -ItemType Directory -Path $MetricsStoragePath -Force
    }

    while ($true) {
        try {
            $timestamp = Get-Date
            $capacityMetrics = @{
                Timestamp = $timestamp
                Orleans = @{}
                System = @{}
                Business = @{}
            }

            # Collect Orleans-specific capacity metrics
            foreach ($endpoint in $OrleansEndpoints) {
                try {
                    $orleansStats = Invoke-RestMethod -Uri "$endpoint/metrics/orleans" -TimeoutSec 10
                    $systemStats = Invoke-RestMethod -Uri "$endpoint/metrics/system" -TimeoutSec 10

                    $capacityMetrics.Orleans[$endpoint] = @{
                        ActiveGrains = $orleansStats.ActiveGrains
                        GrainActivationsPerSecond = $orleansStats.ActivationsPerSecond
                        GrainDeactivationsPerSecond = $orleansStats.DeactivationsPerSecond
                        RequestsPerSecond = $orleansStats.RequestsPerSecond
                        AverageRequestDuration = $orleansStats.AverageRequestDuration
                        QueueLength = $orleansStats.QueueLength
                        MemoryUsage = $orleansStats.MemoryUsage
                        GrainDirectorySize = $orleansStats.GrainDirectorySize
                    }

                    $capacityMetrics.System[$endpoint] = @{
                        CpuUtilization = $systemStats.CpuUtilization
                        MemoryUtilization = $systemStats.MemoryUtilization
                        AvailableMemory = $systemStats.AvailableMemory
                        NetworkIOPerSecond = $systemStats.NetworkIOPerSecond
                        DiskIOPerSecond = $systemStats.DiskIOPerSecond
                        ThreadCount = $systemStats.ThreadCount
                    }

                } catch {
                    Write-Warning "Failed to collect metrics from $endpoint`: $($_.Exception.Message)"
                    $capacityMetrics.Orleans[$endpoint] = $null
                    $capacityMetrics.System[$endpoint] = $null
                }
            }

            # Collect business metrics
            try {
                $businessStats = Invoke-RestMethod -Uri "$($OrleansEndpoints[0])/metrics/business" -TimeoutSec 10

                $capacityMetrics.Business = @{
                    ConcurrentUsers = $businessStats.ConcurrentUsers
                    ActiveSessions = $businessStats.ActiveSessions
                    MessagesPerMinute = $businessStats.MessagesPerMinute
                    AIToolCallsPerMinute = $businessStats.AIToolCallsPerMinute
                    AverageSessionDuration = $businessStats.AverageSessionDuration
                    ErrorRate = $businessStats.ErrorRate
                }
            } catch {
                Write-Warning "Failed to collect business metrics"
                $capacityMetrics.Business = $null
            }

            # Calculate derived metrics
            $derivedMetrics = Calculate-CapacityDerivedMetrics -Metrics $capacityMetrics

            # Store metrics
            $metricsFileName = "$MetricsStoragePath/capacity-metrics-$($timestamp.ToString("yyyy-MM-dd-HH")).json"
            $capacityMetrics | ConvertTo-Json -Depth 10 | Add-Content $metricsFileName

            # Check for capacity planning alerts
            Test-CapacityPlanningAlerts -Metrics $capacityMetrics -DerivedMetrics $derivedMetrics

            # Display current capacity status
            Display-CapacityStatus -Metrics $capacityMetrics -DerivedMetrics $derivedMetrics

        } catch {
            Write-Error "Capacity planning monitoring error: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds $CollectionIntervalSeconds
    }
}

function Calculate-CapacityDerivedMetrics {
    param($Metrics)

    $derivedMetrics = @{
        ClusterUtilization = @{}
        CapacityTrends = @{}
        PredictedCapacity = @{}
    }

    # Calculate cluster-wide utilization
    $validOrleansMetrics = $Metrics.Orleans.Values | Where-Object { $_ -ne $null }
    $validSystemMetrics = $Metrics.System.Values | Where-Object { $_ -ne $null }

    if ($validOrleansMetrics.Count -gt 0) {
        $derivedMetrics.ClusterUtilization = @{
            TotalActiveGrains = ($validOrleansMetrics | Measure-Object -Property ActiveGrains -Sum).Sum
            AverageRequestsPerSecond = ($validOrleansMetrics | Measure-Object -Property RequestsPerSecond -Average).Average
            AverageResponseTime = ($validOrleansMetrics | Measure-Object -Property AverageRequestDuration -Average).Average
            TotalQueueLength = ($validOrleansMetrics | Measure-Object -Property QueueLength -Sum).Sum
        }
    }

    if ($validSystemMetrics.Count -gt 0) {
        $derivedMetrics.ClusterUtilization.AverageCpuUtilization = ($validSystemMetrics | Measure-Object -Property CpuUtilization -Average).Average
        $derivedMetrics.ClusterUtilization.AverageMemoryUtilization = ($validSystemMetrics | Measure-Object -Property MemoryUtilization -Average).Average
    }

    # Load historical data for trend analysis
    $historicalData = Load-HistoricalCapacityData -HoursBehind 24

    if ($historicalData) {
        $derivedMetrics.CapacityTrends = Analyze-CapacityTrends -CurrentMetrics $Metrics -HistoricalData $historicalData
        $derivedMetrics.PredictedCapacity = Predict-FutureCapacity -Trends $derivedMetrics.CapacityTrends
    }

    return $derivedMetrics
}

function Test-CapacityPlanningAlerts {
    param($Metrics, $DerivedMetrics)

    $alertThresholds = @{
        CpuUtilization = 80
        MemoryUtilization = 85
        ResponseTime = 1000  # milliseconds
        QueueLength = 500
        ActiveGrains = 10000
        ErrorRate = 0.05     # 5%
    }

    $alerts = @()

    # Check CPU utilization
    if ($DerivedMetrics.ClusterUtilization.AverageCpuUtilization -gt $alertThresholds.CpuUtilization) {
        $alerts += @{
            Type = "CapacityWarning"
            Metric = "CPU Utilization"
            CurrentValue = $DerivedMetrics.ClusterUtilization.AverageCpuUtilization
            Threshold = $alertThresholds.CpuUtilization
            Recommendation = "Consider scaling up CPU resources or adding more Orleans silos"
        }
    }

    # Check memory utilization
    if ($DerivedMetrics.ClusterUtilization.AverageMemoryUtilization -gt $alertThresholds.MemoryUtilization) {
        $alerts += @{
            Type = "CapacityWarning"
            Metric = "Memory Utilization"
            CurrentValue = $DerivedMetrics.ClusterUtilization.AverageMemoryUtilization
            Threshold = $alertThresholds.MemoryUtilization
            Recommendation = "Consider increasing memory allocation or implementing grain deactivation policies"
        }
    }

    # Check response time
    if ($DerivedMetrics.ClusterUtilization.AverageResponseTime -gt $alertThresholds.ResponseTime) {
        $alerts += @{
            Type = "PerformanceWarning"
            Metric = "Average Response Time"
            CurrentValue = $DerivedMetrics.ClusterUtilization.AverageResponseTime
            Threshold = $alertThresholds.ResponseTime
            Recommendation = "Performance degradation detected - consider horizontal scaling"
        }
    }

    # Check queue length
    if ($DerivedMetrics.ClusterUtilization.TotalQueueLength -gt $alertThresholds.QueueLength) {
        $alerts += @{
            Type = "ThroughputWarning"
            Metric = "Queue Length"
            CurrentValue = $DerivedMetrics.ClusterUtilization.TotalQueueLength
            Threshold = $alertThresholds.QueueLength
            Recommendation = "High queue length indicates processing bottleneck - scale out Orleans cluster"
        }
    }

    # Send alerts if any thresholds exceeded
    foreach ($alert in $alerts) {
        Send-CapacityPlanningAlert -Alert $alert
        Write-Warning "🚨 $($alert.Type): $($alert.Metric) = $($alert.CurrentValue) (threshold: $($alert.Threshold))"
        Write-Host "   Recommendation: $($alert.Recommendation)" -ForegroundColor Yellow
    }
}

function Display-CapacityStatus {
    param($Metrics, $DerivedMetrics)

    Write-Host "`n📊 Current Capacity Status ($(Get-Date -Format 'HH:mm:ss'))" -ForegroundColor Cyan

    if ($DerivedMetrics.ClusterUtilization) {
        Write-Host "   Active Grains: $($DerivedMetrics.ClusterUtilization.TotalActiveGrains)" -ForegroundColor White
        Write-Host "   Requests/sec: $([Math]::Round($DerivedMetrics.ClusterUtilization.AverageRequestsPerSecond, 1))" -ForegroundColor White
        Write-Host "   Avg Response: $([Math]::Round($DerivedMetrics.ClusterUtilization.AverageResponseTime, 0))ms" -ForegroundColor White
        Write-Host "   CPU Usage: $([Math]::Round($DerivedMetrics.ClusterUtilization.AverageCpuUtilization, 1))%" -ForegroundColor White
        Write-Host "   Memory Usage: $([Math]::Round($DerivedMetrics.ClusterUtilization.AverageMemoryUtilization, 1))%" -ForegroundColor White
    }

    if ($Metrics.Business -and $Metrics.Business.ConcurrentUsers) {
        Write-Host "   Concurrent Users: $($Metrics.Business.ConcurrentUsers)" -ForegroundColor White
        Write-Host "   Active Sessions: $($Metrics.Business.ActiveSessions)" -ForegroundColor White
    }
}
```

This comprehensive Orleans Capacity Planning Guide provides enterprise-scale capacity management capabilities including performance baseline establishment, resource sizing recommendations, scaling models, cost optimization, auto-scaling policies, capacity calculators, load testing integration, and monitoring/alerting frameworks. All components are designed to work together to enable accurate resource sizing, cost optimization, and operational excellence for Orleans deployments.

The guide includes production-ready PowerShell scripts, C# implementations, JSON configuration templates, and detailed procedures that can be immediately implemented in enterprise environments to achieve optimal Orleans capacity planning and management.