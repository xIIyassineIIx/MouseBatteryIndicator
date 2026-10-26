$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler (csc.exe) not found.' }

$dist = Join-Path $root 'dist'
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $dist, $assets | Out-Null

$sources = @('Hid.cs', 'IconFactory.cs', 'Settings.cs', 'TrayApp.cs', 'Program.cs') |
    ForEach-Object { Join-Path $root $_ }
$refs = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll')
$options = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu') + $refs

$exe = Join-Path $dist 'MouseBattery.exe'
Write-Host 'Compiling (pass 1)...'
& $csc @options "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Compile failed.' }

$ico = Join-Path $assets 'MouseBattery.ico'
Write-Host 'Generating application icon...'
$p = Start-Process -FilePath $exe -ArgumentList @('--make-icon', ('"' + $ico + '"')) -Wait -PassThru
if ($p.ExitCode -ne 0 -or -not (Test-Path $ico)) { throw 'Icon generation failed.' }

Write-Host 'Compiling (pass 2, with icon)...'
& $csc @options "/win32icon:$ico" "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Compile failed.' }

Write-Host "Built: $exe"
$probeOut = Join-Path $env:TEMP 'MouseBattery-probe.txt'
$probe = Start-Process -FilePath $exe -ArgumentList '--probe' -Wait -PassThru -NoNewWindow -RedirectStandardOutput $probeOut
Get-Content $probeOut
