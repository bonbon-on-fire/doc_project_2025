$url = "http://localhost:5099/api/chat/stream-sse"
$body = @{
    userId = "test-user"
    message = "What's the weather in San Francisco? Also calculate 15 + 25 for me."
} | ConvertTo-Json

Write-Host "Sending request to trigger tool calls..." -ForegroundColor Cyan
Write-Host "Request body: $body" -ForegroundColor Gray

$response = Invoke-WebRequest -Uri $url -Method POST -Body $body -ContentType "application/json" -TimeoutSec 10

Write-Host "`nResponse Status: $($response.StatusCode)" -ForegroundColor Green
Write-Host "Response Content:" -ForegroundColor Yellow
$response.Content

# Check logs for tool execution
Write-Host "`nChecking logs for tool execution..." -ForegroundColor Cyan
$logs = Get-Content "B:\sources\DOC_Project_2025\logs\server\app-test.jsonl" -Tail 50 | 
    Where-Object { $_ -match "FunctionCallMiddleware|tool|aggregate|weather|calculate" }

if ($logs) {
    Write-Host "Related log entries found:" -ForegroundColor Green
    $logs | ForEach-Object {
        $log = $_ | ConvertFrom-Json
        Write-Host "[$($log.'@t')] $($log.'@mt')" -ForegroundColor Gray
    }
} else {
    Write-Host "No related log entries found" -ForegroundColor Red
}