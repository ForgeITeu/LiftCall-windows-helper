using LiftCall.WindowsHelper;

var configTestPath = Path.Combine(Path.GetTempPath(), $"liftcall-helper-config-{Guid.NewGuid():N}.json");
try
{
    var missingConfig = HelperConfig.Load(configTestPath);
    if (missingConfig.State != ConfigLoadState.Missing || missingConfig.IsReady)
    {
        throw new InvalidOperationException("Brak config.json musi oznaczać stan konfiguracji wymaganej.");
    }

    File.WriteAllText(configTestPath, "{ invalid json");
    var invalidConfig = HelperConfig.Load(configTestPath);
    if (invalidConfig.State != ConfigLoadState.Invalid || invalidConfig.IsReady || string.IsNullOrWhiteSpace(invalidConfig.ErrorMessage))
    {
        throw new InvalidOperationException("Niepoprawny config.json musi zostać rozpoznany bez uruchamiania helpera.");
    }

    var savedConfig = new HelperConfig
    {
        Port = 32145,
        WindowTitleContains = "LiftCall",
        PwaShortcutPath = "",
        AllowedOrigins = new List<string> { "https://liftcall.forge-it.eu" },
    };
    savedConfig.Save(configTestPath);
    var validConfig = HelperConfig.Load(configTestPath);
    if (validConfig.State != ConfigLoadState.Valid || !validConfig.IsReady || validConfig.Configuration.Host != "127.0.0.1")
    {
        throw new InvalidOperationException("Poprawnie zapisany config.json musi przejść do normalnego monitorowania.");
    }
}
finally
{
    if (File.Exists(configTestPath))
    {
        File.Delete(configTestPath);
    }
}

var now = DateTimeOffset.UtcNow;

AssertStatus(
    "helper działa, PWA połączona",
    new TrayStatusInput(true, true, true, now, 5, true, true, false),
    "Status: połączony",
    TrayStatus.Green);

AssertStatus(
    "zamknięty heartbeat ma priorytet nad procesem i oknem",
    new TrayStatusInput(true, true, true, now, 1, false, true, false, "closed"),
    "Status: PWA zamknięta",
    TrayStatus.Red);

AssertStatus(
    "świeży heartbeat i okno bez authenticated",
    new TrayStatusInput(true, true, true, now, 5, true, null, false),
    "Status: heartbeat nieaktywny",
    TrayStatus.Yellow);

AssertStatus(
    "helper działa, PWA zamknięta ponad 60 sekund",
    new TrayStatusInput(true, true, false, now.AddSeconds(-61), 61, false, true, false),
    "Status: PWA zamknięta",
    TrayStatus.Red);

AssertStatus(
    "helper działa, PWA i proces zostały zamknięte",
    new TrayStatusInput(true, false, false, now.AddSeconds(-61), 61, false, null, false),
    "Status: heartbeat nieaktywny",
    TrayStatus.Yellow);

AssertStatus(
    "logout heartbeat followed by heartbeat stop remains logged out",
    new TrayStatusInput(true, false, false, now, 1, false, false, false, "closed"),
    "Status: PWA wylogowana",
    TrayStatus.Yellow);

AssertStatus(
    "helper działa, użytkownik został wylogowany",
    new TrayStatusInput(true, true, true, now, 5, true, false, false),
    "Status: PWA wylogowana",
    TrayStatus.Yellow);

AssertStatus(
    "helper działa, proces PWA działa bez właściwego okna",
    new TrayStatusInput(true, true, false, now, 5, true, true, false),
    "Status: PWA zamknięta",
    TrayStatus.Red);

AssertStatus(
    "helper działa, heartbeat wygasł przy istniejącym oknie",
    new TrayStatusInput(true, true, true, now.AddSeconds(-61), 61, false, true, false),
    "Status: heartbeat nieaktywny",
    TrayStatus.Yellow);

AssertStatus(
    "błąd konfiguracji lub serwera",
    new TrayStatusInput(true, true, true, now, 5, true, true, true),
    "Status: błąd helpera",
    TrayStatus.Red);

var neverSeen = TrayStatusCalculator.Calculate(
    new TrayStatusInput(true, false, false, null, null, false, null, false));
if (neverSeen.Label.Contains("helper niedostępny", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Tray działającego helpera nie może pokazywać 'helper niedostępny'.");
}
if (neverSeen.Label != "Status: heartbeat nieaktywny" || neverSeen.Icon != TrayStatus.Yellow)
{
    throw new InvalidOperationException("Brak heartbeat bez znanego uwierzytelnienia nie może być fałszywym zamknięciem PWA.");
}

var alertNow = DateTimeOffset.UtcNow;
var healthyObservation = new TrayAlertObservation(true, false, true, true, true, "healthy", true, true, alertNow);
var loggedOutObservation = new TrayAlertObservation(true, false, true, true, true, "healthy", false, false);
var closedObservation = new TrayAlertObservation(true, false, true, true, false, "closed", true, false, alertNow);
var closedAndLoggedOutObservation = new TrayAlertObservation(true, false, true, true, false, "closed", false, false, alertNow);
var heartbeatInactiveObservation = new TrayAlertObservation(true, false, true, true, false, "stale", null, false);
var staleLoggedOutObservation = new TrayAlertObservation(true, false, true, true, false, "stale", false, false, alertNow);

var logoutAlerts = new TrayAlertState();
if (logoutAlerts.Observe(loggedOutObservation, alertNow) is not null)
{
    throw new InvalidOperationException("Wylogowanie przy starcie nie powinno alarmować bez wcześniejszego poprawnego stanu.");
}
logoutAlerts.Observe(healthyObservation, alertNow);
var logoutStarted = logoutAlerts.Observe(loggedOutObservation, alertNow.AddSeconds(1));
if (logoutStarted?.Signal != TrayAlertSignalKind.Started || logoutStarted.Alert != TrayAlertKind.LoggedOut)
{
    throw new InvalidOperationException("Przejście true -> false musi natychmiast uruchomić alert wylogowania.");
}
if (logoutAlerts.Observe(loggedOutObservation, alertNow.AddSeconds(29)) is not null)
{
    throw new InvalidOperationException("Alert wylogowania nie może powtarzać się przed upływem 30 sekund.");
}
var logoutReminder = logoutAlerts.Observe(loggedOutObservation, alertNow.AddSeconds(31));
if (logoutReminder?.Signal != TrayAlertSignalKind.Reminder || logoutReminder.Alert != TrayAlertKind.LoggedOut)
{
    throw new InvalidOperationException("Alert wylogowania musi powtarzać się co 30 sekund.");
}
if (logoutAlerts.Observe(healthyObservation, alertNow.AddSeconds(32))?.Signal != TrayAlertSignalKind.Stopped)
{
    throw new InvalidOperationException("Świeży heartbeat i ponowne zalogowanie muszą zatrzymać alert.");
}

var closedAlerts = new TrayAlertState();
closedAlerts.Observe(healthyObservation, alertNow);
var closedStarted = closedAlerts.Observe(closedObservation, alertNow.AddSeconds(1));
if (closedStarted?.Signal != TrayAlertSignalKind.Started || closedStarted.Alert != TrayAlertKind.PwaClosed)
{
    throw new InvalidOperationException("Zamknięcie PWA musi natychmiast uruchomić czerwony alert.");
}
if (!closedAlerts.CriticalPwaClosed)
{
    throw new InvalidOperationException("Alert zamkniętej PWA musi blokować degradację trayu do koloru żółtego.");
}
var closedAndLoggedOutAlerts = new TrayAlertState();
closedAndLoggedOutAlerts.Observe(healthyObservation, alertNow);
var criticalClosed = closedAndLoggedOutAlerts.Observe(closedAndLoggedOutObservation, alertNow.AddSeconds(1));
if (criticalClosed?.Alert != TrayAlertKind.LoggedOut || closedAndLoggedOutAlerts.CriticalPwaClosed)
{
    throw new InvalidOperationException("authenticated=false musi mieć priorytet nad page_status=closed.");
}
var closedThenOldLoggedOut = new TrayAlertState();
closedThenOldLoggedOut.Observe(healthyObservation, alertNow);
closedThenOldLoggedOut.Observe(closedObservation, alertNow.AddSeconds(1));
var oldLoggedOut = closedThenOldLoggedOut.Observe(
    new TrayAlertObservation(true, false, true, true, true, "healthy", false, false, alertNow),
    alertNow.AddSeconds(2));
if (oldLoggedOut?.Alert != TrayAlertKind.LoggedOut)
{
    throw new InvalidOperationException("Jawny authenticated=false musi przełączyć alert na wylogowanie.");
}
if (closedThenOldLoggedOut.CriticalPwaClosed)
{
    throw new InvalidOperationException("Alert wylogowania nie może pozostać krytycznym alertem zamknięcia.");
}
var logoutThenClosed = new TrayAlertState();
logoutThenClosed.Observe(healthyObservation, alertNow);
logoutThenClosed.Observe(loggedOutObservation, alertNow.AddSeconds(1));
var closedAfterLogout = logoutThenClosed.Observe(closedAndLoggedOutObservation, alertNow.AddSeconds(2));
if (closedAfterLogout is not null || logoutThenClosed.ActiveAlert != TrayAlertKind.LoggedOut || logoutThenClosed.CriticalPwaClosed)
{
    throw new InvalidOperationException("Zamknięcie PWA po wylogowaniu musi pozostać alertem wylogowania.");
}
var staleLoggedOut = new TrayAlertState();
staleLoggedOut.Observe(healthyObservation, alertNow);
var staleLoggedOutSignal = staleLoggedOut.Observe(staleLoggedOutObservation, alertNow.AddSeconds(1));
if (staleLoggedOutSignal?.Alert != TrayAlertKind.LoggedOut)
{
    throw new InvalidOperationException("authenticated=false musi pokazywać alert wylogowania także przed heartbeat/stop.");
}
if (closedAlerts.Observe(heartbeatInactiveObservation, alertNow.AddSeconds(20)) is not null)
{
    throw new InvalidOperationException("Przejście closed -> heartbeat_inactive nie może resetować alarmu.");
}
var closedReminder = closedAlerts.Observe(heartbeatInactiveObservation, alertNow.AddSeconds(32));
if (closedReminder?.Signal != TrayAlertSignalKind.Reminder || closedReminder.Alert != TrayAlertKind.PwaClosed)
{
    throw new InvalidOperationException("Alert zamknięcia PWA musi zachować wspólny interwał przypomnień.");
}
if (closedAlerts.Observe(healthyObservation, alertNow.AddSeconds(33))?.Signal != TrayAlertSignalKind.Stopped)
{
    throw new InvalidOperationException("Powrót healthy/authenticated=true musi zatrzymać alert zamknięcia.");
}

var helperClosedAlerts = new TrayAlertState();
if (helperClosedAlerts.Observe(new TrayAlertObservation(false, false, false, false, false, "closed", null, false), alertNow, suppressAlerts: true) is not null)
{
    throw new InvalidOperationException("Zamknięcie helpera przez użytkownika nie może uruchamiać alertu PWA.");
}

Console.WriteLine("Tray status tests passed.");

static void AssertStatus(
    string scenario,
    TrayStatusInput input,
    string expectedLabel,
    TrayStatus expectedIcon)
{
    var actual = TrayStatusCalculator.Calculate(input);
    if (actual.Label != expectedLabel || actual.Icon != expectedIcon)
    {
        throw new InvalidOperationException(
            $"{scenario}: expected '{expectedLabel}', {expectedIcon}; "
            + $"got '{actual.Label}', {actual.Icon}.");
    }
}
