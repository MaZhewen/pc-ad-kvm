$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP ('pckvm-port-scan-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
try {
    $sources = @(Join-Path $PSScriptRoot 'Main.cs')
    $scanner = Join-Path $root 'src\agent\WirelessPortScanner.cs'
    if (Test-Path $scanner) { $sources += $scanner }
    $sources += Join-Path $root 'src\agent\WirelessDiscovery.cs'
    & $csc -nologo -target:exe -out:"$out\port-scan.exe" $sources
    if ($LASTEXITCODE -ne 0) { throw 'Port-scan test compile failed' }
    & "$out\port-scan.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Port-scan tests failed' }
} finally {
    Remove-Item -LiteralPath $out -Recurse -Force
}
