namespace LiftCall.WindowsHelper;

internal enum TrayAlertKind
{
    PwaClosed,
    LoggedOut,
    HeartbeatInactive,
    HelperError,
}

internal enum TrayAlertSignalKind
{
    Started,
    Reminder,
    Stopped,
}

internal sealed record TrayAlertObservation(
    bool HelperRunning,
    bool HelperInternalError,
    bool PwaProcessRunning,
    bool WindowFound,
    bool HeartbeatFresh,
    string PageStatus,
    bool? Authenticated,
    bool PwaOperational,
    DateTimeOffset? LastHeartbeat = null);

internal sealed record TrayAlertSignal(
    TrayAlertSignalKind Signal,
    TrayAlertKind? Alert,
    string Reason,
    DateTimeOffset? NextAlertAt);

internal sealed class TrayAlertState
{
    internal static readonly TimeSpan ReminderInterval = TimeSpan.FromSeconds(30);

    private bool _authenticationWasEverTrue;
    private bool _pwaWasOperational;
    private TrayAlertKind? _activeAlert;
    private DateTimeOffset? _nextAlertAt;

    public bool CriticalPwaClosed => _activeAlert == TrayAlertKind.PwaClosed;
    public TrayAlertKind? ActiveAlert => _activeAlert;

    public TrayAlertSignal? Observe(TrayAlertObservation observation, DateTimeOffset now, bool suppressAlerts = false)
    {
        if (suppressAlerts)
        {
            _activeAlert = null;
            _nextAlertAt = null;
            return null;
        }

        if (observation.Authenticated == true)
        {
            _authenticationWasEverTrue = true;
        }

        if (observation.PwaOperational)
        {
            _pwaWasOperational = true;
        }

        var desiredAlert = GetDesiredAlert(observation);
        if (desiredAlert is null)
        {
            if (_activeAlert is null)
            {
                return null;
            }

            var stoppedAlert = _activeAlert;
            _activeAlert = null;
            _nextAlertAt = null;
            return new TrayAlertSignal(TrayAlertSignalKind.Stopped, stoppedAlert, "recovered", null);
        }

        if (_activeAlert is null)
        {
            if (!CanStartAlert(desiredAlert.Value))
            {
                return null;
            }

            _activeAlert = desiredAlert;
            _nextAlertAt = now + ReminderInterval;
            return new TrayAlertSignal(
                TrayAlertSignalKind.Started,
                desiredAlert,
                ReasonFor(desiredAlert.Value, observation),
                _nextAlertAt);
        }

        // A closed PWA can become a stale-heartbeat state while the browser
        // is being reopened. Keep the same alert cadence and do not restart
        // the alarm just because the diagnostic label changed.
        if (_activeAlert == TrayAlertKind.PwaClosed
            && desiredAlert == TrayAlertKind.HeartbeatInactive)
        {
            desiredAlert = _activeAlert;
        }

        if (_activeAlert != desiredAlert)
        {
            _activeAlert = desiredAlert;
            _nextAlertAt = now + ReminderInterval;
            return new TrayAlertSignal(
                TrayAlertSignalKind.Started,
                desiredAlert,
                ReasonFor(desiredAlert.Value, observation),
                _nextAlertAt);
        }

        if (_nextAlertAt is null || now < _nextAlertAt.Value)
        {
            return null;
        }

        _nextAlertAt = now + ReminderInterval;
        return new TrayAlertSignal(
            TrayAlertSignalKind.Reminder,
            _activeAlert,
            ReasonFor(_activeAlert.Value, observation),
            _nextAlertAt);
    }

    private TrayAlertKind? GetDesiredAlert(TrayAlertObservation observation)
    {
        if (observation.HelperInternalError || !observation.HelperRunning)
        {
            return TrayAlertKind.HelperError;
        }

        if (observation.Authenticated == false)
        {
            return TrayAlertKind.LoggedOut;
        }

        if (observation.Authenticated == true
            && (observation.PageStatus.Equals("closed", StringComparison.OrdinalIgnoreCase)
                || (!observation.PwaProcessRunning && !observation.WindowFound)
                || !observation.WindowFound))
        {
            return TrayAlertKind.PwaClosed;
        }

        // A missing/null authentication value is not enough to classify the
        // PWA as logged out or closed.
        if (!observation.HeartbeatFresh
            || !observation.PageStatus.Equals("healthy", StringComparison.OrdinalIgnoreCase))
        {
            return TrayAlertKind.HeartbeatInactive;
        }

        return null;
    }

    private bool CanStartAlert(TrayAlertKind alert)
    {
        return alert switch
        {
            TrayAlertKind.HelperError => true,
            TrayAlertKind.PwaClosed or TrayAlertKind.HeartbeatInactive => _pwaWasOperational,
            TrayAlertKind.LoggedOut => _authenticationWasEverTrue,
            _ => false,
        };
    }

    private static string ReasonFor(TrayAlertKind alert, TrayAlertObservation observation)
    {
        return alert switch
        {
            TrayAlertKind.PwaClosed when observation.PageStatus.Equals("closed", StringComparison.OrdinalIgnoreCase) => "page_closed",
            TrayAlertKind.PwaClosed => "window_or_process_not_found",
            TrayAlertKind.LoggedOut => "authenticated_false",
            TrayAlertKind.HeartbeatInactive => "heartbeat_inactive",
            TrayAlertKind.HelperError => "helper_error",
            _ => "unknown",
        };
    }
}
