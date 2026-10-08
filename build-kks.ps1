[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$CameraSyncRepo,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (!$CameraSyncRepo) { $CameraSyncRepo = Join-Path $PSScriptRoot 'CameraSync' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'output\KKS-VR-Overhaul-0.01' }
$game = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
$stage = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if ($stage -eq $game -or $stage.StartsWith($game + '\', [StringComparison]::OrdinalIgnoreCase) -or $game.StartsWith($stage + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package output must be separate from the game directory.'
}
$packageFiles = @(
    'BepInEx\plugins\KKS_VR\KKS_CharaStudioVR.dll', 'BepInEx\plugins\KKS_VR\KKS_VR_CameraSync.dll',
    'KKVRReShadeBridge.addon64', 'Install-KKS.ps1', 'README.md', 'LICENSE-KKS-VR',
    'LICENSE-CameraSync.txt', 'StartKKSStudioVR.bat', 'StartKKSStudioNoVR.bat'
)
if (Test-Path -LiteralPath $stage) {
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        if ($relative -notin $packageFiles -and $relative -ne 'SHA256SUMS.txt') {
            throw "Unexpected file in package output; choose an empty directory: $relative"
        }
    }
}
& dotnet build (Join-Path $PSScriptRoot 'KKSCharaStudioVRPlugin\KKSCharaStudioVRPlugin.csproj') -c Release --nologo "/p:KKSGameDir=$game"
if ($LASTEXITCODE -ne 0) { throw 'KKS core build failed.' }
& dotnet build (Join-Path $CameraSyncRepo 'KKS_VR_CameraSync.csproj') -c Release --nologo "/p:GameRoot=$game"
if ($LASTEXITCODE -ne 0) { throw 'KKS CameraSync build failed.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio C++ build tools are required for the ReShade bridge.' }
$msbuild = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild with the C++ workload was not found.' }
& $msbuild (Join-Path $PSScriptRoot 'native\KKVRReShadeBridge\KKVRReShadeBridge.vcxproj') /nologo /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'ReShade bridge build failed.' }
$pluginDir = Join-Path $stage 'BepInEx\plugins\KKS_VR'
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'KKSCharaStudioVRPlugin\bin\Release\net462\KKSCharaStudioVRPlugin.dll') -Destination (Join-Path $pluginDir 'KKS_CharaStudioVR.dll') -Force
Copy-Item -LiteralPath (Join-Path $CameraSyncRepo 'bin\KKS\net462\KKS_VR_CameraSync.dll') -Destination $pluginDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native\KKVRReShadeBridge\bin\x64\Release\KKVRReShadeBridge.addon64') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'scripts\Install-Kks.ps1') -Destination (Join-Path $stage 'Install-KKS.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\KKS-COMPATIBILITY.md') -Destination (Join-Path $stage 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'KKSCharaStudioVRPlugin\Resources\LICENSE-KKS-VR') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $CameraSyncRepo 'LICENSE.txt') -Destination (Join-Path $stage 'LICENSE-CameraSync.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launchers\StartKKSStudioVR.bat'), (Join-Path $PSScriptRoot 'launchers\StartKKSStudioNoVR.bat') -Destination $stage -Force
$manifest = $packageFiles | Sort-Object | ForEach-Object {
    (Get-FileHash -LiteralPath (Join-Path $stage $_) -Algorithm SHA256).Hash + '  ' + $_
}
$manifest | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding UTF8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath ($stage + '.zip') -Force
Write-Output "KKS preview package: $stage.zip"
