$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$jbr='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$out=Join-Path $env:TEMP ('pckvm-native-cursor-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
try {
 $sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.java' -Recurse | ForEach-Object FullName)
 foreach($name in @('NativeCursor.java','NativeWindowListener.java')){
  $source=Join-Path $root ('src\injector\'+$name)
  if(Test-Path -LiteralPath $source){$sources+=$source}
 }
 & "$jbr\javac.exe" --release 8 -nowarn -sourcepath (Join-Path $root 'build\android-stubs') -d $out @sources
 if($LASTEXITCODE -ne 0){throw 'Native cursor compile failed'}
 & "$jbr\java.exe" -cp $out TestMain
 if($LASTEXITCODE -ne 0){throw 'Native cursor tests failed'}
 & "$jbr\java.exe" -cp $out ListenerTestMain
 if($LASTEXITCODE -ne 0){throw 'Native listener tests failed'}
} finally {
 $tempRoot=[IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\'
 $resolved=[IO.Path]::GetFullPath($out)
 if($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
