$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$ReleaseDir = Join-Path $ProjectDir "release"
$ProjectFile = Join-Path $ProjectDir "LiftCall.WindowsHelper.csproj"

[xml]$project = Get-Content -LiteralPath $ProjectFile -Raw
$version = ([string]($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Nie znaleziono wersji w LiftCall.WindowsHelper.csproj."
}

$sourceCommit = (& git -C $ProjectDir rev-parse HEAD 2>$null | Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($sourceCommit)) {
    $sourceCommit = "unknown"
}
$sourceTreeDirty = -not [string]::IsNullOrWhiteSpace((& git -C $ProjectDir status --porcelain 2>$null | Out-String).Trim())

$buildCommand = "dotnet publish LiftCall.WindowsHelper.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output release"

& (Join-Path $ProjectDir "generate-icons.ps1")

if (Test-Path -LiteralPath $ReleaseDir) {
    Remove-Item -LiteralPath $ReleaseDir -Recurse -Force
}

dotnet publish $ProjectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:EnableWindowsTargeting=true `
    --output $ReleaseDir

Copy-Item -LiteralPath (Join-Path $ProjectDir "config.example.json") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "config.json") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "install-startup.ps1") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "uninstall-startup.ps1") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "install-task-scheduler.ps1") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "README.md") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "pwa-heartbeat.example.js") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "debug-helper.ps1") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "test-helper.ps1") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "test-show.ps1") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "LiftCall.WindowsHelper.ico") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "LiftCall.WindowsHelper.green.ico") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "LiftCall.WindowsHelper.yellow.ico") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "LiftCall.WindowsHelper.red.ico") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "generate-icons.ps1") -Destination $ReleaseDir -Force
Copy-Item -LiteralPath (Join-Path $ProjectDir "desktop-helper-alert.wav") -Destination $ReleaseDir -Force

$artifactPath = Join-Path $ReleaseDir "LiftCall.WindowsHelper.exe"
$sha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
$buildTimeUtc = (Get-Date).ToUniversalTime().ToString("o")

Set-Content -LiteralPath (Join-Path $ReleaseDir "SHA256SUMS.txt") `
    -Value ("{0} *LiftCall.WindowsHelper.exe" -f $sha256) `
    -Encoding ascii

@{
    product = "LiftCall.WindowsHelper"
    version = $version
    source_commit = $sourceCommit
    source_tree_dirty = $sourceTreeDirty
    build_time_utc = $buildTimeUtc
    runtime = "win-x64"
    self_contained = $true
    single_file = $true
    artifact = "LiftCall.WindowsHelper.exe"
    sha256 = $sha256
    build_command = $buildCommand
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReleaseDir "release-manifest.json") -Encoding utf8

Write-Host "Gotowa wersja produkcyjna: $ReleaseDir"
Write-Host "Wersja: $version"
Write-Host "Commit źródłowy: $sourceCommit"
Write-Host "SHA-256: $sha256"
