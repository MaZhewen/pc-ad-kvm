$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $root 'dist\double-click-tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$jdk='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$sources=Get-ChildItem -LiteralPath (Join-Path $root 'src\injector') -Filter '*.java' | Where-Object Name -ne 'UhidDevice.java' | ForEach-Object FullName
& "$jdk\javac.exe" --release 8 -nowarn -sourcepath (Join-Path $root 'build\android-stubs') -d $out @sources "$PSScriptRoot\TouchTestMain.java" "$PSScriptRoot\SessionTestMain.java" "$PSScriptRoot\UhidDevice.java" "$PSScriptRoot\android\view\InputDevice.java"
if($LASTEXITCODE -ne 0){throw 'Java compile failed'}
foreach($test in @('TouchTestMain','SessionTestMain')) {
 & "$jdk\java.exe" -cp $out $test
 if($LASTEXITCODE -ne 0){throw "$test failed"}
}
