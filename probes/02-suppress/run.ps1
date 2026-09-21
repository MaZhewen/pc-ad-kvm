$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

Write-Host "编译中..." -ForegroundColor Cyan
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -out:"$here\SuppressProbe.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       "$here\SuppressProbe.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败" }
Write-Host "编译成功，启动..." -ForegroundColor Green
& "$here\SuppressProbe.exe"
