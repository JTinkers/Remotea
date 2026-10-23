# Builds the self-contained exe and packs it with the installer scripts into share\Remotea.zip.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $root 'publish\package'
$zip = Join-Path $root 'share\Remotea.zip'

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src\Remotea') -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $staging
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Remove-Item (Join-Path $staging '*.pdb')
Copy-Item (Join-Path $PSScriptRoot '*.ps1'), (Join-Path $PSScriptRoot '*.cmd'), (Join-Path $root 'README.md') $staging
Remove-Item (Join-Path $staging 'Package.ps1')

New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
Write-Host "Created $zip"
