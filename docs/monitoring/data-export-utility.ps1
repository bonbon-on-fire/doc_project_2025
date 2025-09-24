# Orleans Data Export Utility
# This script provides comprehensive data export functionality for Orleans monitoring data

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("csv", "json", "excel", "prometheus")]
    [string]$ExportFormat,

    [Parameter(Mandatory = $false)]
    [string]$PrometheusUrl = "http://localhost:9090",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "./exports",

    [Parameter(Mandatory = $false)]
    [int]$TimeRangeHours = 24,

    [Parameter(Mandatory = $false)]
    [string]$GrainType = ".*",

    [Parameter(Mandatory = $false)]
    [string]$OperationType = ".*",

    [Parameter(Mandatory = $false)]
    [string]$MetricFilter = "",

    [Parameter(Mandatory = $false)]
    [int]$MaxDataPoints = 10000,

    [Parameter(Mandatory = $false)]
    [string]$CustomQuery = "",

    [Parameter(Mandatory = $false)]
    [switch]$IncludeRawData,

    [Parameter(Mandatory = $false)]
    [switch]$CompressOutput
)

# Required assemblies
Add-Type -AssemblyName System.Web

# Predefined metric queries for Orleans data export
$OrleansMetrics = @{
    "grain_activations" = "rate(orleans_grain_activations_total[5m])"
    "grain_deactivations" = "rate(orleans_grain_deactivations_total[5m])"
    "operation_latency_p95" = "histogram_quantile(0.95, rate(orleans_grain_operation_duration_seconds_bucket[5m])) * 1000"
    "operation_latency_p99" = "histogram_quantile(0.99, rate(orleans_grain_operation_duration_seconds_bucket[5m])) * 1000"
    "operation_latency_p50" = "histogram_quantile(0.50, rate(orleans_grain_operation_duration_seconds_bucket[5m])) * 1000"
    "error_rate" = "rate(orleans_grain_operation_errors_total[5m]) / rate(orleans_grain_operation_duration_seconds_count[5m]) * 100"
    "throughput" = "rate(orleans_grain_operation_duration_seconds_count[5m])"
    "active_operations" = "orleans_grain_active_operations"
    "active_connections" = "orleans_grain_active_connections"
    "state_size" = "orleans_grain_state_size_bytes"
    "activation_duration" = "histogram_quantile(0.95, rate(orleans_grain_activation_duration_seconds_bucket[5m])) * 1000"
}

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN" { "Yellow" }
        "SUCCESS" { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Get-PrometheusRangeData {
    param([string]$Query, [string]$MetricName)

    try {
        $encodedQuery = [System.Web.HttpUtility]::UrlEncode($Query)
        $endTime = Get-Date
        $startTime = $endTime.AddHours(-$TimeRangeHours)

        # Calculate step size based on time range to limit data points
        $totalSeconds = ($endTime - $startTime).TotalSeconds
        $step = [math]::Max(15, [math]::Floor($totalSeconds / $MaxDataPoints))

        $url = "$PrometheusUrl/api/v1/query_range?query=$encodedQuery&start=$($startTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.000Z'))&end=$($endTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.000Z'))&step=${step}s"

        Write-Log "Querying: $MetricName (step: ${step}s)" "INFO"
        $response = Invoke-RestMethod -Uri $url -Method Get

        if ($response.status -eq "success") {
            Write-Log "✓ Retrieved $($response.data.result.Count) series for: $MetricName" "SUCCESS"
            return @{
                MetricName = $MetricName
                Query = $Query
                Data = $response.data.result
                Timestamp = Get-Date
                TimeRange = "$TimeRangeHours hours"
                StartTime = $startTime
                EndTime = $endTime
                Step = $step
            }
        }
        else {
            Write-Log "Failed to retrieve data for $MetricName`: $($response.error)" "ERROR"
            return $null
        }
    }
    catch {
        Write-Log "Error querying Prometheus for $MetricName`: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Get-PrometheusInstantData {
    param([string]$Query, [string]$MetricName)

    try {
        $encodedQuery = [System.Web.HttpUtility]::UrlEncode($Query)
        $url = "$PrometheusUrl/api/v1/query?query=$encodedQuery"

        $response = Invoke-RestMethod -Uri $url -Method Get

        if ($response.status -eq "success") {
            Write-Log "✓ Retrieved instant data for: $MetricName" "SUCCESS"
            return @{
                MetricName = $MetricName
                Query = $Query
                Data = $response.data.result
                Timestamp = Get-Date
                Type = "instant"
            }
        }
        else {
            Write-Log "Failed to retrieve instant data for $MetricName`: $($response.error)" "ERROR"
            return $null
        }
    }
    catch {
        Write-Log "Error querying Prometheus for $MetricName`: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Export-ToCSV {
    param([array]$MetricsData, [string]$OutputFile)

    try {
        $csvData = @()

        foreach ($metricSet in $MetricsData) {
            if (-not $metricSet) { continue }

            foreach ($series in $metricSet.Data) {
                $labels = $series.metric | ConvertTo-Json -Compress

                if ($series.values) {
                    # Time series data
                    foreach ($point in $series.values) {
                        $csvData += [PSCustomObject]@{
                            MetricName = $metricSet.MetricName
                            Timestamp = [DateTimeOffset]::FromUnixTimeSeconds([long]$point[0]).ToString("yyyy-MM-dd HH:mm:ss")
                            Value = [double]$point[1]
                            Labels = $labels
                            GrainType = if ($series.metric.grain_type) { $series.metric.grain_type } else { "" }
                            OperationType = if ($series.metric.operation_type) { $series.metric.operation_type } else { "" }
                            GrainId = if ($series.metric.grain_id) { $series.metric.grain_id } else { "" }
                        }
                    }
                }
                elseif ($series.value) {
                    # Instant data
                    $csvData += [PSCustomObject]@{
                        MetricName = $metricSet.MetricName
                        Timestamp = [DateTimeOffset]::FromUnixTimeSeconds([long]$series.value[0]).ToString("yyyy-MM-dd HH:mm:ss")
                        Value = [double]$series.value[1]
                        Labels = $labels
                        GrainType = if ($series.metric.grain_type) { $series.metric.grain_type } else { "" }
                        OperationType = if ($series.metric.operation_type) { $series.metric.operation_type } else { "" }
                        GrainId = if ($series.metric.grain_id) { $series.metric.grain_id } else { "" }
                    }
                }
            }
        }

        $csvData | Export-Csv -Path $OutputFile -NoTypeInformation -Encoding UTF8
        Write-Log "CSV export completed: $OutputFile ($($csvData.Count) data points)" "SUCCESS"
        return $OutputFile
    }
    catch {
        Write-Log "Failed to export CSV: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Export-ToJSON {
    param([array]$MetricsData, [string]$OutputFile)

    try {
        $exportData = @{
            ExportInfo = @{
                ExportTime = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
                TimeRange = "$TimeRangeHours hours"
                ExportFormat = "JSON"
                PrometheusUrl = $PrometheusUrl
                Filters = @{
                    GrainType = $GrainType
                    OperationType = $OperationType
                    MetricFilter = $MetricFilter
                }
            }
            Metrics = @()
        }

        foreach ($metricSet in $MetricsData) {
            if (-not $metricSet) { continue }

            $metricExport = @{
                MetricName = $metricSet.MetricName
                Query = $metricSet.Query
                CollectionTime = $metricSet.Timestamp
                TimeRange = $metricSet.TimeRange
                Series = @()
            }

            foreach ($series in $metricSet.Data) {
                $seriesExport = @{
                    Labels = $series.metric
                    DataPoints = @()
                }

                if ($series.values) {
                    foreach ($point in $series.values) {
                        $seriesExport.DataPoints += @{
                            Timestamp = [DateTimeOffset]::FromUnixTimeSeconds([long]$point[0]).ToString("yyyy-MM-ddTHH:mm:ssZ")
                            UnixTimestamp = [long]$point[0]
                            Value = [double]$point[1]
                        }
                    }
                }
                elseif ($series.value) {
                    $seriesExport.DataPoints += @{
                        Timestamp = [DateTimeOffset]::FromUnixTimeSeconds([long]$series.value[0]).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        UnixTimestamp = [long]$series.value[0]
                        Value = [double]$series.value[1]
                    }
                }

                $metricExport.Series += $seriesExport
            }

            $exportData.Metrics += $metricExport
        }

        $json = $exportData | ConvertTo-Json -Depth 10
        Set-Content -Path $OutputFile -Value $json -Encoding UTF8

        $totalDataPoints = ($exportData.Metrics | ForEach-Object { $_.Series | ForEach-Object { $_.DataPoints.Count } } | Measure-Object -Sum).Sum
        Write-Log "JSON export completed: $OutputFile ($totalDataPoints data points)" "SUCCESS"
        return $OutputFile
    }
    catch {
        Write-Log "Failed to export JSON: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Export-ToExcel {
    param([array]$MetricsData, [string]$OutputFile)

    try {
        # This is a simplified Excel export using CSV format
        # In a full implementation, you would use Excel COM objects or ClosedXML

        $csvFile = $OutputFile -replace "\.xlsx$", ".csv"
        $exportedFile = Export-ToCSV -MetricsData $MetricsData -OutputFile $csvFile

        if ($exportedFile) {
            # Create a placeholder Excel file with instructions
            $excelContent = @"
Excel Export for Orleans Metrics Data
====================================

The data has been exported to CSV format: $csvFile

To create a proper Excel file, you can:

1. Open the CSV file in Excel and save as .xlsx
2. Use PowerShell with Excel COM objects:
   `$excel = New-Object -ComObject Excel.Application`
   `$workbook = $excel.Workbooks.Open('$csvFile')`
   `$workbook.SaveAs('$OutputFile', 51)`  # 51 = xlWorkbookDefault
   `$excel.Quit()`

3. Use third-party libraries like ClosedXML or EPPlus

The CSV file contains all the exported data in a structured format.
"@

            Set-Content -Path $OutputFile -Value $excelContent -Encoding UTF8
            Write-Log "Excel placeholder created: $OutputFile (CSV data: $csvFile)" "SUCCESS"
            return $OutputFile
        }
        else {
            return $null
        }
    }
    catch {
        Write-Log "Failed to export Excel: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Export-ToPrometheus {
    param([array]$MetricsData, [string]$OutputFile)

    try {
        $prometheusData = @()

        foreach ($metricSet in $MetricsData) {
            if (-not $metricSet) { continue }

            $prometheusData += "# HELP $($metricSet.MetricName) Exported Orleans metric: $($metricSet.MetricName)"
            $prometheusData += "# TYPE $($metricSet.MetricName) gauge"

            foreach ($series in $metricSet.Data) {
                $labelPairs = @()
                foreach ($key in $series.metric.PSObject.Properties.Name) {
                    $value = $series.metric.$key
                    $labelPairs += "$key=`"$value`""
                }
                $labelsString = if ($labelPairs.Count -gt 0) { "{" + ($labelPairs -join ",") + "}" } else { "" }

                if ($series.values) {
                    # Use the latest value for Prometheus format
                    $latestValue = $series.values[-1]
                    $prometheusData += "$($metricSet.MetricName)$labelsString $($latestValue[1]) $([long]($latestValue[0]) * 1000)"
                }
                elseif ($series.value) {
                    $prometheusData += "$($metricSet.MetricName)$labelsString $($series.value[1]) $([long]($series.value[0]) * 1000)"
                }
            }

            $prometheusData += ""
        }

        $content = $prometheusData -join "`n"
        Set-Content -Path $OutputFile -Value $content -Encoding UTF8

        Write-Log "Prometheus format export completed: $OutputFile" "SUCCESS"
        return $OutputFile
    }
    catch {
        Write-Log "Failed to export Prometheus format: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Get-FilteredMetrics {
    $metrics = $OrleansMetrics.Clone()

    if ($MetricFilter) {
        $filteredMetrics = @{}
        foreach ($key in $metrics.Keys) {
            if ($key -match $MetricFilter) {
                $filteredMetrics[$key] = $metrics[$key]
            }
        }
        $metrics = $filteredMetrics
    }

    # Apply grain type and operation type filters to queries
    foreach ($key in $metrics.Keys) {
        $query = $metrics[$key]

        if ($GrainType -ne ".*") {
            $query = $query -replace '(\{[^}]*)', "`$1,grain_type=~`"$GrainType`""
            if ($query -notmatch '\{') {
                $query = $query -replace '([a-z_]+)', "`$1{grain_type=~`"$GrainType`"}"
            }
        }

        if ($OperationType -ne ".*") {
            $query = $query -replace '(\{[^}]*)', "`$1,operation_type=~`"$OperationType`""
            if ($query -notmatch '\{') {
                $query = $query -replace '([a-z_]+)', "`$1{operation_type=~`"$OperationType`"}"
            }
        }

        $metrics[$key] = $query
    }

    return $metrics
}

function Compress-OutputFile {
    param([string]$FilePath)

    try {
        $compressedPath = "$FilePath.zip"
        Compress-Archive -Path $FilePath -DestinationPath $compressedPath -Force
        Remove-Item -Path $FilePath -Force
        Write-Log "Output compressed: $compressedPath" "SUCCESS"
        return $compressedPath
    }
    catch {
        Write-Log "Failed to compress output: $($_.Exception.Message)" "ERROR"
        return $FilePath
    }
}

function Main {
    Write-Log "Orleans Data Export Utility started" "INFO"
    Write-Log "Export Format: $ExportFormat | Time Range: $TimeRangeHours hours" "INFO"

    # Create output directory
    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
        Write-Log "Created output directory: $OutputPath" "INFO"
    }

    # Determine metrics to export
    $metricsToExport = if ($CustomQuery) {
        @{ "custom_query" = $CustomQuery }
    }
    else {
        Get-FilteredMetrics
    }

    Write-Log "Exporting $($metricsToExport.Count) metrics..." "INFO"

    # Collect data from Prometheus
    $allMetricsData = @()
    foreach ($metricName in $metricsToExport.Keys) {
        $query = $metricsToExport[$metricName]
        Write-Log "Collecting data for: $metricName" "INFO"

        $data = if ($IncludeRawData) {
            Get-PrometheusRangeData -Query $query -MetricName $metricName
        }
        else {
            Get-PrometheusInstantData -Query $query -MetricName $metricName
        }

        if ($data) {
            $allMetricsData += $data
        }
    }

    if ($allMetricsData.Count -eq 0) {
        Write-Log "No data collected. Exiting." "ERROR"
        exit 1
    }

    # Generate output filename
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $fileExtension = switch ($ExportFormat) {
        "csv" { "csv" }
        "json" { "json" }
        "excel" { "xlsx" }
        "prometheus" { "txt" }
    }

    $baseFileName = "orleans-metrics-export-$timestamp.$fileExtension"
    $outputFile = Join-Path $OutputPath $baseFileName

    # Export data in requested format
    $exportedFile = switch ($ExportFormat) {
        "csv" { Export-ToCSV -MetricsData $allMetricsData -OutputFile $outputFile }
        "json" { Export-ToJSON -MetricsData $allMetricsData -OutputFile $outputFile }
        "excel" { Export-ToExcel -MetricsData $allMetricsData -OutputFile $outputFile }
        "prometheus" { Export-ToPrometheus -MetricsData $allMetricsData -OutputFile $outputFile }
    }

    if ($exportedFile) {
        # Compress if requested
        $finalFile = if ($CompressOutput) {
            Compress-OutputFile -FilePath $exportedFile
        }
        else {
            $exportedFile
        }

        Write-Log "Data export completed successfully!" "SUCCESS"
        Write-Log "Output file: $finalFile" "SUCCESS"
        Write-Log "File size: $([math]::Round((Get-Item $finalFile).Length / 1MB, 2)) MB" "INFO"

        # Display summary
        $totalSeries = ($allMetricsData | ForEach-Object { $_.Data.Count } | Measure-Object -Sum).Sum
        Write-Log "Export Summary:" "INFO"
        Write-Log "  - Metrics exported: $($allMetricsData.Count)" "INFO"
        Write-Log "  - Time series: $totalSeries" "INFO"
        Write-Log "  - Time range: $TimeRangeHours hours" "INFO"
        Write-Log "  - Format: $($ExportFormat.ToUpper())" "INFO"
    }
    else {
        Write-Log "Data export failed!" "ERROR"
        exit 1
    }
}

# Execute main function
Main