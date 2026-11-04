$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$javaHome = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$out = Join-Path $env:TEMP ('pckvm-direct-network-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $out)
try {
    $sources = @((Join-Path $PSScriptRoot 'Main.cs'), (Join-Path $root 'src\agent\Protocol.cs'),
        (Join-Path $root 'src\agent\DirectNetworkGateway.cs'), (Join-Path $root 'src\agent\DirectTlsCertificate.cs'))
    & $csc -nologo -target:exe -platform:x64 "-out:$out\Test.exe" @sources
    if ($LASTEXITCODE -ne 0) { throw 'C# compile failed' }
    $javaSources = @((Join-Path $PSScriptRoot 'TestMain.java'))
    $client = Join-Path $root 'src\injector\PinnedTls.java'
    if (Test-Path -LiteralPath $client) { $javaSources += $client }
    & "$javaHome\javac.exe" --release 8 -Xlint:-options -d $out @javaSources
    if ($LASTEXITCODE -ne 0) { throw 'Java compile failed' }
    & "$out\Test.exe" "$javaHome\java.exe" $out
    if ($LASTEXITCODE -ne 0) { throw 'Direct network tests failed' }
} finally {
    $resolved = [IO.Path]::GetFullPath($out)
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe temporary directory' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
