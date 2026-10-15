$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$jdk='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$out=Join-Path $root 'dist\clipboard-probe'
New-Item -ItemType Directory -Force $out | Out-Null
& "$jdk\javac.exe" --release 8 -nowarn -d $out "$PSScriptRoot\ClipboardProbe.java" "$root\src\injector\AndroidProviders.java"
if($LASTEXITCODE -ne 0){throw 'probe compile failed'}
$r8=if($env:PCKVM_R8_JAR){$env:PCKVM_R8_JAR}else{Join-Path $root 'tools\r8.jar'}
& "$jdk\java.exe" -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $out (Get-ChildItem $out -Filter '*.class' | ForEach-Object FullName)
if($LASTEXITCODE -ne 0){throw 'probe dex failed'}
Compress-Archive -Path "$out\classes.dex" -DestinationPath "$out\probe.zip" -Force
Copy-Item "$out\probe.zip" "$out\probe.jar" -Force
