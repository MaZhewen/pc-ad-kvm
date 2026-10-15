$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $root 'dist\clipboard-paste-probes'
New-Item -ItemType Directory -Force $out | Out-Null
$jdk='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$sources=Get-ChildItem (Join-Path $root 'src\injector') -Filter '*.java' | ForEach-Object FullName
& "$jdk\javac.exe" --release 8 -encoding UTF-8 -nowarn -sourcepath (Join-Path $root 'build\android-stubs') -d $out @sources "$PSScriptRoot\ClipboardDeviceProbe.java" "$PSScriptRoot\KeyboardPasteProbe.java" "$PSScriptRoot\ClipboardMetadataProbe.java"
if($LASTEXITCODE -ne 0){throw 'Paste probe compile failed'}
$r8=if($env:PCKVM_R8_JAR){$env:PCKVM_R8_JAR}else{Join-Path $root 'tools\r8.jar'}
& "$jdk\java.exe" -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $out (Get-ChildItem $out -Filter '*.class' | ForEach-Object FullName)
if($LASTEXITCODE -ne 0){throw 'Paste probe dex failed'}
Compress-Archive -Path "$out\classes.dex" -DestinationPath "$out\paste.zip" -Force
Copy-Item "$out\paste.zip" "$out\paste.jar" -Force
