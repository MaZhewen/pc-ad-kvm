$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out=Join-Path $env:TEMP ('pckvm-clipboard-integration-'+[Guid]::NewGuid().ToString('N')+'.exe')
try {
 & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -r:System.Windows.Forms.dll -out:$out "$PSScriptRoot\IntegrationMain.cs" "$root\src\agent\Config.cs" "$root\src\agent\Protocol.cs" "$root\src\agent\DeviceLauncher.cs" "$root\src\agent\AdbClient.cs" "$root\src\agent\AdbDevice.cs"
 if($LASTEXITCODE -ne 0){throw 'Compile failed'}
 & $out
 if($LASTEXITCODE -ne 0){throw 'Integration tests failed'}
} finally {if(Test-Path -LiteralPath $out){Remove-Item -LiteralPath $out}}
