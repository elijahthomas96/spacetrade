<#
.SYNOPSIS
    Builds the Windows x64 player with Unity in batch mode and zips it.

.EXAMPLE
    .\tools\build-windows.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe"

.EXAMPLE
    .\tools\build-windows.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\6000.6.10f1\Editor\Unity.exe" -Development

.EXAMPLE
    .\tools\build-windows.ps1 -UnityExe "...\Unity.exe" -RunTests
    Runs the Unity edit-mode tests first and stops if any fail.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityExe,

    [switch]$Development,

    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity.exe not found at '$UnityExe'."
}

$projectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$buildsDir = Join-Path $projectPath 'Builds'
New-Item -ItemType Directory -Force -Path $buildsDir | Out-Null

function Invoke-Unity([string[]]$UnityArgs, [string]$LogFile) {
    Write-Host "> $UnityExe $($UnityArgs -join ' ')"
    $proc = Start-Process -FilePath $UnityExe -ArgumentList $UnityArgs -Wait -PassThru -NoNewWindow
    if ($proc.ExitCode -ne 0) {
        Write-Host "Unity exited with code $($proc.ExitCode). Last lines of ${LogFile}:" -ForegroundColor Red
        if (Test-Path -LiteralPath $LogFile) { Get-Content -LiteralPath $LogFile -Tail 60 }
        exit $proc.ExitCode
    }
}

if ($RunTests) {
    $testLog = Join-Path $buildsDir 'editmode-tests.log'
    $testResults = Join-Path $buildsDir 'editmode-results.xml'
    Invoke-Unity @('-batchmode', '-nographics', '-projectPath', "`"$projectPath`"",
        '-runTests', '-testPlatform', 'EditMode',
        '-testResults', "`"$testResults`"", '-logFile', "`"$testLog`"") $testLog
    Write-Host "Edit-mode tests passed. Results: $testResults" -ForegroundColor Green
}

if ($Development) {
    $method = 'SpaceTrade.EditorTools.BuildScript.BuildWindowsDevelopment'
    $outDir = Join-Path $buildsDir 'Windows-x64-Dev'
    $zip = Join-Path $buildsDir 'SpaceTrade-Windows-x64-Dev.zip'
} else {
    $method = 'SpaceTrade.EditorTools.BuildScript.BuildWindowsRelease'
    $outDir = Join-Path $buildsDir 'Windows-x64'
    $zip = Join-Path $buildsDir 'SpaceTrade-Windows-x64.zip'
}
$buildLog = Join-Path $buildsDir ($(if ($Development) { 'build-dev.log' } else { 'build.log' }))

Invoke-Unity @('-batchmode', '-quit', '-projectPath', "`"$projectPath`"",
    '-buildTarget', 'Win64', '-executeMethod', $method, '-logFile', "`"$buildLog`"") $buildLog

$exe = Join-Path $outDir 'SpaceTrade.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build reported success but $exe is missing. See $buildLog."
}

# Debug symbol folders are not needed to run the player.
$items = Get-ChildItem -LiteralPath $outDir | Where-Object { $_.Name -notlike '*_BurstDebugInformation_DoNotShip' -and $_.Name -notlike '*_BackUpThisFolder_ButDontShipItWithYourGame' }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $items.FullName -DestinationPath $zip -CompressionLevel Optimal

Write-Host "Built $exe" -ForegroundColor Green
Write-Host "Zipped to $zip" -ForegroundColor Green
