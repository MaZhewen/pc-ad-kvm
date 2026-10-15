$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $root 'dist\android-clipboard-tests'
New-Item -ItemType Directory -Force $out | Out-Null
$jdk='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
& "$jdk\javac.exe" --release 8 -nowarn -d $out "$PSScriptRoot\AndroidMain.java" "$root\src\injector\ClipboardWire.java" "$root\src\injector\AndroidClipboard.java" "$root\src\injector\AndroidProviders.java" "$root\src\injector\AndroidClipboardCache.java"
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
& "$jdk\java.exe" -cp $out AndroidMain
if($LASTEXITCODE -ne 0){throw 'Android clipboard test failed'}
