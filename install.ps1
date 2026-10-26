param([switch]$NoAutostart)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'dist\MouseBattery.exe'
if (-not (Test-Path $src)) { throw "Build first: $src not found. Run .\build.ps1" }

$destDir = Join-Path $env:LOCALAPPDATA 'MouseBattery'
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
$dest = Join-Path $destDir 'MouseBattery.exe'

Get-Process MouseBattery -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
Copy-Item $src $dest -Force

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($NoAutostart) {
    Remove-ItemProperty -Path $runKey -Name 'MouseBattery' -ErrorAction SilentlyContinue
} else {
    Set-ItemProperty -Path $runKey -Name 'MouseBattery' -Value ('"' + $dest + '"')
}

Start-Process $dest
Write-Host "Installed to: $dest"
if ($NoAutostart) { Write-Host 'Autostart: disabled' } else { Write-Host 'Autostart: enabled (right-click the tray icon to change)' }
Write-Host 'Look for the battery icon in the notification area (it may start in the overflow "^" menu).'
