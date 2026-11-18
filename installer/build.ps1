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
& $iscc /Q (Join-Path $PSScriptRoot 'Pods.iss')
exit $LASTEXITCODE
