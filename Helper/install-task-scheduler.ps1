$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$HelperDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$ExePath = Join-Path $HelperDir "LiftCall.WindowsHelper.exe"
$ConfigPath = Join-Path $HelperDir "config.json"
$TaskName = "LiftCall.WindowsHelper"
$Identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "EXE not found: $ExePath"
}

if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    $ExamplePath = Join-Path $HelperDir "config.example.json"
    if (Test-Path -LiteralPath $ExamplePath -PathType Leaf) {
        Copy-Item -LiteralPath $ExamplePath -Destination $ConfigPath
    }
}

$Arguments = '--config ' + [char]34 + $ConfigPath + [char]34
$Action = New-ScheduledTaskAction -Execute $ExePath -Argument $Arguments -WorkingDirectory $HelperDir
$Trigger = New-ScheduledTaskTrigger -AtLogOn -User $Identity
$Principal = New-ScheduledTaskPrincipal -UserId $Identity -LogonType Interactive -RunLevel Limited
$Settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Force | Out-Null
Write-Host "Task created: $TaskName"
Write-Host "User: $Identity"
Write-Host "The task runs the EXE at logon with limited privileges."
