<#
  Removes Remotea for the current user: stops it, deletes shortcuts, the firewall rule and the install folder
  (including remotea.json).
#>
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Remotea'

Get-Process Remotea -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$programs = [Environment]::GetFolderPath('Programs')
Remove-Item (Join-Path $programs 'Remotea.lnk'), (Join-Path $programs 'Remotea Settings.lnk'), (Join-Path $programs 'Uninstall Remotea.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Startup')) 'Remotea.lnk') -ErrorAction SilentlyContinue

if (Get-NetFirewallRule -DisplayName 'Remotea' -ErrorAction SilentlyContinue) {
    Write-Host 'Removing firewall rule (needs admin)...'
    Start-Process netsh -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList 'advfirewall firewall delete rule name=Remotea'
}

Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Remotea uninstalled.'
