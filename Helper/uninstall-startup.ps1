$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ShortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\LiftCall.WindowsHelper.lnk"
if (Test-Path -LiteralPath $ShortcutPath) {
    Remove-Item -LiteralPath $ShortcutPath -Force
    Write-Host "Usunięto autostart: $ShortcutPath"
}
else {
    Write-Host "Skrót autostartu nie istnieje."
}
