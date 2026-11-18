#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the PodsAap driver and the test certificate install.ps1 trusted. Run in an admin PowerShell.
#>

$ErrorActionPreference = 'Stop'

$published = Get-WindowsDriver -Online | Where-Object { (Split-Path $_.OriginalFileName -Leaf) -ieq 'PodsAap.inf' }
if (-not $published) { Write-Host 'PodsAap is not in the driver store.' }
foreach ($driver in $published) {
    & pnputil.exe /delete-driver $driver.Driver /uninstall /force
    if ($LASTEXITCODE -notin 0, 3010) { throw "pnputil failed to remove $($driver.Driver) ($LASTEXITCODE)." }
    Write-Host "Removed $($driver.Driver)."
}

foreach ($store in 'Root', 'TrustedPublisher') {
    Get-ChildItem "Cert:\LocalMachine\$store" | Where-Object Subject -eq 'CN=Pods Driver Test' | ForEach-Object {
        Remove-Item $_.PSPath
        Write-Host "Removed the Pods Driver Test certificate from LocalMachine\$store."
    }
}
