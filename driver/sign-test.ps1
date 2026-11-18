<#
.SYNOPSIS
    Test-signs the built driver package for a PC in Test Mode. No admin rights needed.

.DESCRIPTION
    Creates a self-signed code-signing certificate "Pods Driver Test" in the current user's store
    (once), exports its public part as PodsAap-test.cer into the package folder, signs PodsAap.sys,
    regenerates the catalog and signs it. install.ps1 trusts that .cer on the target PC.
    A test-signed driver loads only with test signing on (see README.md).
#>
param(
    [ValidateSet('Release', 'Debug')] [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$package = Join-Path $here "bin\x64\$Configuration\PodsAap"
if (-not (Test-Path (Join-Path $package 'PodsAap.sys'))) { throw "No build in $package; run build.ps1 first." }

$packages = Join-Path $here 'packages'
$signtool = Get-ChildItem $packages -Recurse -Filter signtool.exe | Where-Object FullName -Match '\\bin\\[\d.]+\\x64\\' | Select-Object -First 1 -ExpandProperty FullName
$inf2cat = Get-ChildItem $packages -Recurse -Filter Inf2Cat.exe | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool -or -not $inf2cat) { throw 'signtool.exe or Inf2Cat.exe not found; run build.ps1 first.' }

$subject = 'CN=Pods Driver Test'
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Sort-Object NotAfter | Select-Object -Last 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(5)
    Write-Host "Created certificate $subject ($($cert.Thumbprint))"
}
Export-Certificate -Cert $cert -FilePath (Join-Path $package 'PodsAap-test.cer') -Type CERT | Out-Null

function Invoke-Tool([string] $tool, [string[]] $arguments) {
    & $tool @arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $tool -Leaf) failed ($LASTEXITCODE)." }
}

$sign = @('sign', '/fd', 'sha256', '/sha1', $cert.Thumbprint, '/s', 'My')
Invoke-Tool $signtool ($sign + (Join-Path $package 'PodsAap.sys'))
Invoke-Tool $inf2cat @("/driver:$package", '/os:10_X64', '/uselocaltime')
Invoke-Tool $signtool ($sign + (Join-Path $package 'podsaap.cat'))
Write-Host "Signed: $package (certificate $($cert.Thumbprint))"
