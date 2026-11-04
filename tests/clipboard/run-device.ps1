param([Parameter(Mandatory=$true)][int]$TransportId,[Parameter(Mandatory=$true)][string]$Serial,[string]$AdbPath='D:\software\platform-tools\adb.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $root 'dist\clipboard-device-test'
New-Item -ItemType Directory -Force $out | Out-Null
$jdk='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$sources=@('ClipboardWire.java','ClipboardSession.java','AndroidClipboard.java','AndroidProviders.java','AndroidClipboardCache.java','AndroidClipboardImages.java','SessionLink.java','InputFrameQueue.java') | ForEach-Object {Join-Path $root ('src\injector\'+$_)}
& "$jdk\javac.exe" --release 8 -encoding UTF-8 -nowarn -sourcepath "$root\src\injector" -d $out "$root\probes\05-clipboard\ClipboardDeviceProbe.java" @sources
if($LASTEXITCODE -ne 0){throw 'Device probe compile failed'}
$r8=if($env:PCKVM_R8_JAR){$env:PCKVM_R8_JAR}else{Join-Path $root 'tools\r8.jar'}
& "$jdk\java.exe" -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $out (Get-ChildItem $out -Filter '*.class' | ForEach-Object FullName)
if($LASTEXITCODE -ne 0){throw 'Device probe dex failed'}
Compress-Archive -Path "$out\classes.dex" -DestinationPath "$out\device.zip" -Force
Copy-Item "$out\device.zip" "$out\device.jar" -Force
& $AdbPath -t $TransportId push "$out\device.jar" /data/local/tmp/pckvm-clipboard-device.jar
if($LASTEXITCODE -ne 0){throw 'Device probe push failed'}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:"$out\device-test.exe" "$PSScriptRoot\DeviceMain.cs" "$root\src\agent\ClipboardService.cs" "$root\src\agent\WindowsClipboard.cs" "$root\src\agent\ClipboardWire.cs" "$root\src\agent\AdbClient.cs" "$root\src\agent\AdbDevice.cs" "$root\src\agent\DeviceLauncher.cs"
if($LASTEXITCODE -ne 0){throw 'Device harness compile failed'}
& "$out\device-test.exe" "$out\data" $TransportId $Serial $AdbPath
if($LASTEXITCODE -ne 0){throw 'Device clipboard validation failed'}
