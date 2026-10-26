$ErrorActionPreference = 'SilentlyContinue'

Get-Process MouseBattery | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MouseBattery'
Remove-Item -Path 'HKCU:\Software\MouseBattery' -Recurse -Force
Remove-Item -Recurse -Force (Join-Path $env:LOCALAPPDATA 'MouseBattery')
Write-Host 'MouseBattery removed (app files, autostart entry, settings and logs).'
