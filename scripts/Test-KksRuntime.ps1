[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$CameraSyncRepo = (Join-Path $PSScriptRoot '..\CameraSync'),
    [int]$TimeoutSeconds = 420
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$game = [IO.Path]::GetFullPath($GameDir)
if (Get-Process CharaStudio -ErrorAction SilentlyContinue) { throw 'Close Studio before running the automated test.' }
$result = Join-Path $repo ('output\kks-validation-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $result | Out-Null
$changes = @(
    @{ Source = (Join-Path $repo 'KKSCharaStudioVRPlugin\bin\Release\net462\KKSCharaStudioVRPlugin.dll'); Target = 'BepInEx\plugins\KKS_VR\KKS_CharaStudioVR.dll' },
    @{ Source = (Join-Path $CameraSyncRepo 'bin\KKS\net462\KKS_VR_CameraSync.dll'); Target = 'BepInEx\plugins\KKS_VR\KKS_VR_CameraSync.dll' },
    @{ Source = $null; Target = 'BepInEx\config\KKS_CharaStudioVR.cfg' },
    @{ Source = $null; Target = 'BepInEx\config\yukyo.kksvr.camerasync.cfg' }
)
foreach ($entry in $changes) {
    if ($entry.Source -and !(Test-Path -LiteralPath $entry.Source)) { throw "Build output missing: $($entry.Source)" }
}
$backedUp = @()
$testProcess = $null
try {
    foreach ($entry in $changes) {
        $target = Join-Path $game $entry.Target
        $backup = Join-Path $result ([IO.Path]::GetFileName($target) + '.backup')
        $existed = Test-Path -LiteralPath $target
        if ($existed) { Copy-Item -LiteralPath $target -Destination $backup }
        $backedUp += @{ Target = $target; Backup = $backup; Existed = $existed }
        if ($entry.Source) { Copy-Item -LiteralPath $entry.Source -Destination $target -Force }
    }
    $report = Join-Path $result 'report.txt'
    $log = Join-Path $result 'player.log'
    $testProcess = Start-Process -FilePath (Join-Path $game 'CharaStudio.exe') -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList @(
        '--novr', '-vrmode', 'None', '--kksvr-validate', ('"--kksvr-report=' + $report + '"'),
        '-logFile', ('"' + $log + '"'), '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '480')
    Write-Output "Validation PID=$($testProcess.Id); report=$report"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (!$testProcess.HasExited -and !(Test-Path -LiteralPath $report) -and [DateTime]::UtcNow -lt $deadline) {
        $testProcess.WaitForExit(1000) | Out-Null
    }
    if (!(Test-Path -LiteralPath $report)) { throw "Validation did not produce a report. Inspect $log" }
    Get-Content -LiteralPath $report
    if (!(Select-String -LiteralPath $report -SimpleMatch 'RESULT=PASS' -Quiet)) { throw 'KKS runtime validation failed.' }
}
finally {
    if ($testProcess -and !$testProcess.HasExited) {
        # Some installed plugins stall during shutdown. Only stop our own process.
        if (!$testProcess.WaitForExit(5000)) { Stop-Process -Id $testProcess.Id; $testProcess.WaitForExit(10000) | Out-Null }
    }
    foreach ($entry in $backedUp) {
        if ($entry.Existed) { Copy-Item -LiteralPath $entry.Backup -Destination $entry.Target -Force }
        elseif (Test-Path -LiteralPath $entry.Target) { Remove-Item -LiteralPath $entry.Target }
    }
    Write-Output 'Original plugin files and plugin configs restored.'
}
