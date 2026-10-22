<#
.SYNOPSIS
    Waits until every submitted wager has been stored, then prints a reconciliation summary.

.DESCRIPTION
    Polls GET /api/admin/stats once a second. Start it right before (or together with) the tester so the elapsed
    time approximates "first wager sent -> last wager stored". It finishes when the queue is empty and the stored
    count has reached Baseline + Expected, and exits with code 0 only if nothing is missing and nothing was
    dead-lettered.

.PARAMETER Expected
    Wagers the tester submitted successfully (the tester prints the OK count; its scenario sends 7000).

.PARAMETER Baseline
    Wagers already in the database before the run (0 on a fresh database).

.EXAMPLE
    .\scripts\Wait-ForDrain.ps1 -Expected 7000
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5000',
    [int]    $Expected = 7000,
    [int]    $Baseline = 0,
    [int]    $TimeoutSeconds = 600,
    [int]    $PollMilliseconds = 1000
)

$ErrorActionPreference = 'Stop'
$target = $Baseline + $Expected
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$stats = $null

Write-Host "Waiting for $Expected wagers to be stored (baseline $Baseline) at $BaseUrl ..."

while ($true) {
    try {
        $stats = Invoke-RestMethod -Uri "$BaseUrl/api/admin/stats" -TimeoutSec 10
        Write-Host ('{0,7:n1}s  stored {1,8:n0}  queued {2,8:n0}  dead-lettered {3,5:n0}' -f `
                $clock.Elapsed.TotalSeconds, $stats.storedWagers, $stats.queuedWagers, $stats.deadLetteredWagers)

        if ($stats.queuedWagers -eq 0 -and $stats.storedWagers -ge $target) { break }
    }
    catch {
        Write-Host ('{0,7:n1}s  stats endpoint not reachable yet: {1}' -f $clock.Elapsed.TotalSeconds, $_.Exception.Message)
    }

    if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
        Write-Host "Timed out after $TimeoutSeconds s." -ForegroundColor Red
        break
    }

    Start-Sleep -Milliseconds $PollMilliseconds
}

$clock.Stop()
if ($null -eq $stats) { Write-Host 'No statistics could be read.' -ForegroundColor Red; exit 2 }

$stored = $stats.storedWagers - $Baseline
$missing = $Expected - $stored

Write-Host ''
Write-Host '--- Reconciliation ---------------------------------'
Write-Host ('Submitted (expected) : {0,8:n0}' -f $Expected)
Write-Host ('Stored this run      : {0,8:n0}' -f $stored)
Write-Host ('Still queued         : {0,8:n0}' -f $stats.queuedWagers)
Write-Host ('Dead-lettered        : {0,8:n0}' -f $stats.deadLetteredWagers)
Write-Host ('Missing              : {0,8:n0}' -f $missing)
Write-Host ('Elapsed              : {0,8:n1} s' -f $clock.Elapsed.TotalSeconds)

if ($missing -eq 0 -and $stats.deadLetteredWagers -eq 0 -and $stats.queuedWagers -eq 0) {
    Write-Host 'RESULT: every submitted wager was stored.' -ForegroundColor Green
    exit 0
}

Write-Host 'RESULT: wagers are missing, queued or dead-lettered - investigate.' -ForegroundColor Red
exit 1
