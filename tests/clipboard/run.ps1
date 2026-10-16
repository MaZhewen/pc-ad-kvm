$ErrorActionPreference='Stop'
foreach($name in @('wire','integration','providers','cache','images','android','session','lock-order','service','publication','windows')) {
    & (Join-Path $PSScriptRoot ('run-'+$name+'.ps1'))
    if($LASTEXITCODE -ne 0){throw ('Clipboard '+$name+' tests failed')}
}
