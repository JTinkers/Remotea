<#
  Installs Remotea for the current user:
  - copies it to %LOCALAPPDATA%\Programs\Remotea (an existing remotea.json there is kept)
  - adds Start Menu shortcuts ("Remotea", "Remotea Settings" and "Uninstall Remotea")
  - registers it under Installed apps so it can be uninstalled from Settings
  - starts it at logon unless -NoAutostart
  - on a receiver, adds the inbound firewall rule (asks for admin once)
#>
param([switch]$NoAutostart)

$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Remotea'
$exe = Join-Path $dest 'Remotea.exe'
$config = Join-Path $dest 'remotea.json'

function New-Shortcut([string]$Path, [string]$Target, [string]$Arguments = '', [int]$WindowStyle = 7) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $dest
    $shortcut.WindowStyle = $WindowStyle
    $shortcut.Save()
}

function Read-Config {
    # remotea.json allows // comment lines, which Windows PowerShell's JSON parser doesn't.
    (Get-Content $config -Raw) -replace '(?m)^\s*//.*$', '' | ConvertFrom-Json
}

Get-Process Remotea -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $source 'Remotea.exe'), (Join-Path $source 'Uninstall.ps1'), (Join-Path $source 'Uninstall.cmd') $dest -Force
if (-not (Test-Path $config)) {
    Copy-Item (Join-Path $source 'remotea.json') $config
    Write-Host 'Set role, host and secret in the settings file, save it and close Notepad to continue.'
    Start-Process notepad $config -Wait
}

$programs = [Environment]::GetFolderPath('Programs')
New-Shortcut (Join-Path $programs 'Remotea.lnk') $exe
New-Shortcut (Join-Path $programs 'Remotea Settings.lnk') 'notepad.exe' "`"$config`"" 1
New-Shortcut (Join-Path $programs 'Uninstall Remotea.lnk') (Join-Path $dest 'Uninstall.cmd') '' 1

$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Remotea.lnk'
if ($NoAutostart) {
    Remove-Item $startupLink -ErrorAction SilentlyContinue
} else {
    New-Shortcut $startupLink $exe
}

# Per-user entry under Settings > Apps > Installed apps (and Control Panel > Programs and Features).
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Remotea'
$version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
$sizeKb = [int]((Get-ChildItem $dest -File | Measure-Object Length -Sum).Sum / 1KB)
New-Item $uninstallKey -Force | Out-Null
$entry = @{
    DisplayName     = 'Remotea'
    DisplayVersion  = $version
    Publisher       = 'JTinkers'
    DisplayIcon     = $exe
    InstallLocation = $dest
    UninstallString = "`"$(Join-Path $dest 'Uninstall.cmd')`""
    URLInfoAbout    = 'https://github.com/JTinkers/Remotea'
}
foreach ($name in $entry.Keys) {
    New-ItemProperty $uninstallKey -Name $name -Value $entry[$name] -PropertyType String -Force | Out-Null
}
foreach ($name in 'NoModify', 'NoRepair') {
    New-ItemProperty $uninstallKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null
}
New-ItemProperty $uninstallKey -Name EstimatedSize -Value $sizeKb -PropertyType DWord -Force | Out-Null
New-ItemProperty $uninstallKey -Name InstallDate -Value (Get-Date -Format 'yyyyMMdd') -PropertyType String -Force | Out-Null

$settings = Read-Config
if ($settings.role -eq 'receiver' -and -not (Get-NetFirewallRule -DisplayName 'Remotea' -ErrorAction SilentlyContinue)) {
    $port = if ($settings.port) { $settings.port } else { 13333 }
    Write-Host "Adding firewall rule for TCP $port (needs admin)..."
    Start-Process netsh -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList `
        "advfirewall firewall add rule name=Remotea dir=in action=allow protocol=TCP localport=$port"
}

Start-Process $exe -WorkingDirectory $dest -WindowStyle Minimized
Write-Host "Installed to $dest and started ($($settings.role))."
Write-Host 'Find it in the Start Menu as "Remotea"; edit settings via "Remotea Settings".'
