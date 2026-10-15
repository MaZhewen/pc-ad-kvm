$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP ('pckvm-clipboard-service-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
try {
    & $csc -nologo -target:exe -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:"$out\service.exe" (Join-Path $PSScriptRoot 'ServiceMain.cs') (Join-Path $root 'src\agent\ClipboardWire.cs') (Join-Path $root 'src\agent\WindowsClipboard.cs') (Join-Path $root 'src\agent\ClipboardService.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Clipboard service tests compile failed' }
    & "$out\service.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Clipboard service tests failed' }
} finally {
    $target = [IO.Path]::GetFullPath($out)
    if (-not $target.StartsWith([IO.Path]::GetFullPath($env:TEMP) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $target -Recurse -Force
}
