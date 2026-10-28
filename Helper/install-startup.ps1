$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$HelperDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$ExePath = Join-Path $HelperDir "LiftCall.WindowsHelper.exe"
$ConfigPath = Join-Path $HelperDir "config.json"
$StartupDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$ShortcutPath = Join-Path $StartupDir "LiftCall.WindowsHelper.lnk"

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Nie znaleziono $ExePath. Najpierw rozpakuj wersję produkcyjną z plikiem EXE."
}

if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    $ExamplePath = Join-Path $HelperDir "config.example.json"
    if (Test-Path -LiteralPath $ExamplePath -PathType Leaf) {
        Copy-Item -LiteralPath $ExamplePath -Destination $ConfigPath
    }
}

New-Item -ItemType Directory -Force -Path $StartupDir | Out-Null
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $ExePath
$Shortcut.Arguments = '--config "' + $ConfigPath + '"'
$Shortcut.WorkingDirectory = $HelperDir
$Shortcut.IconLocation = "$ExePath,0"
$Shortcut.WindowStyle = 1
$Shortcut.Description = "LiftCall Windows Helper"
$Shortcut.Save()

Write-Host "Utworzono autostart: $ShortcutPath"
Write-Host "Konfiguracja: $ConfigPath"
