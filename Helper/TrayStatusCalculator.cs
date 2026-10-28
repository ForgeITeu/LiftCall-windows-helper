namespace LiftCall.WindowsHelper;

internal enum TrayStatus
{
    Green,
    Yellow,
    Red,
    Neutral,
}

internal sealed record TrayStatusInput(
    bool HelperRunning,
    bool PwaRunning,
    bool WindowFound,
    DateTimeOffset? LastHeartbeat,
    double? HeartbeatAge,
    bool HeartbeatFresh,
    bool? Authenticated,
    bool HelperInternalError,
    string PageStatus = "");

internal sealed record TrayStatusDecision(
    string Label,
    TrayStatus Icon,
    string Reason);

internal static class TrayStatusCalculator
{
    public static TrayStatusDecision Calculate(TrayStatusInput input)
    {
        if (input.HelperInternalError || !input.HelperRunning)
        {
            return new TrayStatusDecision("Status: błąd helpera", TrayStatus.Red, "helper_error");
        }

        // Explicit logout remains authoritative when pagehide later reports
        // page_status=closed.
        if (input.Authenticated == false)
        {
            return new TrayStatusDecision("Status: PWA wylogowana", TrayStatus.Yellow, "authenticated_false");
        }

        if (input.Authenticated == true
            && input.PageStatus.Equals("closed", StringComparison.OrdinalIgnoreCase))
        {
            return new TrayStatusDecision("Status: PWA zamknięta", TrayStatus.Red, "page_closed");
        }

        if (input.Authenticated == true && (!input.PwaRunning || !input.WindowFound))
        {
            return new TrayStatusDecision("Status: PWA zamknięta", TrayStatus.Red, "window_not_found");
        }

        if (!input.HeartbeatFresh
            || input.LastHeartbeat is null
            || input.HeartbeatAge is null
            || input.HeartbeatAge < 0
            || input.HeartbeatAge >= 60)
        {
            return new TrayStatusDecision("Status: heartbeat nieaktywny", TrayStatus.Yellow, "heartbeat_inactive");
        }

        if (input.Authenticated != true)
        {
            return new TrayStatusDecision("Status: heartbeat nieaktywny", TrayStatus.Yellow, "authenticated_unknown");
        }

        if (!input.WindowFound)
        {
            return new TrayStatusDecision("Status: okno LiftCall nieznalezione", TrayStatus.Red, "window_not_found");
        }

        var heartbeatFresh = input.HeartbeatFresh
            && input.LastHeartbeat is not null
            && input.HeartbeatAge is >= 0 and < 60;
        var pwaReady = input.PwaRunning
            && input.WindowFound
            && heartbeatFresh
            && input.Authenticated == true;

        return pwaReady
            ? new TrayStatusDecision("Status: połączony", TrayStatus.Green, "ready")
            : new TrayStatusDecision("Status: PWA wymaga uwagi", TrayStatus.Yellow, "pwa_not_ready");
    }
}
