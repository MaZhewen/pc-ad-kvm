$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$javac = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe'
$java = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe'
$out = Join-Path $env:TEMP ('pckvm-session-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
try {
    & $javac --release 8 -d $out (Join-Path $PSScriptRoot 'TestMain.java') (Join-Path $root 'src\injector\SessionLink.java')
    if ($LASTEXITCODE -ne 0) { throw 'Session test compile failed' }
    & $java -cp $out TestMain
    if ($LASTEXITCODE -ne 0) { throw 'Session tests failed' }
} finally { Remove-Item -LiteralPath $out -Recurse -Force }
