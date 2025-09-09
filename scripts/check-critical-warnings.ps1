#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Checks for critical code quality warnings that must be fixed before checkin.

.DESCRIPTION
    This script builds the solution and checks for specific warnings that indicate
    code quality issues that must be resolved. These warnings are not auto-fixable
    and require manual intervention.

.EXAMPLE
    .\check-critical-warnings.ps1

.NOTES
    Exit codes:
    0 - No critical warnings found
    1 - Critical warnings found that must be fixed
#>

param(
    [string]$Project = ".",
    [switch]$Detailed
)

$ErrorActionPreference = 'Stop'

# Critical warnings that must be fixed before checkin
$CriticalWarnings = @(
    'CA1310',  # Specify StringComparison for correct behavior
    'CA1826',  # Use property instead of LINQ method (performance)
    'CA1513',  # Use ObjectDisposedException.ThrowIf (modern pattern)
    'CA1859',  # Use concrete types for performance
    'IDE0052', # Remove unread private members (dead code)
    'CA1866',  # Use char overload instead of string
    'CA1304',  # Specify CultureInfo
    'CA1305',  # Specify IFormatProvider
    'CA1309',  # Use ordinal string comparison
    'CA2201',  # Do not raise reserved exception types
    'CA1816',  # Call GC.SuppressFinalize correctly
    'CA1869',  # Cache JsonSerializerOptions
    'CA2254'   # Template should be a static expression for structured logging
)

Write-Host "🔍 Checking for critical code quality warnings..." -ForegroundColor Cyan
Write-Host "   These warnings must be fixed before checkin" -ForegroundColor Yellow
Write-Host ""

# Build and capture output
Write-Host "Building project..." -ForegroundColor Gray
$buildOutput = dotnet build $Project --no-incremental --verbosity normal 2>&1 | Out-String

# Parse warnings
$warnings = @()
$lines = $buildOutput -split "`n"

foreach ($line in $lines) {
    foreach ($warning in $CriticalWarnings) {
        if ($line -match "\b$warning\b") {
            $warnings += [PSCustomObject]@{
                Code = $warning
                Line = $line.Trim()
            }
        }
    }
}

# Group and display warnings
if ($warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "❌ CRITICAL WARNINGS FOUND - MUST FIX BEFORE CHECKIN" -ForegroundColor Red
    Write-Host "=" * 70 -ForegroundColor Red
    
    $groupedWarnings = $warnings | Group-Object -Property Code
    
    foreach ($group in $groupedWarnings) {
        $warningCode = $group.Name
        $count = $group.Count
        
        # Get description for each warning
        $description = switch ($warningCode) {
            'CA1310' { "Specify StringComparison for culture-correct string operations" }
            'CA1826' { "Use property instead of LINQ method for better performance" }
            'CA1513' { "Use ObjectDisposedException.ThrowIf for cleaner code" }
            'CA1859' { "Use concrete types when possible for improved performance" }
            'IDE0052' { "Remove unread private members (dead code)" }
            'CA1866' { "Use char overload instead of string with single char" }
            'CA1304' { "Specify CultureInfo for culture-sensitive operations" }
            'CA1305' { "Specify IFormatProvider for formatting operations" }
            'CA1309' { "Use ordinal string comparison for better performance" }
            'CA2201' { "Do not raise reserved exception types (Exception, SystemException)" }
            'CA1816' { "Dispose methods should call GC.SuppressFinalize" }
            'CA1869' { "Cache and reuse JsonSerializerOptions instances" }
            default { "Code quality issue" }
        }
        
        Write-Host ""
        Write-Host "⚠️  $warningCode ($count occurrences)" -ForegroundColor Yellow
        Write-Host "   $description" -ForegroundColor White
        
        if ($Detailed) {
            Write-Host "   Locations:" -ForegroundColor Gray
            foreach ($warning in $group.Group) {
                $location = $warning.Line
                if ($location -match '([^(]+)\((\d+),(\d+)\)') {
                    $file = $Matches[1]
                    $lineNum = $Matches[2]
                    $column = $Matches[3]
                    Write-Host "   - $file : Line $lineNum" -ForegroundColor DarkGray
                }
            }
        }
    }
    
    Write-Host ""
    Write-Host "=" * 70 -ForegroundColor Red
    Write-Host ""
    Write-Host "📋 HOW TO FIX:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "CA1310:" -ForegroundColor Yellow
    Write-Host "  Change: text.StartsWith(`"prefix`")" -ForegroundColor Gray
    Write-Host "  To:     text.StartsWith(`"prefix`", StringComparison.Ordinal)" -ForegroundColor Green
    Write-Host ""
    Write-Host "CA1826:" -ForegroundColor Yellow
    Write-Host "  Change: collection.FirstOrDefault()" -ForegroundColor Gray
    Write-Host "  To:     collection.Count > 0 ? collection[0] : default" -ForegroundColor Green
    Write-Host ""
    Write-Host "CA1513:" -ForegroundColor Yellow
    Write-Host "  Change: if (disposed) throw new ObjectDisposedException(...);" -ForegroundColor Gray
    Write-Host "  To:     ObjectDisposedException.ThrowIf(disposed, this);" -ForegroundColor Green
    Write-Host ""
    Write-Host "CA1859:" -ForegroundColor Yellow
    Write-Host "  Change: IEnumerable<T> Method() { return list; }" -ForegroundColor Gray
    Write-Host "  To:     List<T> Method() { return list; }" -ForegroundColor Green
    Write-Host ""
    Write-Host "IDE0052:" -ForegroundColor Yellow
    Write-Host "  Remove unused private fields/methods or add #pragma warning disable IDE0052" -ForegroundColor Gray
    Write-Host ""
    Write-Host "CA1305/CA1304:" -ForegroundColor Yellow
    Write-Host "  Change: value.ToString()" -ForegroundColor Gray
    Write-Host "  To:     value.ToString(CultureInfo.InvariantCulture)" -ForegroundColor Green
    Write-Host ""
    Write-Host "CA2254:" -ForegroundColor Yellow
    Write-Host "  Change: logger.LogError(dynamicMessage, args)" -ForegroundColor Gray
    Write-Host "  To:     logger.LogError(`"Static message template with {Placeholder}`", args)" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "Total critical warnings: $($warnings.Count)" -ForegroundColor Red
    Write-Host ""
    Write-Host "❌ Build validation FAILED - Fix these warnings before checkin" -ForegroundColor Red
    exit 1
}
else {
    Write-Host ""
    Write-Host "✅ No critical warnings found - code is ready for checkin" -ForegroundColor Green
    exit 0
}