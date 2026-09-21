param([string]$Mode = "create")
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "启动 UHID 探针 (mode=$Mode)..." -ForegroundColor Cyan
Write-Host "预期: 手机上出现一个鼠标光标，并且它会自己缓慢向右下移动。Ctrl+C 结束。" -ForegroundColor Yellow
Write-Host ""
& adb shell "CLASSPATH=/data/local/tmp/uhidprobe.jar app_process / UhidProbe $Mode"
