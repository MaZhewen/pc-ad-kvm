$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $root 'dist\message-host-test.exe'
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc -nologo -target:exe -platform:x64 -out:$out -r:System.Windows.Forms.dll -r:System.Drawing.dll "$PSScriptRoot\Main.cs" "$root\src\agent\MessageHost.cs" "$root\src\agent\Suppressor.cs"
if($LASTEXITCODE -ne 0){throw 'MessageHost compile failed'}
& $out
if($LASTEXITCODE -ne 0){throw 'MessageHost hit-test failed'}
