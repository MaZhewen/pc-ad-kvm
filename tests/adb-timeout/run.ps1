$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP ('pckvm-adb-timeout-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
try {
    & $csc -nologo -target:exe -platform:x64 -out:"$out\adb.exe" (Join-Path $PSScriptRoot 'FakeAdb.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Fake adb compile failed' }
    & $csc -nologo -target:exe -platform:x64 -out:"$out\adb-timeout.exe" `
        (Join-Path $PSScriptRoot 'Main.cs') (Join-Path $root 'src\agent\DeviceLauncher.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Timeout test compile failed' }
    & "$out\adb-timeout.exe"
    if ($LASTEXITCODE -ne 0) { throw 'ADB timeout regression failed' }
} finally {
    Remove-Item -LiteralPath $out -Recurse -Force
}
