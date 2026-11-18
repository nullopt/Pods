# Tags the current commit and publishes a signed release to GitHub. Run after build.ps1 and the
# release signer's "sign" command; refuses to publish anything the trusted keys do not verify.
param([Parameter(Mandatory)][string]$Notes)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$version = ([xml](Get-Content (Join-Path $root 'src\Pods.App\Pods.App.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Could not read a X.Y.Z version from Pods.App.csproj." }
if (-not (Test-Path $Notes)) { throw "Release notes not found: $Notes" }
if (git -C $root status --porcelain) { throw 'The working tree has uncommitted changes.' }

$base = Join-Path $root "artifacts\PodsSetup-$version"
$files = "$base.exe", "$base.json", "$base.json.sig"
foreach ($file in $files) { if (-not (Test-Path $file)) { throw "Missing $file. Run build.ps1, then the signer's sign command." } }

dotnet run --project (Join-Path $root 'tools\Pods.ReleaseSigner') -- verify "$base.exe"
if ($LASTEXITCODE) { throw 'The release does not verify against the trusted keys; not publishing.' }

git -C $root tag -a "v$version" -m "Pods $version"
if ($LASTEXITCODE) { throw "Tagging v$version failed." }
git -C $root push origin main --follow-tags
if ($LASTEXITCODE) { throw 'Push failed.' }
gh release create "v$version" @files --title "Pods $version" --notes-file $Notes
if ($LASTEXITCODE) { throw 'Creating the GitHub release failed.' }
