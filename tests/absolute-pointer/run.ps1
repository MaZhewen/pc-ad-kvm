$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $root 'dist\pointer-tests'
New-Item -ItemType Directory -Force $out | Out-Null
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc -nologo -out:"$out\pointer-tests.exe" "$PSScriptRoot\Main.cs" "$root\src\agent\Protocol.cs" "$root\src\agent\FrameQueue.cs" "$root\src\agent\PointerSession.cs" "$root\src\agent\RotationMap.cs"
if($LASTEXITCODE -ne 0){throw 'C# compile failed'}
& "$out\pointer-tests.exe"
if($LASTEXITCODE -ne 0){throw 'C# tests failed'}
$jdk='C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
& "$jdk\javac.exe" --release 8 -nowarn -d $out "$PSScriptRoot\TestMain.java" "$PSScriptRoot\TransformTest.java" "$root\src\injector\AbsolutePointer.java" "$root\src\injector\AbsoluteTransform.java"
if($LASTEXITCODE -ne 0){throw 'Java compile failed'}
& "$jdk\java.exe" -cp $out TestMain
if($LASTEXITCODE -ne 0){throw 'Java tests failed'}
& "$jdk\java.exe" -cp $out TransformTest
if($LASTEXITCODE -ne 0){throw 'Transform tests failed'}
