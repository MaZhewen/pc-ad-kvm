$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $here)
$javac = "C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe"
$java  = "C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe"
$r8    = Join-Path $root "tools\r8.jar"

foreach ($p in @($javac, $java, $r8)) {
    if (-not (Test-Path $p)) { throw "缺少: $p" }
}

$out = Join-Path $here "classes"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "javac 编译中（--release 8，会打印弃用警告，正常）..." -ForegroundColor Cyan
& $javac --release 8 -d $out (Join-Path $here "UhidProbe.java")
if ($LASTEXITCODE -ne 0) { throw "javac 失败" }

$jar = Join-Path $here "uhidprobe.jar"
Write-Host "d8 转 dex..." -ForegroundColor Cyan
& $java -cp $r8 com.android.tools.r8.D8 `
    --release --min-api 28 --output $out (Join-Path $out "UhidProbe.class")
if ($LASTEXITCODE -ne 0) { throw "d8 失败" }

$dex = Join-Path $out "classes.dex"
if (-not (Test-Path $dex)) { throw "未生成 classes.dex" }

# 打成含 classes.dex 的 jar（app_process 可直接吃）
$zip = Join-Path $out "payload.zip"
Compress-Archive -Path $dex -DestinationPath $zip -Force
if (Test-Path $jar) { Remove-Item -Force $jar }
Move-Item $zip $jar
Write-Host "生成: $jar" -ForegroundColor Green

Write-Host "推送到设备..." -ForegroundColor Cyan
& adb push $jar /data/local/tmp/uhidprobe.jar
if ($LASTEXITCODE -ne 0) { throw "adb push 失败" }
Write-Host "完成。运行: powershell -File `"$here\run.ps1`"" -ForegroundColor Green
