$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$HelperDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$ExePath = Join-Path $HelperDir "LiftCall.WindowsHelper.exe"
$ConfigPath = Join-Path $HelperDir "config.json"
$Origin = "https://liftcall.forge-it.eu"
$DeniedOrigin = "https://not-allowed.example"
$BaseUrl = "http://127.0.0.1:32145"

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "Nie znaleziono EXE: $ExePath" }
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw "Nie znaleziono config.json: $ConfigPath" }

Get-Process -Name "LiftCall.WindowsHelper" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$process = Start-Process -FilePath $ExePath -ArgumentList @("--config", $ConfigPath) -WorkingDirectory $HelperDir -WindowStyle Hidden -PassThru

function Assert-Equal([object] $Actual, [object] $Expected, [string] $Name) {
    if ($Actual -ne $Expected) { throw "${Name}: oczekiwano '$Expected', otrzymano '$Actual'." }
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-StatusJson($Response, [string] $Name, [bool] $Extended = $false) {
    Assert-True ($Response.StatusCode -in @(200, 503)) "${Name}: nieoczekiwany status HTTP $($Response.StatusCode)."
    $expectedStatus = if ($Response.StatusCode -eq 200) { "ok" } else { "error" }
    $json = $Response.Content | ConvertFrom-Json
    Assert-Equal $json.status $expectedStatus "${Name} status JSON"
    if ($Extended) {
        Assert-Equal $json.service "liftcall-windows-helper" "${Name} service"
        Assert-Equal $json.version "1.0.0" "${Name} version"
        Assert-Equal $json.platform "windows" "${Name} platform"
        Assert-True ($null -ne $json.PSObject.Properties['page_heartbeat_seen']) "${Name}: brak page_heartbeat_seen."
        Assert-True ($null -ne $json.PSObject.Properties['page_heartbeat_age_seconds']) "${Name}: brak page_heartbeat_age_seconds."
        Assert-True ($null -ne $json.PSObject.Properties['page_status']) "${Name}: brak page_status."
        Assert-True ($null -ne $json.PSObject.Properties['authenticated']) "${Name}: brak authenticated."
    } else {
        Assert-Equal @($json.PSObject.Properties.Name).Count 1 "${Name} liczba pól JSON"
        Assert-Equal (($Response.Content -replace '\s', '') ) (('{"status":"' + $expectedStatus + '"}')) "${Name} dokładny JSON"
    }
}

try {
    $health = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $health = Invoke-WebRequest -Uri "$BaseUrl/health" -Headers @{ Origin = $Origin } -UseBasicParsing -TimeoutSec 2
            break
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if ($null -eq $health) { throw "Helper nie uruchomił serwera HTTP." }

    Assert-StatusJson $health "/health" $true
    Assert-Equal $health.Headers["Access-Control-Allow-Origin"] $Origin "/health CORS"
    Assert-Equal $health.Headers["Vary"] "Origin" "/health Vary"

    $options = Invoke-WebRequest -Method Options -Uri "$BaseUrl/show" -Headers @{
        Origin = $Origin
        "Access-Control-Request-Method" = "POST"
        "Access-Control-Request-Headers" = "Content-Type"
    } -UseBasicParsing
    Assert-Equal $options.StatusCode 204 "OPTIONS status HTTP"
    Assert-Equal $options.Headers["Access-Control-Allow-Origin"] $Origin "OPTIONS CORS"
    Assert-Equal $options.Headers["Access-Control-Allow-Methods"] "GET, POST, OPTIONS" "OPTIONS methods"
    Assert-Equal $options.Headers["Access-Control-Allow-Headers"] "Content-Type, X-LiftCall-Authenticated" "OPTIONS headers"
    Assert-Equal $options.Headers["Vary"] "Origin" "OPTIONS Vary"

    $show = Invoke-WebRequest -Method Post -Uri "$BaseUrl/show" -Headers @{ Origin = $Origin } -UseBasicParsing
    Assert-StatusJson $show "/show"
    Assert-Equal $show.Headers["Access-Control-Allow-Origin"] $Origin "/show CORS"

    $heartbeat = Invoke-WebRequest -Method Post -Uri "$BaseUrl/heartbeat" -Headers @{ Origin = $Origin; "X-LiftCall-Authenticated" = "true" } -UseBasicParsing
    Assert-Equal $heartbeat.StatusCode 200 "/heartbeat status HTTP"
    Assert-Equal $heartbeat.Headers["Access-Control-Allow-Origin"] $Origin "/heartbeat CORS"
    $heartbeatJson = $heartbeat.Content | ConvertFrom-Json
    Assert-Equal $heartbeatJson.ok $true "/heartbeat ok"
    Assert-Equal $heartbeatJson.ttl 60 "/heartbeat ttl"
    Assert-Equal @($heartbeatJson.PSObject.Properties.Name).Count 2 "/heartbeat liczba pól JSON"

    $stop = Invoke-WebRequest -Method Post -Uri "$BaseUrl/heartbeat/stop" -Headers @{ Origin = $Origin } -UseBasicParsing
    Assert-Equal $stop.StatusCode 200 "/heartbeat/stop status HTTP"
    Assert-Equal $stop.Headers["Access-Control-Allow-Origin"] $Origin "/heartbeat/stop CORS"
    $stopJson = $stop.Content | ConvertFrom-Json
    Assert-Equal $stopJson.ok $true "/heartbeat/stop ok"
    Assert-Equal $stopJson.status "closed" "/heartbeat/stop status JSON"
    Assert-Equal @($stopJson.PSObject.Properties.Name).Count 2 "/heartbeat/stop liczba pól JSON"

    $reconnect = Invoke-WebRequest -Method Post -Uri "$BaseUrl/heartbeat" -Headers @{ Origin = $Origin; "X-LiftCall-Authenticated" = "true" } -UseBasicParsing
    Assert-Equal $reconnect.StatusCode 200 "/heartbeat reconnect status HTTP"

    $heartbeatOptions = Invoke-WebRequest -Method Options -Uri "$BaseUrl/heartbeat" -Headers @{
        Origin = $Origin
        "Access-Control-Request-Method" = "POST"
        "Access-Control-Request-Headers" = "Content-Type"
    } -UseBasicParsing
    Assert-Equal $heartbeatOptions.StatusCode 204 "OPTIONS /heartbeat status HTTP"
    Assert-Equal $heartbeatOptions.Headers["Access-Control-Allow-Origin"] $Origin "OPTIONS /heartbeat CORS"
    Assert-Equal $heartbeatOptions.Headers["Vary"] "Origin" "OPTIONS /heartbeat Vary"

    $stopOptions = Invoke-WebRequest -Method Options -Uri "$BaseUrl/heartbeat/stop" -Headers @{
        Origin = $Origin
        "Access-Control-Request-Method" = "POST"
        "Access-Control-Request-Headers" = "Content-Type"
    } -UseBasicParsing
    Assert-Equal $stopOptions.StatusCode 204 "OPTIONS /heartbeat/stop status HTTP"
    Assert-Equal $stopOptions.Headers["Access-Control-Allow-Origin"] $Origin "OPTIONS /heartbeat/stop CORS"

    $denied = Invoke-WebRequest -Uri "$BaseUrl/health" -Headers @{ Origin = $DeniedOrigin } -UseBasicParsing
    Assert-True ([string]::IsNullOrEmpty([string]$denied.Headers["Access-Control-Allow-Origin"])) "Niedozwolony origin otrzymał CORS."

    [pscustomobject]@{
        Result = "PASS"
        ConfigPath = $ConfigPath
        HealthStatus = $health.StatusCode
        HealthJson = $health.Content
        OptionsStatus = $options.StatusCode
        ShowStatus = $show.StatusCode
        HeartbeatStatus = $heartbeat.StatusCode
        HeartbeatStopStatus = $stop.StatusCode
        HeartbeatReconnectStatus = $reconnect.StatusCode
        DeniedOriginHeader = "brak"
    }
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Milliseconds 400
}
