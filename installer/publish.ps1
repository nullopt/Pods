# Tags the current commit and publishes a signed release to GitHub. Run after build.ps1 and the
# release signer's "sign" command; refuses to publish anything the trusted keys do not verify.
# Safe to run again after a failure: an existing tag on this commit is reused.
# Native commands report failure through their exit code (git writes progress to stderr), so
# errors are checked with $LASTEXITCODE rather than $ErrorActionPreference.
param([Parameter(Mandatory)][string]$Notes)
$root = Split-Path $PSScriptRoot -Parent

$version = ([xml](Get-Content (Join-Path $root 'src\Pods.App\Pods.App.csproj') -ErrorAction Stop)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Could not read a X.Y.Z version from Pods.App.csproj." }
if (-not (Test-Path $Notes)) { throw "Release notes not found: $Notes" }
if (git -C $root status --porcelain) { throw 'The working tree has uncommitted changes.' }

$base = Join-Path $root "artifacts\PodsSetup-$version"
$files = "$base.exe", "$base.json", "$base.json.sig"
foreach ($file in $files) { if (-not (Test-Path $file)) { throw "Missing $file. Run build.ps1, then the signer's sign command." } }

dotnet run --project (Join-Path $root 'tools\Pods.ReleaseSigner') -- verify "$base.exe"
if ($LASTEXITCODE) { throw 'The release does not verify against the trusted keys; not publishing.' }

$tag = "v$version"
$tagged = git -C $root rev-parse -q --verify "refs/tags/$tag^{commit}"
if (-not $tagged) {
    git -C $root tag -a $tag -m "Pods $version"
    if ($LASTEXITCODE) { throw "Tagging $tag failed." }
} elseif ($tagged -ne (git -C $root rev-parse HEAD)) {
    throw "$tag already exists on another commit."
}
git -C $root push origin main $tag 2>&1 | Write-Host
if ($LASTEXITCODE) { throw 'Push failed.' }
gh release create $tag @files --title "Pods $version" --notes-file $Notes
if ($LASTEXITCODE) { throw 'Creating the GitHub release failed.' }
