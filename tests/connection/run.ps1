$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP ('pckvm-connection-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
try {
    & $csc -nologo -target:exe -out:"$out\adb.exe" (Join-Path $PSScriptRoot 'FakeAdb.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Fake adb compile failed' }
    & $csc -nologo -target:exe -out:"$out\connection.exe" `
        (Join-Path $PSScriptRoot 'Main.cs') `
        (Join-Path $root 'src\agent\AdbClient.cs') `
        (Join-Path $root 'src\agent\AdbDevice.cs') `
        (Join-Path $root 'src\agent\WirelessDiscovery.cs') `
        (Join-Path $root 'src\agent\ConnectionProfile.cs') `
        (Join-Path $root 'src\agent\HeartbeatLease.cs') `
        (Join-Path $root 'src\agent\DeviceLauncher.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Connection test compile failed' }
    & "$out\connection.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Connection tests failed' }
} finally {
    Remove-Item -LiteralPath $out -Recurse -Force
}
