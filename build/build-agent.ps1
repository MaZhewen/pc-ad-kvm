$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src  = Join-Path $root 'src\agent'
$dist = Join-Path $root 'dist'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Force -Path $dist | Out-Null }

$sources = Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "src\agent 下没有 .cs 文件" }

$icon = Join-Path $root 'assets\pc-kvm.ico'
if (-not (Test-Path $icon)) { throw "找不到图标: $icon" }

# --- jar 新鲜度守卫。**必须在编译之前**：否则守卫触发时会留下一对"新 exe + 旧 jar"的产物 ---
# dist\pckvm.jar 由 build-injector.ps1 产出（它才是唯一会被 exe 推给手机的那一份）。
# 这里**不再**无条件从 %TEMP% 拷贝——%TEMP% 里那份可能比 src\injector 旧，
# 于是"构建成功"而设备侧改动从没到过手机（2026-09-23 的实际事故）。
$jar = "$dist\pckvm.jar"
if (-not (Test-Path $jar)) { throw "缺少 dist\pckvm.jar：请先跑 build\build-injector.ps1" }
$javaSrc = Get-ChildItem (Join-Path $root 'src\injector') -Filter '*.java'
if ($javaSrc.Count -eq 0) { throw "src\injector 下没有 .java 文件，无法判定 jar 是否新鲜" }
$newest = $javaSrc | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($newest.LastWriteTime -gt (Get-Item $jar).LastWriteTime) {
    throw ("dist\pckvm.jar 比设备侧源码旧（jar=" + (Get-Item $jar).LastWriteTime +
           "；最新源码=" + $newest.Name + " " + $newest.LastWriteTime +
           "）：请先跑 build\build-injector.ps1")
}
Write-Host ("pckvm.jar 新鲜度 OK（{0:N0} 字节）" -f (Get-Item $jar).Length) -ForegroundColor Green

Write-Host "编译 $($sources.Count) 个源文件..." -ForegroundColor Cyan
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -win32icon:"$icon" `
       -out:"$dist\pc-kvm.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       $sources

if ($LASTEXITCODE -ne 0) { throw "编译失败" }
$exe = Get-Item "$dist\pc-kvm.exe"
Write-Host ("编译成功: {0}  ({1:N1} KB)" -f $exe.FullName, ($exe.Length / 1KB)) -ForegroundColor Green
