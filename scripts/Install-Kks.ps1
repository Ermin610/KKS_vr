[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [switch]$Restore,
    [string]$BackupDirectory
)
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
if (Get-Process CharaStudio -ErrorAction SilentlyContinue) { throw 'Close Studio before installing or restoring.' }
function Resolve-GamePath([string]$relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $game $relative))
    if (!$path.StartsWith($game + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path is outside the game directory: $relative" }
    return $path
}
if ($Restore) {
    if (!$BackupDirectory) { throw 'Restore requires -BackupDirectory from the installation output.' }
    $backup = [IO.Path]::GetFullPath($BackupDirectory)
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $backup 'manifest.json') | ConvertFrom-Json
    if ($manifest.GameDir -ne $game) { throw 'This backup belongs to a different game directory.' }
    foreach ($entry in $manifest.Files) {
        $target = Resolve-GamePath $entry.Relative
        $saved = [IO.Path]::GetFullPath((Join-Path $backup $entry.Relative))
        if (!$saved.StartsWith($backup.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid backup path.' }
        if ($entry.Existed -and (!(Test-Path -LiteralPath $saved) -or (Get-FileHash -LiteralPath $saved).Hash -ne $entry.OriginalHash)) { throw "Backup is missing or changed: $saved" }
        if (Test-Path -LiteralPath $target) {
            $currentHash = (Get-FileHash -LiteralPath $target).Hash
            $alreadyRestored = $entry.Existed -and $currentHash -eq $entry.OriginalHash
            if (!$alreadyRestored -and (!$entry.InstalledHash -or $currentHash -ne $entry.InstalledHash)) {
                throw "File changed after installation; preserve it before restoring: $target"
            }
        }
    }
    foreach ($entry in $manifest.Files) {
        $target = Resolve-GamePath $entry.Relative
        if ($entry.Existed) {
            New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath (Join-Path $backup $entry.Relative) -Destination $target -Force
        }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    Write-Output 'Previous VR files restored. Scene cards and settings were not replaced.'
    return
}
foreach ($relative in @('CharaStudio.exe', 'CharaStudio_Data\Managed\Assembly-CSharp.dll', 'BepInEx\plugins\KKS_VR\VRGIN_OpenXR.dll')) {
    if (!(Test-Path -LiteralPath (Resolve-GamePath $relative))) { throw "KKS with its existing VR runtime is required: $relative" }
}
$payload = @('BepInEx\plugins\KKS_VR\KKS_CharaStudioVR.dll', 'BepInEx\plugins\KKS_VR\KKS_VR_CameraSync.dll', 'KKVRReShadeBridge.addon64', 'StartKKSStudioVR.bat', 'StartKKSStudioNoVR.bat')
foreach ($relative in $payload) { if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $relative))) { throw "Package file missing: $relative" } }
$checksums = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$') { throw 'Invalid package checksum manifest.' }
    if ($checksums.ContainsKey($Matches[2])) { throw "Duplicate package checksum: $($Matches[2])" }
    $checksums[$Matches[2]] = $Matches[1]
}
foreach ($relative in $payload) {
    $source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    if ($source -eq (Resolve-GamePath $relative)) { throw 'Extract the package outside the game directory before installing.' }
    if (!$checksums.ContainsKey($relative) -or (Get-FileHash -LiteralPath $source).Hash -ne $checksums[$relative]) {
        throw "Package file checksum failed; extract a fresh package: $relative"
    }
}
$duplicates = @(Get-ChildItem -LiteralPath (Join-Path $game 'BepInEx\plugins') -Recurse -File | Where-Object {
    $_.Name -in @('KKS_CharaStudioVR.dll', 'KKSCharaStudioVRPlugin.dll', 'KKS_VR_CameraSync.dll', 'KK_VR_CameraSync.dll')
} | ForEach-Object { $_.FullName.Substring($game.Length + 1) } | Where-Object { $_ -notin $payload })
$backup = Resolve-GamePath ('KKS-VR-Backups\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $backup | Out-Null
$files = @()
foreach ($relative in @($payload) + @($duplicates)) {
    $target = Resolve-GamePath $relative
    $existed = Test-Path -LiteralPath $target
    $hash = $null
    if ($existed) {
        $saved = Join-Path $backup $relative
        New-Item -ItemType Directory -Force (Split-Path $saved -Parent) | Out-Null
        Copy-Item -LiteralPath $target -Destination $saved
        $hash = (Get-FileHash -LiteralPath $target).Hash
    }
    $installedHash = if ($relative -in $payload) { (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $relative)).Hash } else { $null }
    $files += @{ Relative = $relative; Existed = $existed; OriginalHash = $hash; InstalledHash = $installedHash }
}
@{ GameDir = $game; Files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
try {
    foreach ($relative in $duplicates) { Remove-Item -LiteralPath (Resolve-GamePath $relative) }
    foreach ($relative in $payload) {
        $target = Resolve-GamePath $relative
        New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $relative) -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target).Hash -ne $checksums[$relative]) { throw "Installed file verification failed: $relative" }
    }
}
catch {
    $installFailure = $_
    $rollbackFailures = @()
    foreach ($entry in $files) {
        try {
            $target = Resolve-GamePath $entry.Relative
            if ($entry.Existed) {
                # A locked file may have rejected the copy before changing.
                # Leave it alone and still restore every other affected file.
                if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $entry.OriginalHash) { continue }
                Copy-Item -LiteralPath (Join-Path $backup $entry.Relative) -Destination $target -Force
            }
            elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
        }
        catch { $rollbackFailures += $_.Exception.Message }
    }
    if ($rollbackFailures.Count -gt 0) {
        throw "Install failed: $($installFailure.Exception.Message). Some files could not be restored; backup: $backup. $($rollbackFailures -join '; ')"
    }
    throw $installFailure
}
Write-Output "Installed KKS preview. Backup: $backup"
Write-Output "Restore: .\Install-KKS.ps1 -GameDir `"$game`" -Restore -BackupDirectory `"$backup`""
