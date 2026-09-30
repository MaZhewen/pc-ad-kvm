$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outDir=Join-Path $env:TEMP 'pckvm-tests\message-host'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out=Join-Path $outDir 'message-host-test.exe'
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sources = Get-ChildItem (Join-Path $root 'src\agent') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $csc -nologo -target:exe -platform:x64 -main:HostTest -out:$out -r:System.Windows.Forms.dll -r:System.Drawing.dll "$PSScriptRoot\Main.cs" $sources
if($LASTEXITCODE -ne 0){throw 'MessageHost compile failed'}
& $out
if($LASTEXITCODE -ne 0){throw 'MessageHost hit-test failed'}
