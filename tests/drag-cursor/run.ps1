$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$jbr='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$out=Join-Path $env:TEMP ('pckvm-drag-cursor-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
try {
 $sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.java' -Recurse | ForEach-Object FullName)
 $cursor=Join-Path $root 'src\injector\DragCursor.java'
 if(Test-Path -LiteralPath $cursor){$sources+=$cursor}
 & "$jbr\javac.exe" --release 8 -nowarn -d $out @sources
 if($LASTEXITCODE -ne 0){throw 'Drag cursor compile failed'}
 & "$jbr\java.exe" -cp $out TestMain
 if($LASTEXITCODE -ne 0){throw 'Drag cursor tests failed'}
} finally {
 $tempRoot=[IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\'
 $resolved=[IO.Path]::GetFullPath($out)
 if($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
