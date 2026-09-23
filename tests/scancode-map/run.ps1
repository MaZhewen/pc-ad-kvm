$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src  = Join-Path $root 'src\injector'

# 与 build\build-injector.ps1 用同一套 JDK（硬编码路径，换机器需改两处）
$javac = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe'
$java  = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe'
foreach ($p in @($javac, $java)) { if (-not (Test-Path $p)) { throw "缺少: $p" } }

# 每次从零编译，只编被测的 ScancodeMap + harness（不走 d8，不需要设备）
$out = Join-Path $env:TEMP 'pckvm-tests\scan'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$harness = Join-Path $PSScriptRoot 'TestMain.java'
$target  = Join-Path $src 'ScancodeMap.java'
foreach ($s in @($harness, $target)) { if (-not (Test-Path $s)) { throw "缺少: $s" } }

Write-Host "javac（--release 8，与构建脚本一致；会打印弃用警告，正常）..." -ForegroundColor Cyan
& $javac --release 8 -nowarn -d $out $harness $target
if ($LASTEXITCODE -ne 0) { throw "javac 失败" }

& $java -cp $out TestMain
if ($LASTEXITCODE -ne 0) { throw "ScancodeMap 用例未全过（harness 自身以失败数作为退出码）" }

Write-Host "ScancodeMap: 全部用例通过" -ForegroundColor Green
