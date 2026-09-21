$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src  = Join-Path $root 'src\agent'
$dist = Join-Path $root 'dist'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Force -Path $dist | Out-Null }

$sources = Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "src\agent 下没有 .cs 文件" }

Write-Host "编译 $($sources.Count) 个源文件..." -ForegroundColor Cyan
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -out:"$dist\pc-kvm.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       $sources

if ($LASTEXITCODE -ne 0) { throw "编译失败" }
$exe = Get-Item "$dist\pc-kvm.exe"
Write-Host ("编译成功: {0}  ({1:N1} KB)" -f $exe.FullName, ($exe.Length / 1KB)) -ForegroundColor Green
