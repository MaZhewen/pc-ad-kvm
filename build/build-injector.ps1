$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src   = Join-Path $root 'src\injector'
$r8    = Join-Path $root 'tools\r8.jar'
$javac = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe'
$java  = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe'

foreach ($p in @($javac, $java, $r8)) {
    if (-not (Test-Path $p)) { throw "缺少: $p" }
}

$out = Join-Path $env:TEMP 'pckvm-injector-classes'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = Get-ChildItem -Path $src -Filter '*.java' | ForEach-Object { $_.FullName }

Write-Host "javac（--release 8，会打印弃用警告，正常）..." -ForegroundColor Cyan
& $javac --release 8 -nowarn -d $out $sources
if ($LASTEXITCODE -ne 0) { throw "javac 失败" }

Write-Host "d8 转 dex..." -ForegroundColor Cyan
$classes = Get-ChildItem -Path $out -Filter '*.class' | ForEach-Object { $_.FullName }
& $java -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $out $classes
if ($LASTEXITCODE -ne 0) { throw "d8 失败" }

$dex = Join-Path $out 'classes.dex'
if (-not (Test-Path $dex)) { throw "未生成 classes.dex" }

$jar = Join-Path $env:TEMP 'pckvm.jar'
if (Test-Path $jar) { Remove-Item -Force $jar }
$zip = Join-Path $out 'payload.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $dex -DestinationPath $zip -Force
Move-Item $zip $jar

Write-Host "推送到设备..." -ForegroundColor Cyan
& adb push $jar /data/local/tmp/pckvm.jar
if ($LASTEXITCODE -ne 0) { throw "adb push 失败" }

Write-Host "完成: /data/local/tmp/pckvm.jar" -ForegroundColor Green
