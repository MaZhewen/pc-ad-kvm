$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$javac = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe'
$java = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
$out = Join-Path $env:TEMP ('pckvm-clipboard-wire-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
try {
 & $csc /nologo /warnaserror+ /out:"$out\WireMain.exe" (Join-Path $PSScriptRoot 'WireMain.cs') (Join-Path $root 'src\agent\ClipboardWire.cs')
 if ($LASTEXITCODE -ne 0) { throw 'C# wire compile failed' }
 & $javac --release 8 -encoding UTF-8 -Xlint:-options -d $out (Join-Path $PSScriptRoot 'WireTestMain.java') (Join-Path $root 'src\injector\ClipboardWire.java')
 if ($LASTEXITCODE -ne 0) { throw 'Java wire compile failed' }
 & "$out\WireMain.exe" self $out
 if ($LASTEXITCODE -ne 0) { throw 'C# wire tests failed' }
 & $java -cp $out WireTestMain self $out
 if ($LASTEXITCODE -ne 0) { throw 'Java wire tests failed' }
 & "$out\WireMain.exe" write $out
 if ($LASTEXITCODE -ne 0) { throw 'C# fixture write failed' }
 & $java -cp $out WireTestMain verify $out
 if ($LASTEXITCODE -ne 0) { throw 'C# -> Java fixture failed' }
 & $java -cp $out WireTestMain write $out
 if ($LASTEXITCODE -ne 0) { throw 'Java fixture write failed' }
 & "$out\WireMain.exe" verify $out
 if ($LASTEXITCODE -ne 0) { throw 'Java -> C# fixture failed' }
} finally {
 $resolved = [IO.Path]::GetFullPath($out)
 $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
 if ($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('pckvm-clipboard-wire-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
