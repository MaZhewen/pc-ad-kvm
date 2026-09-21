$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }

Write-Host "编译中..." -ForegroundColor Cyan
# 注意：全部用 - 前缀开关。用 / 前缀会被 MSYS 路径转换破坏。
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -out:"$here\RawInputProbe.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       "$here\RawInputProbe.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败" }
Write-Host "编译成功: $here\RawInputProbe.exe" -ForegroundColor Green
Write-Host "启动探针..." -ForegroundColor Cyan
& "$here\RawInputProbe.exe"
