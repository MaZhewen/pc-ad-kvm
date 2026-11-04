$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP ('pckvm-direct-bootstrap-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    $sources = @('DirectBootstrap.cs', 'AdbClient.cs', 'AdbDevice.cs', 'Protocol.cs') | ForEach-Object { Join-Path $root ('src\agent\' + $_) }
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -out:$out (Join-Path $PSScriptRoot 'Main.cs') $sources
    if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
    & $out
    if ($LASTEXITCODE -ne 0) { throw 'Direct bootstrap tests failed' }
} finally { if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out } }
