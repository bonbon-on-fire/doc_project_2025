param(
	[switch]$Watch
)

$env:ASPNETCORE_ENVIRONMENT = 'Test'
$env:ASPNETCORE_URLS = 'http://localhost:5099'
$env:LLM_API_KEY = 'DUMMY'

$dll = Join-Path $PSScriptRoot 'bin\Debug\net9.0\AIChat.Server.dll'
Write-Host "Starting AIChat.Server in Test mode on http://localhost:5099 ..."
$outLog = Join-Path $PSScriptRoot 'test-mode.out.log'
$errLog = Join-Path $PSScriptRoot 'test-mode.err.log'
if (Test-Path $outLog) { Remove-Item $outLog -Force }
if (Test-Path $errLog) { Remove-Item $errLog -Force }

dotnet build "$PSScriptRoot\AIChat.Server.csproj" | Out-Null

if ($Watch) {
	# Use watch without launch profile so appsettings.Test Urls are honored
	$env:DOTNET_LAUNCH_PROFILE = ''
	dotnet watch run --no-launch-profile --project "$PSScriptRoot\AIChat.Server.csproj"
}
else {
	# Run DLL directly to bypass launchSettings and honor appsettings.Test.json
	dotnet $dll
}

# $p = Start-Process -FilePath 'dotnet' -ArgumentList @("$dll") -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
# if ($p) { Write-Host "Server PID: $($p.Id). Logs: $outLog, $errLog" } else { Write-Host 'Failed to start server process.' }
