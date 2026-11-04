[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9._:-]+$')][string]$Target,
    [Parameter(Mandatory)][ipaddress]$TabletAddress,
    [ValidateRange(30,900)][int]$Seconds = 480
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$adb = (Get-Command adb.exe).Source
$jbr = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin'
$r8 = if ($env:PCKVM_R8_JAR) { $env:PCKVM_R8_JAR } else { Join-Path $root 'tools\r8.jar' }
$id = [Guid]::NewGuid().ToString('N')
$replacement = [Guid]::NewGuid().ToString('N')
$work = Join-Path $root ".superpowers\sdd\2026-11-04-direct-network-implementation-plan\probe-$id"
$remote = "/data/local/tmp/pckvm-direct-probe-$id"
$instance = "$remote/$id"
$pcAddress = (Find-NetRoute -RemoteIPAddress $TabletAddress | Where-Object IPAddress | Select-Object -First 1).IPAddress
if (-not $pcAddress -or $pcAddress -eq '0.0.0.0') { throw 'No local route to the tablet' }

function Invoke-Target {
    & $adb -s $Target @args
    if ($LASTEXITCODE -ne 0) { throw "ADB target operation failed (exit $LASTEXITCODE)" }
}

function Start-HiddenProcess([string]$File, [string]$Arguments, [bool]$PipeInput = $false) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $File
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardInput = $PipeInput
    return [Diagnostics.Process]::Start($info)
}

function Read-LineWithin($Process, [int]$TimeoutMs) {
    $read = $Process.StandardOutput.ReadLineAsync()
    if (-not $read.Wait($TimeoutMs) -or $null -eq $read.Result) { throw 'Probe process did not report readiness' }
    return $read.Result
}

function Test-OwnerLock {
    Invoke-Target shell "umask 077; $entry publish $remote $id && $entry publish $remote $replacement && $entry cleanup $remote $id"
    if ((Invoke-Target shell "cat $remote/owner") -ne $replacement) { throw 'Old cleanup removed the new owner' }
    $holder = Start-HiddenProcess $adb "-s $Target shell `"umask 077; $entry hold $remote $replacement`""
    $writer = $null
    $lockPid = $null
    try {
        $line = Read-LineWithin $holder 8000
        if ($line -notmatch '^LOCK_HELD pid=([1-9][0-9]*)$') { throw 'Unexpected lock holder output' }
        $lockPid = $Matches[1]
        $writer = Start-HiddenProcess $adb "-s $Target shell `"umask 077; $entry publish $remote $id`""
        if ($writer.WaitForExit(1000)) { throw 'Second publisher completed while the lock was held' }
        if ((Invoke-Target shell "cat $remote/owner") -ne $replacement) { throw 'Owner changed while locked' }
        Invoke-Target shell "kill -9 $lockPid"
        $lockPid = $null
        if (-not $writer.WaitForExit(8000) -or $writer.ExitCode -ne 0) { throw 'Publisher did not acquire the released lock' }
        if ((Invoke-Target shell "cat $remote/owner") -ne $id) { throw 'Owner did not update after lock release' }
        Write-Output 'PASS owner replacement, cross-process lock exclusion and crash release'
    } finally {
        if ($lockPid) { & $adb -s $Target shell "kill -9 $lockPid" }
        if ($writer) { $writer.Dispose() }
        $holder.Dispose()
    }
}

$classes = Join-Path $work 'classes'
New-Item -ItemType Directory -Path $classes -Force | Out-Null
& "$jbr\javac.exe" --release 8 -Xlint:-options -d $classes (Join-Path $PSScriptRoot 'DirectProbe.java') (Join-Path $root 'src\injector\PinnedTls.java')
if ($LASTEXITCODE -ne 0) { throw 'Probe Java compilation failed' }
& "$jbr\java.exe" -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $work @((Get-ChildItem $classes -Filter '*.class').FullName)
if ($LASTEXITCODE -ne 0) { throw 'Probe DEX compilation failed' }
$zip = Join-Path $work 'probe.zip'
Compress-Archive -LiteralPath (Join-Path $work 'classes.dex') -DestinationPath $zip
$jar = Join-Path $work 'probe.jar'
Move-Item -LiteralPath $zip -Destination $jar
$exe = Join-Path $work 'probe.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:exe -platform:x64 "-out:$exe" `
    (Join-Path $PSScriptRoot 'ProbeHost.cs') (Join-Path $root 'src\agent\DirectNetworkGateway.cs') `
    (Join-Path $root 'src\agent\DirectTlsCertificate.cs') (Join-Path $root 'src\agent\Protocol.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe host compilation failed' }

$entry = "CLASSPATH=$instance/probe.jar app_process / DirectProbe"
$server = $null
$deployed = $false
$started = $false
try {
    Invoke-Target shell "umask 077; mkdir $remote"
    $deployed = $true
    Invoke-Target shell "umask 077; mkdir $instance $remote/$replacement && touch $instance/config $instance/probe.log $remote/owner.lock"
    Invoke-Target push $jar "$instance/probe.jar"
    Invoke-Target shell "chmod 600 $instance/probe.jar"
    $hash = ((Invoke-Target shell "sha256sum $instance/probe.jar") -split '\s+')[0]
    if ($hash -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $jar).Hash) { throw 'Probe jar integrity check failed' }
    Test-OwnerLock
    Invoke-Target shell "stat -c '%a %n' $remote $instance $instance/config $instance/probe.jar $instance/probe.log $remote/owner $remote/owner.lock"

    $server = Start-HiddenProcess $exe "$pcAddress $Seconds $id" $true
    $ready = Read-LineWithin $server 10000
    if ($ready -notmatch '^([0-9]+) ([a-f0-9]{64}) ([A-F0-9]{64})$') { throw 'Invalid private TLS probe setup response' }
    $port, $pin, $token = $Matches[1], $Matches[2], $Matches[3]
    @($pcAddress, $port, $pin, $token, $Seconds) | & $adb -s $Target shell -T "cat > $instance/config"
    if ($LASTEXITCODE -ne 0) { throw 'Probe config upload failed' }
    Remove-Variable ready,token,Matches
    $start = "umask 077; (CLASSPATH=$instance/probe.jar nohup setsid app_process / DirectProbe run $remote $id </dev/null >$instance/probe.log 2>&1 &)"
    Invoke-Target shell $start
    $started = $true
    Write-Output "Listening on ${pcAddress}:$port; target=$Target; device log: $instance/probe.log"
    $log = Join-Path $work 'host.log'
    $samples = 0
    while ($null -ne ($line = $server.StandardOutput.ReadLine())) {
        Add-Content -LiteralPath $log -Encoding UTF8 -Value $line
        if ($line -notmatch ' echo ' -or (++$samples % 15) -eq 1) { Write-Output $line }
    }
    $server.WaitForExit()
    if ($server.ExitCode -ne 0) { throw "No verified TLS echo. Read $instance/probe.log using an available ADB target." }
    Write-Output "Finished: $samples echoes. Host log: $log"
} finally {
    if ($server) {
        if (-not $server.HasExited) {
            $server.StandardInput.Close()
            if (-not $server.WaitForExit(6000)) { Write-Warning 'Probe host did not stop within six seconds' }
        }
        $server.Dispose()
    }
    if ($deployed -and -not $started) {
        & $adb -s $Target shell "umask 077; touch $instance/stop; rm -f $instance/config"
    }
}
