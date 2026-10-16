$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $env:TEMP ('pckvm-clipboard-lock-order-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$jdk='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
try {
 & "$jdk\javac.exe" --release 8 -encoding UTF-8 -Xlint:-options -d $out "$PSScriptRoot\SessionMain.java" "$PSScriptRoot\AndroidMain.java" "$PSScriptRoot\AckLockMain.java" "$root\src\injector\ClipboardWire.java" "$root\src\injector\AndroidClipboard.java" "$root\src\injector\AndroidProviders.java" "$root\src\injector\ClipboardSession.java" "$root\src\injector\AndroidClipboardCache.java" "$root\src\injector\AndroidClipboardImages.java"
 if($LASTEXITCODE -ne 0){throw 'Clipboard session compile failed'}
 & "$jdk\java.exe" -cp $out AckLockMain
 if($LASTEXITCODE -ne 0){throw 'Clipboard session tests failed'}
} finally {
 $resolved=[IO.Path]::GetFullPath($out)
 $tempRoot=[IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\'
 if($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('pckvm-clipboard-lock-order-')){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
