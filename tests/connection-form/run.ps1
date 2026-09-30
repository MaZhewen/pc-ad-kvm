$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP ('pckvm-connection-form-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
try {
    & $csc -nologo -target:exe -out:"$out\adb.exe" (Join-Path $root 'tests\coordinator\FakeAdb.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Fake adb compile failed' }
    $sources = Get-ChildItem (Join-Path $root 'src\agent') -Filter '*.cs' | ForEach-Object { $_.FullName }
    & $csc -nologo -target:exe -main:ConnectionFormTests -out:"$out\connection-form.exe" `
        -r:System.Windows.Forms.dll -r:System.Drawing.dll `
        (Join-Path $PSScriptRoot 'Main.cs') $sources
    if ($LASTEXITCODE -ne 0) { throw 'Connection form test compile failed' }
    & "$out\connection-form.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Connection form test failed' }
} finally {
    Remove-Item -LiteralPath $out -Recurse -Force
}
