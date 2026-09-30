$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root 'src\injector'
$r8 = if ($env:PCKVM_R8_JAR) { $env:PCKVM_R8_JAR } else { Join-Path $root 'tools\r8.jar' }
$javac = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe'
$java = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe'
foreach ($path in @($javac, $java, $r8)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺少: $path" }
}

$out = Join-Path $env:TEMP ('pckvm-injector-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
try {
    $sources = Get-ChildItem -Path $src -Filter '*.java' | ForEach-Object FullName
    & $javac --release 8 -nowarn -d $out $sources
    if ($LASTEXITCODE -ne 0) { throw 'javac 失败' }

    $classes = Get-ChildItem -Path $out -Filter '*.class' | ForEach-Object FullName
    & $java -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $out $classes
    if ($LASTEXITCODE -ne 0) { throw 'd8 失败' }
    $dex = Join-Path $out 'classes.dex'
    if (-not (Test-Path -LiteralPath $dex)) { throw '未生成 classes.dex' }

    $zip = Join-Path $out 'payload.zip'
    Compress-Archive -Path $dex -DestinationPath $zip
    $dist = Join-Path $root 'dist'
    if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
    Copy-Item -LiteralPath $zip -Destination (Join-Path $dist 'pckvm.jar') -Force
    Write-Host "已生成 dist\pckvm.jar；连接时会推送到所选设备。" -ForegroundColor Green
}
finally {
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($out)
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
