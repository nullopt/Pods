<#
.SYNOPSIS
    Builds PodsAap.sys and its INF (Release x64 by default). No admin rights needed.

.DESCRIPTION
    Needs Visual Studio 2026 (or its Build Tools) with the C++ workload. The WDK comes from NuGet
    packages restored into .\packages. If Visual Studio's "Windows Driver Kit" component is not
    installed, its small MSBuild toolset package (pinned by SHA-256) is downloaded and laid over a
    private copy of Visual Studio's C++ build targets in .\obj\vctargets, so nothing is installed
    system-wide.
#>
param(
    [ValidateSet('Release', 'Debug')] [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$tools = Join-Path $here 'obj\tools'
New-Item -ItemType Directory -Force $tools | Out-Null

# Visual Studio with the C++ tools.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio is not installed (vswhere.exe not found).' }
$vs = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No Visual Studio with the C++ x64 tools found.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\amd64\MSBuild.exe'
$vcTargets = Get-ChildItem (Join-Path $vs 'MSBuild\Microsoft\VC') -Directory | Where-Object Name -Match '^v\d+$' |
    Sort-Object { [int]$_.Name.Substring(1) } | Select-Object -Last 1
Write-Host "Visual Studio: $vs ($($vcTargets.Name))"

# WDK and SDK from NuGet.
$nuget = Join-Path $tools 'nuget.exe'
if (-not (Test-Path $nuget)) {
    Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/v6.14.0/nuget.exe' -OutFile $nuget
}
& $nuget restore (Join-Path $here 'packages.config') -PackagesDirectory (Join-Path $here 'packages') -NonInteractive
if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed ($LASTEXITCODE)." }

# The kernel-mode platform toolset, from Visual Studio or from the pinned toolset package.
$properties = @("/p:Configuration=$Configuration", '/p:Platform=x64')
$toolset = Join-Path $vcTargets.FullName 'Platforms\x64\PlatformToolsets\WindowsKernelModeDriver10.0'
if (-not (Test-Path $toolset)) {
    if ($vcTargets.Name -ne 'v180') { throw "Install Visual Studio's 'Windows Driver Kit' component (no pinned toolset for $($vcTargets.Name))." }
    $vsix = Join-Path $tools 'wdk-build.vsix'
    $vsixUrl = 'https://download.visualstudio.microsoft.com/download/pr/bb2a0130-3d29-484b-a4a0-eea5313ed282/fc94cb3a77269a0c5374ee08292cb6b5804af86da04112763566f666daccf15b/payload.vsix'
    $vsixHash = 'FC94CB3A77269A0C5374EE08292CB6B5804AF86DA04112763566F666DACCF15B'
    if (-not (Test-Path $vsix) -or (Get-FileHash $vsix).Hash -ne $vsixHash) {
        Invoke-WebRequest $vsixUrl -OutFile $vsix
        if ((Get-FileHash $vsix).Hash -ne $vsixHash) { Remove-Item $vsix; throw 'Toolset package hash mismatch.' }
    }
    $overlay = Join-Path $here 'obj\vctargets'
    $extract = Join-Path $tools 'wdk-build'
    Remove-Item $overlay, $extract -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item $vcTargets.FullName $overlay -Recurse
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($vsix, $extract)
    Copy-Item (Join-Path $extract 'Contents\MSBuild\Microsoft\VC\v180\*') $overlay -Recurse -Force
    $properties += "/p:VCTargetsPath=$overlay\"
    Write-Host 'Using the downloaded WDK toolset over a private copy of the C++ build targets.'
}

& $msbuild (Join-Path $here 'PodsAap.vcxproj') /nologo /m /v:minimal @properties
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

$out = Join-Path $here "bin\x64\$Configuration"
Write-Host "Built: $out\PodsAap\  (PodsAap.sys, PodsAap.inf)"
