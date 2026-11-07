# Realistic Life Simulator - build, test and (optionally) install.
#   powershell -ExecutionPolicy Bypass -File build.ps1            build + test
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Install   also copy the DLL into the game's scripts folder
# Uses the C# compiler included with Windows (.NET Framework 4.x). No downloads needed.

#   powershell -ExecutionPolicy Bypass -File build.ps1 -Shvdn "D:\Grand Theft Auto V\ScriptHookVDotNet3.dll"
#       build a copy of the project that is not inside the GTA V folder (no -Install then)

param([switch]$Install, [string]$Shvdn = '')
$ErrorActionPreference = 'Stop'

$src  = $PSScriptRoot
$root = Split-Path $src -Parent
$game = Split-Path $root -Parent
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$api  = if ($Shvdn -ne '') { $Shvdn } else { Join-Path $game 'ScriptHookVDotNet3.dll' }
if ($Install -and $Shvdn -ne '') { throw '-Install only works when the project sits inside the GTA V folder.' }
$bin  = Join-Path $src 'bin'
$dll  = Join-Path $bin 'RealisticLifeSimulator.dll'
$test = Join-Path $bin 'RlsCoreTests.exe'

foreach ($f in $csc, $api) { if (-not (Test-Path $f)) { throw "Missing: $f" } }
New-Item -ItemType Directory -Force $bin | Out-Null

$core  = Get-ChildItem (Join-Path $src 'Core')  -Filter *.cs | ForEach-Object { $_.FullName }
$gameF = Get-ChildItem (Join-Path $src 'Game')  -Filter *.cs | ForEach-Object { $_.FullName }
$tests = Get-ChildItem (Join-Path $src 'Tests') -Filter *.cs | ForEach-Object { $_.FullName }

Write-Host '== Compiling mod DLL'
& $csc /nologo /codepage:65001 /target:library /optimize+ /warn:4 /out:$dll "/r:$api" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll $core $gameF
if ($LASTEXITCODE -ne 0) { throw 'Mod compile failed' }

Write-Host '== Compiling core tests'
& $csc /nologo /codepage:65001 /target:exe /optimize+ /warn:4 /out:$test /r:System.Windows.Forms.dll /r:System.Xml.dll $core $tests
if ($LASTEXITCODE -ne 0) { throw 'Test compile failed' }

Write-Host '== Running core tests'
& $test
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }

New-Item -ItemType Directory -Force (Join-Path $root 'Scripts') | Out-Null  # not present in a fresh clone
Copy-Item $dll (Join-Path $root 'Scripts\RealisticLifeSimulator.dll') -Force
Write-Host "Built: $root\Scripts\RealisticLifeSimulator.dll"

if ($Install) {
    if (Get-Process GTA5 -ErrorAction SilentlyContinue) { throw 'GTA V is running. Close it before installing.' }
    New-Item -ItemType Directory -Force (Join-Path $game 'scripts') | Out-Null
    $target = Join-Path $game 'scripts\RealisticLifeSimulator.dll'
    Copy-Item $dll $target -Force
    Write-Host "Installed: $target"
}
