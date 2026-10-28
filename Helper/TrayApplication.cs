using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace LiftCall.WindowsHelper;

internal sealed record ServerSnapshot(
    bool IsListening,
    string? LastError,
    bool PwaProcessRunning,
    bool WindowFound,
    bool PageHeartbeatSeen,
    double? PageHeartbeatAgeSeconds,
    string PageStatus,
    bool? Authenticated,
    DateTimeOffset? LastHeartbeat,
    bool PwaLaunchError);

internal sealed class HelperServerHost
{
    private readonly HelperConfig _config;
    private readonly FileLogger _logger;
    private readonly object _sync = new();
    private LocalHttpServer? _server;
    private string? _lastMonitorLog;

    public HelperServerHost(HelperConfig config, FileLogger logger)
    {
        _config = config;
        _logger = logger;
        Start();
    }

    public ServerSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            if (_server is null)
            {
                return new ServerSnapshot(false, "Serwer nie jest uruchomiony.", false, false, false, null, "never_seen", false, null, false);
            }

            var page = _server.GetPageStatus();
            var window = WindowManager.Find(_config);
            var snapshot = new ServerSnapshot(
                _server.IsListening,
                _server.LastError,
                WindowManager.IsProcessRunning(_config, window),
                window is not null,
                page.Seen,
                page.AgeSeconds,
                page.Status,
                page.Authenticated,
                _server.LastHeartbeat,
                _server.PwaLaunchError);
            var monitorLog = $"listening={snapshot.IsListening}, process={snapshot.PwaProcessRunning}, window={snapshot.WindowFound}, "
                + $"heartbeat_seen={snapshot.PageHeartbeatSeen}, page_status={snapshot.PageStatus}, "
                + $"authenticated={snapshot.Authenticated}, launch_error={snapshot.PwaLaunchError}";
            if (!string.Equals(_lastMonitorLog, monitorLog, StringComparison.Ordinal))
            {
                _lastMonitorLog = monitorLog;
                _logger.Info($"Monitor state changed: {monitorLog}.");
            }

            return snapshot;
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_server is not null)
            {
                return;
            }

            _server = new LocalHttpServer(_config, _logger);
            _server.Start();
        }
    }

    public async Task StopAsync()
    {
        await StopAsync(closePwa: true).ConfigureAwait(true);
    }

    public async Task RestartAsync()
    {
        await StopAsync(closePwa: false).ConfigureAwait(true);
        Start();
    }

    private async Task StopAsync(bool closePwa)
    {
        LocalHttpServer? server;
        lock (_sync)
        {
            server = _server;
            _server = null;
        }

        if (server is not null)
        {
            await server.StopAsync(closePwa).ConfigureAwait(true);
        }
    }

    public async Task<bool> ShowLiftCallAsync()
    {
        LocalHttpServer? server;
        lock (_sync)
        {
            server = _server;
        }

        if (server is null)
        {
            return false;
        }

        return await Task.Run(server.ShowLiftCall).ConfigureAwait(true);
    }

    public async Task<bool> LaunchPwaAsync()
    {
        LocalHttpServer? server;
        lock (_sync)
        {
            server = _server;
        }

        if (server is null)
        {
            return false;
        }

        return await Task.Run(() => server.TryLaunchPwa("Popup reopen")).ConfigureAwait(true);
    }

    public void LaunchPwaOnStart()
    {
        lock (_sync)
        {
            _server?.LaunchPwaOnStart();
        }
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly HelperConfig _config;
    private readonly string _configPath;
    private readonly FileLogger _logger;
    private HelperServerHost? _serverHost;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly ToolStripMenuItem _statusMenuItem;
    private Icon? _currentIcon;
    private bool _isExiting;
    private bool _expectedPwaClose;
    private string? _lastTrayStatus;
    private bool? _lastAuthenticated;
    private string? _lastPageStatus;
    private readonly TrayAlertState _alertState = new();
    private PwaAlertDialog? _alertDialog;
    private SoundPlayer? _alertSound;
    private DateTimeOffset? _lastAlertSoundAt;
    private bool _configurationRequired;
    private string? _configurationLoadError;

    public TrayApplicationContext(
        HelperConfig config,
        string configPath,
        FileLogger logger,
        ConfigLoadResult configResult)
    {
        _config = config;
        _configPath = configPath;
        _logger = logger;
        _configurationRequired = !configResult.IsReady;
        _configurationLoadError = configResult.ErrorMessage;
        if (!_configurationRequired)
        {
            _serverHost = new HelperServerHost(config, logger);
        }
        _menu = new ContextMenuStrip();
        _statusMenuItem = new ToolStripMenuItem(_configurationRequired ? "Konfiguracja wymagana" : "Status: niepołączony")
        {
            Enabled = false
        };
        _menu.Items.Add(_statusMenuItem);
        AddMenuItem("Konfiguruj", async (_, _) => await ConfigureAsync());
        AddMenuItem("Pokaż LiftCall", async (_, _) => await ShowLiftCallAsync());
        AddMenuItem("Zamknij", async (_, _) => await ExitAsync());

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true
        };

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (_, _) => UpdateTrayStatus();
        _statusTimer.Start();
        UpdateTrayStatus();
        if (!_configurationRequired && _config.LaunchPwaOnStart)
        {
            _ = Task.Run(() => _serverHost?.LaunchPwaOnStart());
        }
        _logger.Info("Tray application started.");
    }

    public void ShowConfigurationOnStart()
    {
        if (!_configurationRequired || _isExiting)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_configurationLoadError))
        {
            MessageBox.Show(
                $"Plik config.json jest niepoprawny i wymaga zapisania ponownie.\n\n{_configurationLoadError}",
                "Konfiguracja LiftCall wymagana",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        ConfigureAsync().GetAwaiter().GetResult();
    }

    private ToolStripMenuItem AddMenuItem(string text, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += handler;
        _menu.Items.Add(item);
        return item;
    }

    private async Task ConfigureAsync()
    {
        using var dialog = new HelperConfigurationForm(_config);
        if (dialog.ShowDialog() != DialogResult.OK || dialog.Configuration is null)
        {
            return;
        }

        try
        {
            var updatedConfig = dialog.Configuration;
            updatedConfig.Save(_configPath);
            _config.ApplyFrom(updatedConfig);
            _serverHost = _serverHost is null
                ? new HelperServerHost(_config, _logger)
                : await RestartServerAsync();
            _configurationRequired = false;
            _configurationLoadError = null;
            UpdateTrayStatus();
            if (_config.LaunchPwaOnStart)
            {
                _ = Task.Run(() => _serverHost?.LaunchPwaOnStart());
            }
            _logger.Info($"Configuration saved to {_configPath} and HTTP server restarted.");
        }
        catch (Exception exception)
        {
            _logger.Error($"Could not save configuration to {_configPath}.", exception);
            MessageBox.Show(
                $"Nie udało się zapisać konfiguracji.\n\n{exception.Message}",
                "LiftCall Helper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdateTrayStatus()
    {
        if (_isExiting)
        {
            return;
        }

        if (_configurationRequired || _serverHost is null)
        {
            SetConfigurationRequiredStatus();
            return;
        }

        var snapshot = _serverHost.GetSnapshot();
        var helperRunning = snapshot.IsListening;
        var helperInternalError = snapshot.LastError is not null
            || (snapshot.PwaLaunchError && !snapshot.WindowFound)
            || !snapshot.IsListening;
        var heartbeatFresh = snapshot.PageHeartbeatSeen
            && !snapshot.PageStatus.Equals("closed", StringComparison.OrdinalIgnoreCase)
            && snapshot.PageHeartbeatAgeSeconds is >= 0 and < 60;
        var decision = TrayStatusCalculator.Calculate(new TrayStatusInput(
            helperRunning,
            snapshot.PwaProcessRunning,
            snapshot.WindowFound,
            snapshot.LastHeartbeat,
            snapshot.PageHeartbeatAgeSeconds,
            heartbeatFresh,
            snapshot.Authenticated,
            helperInternalError,
            snapshot.PageStatus));

        if (!string.Equals(_lastTrayStatus, decision.Label, StringComparison.Ordinal)
            || _lastAuthenticated != snapshot.Authenticated
            || !string.Equals(_lastPageStatus, snapshot.PageStatus, StringComparison.OrdinalIgnoreCase))
        {
            var previousStatus = _lastTrayStatus ?? "brak";
            var previousAuthenticated = _lastAuthenticated?.ToString() ?? "null";
            var previousPageStatus = _lastPageStatus ?? "brak";
            _lastTrayStatus = decision.Label;
            _lastAuthenticated = snapshot.Authenticated;
            _lastPageStatus = snapshot.PageStatus;
            _logger.Info(
                $"Tray state changed: previous={previousStatus}, current={decision.Label}, "
                + $"previous_authenticated={previousAuthenticated}, authenticated={snapshot.Authenticated?.ToString() ?? "null"}, "
                + $"previous_page_status={previousPageStatus}, page_status={snapshot.PageStatus}, "
                + $"heartbeat_fresh={heartbeatFresh}, "
                + $"window_found={snapshot.WindowFound}, pwa_process_running={snapshot.PwaProcessRunning}, icon={decision.Icon}.");
            DebugConsole.WriteStateChange(previousStatus, decision.Label, decision.Reason);
        }

        HandleStateAlerts(snapshot, decision, helperInternalError);
        _statusMenuItem.Text = decision.Label;
        var icon = TrayIconFactory.Create(decision.Icon);
        var oldIcon = _currentIcon;
        _currentIcon = icon;
        _notifyIcon.Icon = icon;
        oldIcon?.Dispose();
        _notifyIcon.Text = $"LiftCall Helper\nPort: {_config.Port}";
    }

    private async Task<HelperServerHost> RestartServerAsync()
    {
        var server = _serverHost!;
        await server.RestartAsync();
        return server;
    }

    private void SetConfigurationRequiredStatus()
    {
        const string label = "Konfiguracja wymagana";
        if (!string.Equals(_lastTrayStatus, label, StringComparison.Ordinal))
        {
            _logger.Info("Tray status changed: Konfiguracja wymagana.");
            _lastTrayStatus = label;
        }

        _statusMenuItem.Text = label;
        var icon = TrayIconFactory.Create(TrayStatus.Neutral);
        var oldIcon = _currentIcon;
        _currentIcon = icon;
        _notifyIcon.Icon = icon;
        oldIcon?.Dispose();
        _notifyIcon.Text = "LiftCall Helper\nKonfiguracja wymagana";
    }

    private void HandleStateAlerts(ServerSnapshot snapshot, TrayStatusDecision decision, bool helperInternalError)
    {
        if (_expectedPwaClose)
        {
            return;
        }

        var heartbeatFresh = snapshot.PageHeartbeatSeen
            && snapshot.PageStatus.Equals("healthy", StringComparison.OrdinalIgnoreCase)
            && snapshot.PageHeartbeatAgeSeconds is >= 0 and < 60;
        var pwaOperational = snapshot.IsListening
            && snapshot.WindowFound
            && heartbeatFresh
            && snapshot.Authenticated == true;
        var now = DateTimeOffset.UtcNow;
        var signal = _alertState.Observe(new TrayAlertObservation(
            snapshot.IsListening,
            helperInternalError,
            snapshot.PwaProcessRunning,
            snapshot.WindowFound,
            heartbeatFresh,
            snapshot.PageStatus,
            snapshot.Authenticated,
            pwaOperational,
            snapshot.LastHeartbeat), now);

        if (signal is null)
        {
            return;
        }

        if (signal.Signal == TrayAlertSignalKind.Stopped)
        {
            _logger.Info($"alert_stopped; alert_reason={signal.Reason}.");
            CloseAlertDialog();
            return;
        }

        if (signal.Alert is null)
        {
            return;
        }

        var content = TrayAlertContent.For(signal.Alert.Value);
        var nextAt = signal.NextAlertAt?.ToString("O") ?? "none";
        _logger.Warn(
            $"{(signal.Signal == TrayAlertSignalKind.Started ? "alert_started" : "alert_reminder")}; "
            + $"alert_reason={signal.Reason}; alert_next_at={nextAt}; alert={signal.Alert.Value}.");
        ShowOrUpdateAlert(signal.Alert.Value);
        ShowTrayNotification(content, signal.Alert.Value == TrayAlertKind.PwaClosed || signal.Alert.Value == TrayAlertKind.HelperError);
        PlayAlertSound();
    }

    private void ShowOrUpdateAlert(TrayAlertKind alert)
    {
        if (_alertDialog is null || _alertDialog.IsDisposed)
        {
            _alertDialog = new PwaAlertDialog(alert);
            _alertDialog.PrimaryActionRequested += HandleAlertPrimaryAction;
            _alertDialog.FormClosed += (_, _) => _alertDialog = null;
            _alertDialog.Show();
        }
        else
        {
            _alertDialog.UpdateAlert(alert);
            if (!_alertDialog.Visible)
            {
                _alertDialog.Show();
            }

            _alertDialog.BringToFront();
            _alertDialog.Activate();
        }
    }

    private void HandleAlertPrimaryAction(TrayAlertKind alert)
    {
        if (alert == TrayAlertKind.PwaClosed)
        {
            _ = ReopenPwaAsync();
            return;
        }

        _ = ShowLiftCallAsync();
    }

    private async Task ReopenPwaAsync()
    {
        if (_serverHost is null)
        {
            return;
        }

        _logger.Info("PWA reopen requested from the closed-PWA dialog.");
        var launched = await _serverHost.LaunchPwaAsync();
        if (!launched)
        {
            _logger.Error("PWA reopen failed: configured PWA could not be started or its window did not appear.");
            return;
        }

        _logger.Info("PWA reopen completed; monitoring continues until heartbeat and authentication recover.");
        UpdateTrayStatus();
    }

    private void CloseAlertDialog()
    {
        if (_alertDialog is null)
        {
            return;
        }

        var dialog = _alertDialog;
        _alertDialog = null;
        dialog.Close();
        dialog.Dispose();
    }

    private void ShowTrayNotification(TrayAlertContent content, bool error)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(5000, content.Title, content.Message, error ? ToolTipIcon.Error : ToolTipIcon.Warning);
            _logger.Info($"Tray notification shown: {content.Title}");
        }
        catch (Exception exception)
        {
            _logger.Warn($"Tray notification could not be shown: {exception.Message}");
        }
    }

    private void PlayAlertSound()
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastAlertSoundAt is not null && now - _lastAlertSoundAt.Value < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastAlertSoundAt = now;
        try
        {
            _alertSound ??= new SoundPlayer(Path.Combine(AppContext.BaseDirectory, "desktop-helper-alert.wav"));
            _alertSound.Play();
        }
        catch (Exception exception)
        {
            _logger.Warn($"Alert sound could not be played: {exception.Message}");
        }
    }

    internal async Task ShowLiftCallAsync()
    {
        if (_serverHost is null)
        {
            _logger.Info("Pokaż LiftCall ignored because configuration is required.");
            return;
        }

        _logger.Info("Pokaż LiftCall requested from tray.");
        var success = await _serverHost.ShowLiftCallAsync();
        _logger.Info($"Pokaż LiftCall result: success={success}.");
        if (!success)
        {
            _logger.Warn("Tray Pokaż LiftCall failed: window activation was not verified.");
        }
    }

    internal async Task ExitAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _expectedPwaClose = true;
        _statusTimer.Stop();
        _alertState.Observe(new TrayAlertObservation(false, true, false, false, false, "closed", null, false), DateTimeOffset.UtcNow);
        CloseAlertDialog();
        _alertSound?.Stop();
        if (_serverHost is not null)
        {
            await _serverHost.StopAsync();
        }
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _currentIcon?.Dispose();
            _alertSound?.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed record TrayAlertContent(string Title, string Message)
{
    public static TrayAlertContent For(TrayAlertKind alert) => alert switch
    {
        TrayAlertKind.PwaClosed => new(
            "LiftCall jest zamknięte",
            "LiftCall nie może odbierać powiadomień o połączeniach."),
        TrayAlertKind.LoggedOut => new(
            "LiftCall: użytkownik wylogowany",
            "Zaloguj się ponownie, aby odbierać powiadomienia o połączeniach."),
        TrayAlertKind.HeartbeatInactive => new(
            "LiftCall nie odpowiada",
            "Heartbeat LiftCall jest nieaktywny. Sprawdź, czy PWA działa."),
        TrayAlertKind.HelperError => new(
            "Błąd LiftCall Helper",
            "Helper ma problem lokalny i nie może monitorować PWA."),
        _ => throw new ArgumentOutOfRangeException(nameof(alert), alert, null),
    };
}

internal static class TrayIconFactory
{
    public static Icon Create(TrayStatus status) => IconRenderer.CreateTrayIcon(status);
}
