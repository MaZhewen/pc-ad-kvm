$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $env:TEMP ('pckvm-clipboard-windows-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
try {
    & $csc -nologo -target:exe -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:"$out\windows.exe" (Join-Path $PSScriptRoot 'WindowsMain.cs') (Join-Path $root 'src\agent\ClipboardWire.cs') (Join-Path $root 'src\agent\WindowsClipboard.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Windows clipboard tests compile failed' }
    & "$out\windows.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Windows clipboard tests failed' }
} finally {
    $target = [IO.Path]::GetFullPath($out)
    if (-not $target.StartsWith([IO.Path]::GetFullPath($env:TEMP) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $target -Recurse -Force
}