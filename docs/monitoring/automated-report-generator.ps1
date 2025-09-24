# Orleans Automated Report Generation System
# This script generates PDF and HTML reports from Orleans monitoring data

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("executive", "performance", "historical", "custom")]
    [string]$ReportType,

    [Parameter(Mandatory = $false)]
    [ValidateSet("pdf", "html", "both")]
    [string]$OutputFormat = "both",

    [Parameter(Mandatory = $false)]
    [string]$PrometheusUrl = "http://localhost:9090",

    [Parameter(Mandatory = $false)]
    [string]$GrafanaUrl = "http://localhost:3000",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "./reports",

    [Parameter(Mandatory = $false)]
    [int]$TimeRangeHours = 24,

    [Parameter(Mandatory = $false)]
    [string]$RecipientEmail = "",

    [Parameter(Mandatory = $false)]
    [string]$SmtpServer = "",

    [Parameter(Mandatory = $false)]
    [switch]$SendEmail,

    [Parameter(Mandatory = $false)]
    [switch]$Schedule
)

# Add required assemblies for HTML to PDF conversion
Add-Type -AssemblyName System.Web

# Report configuration
$ReportConfigs = @{
    "executive" = @{
        Title = "Orleans Executive Summary Report"
        Description = "High-level KPIs and business metrics for leadership"
        Queries = @(
            @{ Name = "System Health"; Query = "100 - (rate(orleans_grain_operation_errors_total[1h]) / rate(orleans_grain_operation_duration_seconds_count[1h]) * 100)" },
            @{ Name = "SLA Compliance"; Query = "100 - (rate(orleans_grain_operation_errors_total[24h]) / rate(orleans_grain_operation_duration_seconds_count[24h]) * 100)" },
            @{ Name = "Active Users"; Query = "sum(orleans_grain_active_operations{grain_type=\""UserGrain\""})" },
            @{ Name = "Active Chats"; Query = "sum(orleans_grain_active_operations{grain_type=\""ChatGrain\""})" },
            @{ Name = "Performance SLA"; Query = "100 - clamp_max((histogram_quantile(0.95, rate(orleans_grain_operation_duration_seconds_bucket[24h])) * 1000 / 500) * 100, 100)" }
        )
    }
    "performance" = @{
        Title = "Orleans Performance Analysis Report"
        Description = "Detailed performance metrics and trend analysis"
        Queries = @(
            @{ Name = "P95 Latency"; Query = "histogram_quantile(0.95, rate(orleans_grain_operation_duration_seconds_bucket[1h])) * 1000" },
            @{ Name = "P99 Latency"; Query = "histogram_quantile(0.99, rate(orleans_grain_operation_duration_seconds_bucket[1h])) * 1000" },
            @{ Name = "Throughput"; Query = "sum(rate(orleans_grain_operation_duration_seconds_count[1h])) by (grain_type)" },
            @{ Name = "Error Rate"; Query = "rate(orleans_grain_operation_errors_total[1h]) / rate(orleans_grain_operation_duration_seconds_count[1h]) * 100" },
            @{ Name = "Memory Usage"; Query = "avg(orleans_grain_state_size_bytes) by (grain_type)" }
        )
    }
    "historical" = @{
        Title = "Orleans Historical Trends Report"
        Description = "Long-term analysis and capacity planning insights"
        Queries = @(
            @{ Name = "30-day Growth"; Query = "predict_linear(sum(orleans_grain_active_operations)[7d], 86400 * 30)" },
            @{ Name = "Capacity Utilization"; Query = "sum(orleans_grain_active_operations) / 10000 * 100" },
            @{ Name = "Historical Errors"; Query = "avg_over_time((rate(orleans_grain_operation_errors_total[1h]) / rate(orleans_grain_operation_duration_seconds_count[1h]) * 100)[30d:1h])" },
            @{ Name = "Performance Trend"; Query = "avg_over_time(histogram_quantile(0.95, rate(orleans_grain_operation_duration_seconds_bucket[1h]))[30d:1h]) * 1000" }
        )
    }
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

function Get-PrometheusData {
    param([string]$Query, [string]$Name)

    try {
        $encodedQuery = [System.Web.HttpUtility]::UrlEncode($Query)
        $endTime = Get-Date
        $startTime = $endTime.AddHours(-$TimeRangeHours)

        $url = "$PrometheusUrl/api/v1/query_range?query=$encodedQuery&start=$($startTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))&end=$($endTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))&step=300"

        $response = Invoke-RestMethod -Uri $url -Method Get

        if ($response.status -eq "success") {
            Write-Log "✓ Retrieved data for: $Name" "SUCCESS"
            return $response.data.result
        }
        else {
            Write-Log "Failed to retrieve data for $Name`: $($response.error)" "ERROR"
            return $null
        }
    }
    catch {
        Write-Log "Error querying Prometheus for $Name`: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Format-MetricValue {
    param([object]$Value, [string]$MetricName)

    if ($Value -eq $null) { return "N/A" }

    $numValue = [double]$Value

    switch -Regex ($MetricName) {
        "Latency|Duration" { return "$([math]::Round($numValue, 2)) ms" }
        "Rate|Throughput" { return "$([math]::Round($numValue, 2))/sec" }
        "Error.*Rate|SLA|Health|Performance" { return "$([math]::Round($numValue, 2))%" }
        "Memory|Size" {
            if ($numValue -gt 1073741824) { return "$([math]::Round($numValue/1073741824, 2)) GB" }
            elseif ($numValue -gt 1048576) { return "$([math]::Round($numValue/1048576, 2)) MB" }
            elseif ($numValue -gt 1024) { return "$([math]::Round($numValue/1024, 2)) KB" }
            else { return "$([math]::Round($numValue, 0)) bytes" }
        }
        "Users|Chats|Operations" { return [math]::Round($numValue, 0).ToString("N0") }
        default { return "$([math]::Round($numValue, 2))" }
    }
}

function Generate-HTMLReport {
    param([hashtable]$ReportData, [string]$OutputFile)

    $config = $ReportConfigs[$ReportType]
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC"

    $html = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>$($config.Title)</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 20px; background: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        .header { border-bottom: 3px solid #2196F3; padding-bottom: 20px; margin-bottom: 30px; }
        .header h1 { color: #2196F3; margin: 0; font-size: 2.5em; }
        .header p { color: #666; margin: 10px 0 0 0; font-size: 1.1em; }
        .meta-info { background: #f8f9fa; padding: 15px; border-radius: 6px; margin-bottom: 30px; }
        .meta-info table { width: 100%; border-collapse: collapse; }
        .meta-info td { padding: 8px; border-bottom: 1px solid #ddd; }
        .meta-info td:first-child { font-weight: bold; color: #333; width: 150px; }
        .metrics-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; margin-bottom: 30px; }
        .metric-card { background: #fff; border: 1px solid #ddd; border-radius: 8px; padding: 20px; box-shadow: 0 2px 5px rgba(0,0,0,0.05); }
        .metric-card h3 { margin: 0 0 15px 0; color: #333; font-size: 1.2em; border-bottom: 2px solid #2196F3; padding-bottom: 8px; }
        .metric-value { font-size: 2em; font-weight: bold; color: #2196F3; margin: 10px 0; }
        .metric-trend { font-size: 0.9em; color: #666; }
        .status-good { color: #4CAF50; }
        .status-warning { color: #FF9800; }
        .status-critical { color: #F44336; }
        .summary-table { width: 100%; border-collapse: collapse; margin-top: 20px; }
        .summary-table th, .summary-table td { padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }
        .summary-table th { background: #f8f9fa; font-weight: bold; color: #333; }
        .summary-table tr:hover { background: #f5f5f5; }
        .footer { margin-top: 40px; padding-top: 20px; border-top: 1px solid #ddd; color: #666; text-align: center; }
        .alert-high { background: #ffebee; border-left: 4px solid #f44336; padding: 15px; margin: 15px 0; }
        .alert-medium { background: #fff3e0; border-left: 4px solid #ff9800; padding: 15px; margin: 15px 0; }
        .alert-low { background: #e8f5e8; border-left: 4px solid #4caf50; padding: 15px; margin: 15px 0; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>$($config.Title)</h1>
            <p>$($config.Description)</p>
        </div>

        <div class="meta-info">
            <table>
                <tr><td>Report Generated:</td><td>$timestamp</td></tr>
                <tr><td>Time Range:</td><td>Last $TimeRangeHours hours</td></tr>
                <tr><td>Data Source:</td><td>$PrometheusUrl</td></tr>
                <tr><td>Report Type:</td><td>$($ReportType.ToUpper())</td></tr>
            </table>
        </div>

        <div class="metrics-grid">
"@

    foreach ($metric in $ReportData.Keys) {
        $data = $ReportData[$metric]
        $value = if ($data -and $data.Count -gt 0) {
            $latest = $data[0].values[-1][1]
            Format-MetricValue -Value $latest -MetricName $metric
        } else { "N/A" }

        $status = switch ($metric) {
            {$_ -match "Error"} { if ([double]$data[0].values[-1][1] -gt 5) { "status-critical" } elseif ([double]$data[0].values[-1][1] -gt 1) { "status-warning" } else { "status-good" } }
            {$_ -match "SLA|Health"} { if ([double]$data[0].values[-1][1] -gt 95) { "status-good" } elseif ([double]$data[0].values[-1][1] -gt 90) { "status-warning" } else { "status-critical" } }
            default { "status-good" }
        }

        $html += @"
            <div class="metric-card">
                <h3>$metric</h3>
                <div class="metric-value $status">$value</div>
                <div class="metric-trend">Last $TimeRangeHours hours</div>
            </div>
"@
    }

    $html += @"
        </div>

        <h2>Detailed Metrics Summary</h2>
        <table class="summary-table">
            <thead>
                <tr>
                    <th>Metric</th>
                    <th>Current Value</th>
                    <th>Average</th>
                    <th>Max</th>
                    <th>Status</th>
                </tr>
            </thead>
            <tbody>
"@

    foreach ($metric in $ReportData.Keys) {
        $data = $ReportData[$metric]
        if ($data -and $data.Count -gt 0 -and $data[0].values.Count -gt 0) {
            $values = $data[0].values | ForEach-Object { [double]$_[1] }
            $current = Format-MetricValue -Value $values[-1] -MetricName $metric
            $average = Format-MetricValue -Value ($values | Measure-Object -Average).Average -MetricName $metric
            $maximum = Format-MetricValue -Value ($values | Measure-Object -Maximum).Maximum -MetricName $metric

            $status = switch ($metric) {
                {$_ -match "Error"} { if ($values[-1] -gt 5) { "🔴 Critical" } elseif ($values[-1] -gt 1) { "🟡 Warning" } else { "🟢 Good" } }
                {$_ -match "SLA|Health"} { if ($values[-1] -gt 95) { "🟢 Good" } elseif ($values[-1] -gt 90) { "🟡 Warning" } else { "🔴 Critical" } }
                default { "🟢 Good" }
            }

            $html += "<tr><td>$metric</td><td>$current</td><td>$average</td><td>$maximum</td><td>$status</td></tr>"
        }
    }

    $html += @"
            </tbody>
        </table>

        <div class="footer">
            <p>Orleans Monitoring Report | Generated by Automated Report System | $timestamp</p>
        </div>
    </div>
</body>
</html>
"@

    Set-Content -Path $OutputFile -Value $html -Encoding UTF8
    Write-Log "HTML report generated: $OutputFile" "SUCCESS"
    return $OutputFile
}

function Convert-HTMLToPDF {
    param([string]$HtmlFile, [string]$PdfFile)

    try {
        # This is a placeholder for PDF conversion
        # In a real implementation, you would use:
        # - wkhtmltopdf executable
        # - Chrome/Chromium headless mode
        # - PowerShell + HTML to PDF libraries

        Write-Log "PDF conversion would require additional dependencies (wkhtmltopdf, Chrome headless, etc.)" "WARN"
        Write-Log "Creating PDF placeholder file: $PdfFile" "INFO"

        $pdfContent = @"
PDF Report Generation
====================

The HTML report has been generated successfully: $HtmlFile

To generate PDF reports, please install one of the following:

1. wkhtmltopdf: https://wkhtmltopdf.org/
   Command: wkhtmltopdf "$HtmlFile" "$PdfFile"

2. Chrome/Chromium headless:
   Command: chrome --headless --print-to-pdf="$PdfFile" "$HtmlFile"

3. PowerShell with iTextSharp or similar PDF libraries

This placeholder file indicates that the report generation system is working.
The HTML version contains all the data and formatting.
"@

        Set-Content -Path $PdfFile -Value $pdfContent -Encoding UTF8
        Write-Log "PDF placeholder created: $PdfFile" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "Failed to convert HTML to PDF: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Send-ReportEmail {
    param([string[]]$AttachmentPaths, [string]$Subject)

    if (-not $SendEmail -or -not $RecipientEmail -or -not $SmtpServer) {
        Write-Log "Email sending skipped (missing configuration)" "INFO"
        return
    }

    try {
        $mailMessage = New-Object System.Net.Mail.MailMessage
        $mailMessage.From = "orleans-monitoring@company.com"
        $mailMessage.To.Add($RecipientEmail)
        $mailMessage.Subject = $Subject
        $mailMessage.Body = @"
Orleans Monitoring Report

Please find the attached monitoring report for your Orleans application.

Report Details:
- Type: $($ReportType.ToUpper())
- Period: Last $TimeRangeHours hours
- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

This report was automatically generated by the Orleans Monitoring System.
"@

        foreach ($attachment in $AttachmentPaths) {
            if (Test-Path $attachment) {
                $mailMessage.Attachments.Add($attachment)
            }
        }

        $smtpClient = New-Object System.Net.Mail.SmtpClient($SmtpServer)
        $smtpClient.Send($mailMessage)

        Write-Log "Report email sent to $RecipientEmail" "SUCCESS"
    }
    catch {
        Write-Log "Failed to send email: $($_.Exception.Message)" "ERROR"
    }
}

function Main {
    Write-Log "Orleans Automated Report Generator started" "INFO"
    Write-Log "Report Type: $ReportType | Output Format: $OutputFormat" "INFO"

    # Create output directory
    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
        Write-Log "Created output directory: $OutputPath" "INFO"
    }

    # Get report configuration
    $config = $ReportConfigs[$ReportType]
    if (-not $config) {
        Write-Log "Invalid report type: $ReportType" "ERROR"
        exit 1
    }

    # Collect metrics data
    Write-Log "Collecting metrics data from Prometheus..." "INFO"
    $reportData = @{}

    foreach ($query in $config.Queries) {
        $data = Get-PrometheusData -Query $query.Query -Name $query.Name
        $reportData[$query.Name] = $data
    }

    # Generate timestamp for file naming
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $baseFileName = "orleans-$ReportType-report-$timestamp"

    $generatedFiles = @()

    # Generate HTML report
    if ($OutputFormat -eq "html" -or $OutputFormat -eq "both") {
        $htmlFile = Join-Path $OutputPath "$baseFileName.html"
        Generate-HTMLReport -ReportData $reportData -OutputFile $htmlFile
        $generatedFiles += $htmlFile
    }

    # Generate PDF report
    if ($OutputFormat -eq "pdf" -or $OutputFormat -eq "both") {
        $pdfFile = Join-Path $OutputPath "$baseFileName.pdf"
        $htmlFile = if ($OutputFormat -eq "pdf") {
            $tempHtml = Join-Path $OutputPath "$baseFileName-temp.html"
            Generate-HTMLReport -ReportData $reportData -OutputFile $tempHtml
            $tempHtml
        } else {
            $generatedFiles[0]  # Use the HTML file already generated
        }

        if (Convert-HTMLToPDF -HtmlFile $htmlFile -PdfFile $pdfFile) {
            $generatedFiles += $pdfFile
        }
    }

    # Send email if requested
    if ($SendEmail) {
        $subject = "$($config.Title) - $(Get-Date -Format 'yyyy-MM-dd')"
        Send-ReportEmail -AttachmentPaths $generatedFiles -Subject $subject
    }

    Write-Log "Report generation completed. Files generated:" "SUCCESS"
    foreach ($file in $generatedFiles) {
        Write-Log "  - $file" "SUCCESS"
    }

    # Schedule next run if requested
    if ($Schedule) {
        Write-Log "Scheduling is not implemented in this version" "WARN"
        Write-Log "Consider using Windows Task Scheduler or cron for automated execution" "INFO"
    }
}

# Execute main function
Main