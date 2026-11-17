[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{32}$')]
    [string] $RunId
)

$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)
{
    throw 'The local process-tree cleanup script currently requires Windows PowerShell or PowerShell on Windows.'
}

$runDirectory = Join-Path $PSScriptRoot ".local-runs\$RunId"
$manifestPath = Join-Path $runDirectory 'run.json'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf))
{
    throw "No owned local run manifest exists for run id '$RunId'."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Owner -ne 'PizzaOps.Start-Local' -or $manifest.RunId -ne $RunId)
{
    throw 'The local run manifest does not match the expected owner; no processes or files were removed.'
}

$processes = @(Get-CimInstance -ClassName Win32_Process -Property ProcessId, ParentProcessId)
$ownedIds = [System.Collections.Generic.HashSet[int]]::new()
$rootIds = [System.Collections.Generic.HashSet[int]]::new()

foreach ($entry in $manifest.Processes)
{
    $pidValue = [int] $entry.ProcessId
    $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    if ($null -eq $process)
    {
        continue
    }

    $expectedStart = ([DateTime] $entry.StartTimeUtc).ToUniversalTime()
    if ([Math]::Abs(($process.StartTime.ToUniversalTime() - $expectedStart).TotalSeconds) -gt 2)
    {
        continue
    }

    [void] $rootIds.Add($pidValue)
    [void] $ownedIds.Add($pidValue)
}

if ($rootIds.Count -gt 0)
{
    $foundChild = $true
    while ($foundChild)
    {
        $foundChild = $false
        foreach ($candidate in $processes)
        {
            $candidateId = [int] $candidate.ProcessId
            if ($ownedIds.Contains([int] $candidate.ParentProcessId) -and $ownedIds.Add($candidateId))
            {
                $foundChild = $true
            }
        }
    }

    $descendants = @($ownedIds | Where-Object { -not $rootIds.Contains($_) } | Sort-Object -Descending)
    foreach ($processId in $descendants)
    {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }

    foreach ($processId in $rootIds)
    {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }

    foreach ($processId in $ownedIds)
    {
        try
        {
            Wait-Process -Id $processId -Timeout 5 -ErrorAction SilentlyContinue
        }
        catch
        {
        }

        if (Get-Process -Id $processId -ErrorAction SilentlyContinue)
        {
            throw "Could not stop owned process $processId; run artifacts were preserved."
        }
    }
}

$cleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
while (Test-Path -LiteralPath $runDirectory)
{
    try
    {
        Remove-Item -LiteralPath $runDirectory -Recurse -Force
    }
    catch
    {
        if ([DateTimeOffset]::UtcNow -ge $cleanupDeadline)
        {
            throw 'Could not remove the owned run artifacts because a file remained in use; the run directory was preserved.'
        }

        Start-Sleep -Milliseconds 200
    }
}

$runsDirectory = Join-Path $PSScriptRoot '.local-runs'
if ((Test-Path -LiteralPath $runsDirectory -PathType Container) -and
    -not (Get-ChildItem -LiteralPath $runsDirectory -Force | Select-Object -First 1))
{
    Remove-Item -LiteralPath $runsDirectory -Force
}

Write-Host "Stopped and cleaned up owned PizzaOps local run $RunId."
