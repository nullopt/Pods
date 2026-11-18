#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the test-signed PodsAap driver. Run in an admin PowerShell on a PC with test signing on.

.DESCRIPTION
    Trusts the package's test certificate (PodsAap-test.cer) on this PC, then adds the driver to the
    driver store and installs it on every paired device that offers Apple's AAP service (AirPods,
    Apple-chip Beats). See README.md for turning test signing on first.
#>
param(
    [string] $Package = (Join-Path $PSScriptRoot 'bin\x64\Release\PodsAap')
)

$ErrorActionPreference = 'Stop'

$options = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control').SystemStartOptions
if ($options -notmatch 'TESTSIGNING') {
    Write-Host 'Test signing is off, so Windows would refuse to load this driver.' -ForegroundColor Yellow
    $secureBoot = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -ErrorAction SilentlyContinue).UEFISecureBootEnabled
    if ($secureBoot -eq 1) {
        Write-Host 'Secure Boot is on, and Windows will not turn test signing on until it is off.'
    }
    Write-Host 'Follow "One-time setup" in driver\README.md (BitLocker, Secure Boot, bcdedit /set testsigning on, reboot) and run this again.'
    exit 1
}

$inf = Join-Path $Package 'PodsAap.inf'
$cer = Join-Path $Package 'PodsAap-test.cer'
foreach ($file in $inf, $cer, (Join-Path $Package 'PodsAap.sys'), (Join-Path $Package 'podsaap.cat')) {
    if (-not (Test-Path $file)) { throw "Missing $file; run build.ps1 and sign-test.ps1 first." }
}

# Trusted root: the chain validates. Trusted publisher: pnputil installs without a prompt.
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
Write-Host 'Trusted the Pods Driver Test certificate on this PC.'

& pnputil.exe /add-driver $inf /install
switch ($LASTEXITCODE) {
    0 { Write-Host 'Installed. Connected AirPods now have a "Pods AAP channel" device under Bluetooth in Device Manager.' -ForegroundColor Green }
    3010 { Write-Host 'Installed; Windows asks for a restart to finish.' -ForegroundColor Green }
    259 { Write-Host 'Added to the driver store; no paired AirPods were found to install it on yet. Pair them and it installs automatically.' -ForegroundColor Green }
    default { throw "pnputil failed ($LASTEXITCODE)." }
}
