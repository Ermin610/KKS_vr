[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $repo ('output\installer-regression-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $root 'package'
$script:checks = 0
$payload = @('BepInEx\plugins\KKS_VR\KKS_CharaStudioVR.dll', 'BepInEx\plugins\KKS_VR\KKS_VR_CameraSync.dll', 'KKVRReShadeBridge.addon64', 'StartKKSStudioVR.bat', 'StartKKSStudioNoVR.bat')
$duplicate = 'BepInEx\plugins\old\KKSCharaStudioVRPlugin.dll'
function Write-File([string]$path, [string]$content) {
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    [IO.File]::WriteAllText($path, $content)
}
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw "FAIL: $message" }
    $script:checks++
}
function Expect-Failure([scriptblock]$action, [string]$pattern) {
    $failure = $null
    try { & $action | Out-Null } catch { $failure = $_.Exception.Message }
    Check ($failure -and $failure -like $pattern) "Expected '$pattern', got '$failure'"
}
function New-Fixture([string]$name) {
    $game = Join-Path $root $name
    foreach ($relative in @('CharaStudio.exe', 'CharaStudio_Data\Managed\Assembly-CSharp.dll', 'BepInEx\plugins\KKS_VR\VRGIN_OpenXR.dll', $payload[0], $payload[2], $duplicate, 'BepInEx\plugins\unrelated.dll')) {
        Write-File (Join-Path $game $relative) "original:$relative"
    }
    return $game
}
function Snapshot([string]$game) {
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $game -Recurse -File) {
        $relative = $file.FullName.Substring($game.Length + 1)
        if (!$relative.StartsWith('KKS-VR-Backups\')) { $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName).Hash }
    }
    return $hashes
}
function Same-Files([string]$game, [hashtable]$before, [string]$reason) {
    $after = Snapshot $game
    Check ($after.Count -eq $before.Count) "$reason file count"
    foreach ($relative in $before.Keys) { Check ($after[$relative] -eq $before[$relative]) "$reason $relative" }
}
function Backup([string]$game) {
    return (Get-ChildItem -LiteralPath (Join-Path $game 'KKS-VR-Backups') -Directory | Select-Object -Last 1).FullName
}
function Install([string]$game) { & (Join-Path $package 'Install-KKS.ps1') -GameDir $game | Out-Null }
function Restore([string]$game, [string]$backup) { & (Join-Path $package 'Install-KKS.ps1') -GameDir $game -Restore -BackupDirectory $backup | Out-Null }

foreach ($relative in $payload) { Write-File (Join-Path $package $relative) "preview:$relative" }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Kks.ps1') -Destination (Join-Path $package 'Install-KKS.ps1')
$payload | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $package $_)).Hash + '  ' + $_ } | Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding UTF8

$game = New-Fixture 'normal'
$before = Snapshot $game
Install $game
$backup = Backup $game
Check (!(Test-Path -LiteralPath (Join-Path $game $duplicate))) 'Duplicate plugin removed'
foreach ($relative in $payload) { Check ((Get-FileHash -LiteralPath (Join-Path $game $relative)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $package $relative)).Hash) "Installed hash $relative" }
Restore $game $backup
Same-Files $game $before 'Restore originals'
Restore $game $backup
Same-Files $game $before 'Repeated restore is harmless'

$game = New-Fixture 'recreated-duplicate'
Install $game
$backup = Backup $game
Write-File (Join-Path $game $duplicate) 'User installed a different plugin after preview'
$before = Snapshot $game
Expect-Failure { Restore $game $backup } '*File changed after installation*'
Same-Files $game $before 'Conflicting duplicate must not be overwritten'

$game = New-Fixture 'changed-installed-file'
Install $game
$backup = Backup $game
Write-File (Join-Path $game $payload[2]) 'User updated the ReShade bridge'
$before = Snapshot $game
Expect-Failure { Restore $game $backup } '*File changed after installation*'
Same-Files $game $before 'Restore preflight leaves all files untouched'

$game = New-Fixture 'corrupt-backup'
Install $game
$backup = Backup $game
Write-File (Join-Path $backup $duplicate) 'Damaged backup'
$before = Snapshot $game
Expect-Failure { Restore $game $backup } '*Backup is missing or changed*'
Same-Files $game $before 'Corrupt backup rejected before writes'

$game = New-Fixture 'locked-destination'
$before = Snapshot $game
$locked = [IO.File]::Open((Join-Path $game $payload[2]), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Expect-Failure { Install $game } '*' } finally { $locked.Dispose() }
Same-Files $game $before 'Failed copy rolls back earlier copies and duplicate removal'

$game = New-Fixture 'corrupt-payload'
$before = Snapshot $game
Write-File (Join-Path $package $payload[0]) 'Corrupted download'
Expect-Failure { Install $game } '*checksum failed*'
Same-Files $game $before 'Bad package rejected before game changes'
Check (!(Test-Path -LiteralPath (Join-Path $game 'KKS-VR-Backups'))) 'Bad package does not begin installation'
Write-File (Join-Path $package $payload[0]) "preview:$($payload[0])"

$game = New-Fixture 'missing-runtime'
$runtime = Join-Path $game 'BepInEx\plugins\KKS_VR\VRGIN_OpenXR.dll'
Remove-Item -LiteralPath $runtime
$before = Snapshot $game
Expect-Failure { Install $game } '*existing VR runtime is required*'
Same-Files $game $before 'Missing dependency rejected before game changes'

$game = New-Fixture 'output-guard'
Expect-Failure { & (Join-Path $repo 'build-kks.ps1') -GameDir $game -OutputDirectory $game } '*separate from the game directory*'
$stale = Join-Path $root 'stale-output'
Write-File (Join-Path $stale 'unexpected.dll') 'Unrelated file'
Expect-Failure { & (Join-Path $repo 'build-kks.ps1') -GameDir $game -OutputDirectory $stale } '*Unexpected file in package output*'

"PASS: $script:checks installer/package assertions. Fixtures retained: $root" | Tee-Object -FilePath (Join-Path $root 'result.txt')
