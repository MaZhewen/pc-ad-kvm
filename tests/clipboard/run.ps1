$ErrorActionPreference='Stop'
foreach($name in @('wire','integration','providers','cache','android','session','service','windows')) {
    & (Join-Path $PSScriptRoot ('run-'+$name+'.ps1'))
    if($LASTEXITCODE -ne 0){throw ('Clipboard '+$name+' tests failed')}
}
