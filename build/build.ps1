<#
.SYNOPSIS
    Builds a release: the agent, llama.cpp and the screensaver as one Velopack package, its update files, and the MSI.

.DESCRIPTION
    The package's folder is the agent at the root, llama.cpp in llama\ and the screensaver in screensaver\. Velopack
    packs it (artifacts\releases: the update feed, with earlier releases kept so updates can be deltas), and the MSI
    installs its portable layout - Update.exe and current\ - so the first install and every update are the same files.

.PARAMETER AgentProject
    The agent's project file (Tharga.Neurolito.Agent.csproj) to publish. Until the agent publishes a Windows build of its
    own, the release is built from its source.

.PARAMETER Version
    The release's version, three numbers. The agent, the package and the MSI all carry it.

.EXAMPLE
    ./build/build.ps1 -AgentProject ../neurolito-agent/Tharga.Neurolito.Agent/Tharga.Neurolito.Agent.csproj -Version 0.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AgentProject,
    [Parameter(Mandatory)] [string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts'
$stage = Join-Path $out 'stage'
$releases = Join-Path $out 'releases'
$portable = Join-Path $out 'portable'
$launcherDir = Join-Path $out 'launcher'
$cache = Join-Path $out 'cache'

function Step($text) { Write-Host "== $text" -ForegroundColor Cyan }
function Fresh($dir) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }; New-Item -ItemType Directory $dir | Out-Null }
function Check($what) { if ($LASTEXITCODE) { throw "$what failed." } }

Fresh $stage

Step "Agent $Version from $AgentProject"
dotnet publish $AgentProject -c Release -r win-x64 --self-contained -o $stage -p:Version=$Version -p:DebugType=none -nologo -v q
Check 'Publishing the agent'
# Development settings are for a developer's machine, not an installed agent.
Get-ChildItem $stage -Filter 'appsettings.Development.json' | Remove-Item

Step 'Screensaver'
dotnet publish (Join-Path $root 'Tharga.Neurolito.Screensaver') -c Release -r win-x64 --self-contained -o (Join-Path $stage 'screensaver') `
    -p:Version=$Version -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -nologo -v q
Check 'Publishing the screensaver'

Step 'Screensaver launcher'
Fresh $launcherDir
dotnet publish (Join-Path $root 'Tharga.Neurolito.ScreensaverLauncher') -c Release -r win-x64 --self-contained -o $launcherDir `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true -p:DebugType=none -nologo -v q
Check 'Publishing the launcher'

$llama = Get-Content (Join-Path $PSScriptRoot 'llama.json') -Raw | ConvertFrom-Json
Step "llama.cpp $($llama.build)"
New-Item -ItemType Directory $cache -Force | Out-Null
$zip = Join-Path $cache $llama.asset
if (-not (Test-Path $zip)) {
    Invoke-WebRequest "https://github.com/ggml-org/llama.cpp/releases/download/$($llama.build)/$($llama.asset)" -OutFile $zip
}
$digest = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($digest -ne $llama.sha256) { Remove-Item $zip; throw "$($llama.asset) does not match its digest: expected $($llama.sha256), got $digest." }

$unpacked = Join-Path $cache "llama-$($llama.build)"
if (-not (Test-Path $unpacked)) { Expand-Archive $zip $unpacked }
$llamaDir = Join-Path $stage 'llama'
New-Item -ItemType Directory $llamaDir | Out-Null
# llama-server and what it loads; not the other tools in the archive, nor their libraries.
Get-ChildItem $unpacked -File | Where-Object {
    $_.Name -eq 'llama-server.exe' -or $_.Name -like 'LICENSE*' -or
    ($_.Extension -eq '.dll' -and ($_.Name -notlike 'llama-*-impl.dll' -or $_.Name -eq 'llama-server-impl.dll'))
} | Copy-Item -Destination $llamaDir
$license = Join-Path $cache "LICENSE-llama.cpp-$($llama.build)"
if (-not (Test-Path $license)) { Invoke-WebRequest "https://raw.githubusercontent.com/ggml-org/llama.cpp/$($llama.build)/LICENSE" -OutFile $license }
Copy-Item $license (Join-Path $llamaDir 'LICENSE-llama.cpp')

Step "Velopack package $Version"
New-Item -ItemType Directory $releases -Force | Out-Null
vpk pack --packId NeurolitoAgent --packVersion $Version --packDir $stage --mainExe Tharga.Neurolito.Agent.exe `
    --packTitle 'Neurolito Agent' --packAuthors Neurolito --outputDir $releases
Check 'Packing'

Step 'Portable layout for the MSI'
Fresh $portable
Expand-Archive (Join-Path $releases 'NeurolitoAgent-win-Portable.zip') $portable

Step 'Installer'
$launcher = Join-Path $launcherDir 'NeurolitoLauncher.exe'
dotnet build (Join-Path $root 'Tharga.Neurolito.Installer') -c Release -o (Join-Path $out 'msi') -nologo -v q `
    "-p:Version=$Version" "-p:AgentDir=$portable" "-p:ScreensaverFile=$launcher"
Check 'Building the installer'

$msi = Get-Item (Join-Path $out 'msi\NeurolitoAgent.msi')
Write-Host ("Built {0} ({1:N0} MB) and the update feed in {2}" -f $msi.FullName, ($msi.Length / 1MB), $releases) -ForegroundColor Green
