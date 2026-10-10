<#
.SYNOPSIS
    Builds the Neurolito Agent MSI: publishes the agent, the screensaver and llama.cpp, then the installer.

.PARAMETER AgentProject
    The agent's project file (Tharga.Neurolito.Agent.csproj) to publish. Until the agent publishes a Windows build of its
    own, the MSI is built from its source.

.PARAMETER Version
    The MSI's version, three numbers. Default 0.1.0.

.EXAMPLE
    ./build/build.ps1 -AgentProject ../neurolito-agent/Tharga.Neurolito.Agent/Tharga.Neurolito.Agent.csproj -Version 0.1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AgentProject,
    [string] $Version = '0.1.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts'
$agentDir = Join-Path $out 'agent'
$screensaverDir = Join-Path $out 'screensaver'
$llamaDir = Join-Path $out 'llama'
$cache = Join-Path $out 'cache'

function Step($text) { Write-Host "== $text" -ForegroundColor Cyan }
function Fresh($dir) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }; New-Item -ItemType Directory $dir | Out-Null }

Step "Agent $Version from $AgentProject"
Fresh $agentDir
dotnet publish $AgentProject -c Release -r win-x64 --self-contained -o $agentDir -p:Version=$Version -p:DebugType=none -nologo -v q
if ($LASTEXITCODE) { throw 'The agent did not publish.' }
# Development settings are for a developer's machine, not an installed agent.
Get-ChildItem $agentDir -Filter 'appsettings.Development.json' | Remove-Item

Step 'Screensaver'
Fresh $screensaverDir
dotnet publish (Join-Path $root 'Tharga.Neurolito.Screensaver') -c Release -r win-x64 --self-contained -o $screensaverDir `
    -p:Version=$Version -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -nologo -v q
if ($LASTEXITCODE) { throw 'The screensaver did not publish.' }

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
Fresh $llamaDir
# llama-server and what it loads; not the other tools in the archive, nor their libraries.
Get-ChildItem $unpacked -File | Where-Object {
    $_.Name -eq 'llama-server.exe' -or $_.Name -like 'LICENSE*' -or
    ($_.Extension -eq '.dll' -and ($_.Name -notlike 'llama-*-impl.dll' -or $_.Name -eq 'llama-server-impl.dll'))
} | Copy-Item -Destination $llamaDir
Invoke-WebRequest "https://raw.githubusercontent.com/ggml-org/llama.cpp/$($llama.build)/LICENSE" -OutFile (Join-Path $llamaDir 'LICENSE-llama.cpp')

Step 'Installer'
$screensaver = Join-Path $screensaverDir 'Neurolito.exe'
dotnet build (Join-Path $root 'Tharga.Neurolito.Installer') -c Release -o (Join-Path $out 'msi') -nologo -v q `
    "-p:Version=$Version" "-p:AgentDir=$agentDir" "-p:LlamaDir=$llamaDir" "-p:ScreensaverFile=$screensaver"
if ($LASTEXITCODE) { throw 'The installer did not build.' }

$msi = Get-Item (Join-Path $out 'msi\NeurolitoAgent.msi')
Write-Host ("Built {0} ({1:N0} MB)" -f $msi.FullName, ($msi.Length / 1MB)) -ForegroundColor Green
