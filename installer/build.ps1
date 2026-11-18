# Publishes a self-contained Release build and wraps it in artifacts\PodsSetup-<version>.exe.
# Needs Inno Setup 6: winget install JRSoftware.InnoSetup
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$publish = Join-Path $root 'artifacts\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\Pods.App\Pods.App.csproj') -c Release -r win-x64 --self-contained -p:Platform=x64 -o $publish
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup' }
# Compiled in a staging folder and moved when complete: a setup started while it is still being
# written reports "The setup files are corrupted".
$staging = Join-Path $root 'artifacts\staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
Write-Host 'Building the installer (about half a minute)...'
& $iscc /Q "/O$staging" (Join-Path $PSScriptRoot 'Pods.iss')
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$setup = Get-ChildItem (Join-Path $staging 'PodsSetup-*.exe') | Move-Item -Destination (Join-Path $root 'artifacts') -Force -PassThru
Remove-Item $staging -Recurse -Force
Write-Host "Built $($setup.Name). Next:"
Write-Host "  dotnet run --project tools/Pods.ReleaseSigner -- sign artifacts\$($setup.Name)"
Write-Host "  installer\publish.ps1 -Notes <release-notes.md>"
