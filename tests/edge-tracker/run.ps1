$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src  = Join-Path $root 'src\agent'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }

# 每次从零编译，且**只编被测的两个文件 + harness**：
#  - 不编译整个 src\agent（会拖进 WinForms，而这个 harness 是纯逻辑、无 UI 依赖）
#  - 绝不复用 %TEMP% 里任何预编译产物——测试必须测"仓库当前源码"，
#    本项目已经栽过一次"验的不是用户实际会跑的那一份"（见交接 §六）
$out = Join-Path $env:TEMP 'pckvm-tests\edge'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = @(
    (Join-Path $PSScriptRoot 'Main.cs'),
    (Join-Path $src 'EdgeTracker.cs'),
    (Join-Path $src 'CursorModel.cs')
)
foreach ($s in $sources) { if (-not (Test-Path $s)) { throw "缺少: $s" } }

Write-Host "编译 EdgeTracker harness..." -ForegroundColor Cyan
& $csc -nologo -target:exe -platform:x64 -out:"$out\edgetest.exe" $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

& "$out\edgetest.exe"
if ($LASTEXITCODE -ne 0) { throw "EdgeTracker 用例未全过（harness 自身在 fail>0 时返回 1）" }

Write-Host "EdgeTracker: 全部用例通过" -ForegroundColor Green
