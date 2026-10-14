$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$jbr = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$out = Join-Path $env:TEMP ('pckvm-wifi-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
try {
    $sources = Get-ChildItem -LiteralPath (Join-Path $root 'src\injector') -Filter '*.java' | ForEach-Object FullName
    & "$jbr\javac.exe" --release 8 -nowarn -sourcepath (Join-Path $root 'build\android-stubs') -d $out (Join-Path $PSScriptRoot 'TestMain.java') @sources
    if ($LASTEXITCODE -ne 0) { throw 'Wi-Fi test compile failed' }
    & "$jbr\java.exe" -cp $out TestMain
    if ($LASTEXITCODE -ne 0) { throw 'Wi-Fi lifecycle tests failed' }
} finally {
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($out)
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
