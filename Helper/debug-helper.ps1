param(
    [string] $ConfigPath = ".\config.json"
)

$ErrorActionPreference = "Stop"
$exePath = Join-Path $PSScriptRoot "LiftCall.WindowsHelper.exe"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Nie znaleziono pliku helpera: $exePath"
}

& $exePath --debug --config $ConfigPath
exit $LASTEXITCODE
