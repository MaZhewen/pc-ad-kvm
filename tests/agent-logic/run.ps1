$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src  = Join-Path $root 'src\agent'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }

# 每次从零编译仓库当前源码，绝不复用 %TEMP% 里的预编译产物
#（本项目栽过一次"验的不是用户实际会跑的那一份"）
$out = Join-Path $env:TEMP 'pckvm-tests\agent-logic'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = @(
    (Join-Path $PSScriptRoot 'Main.cs'),
    (Join-Path $src 'Config.cs'),
    (Join-Path $src 'MouseScaler.cs'),
    (Join-Path $src 'KeyMap.cs')
)
# 尚不存在的源文件先跳过（本任务只测 Config；后续任务逐个补上）
$sources = $sources | Where-Object { Test-Path $_ }
if ($sources.Count -lt 2) { throw "待编译的源文件不足" }

Write-Host "编译 PC 侧纯逻辑 harness（$($sources.Count) 个文件）..." -ForegroundColor Cyan
& $csc -nologo -target:exe -platform:x64 -out:"$out\agent-logic.exe" $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

& "$out\agent-logic.exe"
if ($LASTEXITCODE -ne 0) { throw "PC 侧纯逻辑用例未全过" }

Write-Host "PC 侧纯逻辑: 全部用例通过" -ForegroundColor Green
