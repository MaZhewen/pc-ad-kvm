$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outDir = Join-Path $env:TEMP 'pckvm-tests\single-instance'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir 'single-instance-test.exe'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc -nologo -target:exe -platform:x64 -out:$out -r:System.Windows.Forms.dll "$PSScriptRoot\Main.cs" "$root\src\agent\SingleInstanceGuard.cs"
if ($LASTEXITCODE -ne 0) { throw 'Single-instance test compile failed' }
& $out
if ($LASTEXITCODE -ne 0) { throw 'Single-instance test failed' }