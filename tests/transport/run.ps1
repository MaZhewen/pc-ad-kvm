$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $env:TEMP ('pckvm-transport-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $csc -nologo -target:exe -platform:x64 -out:$out (Join-Path $PSScriptRoot 'Main.cs') (Join-Path $root 'src\agent\Transport.cs') (Join-Path $root 'src\agent\Protocol.cs') (Join-Path $root 'src\agent\FrameQueue.cs') (Join-Path $root 'src\agent\FrameWriter.cs') (Join-Path $root 'src\agent\DeviceLauncher.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
    & $out
    if ($LASTEXITCODE -ne 0) { throw 'Transport regression failed' }
} finally {
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out }
}
