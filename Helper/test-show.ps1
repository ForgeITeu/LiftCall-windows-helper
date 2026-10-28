$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type @"
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class LiftCallWindowTest
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    private const int SwMinimize = 6;

    public static IntPtr Find(string titleContains, string className, string processName)
    {
        IntPtr result = IntPtr.Zero;
        var titleSelector = (titleContains ?? "").Trim().ToUpperInvariant();
        var classSelector = (className ?? "").Trim().ToUpperInvariant();
        var processSelector = (processName ?? "").Trim().ToUpperInvariant();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var title = GetTitle(hwnd);
            var windowClass = GetClass(hwnd);
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            var process = "";
            try { process = Process.GetProcessById((int)pid).ProcessName + ".exe"; } catch { }
            var titleOk = titleSelector.Length == 0 || title.ToUpperInvariant().Contains(titleSelector);
            var classOk = classSelector.Length == 0 || windowClass.ToUpperInvariant() == classSelector;
            var processOk = processSelector.Length == 0 || process.ToUpperInvariant() == processSelector;
            if (titleOk && classOk && processOk)
            {
                if (result == IntPtr.Zero) result = hwnd;
                if (titleSelector.Length > 0 && title.ToUpperInvariant() == titleSelector)
                {
                    result = hwnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool Minimize(IntPtr hwnd) { return ShowWindow(hwnd, SwMinimize); }
    public static bool IsVisible(IntPtr hwnd) { return IsWindowVisible(hwnd); }
    public static bool IsMinimized(IntPtr hwnd) { return IsIconic(hwnd); }
    public static IntPtr Foreground() { return GetForegroundWindow(); }

    private static string GetTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        var buffer = new StringBuilder(Math.Max(length + 1, 1));
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
"@

$HelperDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$ExePath = Join-Path $HelperDir "LiftCall.WindowsHelper.exe"
$ConfigPath = Join-Path $HelperDir "config.json"
$LogPath = Join-Path $HelperDir "helper.log"
$BaseUrl = "http://127.0.0.1:32145"
$Origin = "https://liftcall.forge-it.eu"
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json

Get-Process -Name "LiftCall.WindowsHelper" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$process = Start-Process -FilePath $ExePath -ArgumentList @("--config", $ConfigPath) -WorkingDirectory $HelperDir -WindowStyle Hidden -PassThru

try {
    $window = [LiftCallWindowTest]::Find($config.window_title_contains, $config.window_class, $config.process_name)
    if ($window -eq [IntPtr]::Zero) { throw "Nie znaleziono okna LiftCall przed testem." }

    [void][LiftCallWindowTest]::Minimize($window)
    Start-Sleep -Milliseconds 500
    if (-not [LiftCallWindowTest]::IsMinimized($window)) { throw "Nie udało się zminimalizować okna LiftCall." }

    $curlLines = @(& curl.exe --silent --show-error --request POST --header "Origin: $Origin" --write-out "`n%{http_code}" "$BaseUrl/show")
    if ($LASTEXITCODE -ne 0 -or $curlLines.Count -lt 2) { throw "Nie udało się wykonać POST /show przez curl." }
    $httpStatus = [int]$curlLines[$curlLines.Count - 1]
    $responseBody = ($curlLines[0..($curlLines.Count - 2)] -join "`n")
    $json = $responseBody | ConvertFrom-Json
    $states = @()
    for ($sample = 0; $sample -lt 7; $sample++) {
        $states += "${sample}:visible=$([LiftCallWindowTest]::IsVisible($window)),iconic=$([LiftCallWindowTest]::IsMinimized($window)),foreground=$([LiftCallWindowTest]::Foreground() -eq $window)"
        Start-Sleep -Milliseconds 50
    }
    $visible = [LiftCallWindowTest]::IsVisible($window)
    $minimized = [LiftCallWindowTest]::IsMinimized($window)
    $foreground = [LiftCallWindowTest]::Foreground() -eq $window

    if ($httpStatus -ne 200 -or $json.status -ne "ok" -or -not $visible -or $minimized -or -not $foreground) {
        $hwndText = "0x{0:X}" -f $window.ToInt64()
        throw "POST /show nie przeszedł weryfikacji: HWND=$hwndText, HTTP=$httpStatus, JSON=$responseBody, visible=$visible, iconic=$minimized, foreground=$foreground; próbki=$($states -join ' | ')."
    }

    [pscustomobject]@{
        Result = "PASS"
        Hwnd = "0x{0:X}" -f $window.ToInt64()
        HttpStatus = $httpStatus
        Json = $responseBody
        Visible = $visible
        Minimized = $minimized
        Foreground = $foreground
    }
}
catch {
    Write-Host "TEST ERROR: $($_.Exception.Message)"
    if (Test-Path -LiteralPath $LogPath) {
        Write-Host "--- helper.log ---"
        Get-Content -LiteralPath $LogPath -Tail 60
    }
    throw
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Milliseconds 400
}
