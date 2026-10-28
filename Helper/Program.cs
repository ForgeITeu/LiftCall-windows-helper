using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiftCall.WindowsHelper;

internal static class Program
{
    private const string ServiceName = "liftcall-windows-helper";
    private const string Version = "1.0.0";
    private const string DefaultConfigName = "config.json";
    private const string MutexName = "Local\\LiftCall.WindowsHelper";

    [STAThread]
    private static int Main(string[] args)
    {
        DebugConsole.Initialize(CommandLine.HasFlag(args, "--debug"));
        var logger = new FileLogger(Path.Combine(AppContext.BaseDirectory, "helper.log"));
        logger.Info($"Starting {ServiceName} {Version}.");

        try
        {
            using var instanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                logger.Info("Another helper instance is already running; exiting.");
                return 0;
            }

            var configPath = ResolveConfigPath(CommandLine.GetOption(args, "--config"));
            logger.Info($"Loading config.json from: {configPath}");
            var configResult = HelperConfig.Load(configPath);
            var config = configResult.Configuration;
            switch (configResult.State)
            {
                case ConfigLoadState.Missing:
                    logger.Info("Config file not found; opening configuration window.");
                    break;
                case ConfigLoadState.Invalid:
                    logger.Info($"Config file is invalid; opening configuration window. {configResult.ErrorMessage}");
                    break;
                case ConfigLoadState.Valid:
                    logger.Info($"Configured CORS origins: {string.Join(", ", config.AllowedOrigins)}");
                    break;
            }

            logger.Info($"Starting tray application; config={configPath}");
            ApplicationConfiguration.Initialize();
            using var tray = new TrayApplicationContext(config, configPath, logger, configResult);
            tray.ShowConfigurationOnStart();
            Application.Run(tray);
            return 0;
        }
        catch (Exception exception)
        {
            logger.Error("Helper stopped because of an error.", exception);
            return 1;
        }
        finally
        {
            logger.Dispose();
        }
    }

    private static string ResolveConfigPath(string? configArgument)
    {
        if (!string.IsNullOrWhiteSpace(configArgument))
        {
            return Path.IsPathRooted(configArgument)
                ? configArgument
                : Path.GetFullPath(configArgument, AppContext.BaseDirectory);
        }

        var adjacentPath = Path.Combine(AppContext.BaseDirectory, DefaultConfigName);
        if (File.Exists(adjacentPath))
        {
            return adjacentPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? adjacentPath
            : Path.Combine(localAppData, "LiftCall", "Helper", DefaultConfigName);
    }
}

internal sealed class FileLogger : IDisposable
{
    private readonly object _sync = new();
    private StreamWriter? _writer;

    public FileLogger(string path)
    {
        try
        {
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }
        catch
        {
            // The helper remains functional even if the containing directory is read-only.
        }
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        if (exception is not null)
        {
            line += $" {exception.GetType().Name}: {exception.Message}";
        }

        lock (_sync)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // Logging must never take down the helper.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

internal static class CommandLine
{
    public static bool HasFlag(IReadOnlyList<string> args, string name)
    {
        return args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    }

    public static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

internal static class DebugConsole
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private static readonly object Sync = new();

    public static bool Enabled { get; private set; }

    public static void Initialize(bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        try
        {
            AllocConsole();
            Console.OutputEncoding = Encoding.UTF8;
            Enabled = true;
            WriteLine("[DEBUG] LiftCall Windows Helper diagnostic mode enabled.");
        }
        catch
        {
            Enabled = false;
        }
    }

    public static void WriteHeartbeat(
        DateTimeOffset timestamp,
        string? origin,
        string authenticatedHeader,
        bool? authenticatedParsed,
        string pageStatus,
        bool windowFound,
        bool pwaProcessRunning,
        string calculatedStatus)
    {
        WriteBlock("[HEARTBEAT]", new[]
        {
            $"timestamp={timestamp:O}",
            "method=POST",
            "path=/heartbeat",
            $"origin={origin ?? "missing"}",
            $"authenticated_header={authenticatedHeader}",
            $"authenticated_parsed={FormatBool(authenticatedParsed)}",
            $"page_status={pageStatus}",
            $"window_found={windowFound.ToString().ToLowerInvariant()}",
            $"pwa_process_running={pwaProcessRunning.ToString().ToLowerInvariant()}",
            $"calculated_status={calculatedStatus}",
        });
    }

    public static void WriteHealth(
        bool pageHeartbeatSeen,
        double? pageHeartbeatAgeSeconds,
        string pageStatus,
        bool? authenticated,
        bool windowFound,
        bool pwaProcessRunning,
        bool helperListening,
        string calculatedStatus)
    {
        WriteBlock("[HEALTH]", new[]
        {
            $"page_heartbeat_seen={pageHeartbeatSeen.ToString().ToLowerInvariant()}",
            $"page_heartbeat_age_seconds={(pageHeartbeatAgeSeconds?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "null")}",
            $"page_status={pageStatus}",
            $"authenticated={FormatBool(authenticated)}",
            $"window_found={windowFound.ToString().ToLowerInvariant()}",
            $"pwa_process_running={pwaProcessRunning.ToString().ToLowerInvariant()}",
            $"helper_listening={helperListening.ToString().ToLowerInvariant()}",
            $"calculated_status={calculatedStatus}",
        });
    }

    public static void WriteStateChange(string previous, string current, string reason)
    {
        WriteBlock("[STATE CHANGE]", new[]
        {
            $"previous={previous}",
            $"current={current}",
            $"reason={reason}",
        });
    }

    private static string FormatBool(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "null",
    };

    private static void WriteBlock(string heading, IEnumerable<string> lines)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Sync)
        {
            Console.WriteLine(heading);
            foreach (var line in lines)
            {
                Console.WriteLine(line);
            }

            Console.WriteLine();
        }
    }

    private static void WriteLine(string line)
    {
        if (Enabled)
        {
            lock (Sync)
            {
                Console.WriteLine(line);
            }
        }
    }
}

internal sealed class PageWatchdog
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActionCooldown = TimeSpan.FromMinutes(2);

    private readonly HelperConfig _config;
    private readonly FileLogger _logger;
    private readonly object _sync = new();
    private DateTimeOffset? _lastHeartbeat;
    private DateTimeOffset? _lastAction;
    private bool _failureActive;
    private bool _closed;
    private bool? _authenticated;
    private string? _pageSession;
    private string? _closedPageSession;

    public PageWatchdog(HelperConfig config, FileLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public void RecordHeartbeat(bool? authenticated, string? pageSession)
    {
        var recovered = false;
        var authenticationChanged = false;
        var ignoredAfterStop = false;
        pageSession = NormalisePageSession(pageSession);
        lock (_sync)
        {
            if (_closed
                && !string.IsNullOrWhiteSpace(_closedPageSession)
                && string.Equals(_closedPageSession, pageSession, StringComparison.Ordinal))
            {
                ignoredAfterStop = true;
            }
            else
            {
                _lastHeartbeat = DateTimeOffset.UtcNow;
                _lastAction = null;
                recovered = _failureActive;
                _failureActive = false;
                _closed = false;
                _closedPageSession = null;
                _pageSession = pageSession;
                authenticationChanged = _authenticated != authenticated;
                _authenticated = authenticated;
            }
        }

        if (ignoredAfterStop)
        {
            _logger.Info($"Ignored late heartbeat from closed PWA page session; authenticated={authenticated}.");
            return;
        }

        if (recovered)
        {
            _logger.Info("PWA heartbeat recovered; watchdog failure state reset.");
        }

        if (authenticationChanged)
        {
            _logger.Info($"PWA user {(authenticated == true ? "authenticated" : authenticated == false ? "logged out" : "authentication unknown")}.");
        }

        _logger.Info($"PWA heartbeat received; authenticated={authenticated}.");
    }

    public void RecordStop()
    {
        bool? authenticated;
        lock (_sync)
        {
            _lastAction = null;
            _failureActive = false;
            _closed = true;
            _closedPageSession = _pageSession;
            authenticated = _authenticated;
        }

        _logger.Info(
            $"PWA heartbeat stopped by page shutdown; last_authenticated={authenticated?.ToString() ?? "null"}; "
            + $"logout_heartbeat_before_stop={(authenticated == false ? "true" : "false")}.");
    }

    private static string? NormalisePageSession(string? pageSession)
    {
        if (string.IsNullOrWhiteSpace(pageSession))
        {
            return null;
        }

        var value = pageSession.Trim();
        return value.Length <= 128 ? value : null;
    }

    public (bool Seen, double? AgeSeconds, string Status, bool? Authenticated) GetStatus()
    {
        lock (_sync)
        {
            if (_lastHeartbeat is null)
            {
                return (false, null, _closed ? "closed" : "never_seen", null);
            }

            var age = Math.Max(0, (DateTimeOffset.UtcNow - _lastHeartbeat.Value).TotalSeconds);
            if (_closed)
            {
                // heartbeat/stop describes page lifecycle only. Preserve the
                // last explicit authentication state, especially false after
                // logout, so closed does not masquerade as logged out/unknown.
                return (true, age, "closed", _authenticated);
            }

            return age >= StaleAfter.TotalSeconds
                ? (true, age, "stale", _authenticated)
                : (true, age, "healthy", _authenticated);
        }
    }

    public DateTimeOffset? LastHeartbeat
    {
        get
        {
            lock (_sync)
            {
                return _lastHeartbeat;
            }
        }
    }

    public bool? Authenticated
    {
        get
        {
            lock (_sync)
            {
                return _authenticated;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(WatchdogInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    CheckOnce();
                }
                catch (Exception exception)
                {
                    _logger.Error("Watchdog check failed.", exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CheckOnce()
    {
        WindowInfo? window;
        double ageSeconds;

        lock (_sync)
        {
            if (_lastHeartbeat is null || _closed)
            {
                return;
            }

            ageSeconds = Math.Max(0, (DateTimeOffset.UtcNow - _lastHeartbeat.Value).TotalSeconds);
            if (ageSeconds < StaleAfter.TotalSeconds)
            {
                return;
            }

            if (_lastAction is not null && DateTimeOffset.UtcNow - _lastAction.Value < ActionCooldown)
            {
                return;
            }

            _lastAction = DateTimeOffset.UtcNow;
            _failureActive = true;
            _logger.Warn($"PWA heartbeat stale ({ageSeconds:F0}s); checking LiftCall window.");
            window = WindowManager.Find(_config);
            if (window is null)
            {
                _logger.Warn("Watchdog could not find the configured LiftCall window.");
                return;
            }

            var activated = WindowManager.Activate(window, _config.Maximize, _logger);
            _logger.Warn(activated
                ? "Watchdog activated the LiftCall window."
                : "Watchdog found the LiftCall window but activation failed.");
        }
    }
}

internal sealed class LocalHttpServer
{
    private const int MaxBodyBytes = 64 * 1024;
    private readonly HelperConfig _config;
    private readonly FileLogger _logger;
    private readonly TcpListener _listener;
    private readonly PageWatchdog _watchdog;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly ConcurrentDictionary<int, TcpClient> _connections = new();
    private int _connectionId;
    private Task? _acceptTask;
    private Task? _watchdogTask;
    private bool _pwaLaunchedByHelper;
    private int _pwaLaunchError;

    public LocalHttpServer(HelperConfig config, FileLogger logger)
    {
        _config = config;
        _logger = logger;
        _listener = new TcpListener(IPAddress.Loopback, config.Port);
        _watchdog = new PageWatchdog(config, logger);
    }

    public int Port => _config.Port;
    public bool IsListening { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastHeartbeat => _watchdog.LastHeartbeat;
    public bool PwaLaunchError => Volatile.Read(ref _pwaLaunchError) == 1;

    public (bool Seen, double? AgeSeconds, string Status, bool? Authenticated) GetPageStatus() => _watchdog.GetStatus();

    private TrayStatusDecision CalculateTrayStatus(
        WindowInfo? window,
        bool processRunning,
        (bool Seen, double? AgeSeconds, string Status, bool? Authenticated) page)
    {
        var heartbeatFresh = page.Seen
            && !page.Status.Equals("closed", StringComparison.OrdinalIgnoreCase)
            && page.AgeSeconds is >= 0 and < 60;
        return TrayStatusCalculator.Calculate(new TrayStatusInput(
            IsListening,
            processRunning,
            window is not null,
            _watchdog.LastHeartbeat,
            page.AgeSeconds,
            heartbeatFresh,
            page.Authenticated,
            LastError is not null || (PwaLaunchError && window is null) || !IsListening,
            page.Status));
    }

    private void DebugHeartbeat(HttpRequest request)
    {
        if (!DebugConsole.Enabled)
        {
            return;
        }

        var page = _watchdog.GetStatus();
        var window = WindowManager.Find(_config);
        var processRunning = WindowManager.IsProcessRunning(_config, window);
        var decision = CalculateTrayStatus(window, processRunning, page);
        DebugConsole.WriteHeartbeat(
            DateTimeOffset.Now,
            request.Origin,
            request.AuthenticatedHeaderState,
            request.Authenticated,
            page.Status,
            window is not null,
            processRunning,
            decision.Label);
    }

    public bool ShowLiftCall()
    {
        var window = WindowManager.Find(_config);
        if (window is null)
        {
            if (!TryLaunchPwa("Pokaż LiftCall"))
            {
                _logger.Warn("Pokaż LiftCall failed: configured LiftCall window was not found after launch attempt.");
                return false;
            }

            window = WindowManager.Find(_config);
            if (window is null)
            {
                _logger.Warn("Pokaż LiftCall failed: configured LiftCall window was not found after launch attempt.");
                return false;
            }
        }

        _logger.Info($"PWA window selected: HWND=0x{window.Handle.ToInt64():X}, title=\"{window.Title}\", class=\"{window.ClassName}\", process=\"{window.ProcessName}\".");
        var activated = WindowManager.Activate(window, _config.Maximize, _logger);
        if (activated)
        {
            Volatile.Write(ref _pwaLaunchError, 0);
        }

        return activated;
    }

    public void LaunchPwaOnStart()
    {
        if (_config.LaunchPwaOnStart)
        {
            TryLaunchPwa("Helper start");
        }
    }

    public bool TryLaunchPwa(string reason)
    {
        var existingWindow = WindowManager.Find(_config);
        if (existingWindow is not null)
        {
            _logger.Info($"{reason}: PWA window already exists; launch skipped.");
            Volatile.Write(ref _pwaLaunchError, 0);
            return true;
        }

        if (WindowManager.IsProcessRunning(_config, null))
        {
            _logger.Info($"{reason}: PWA process already exists; launch skipped.");
        }
        else if (!PwaLauncher.TryLaunch(_config, _logger, reason))
        {
            Volatile.Write(ref _pwaLaunchError, 1);
            return false;
        }
        else
        {
            _pwaLaunchedByHelper = true;
        }

        var window = WindowManager.WaitFor(_config, TimeSpan.FromSeconds(15), _logger);
        if (window is null)
        {
            Volatile.Write(ref _pwaLaunchError, 1);
            _logger.Error($"{reason}: PWA process was started or already existed, but the configured LiftCall window did not appear.");
            return false;
        }

        Volatile.Write(ref _pwaLaunchError, 0);
        _logger.Info($"{reason}: PWA ready: HWND=0x{window.Handle.ToInt64():X}, title=\"{window.Title}\", class=\"{window.ClassName}\", process=\"{window.ProcessName}\".");
        return true;
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            IsListening = true;
            LastError = null;
            _acceptTask = AcceptLoopAsync(_stopSource.Token);
            _watchdogTask = _watchdog.RunAsync(_stopSource.Token);
            _logger.Info($"Listening on http://127.0.0.1:{_config.Port}/");
        }
        catch (Exception exception)
        {
            IsListening = false;
            LastError = exception.Message;
            _logger.Error("HTTP server failed to start.", exception);
        }
    }

    public async Task StopAsync(bool closePwa)
    {
        _stopSource.Cancel();
        _listener.Stop();
        foreach (var client in _connections.Values)
        {
            client.Dispose();
        }

        if (_acceptTask is not null)
        {
            await _acceptTask.ConfigureAwait(false);
        }

        if (_watchdogTask is not null)
        {
            await _watchdogTask.ConfigureAwait(false);
        }

        if (closePwa && _config.ClosePwaOnExit && _pwaLaunchedByHelper)
        {
            var window = WindowManager.Find(_config);
            if (window is null)
            {
                _logger.Info("close_pwa_on_exit enabled, but the configured LiftCall window was not found.");
            }
            else
            {
                WindowManager.Close(window, _logger);
            }
        }

        IsListening = false;
        _logger.Info("HTTP server stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                var id = Interlocked.Increment(ref _connectionId);
                _connections[id] = client;
                _ = HandleConnectionSafeAsync(id, client);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            IsListening = false;
            LastError = exception.Message;
            _logger.Error("HTTP server stopped unexpectedly.", exception);
        }
        finally
        {
            IsListening = false;
        }
    }

    private async Task HandleConnectionSafeAsync(int id, TcpClient client)
    {
        try
        {
            await HandleConnectionAsync(client).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            _logger.Info($"HTTP connection closed: {exception.GetType().Name}.");
        }
        catch (Exception exception)
        {
            _logger.Error("HTTP request failed.", exception);
        }
        finally
        {
            _connections.TryRemove(id, out _);
            client.Dispose();
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        await using var stream = client.GetStream();
        var request = await HttpRequest.ReadAsync(stream).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        _logger.Info($"HTTP {request.Method} {request.Path}");
        var allowedOrigin = _config.GetAllowedOrigin(request.Origin);

        if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase)
            && (request.Path is "/health" or "/show" or "/heartbeat" or "/heartbeat/stop"))
        {
            await HttpResponse.WriteOptionsAsync(stream, allowedOrigin).ConfigureAwait(false);
            return;
        }

        if (request.ContentLength < 0)
        {
            await HttpResponse.WriteJsonAsync(stream, 400, new { error = "invalid_content_length" }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            await HttpResponse.WriteJsonAsync(stream, 413, new { error = "body_too_large" }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        var isHeartbeat = request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && request.Path == "/heartbeat";
        var isHeartbeatStop = request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && request.Path == "/heartbeat/stop";
        if ((isHeartbeat || isHeartbeatStop) && request.ContentLength != 0)
        {
            await HttpResponse.WriteJsonAsync(stream, 400, new { error = "body_not_allowed" }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        if (isHeartbeat)
        {
            _watchdog.RecordHeartbeat(request.Authenticated, request.PageSession);
            Volatile.Write(ref _pwaLaunchError, 0);
            DebugHeartbeat(request);
            await HttpResponse.WriteJsonAsync(stream, 200, new { ok = true, ttl = 60 }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        if (isHeartbeatStop)
        {
            _watchdog.RecordStop();
            await HttpResponse.WriteJsonAsync(stream, 200, new { ok = true, status = "closed" }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        // The body is only drained to keep the TCP protocol well-formed. It is never parsed,
        // logged, stored, or used for window selection. This endpoint accepts no business data.
        await request.DrainBodyAsync(stream).ConfigureAwait(false);

        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) && request.Path == "/health")
        {
            var window = WindowManager.Find(_config);
            var healthOk = window is not null;
            var processRunning = WindowManager.IsProcessRunning(_config, window);
            var page = _watchdog.GetStatus();
            var decision = CalculateTrayStatus(window, processRunning, page);
            DebugConsole.WriteHealth(
                page.Seen,
                page.AgeSeconds,
                page.Status,
                page.Authenticated,
                window is not null,
                processRunning,
                IsListening,
                decision.Label);
            await HttpResponse.WriteJsonAsync(stream, healthOk ? 200 : 503,
                new
                {
                    service = "liftcall-windows-helper",
                    version = "1.0.0",
                    platform = "windows",
                    status = healthOk ? "ok" : "error",
                    page_heartbeat_seen = page.Seen,
                    page_heartbeat_age_seconds = page.AgeSeconds,
                    page_status = page.Status,
                    authenticated = page.Authenticated,
                    pwa_process_running = processRunning,
                    window_found = window is not null,
                }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        if (request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) && request.Path == "/show")
        {
            var activated = ShowLiftCall();
            await HttpResponse.WriteJsonAsync(stream, activated ? 200 : 503,
                new { status = activated ? "ok" : "error" }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await HttpResponse.WriteJsonAsync(stream, 404, new { error = "not_found" }, allowedOrigin).ConfigureAwait(false);
            return;
        }

        await HttpResponse.WriteJsonAsync(stream, 405, new { error = "method_not_allowed" }, allowedOrigin).ConfigureAwait(false);
    }
}

internal sealed class HttpRequest
{
    private HttpRequest(string method, string path, int contentLength)
    {
        Method = method;
        Path = path;
        ContentLength = contentLength;
    }

    public string Method { get; }
    public string Path { get; }
    public int ContentLength { get; }
    public string? Origin { get; private set; }
    public bool? Authenticated { get; private set; }
    public string AuthenticatedHeaderState { get; private set; } = "missing";
    public string? PageSession { get; private set; }

    public static async Task<HttpRequest?> ReadAsync(NetworkStream stream)
    {
        using var headerBuffer = new MemoryStream();
        var oneByte = new byte[1];
        var lastFour = new byte[4];
        var lastCount = 0;

        while (headerBuffer.Length < 16 * 1024)
        {
            var read = await stream.ReadAsync(oneByte.AsMemory(0, 1)).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            headerBuffer.WriteByte(oneByte[0]);
            if (lastCount < 4)
            {
                lastFour[lastCount++] = oneByte[0];
            }
            else
            {
                lastFour[0] = lastFour[1];
                lastFour[1] = lastFour[2];
                lastFour[2] = lastFour[3];
                lastFour[3] = oneByte[0];
            }

            if (lastCount == 4 && lastFour[0] == '\r' && lastFour[1] == '\n' && lastFour[2] == '\r' && lastFour[3] == '\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBuffer.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines.Length == 0 ? Array.Empty<string>() : lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
        {
            return null;
        }

        var target = requestLine[1];
        var path = target.Split('?', 2)[0];
        var contentLength = 0;
        string? origin = null;
        bool? requestAuthenticated = null;
        string? pageSession = null;
        var authenticatedHeaderState = "missing";
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (line[..separator].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && !int.TryParse(line[(separator + 1)..].Trim(), out contentLength))
            {
                contentLength = -1;
            }

            if (line[..separator].Equals("Origin", StringComparison.OrdinalIgnoreCase))
            {
                origin = line[(separator + 1)..].Trim();
            }

            if (line[..separator].Equals("X-LiftCall-Authenticated", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(line[(separator + 1)..].Trim(), out var authenticated))
            {
                requestAuthenticated = authenticated;
                authenticatedHeaderState = authenticated ? "true" : "false";
            }
            else if (line[..separator].Equals("X-LiftCall-Authenticated", StringComparison.OrdinalIgnoreCase))
            {
                authenticatedHeaderState = "missing";
            }

            if (line[..separator].Equals("X-LiftCall-Page-Session", StringComparison.OrdinalIgnoreCase))
            {
                pageSession = line[(separator + 1)..].Trim();
            }
        }

        return new HttpRequest(requestLine[0], path, contentLength)
        {
            Origin = origin,
            Authenticated = requestAuthenticated,
            AuthenticatedHeaderState = authenticatedHeaderState,
            PageSession = pageSession,
        };
    }

    public async Task DrainBodyAsync(NetworkStream stream)
    {
        if (ContentLength <= 0)
        {
            return;
        }

        var remaining = ContentLength;
        var buffer = new byte[4096];
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining))).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            remaining -= read;
        }
    }
}

internal static class HttpResponse
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteJsonAsync(NetworkStream stream, int statusCode, object payload, string? allowedOrigin)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var reason = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            503 => "Service Unavailable",
            500 => "Internal Server Error",
            _ => "Error"
        };
        var header = $"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\n{CorsHeaders(allowedOrigin)}Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
    }

    public static async Task WriteOptionsAsync(NetworkStream stream, string? allowedOrigin)
    {
        var header = $"HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n{CorsHeaders(allowedOrigin, preflight: true)}Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
    }

    private static string CorsHeaders(string? allowedOrigin, bool preflight = false)
    {
        var builder = new StringBuilder("Vary: Origin\r\n");
        if (!string.IsNullOrEmpty(allowedOrigin))
        {
            builder.Append($"Access-Control-Allow-Origin: {allowedOrigin}\r\n");
            if (preflight)
            {
                builder.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
                builder.Append("Access-Control-Allow-Headers: Content-Type, X-LiftCall-Authenticated, X-LiftCall-Page-Session\r\n");
            }
        }

        return builder.ToString();
    }
}

internal sealed record WindowInfo(IntPtr Handle, string Title, string ClassName, string ProcessName);

internal static class PwaLauncher
{
    public static bool TryLaunch(HelperConfig config, FileLogger logger, string reason)
    {
        if (string.IsNullOrWhiteSpace(config.PwaShortcutPath))
        {
            logger.Info($"{reason}: pwa_shortcut_path is empty; automatic PWA launch skipped.");
            return false;
        }

        var shortcutPath = config.PwaShortcutPath.Trim();
        if (!Path.IsPathRooted(shortcutPath))
        {
            shortcutPath = Path.GetFullPath(shortcutPath, AppContext.BaseDirectory);
        }

        if (!File.Exists(shortcutPath))
        {
            logger.Warn($"{reason}: PWA shortcut was not found: {shortcutPath}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = shortcutPath,
                UseShellExecute = true,
            });
            logger.Info($"{reason}: launched PWA shortcut {shortcutPath}.");
            return true;
        }
        catch (Exception exception)
        {
            logger.Error($"{reason}: failed to launch PWA shortcut {shortcutPath}.", exception);
            return false;
        }
    }
}

internal static class WindowManager
{
    private const uint WmClose = 0x0010;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    public static WindowInfo? Find(HelperConfig config)
    {
        var titleSelector = Normalise(config.WindowTitleContains);
        var classSelector = Normalise(config.WindowClass);
        var processSelector = Normalise(config.ProcessName);
        WindowInfo? result = null;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var info = ReadWindow(hwnd);
            var checks = new List<bool>(3);
            if (titleSelector.Length > 0)
            {
                checks.Add(Normalise(info.Title).Contains(titleSelector, StringComparison.Ordinal));
            }

            if (classSelector.Length > 0)
            {
                checks.Add(Normalise(info.ClassName).Equals(classSelector, StringComparison.Ordinal));
            }

            if (processSelector.Length > 0)
            {
                checks.Add(Normalise(info.ProcessName).Equals(processSelector, StringComparison.Ordinal));
            }

            if (checks.Count > 0 && checks.All(value => value))
            {
                result ??= info;
                if (titleSelector.Length > 0
                    && Normalise(info.Title).Equals(titleSelector, StringComparison.Ordinal))
                {
                    result = info;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static bool IsProcessRunning(HelperConfig config, WindowInfo? window)
    {
        var configuredName = config.ProcessName?.Trim();
        var processName = string.IsNullOrWhiteSpace(configuredName)
            ? window?.ProcessName
            : configuredName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return window is not null;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(processName);
        try
        {
            return Process.GetProcessesByName(nameWithoutExtension).Any(process =>
            {
                try
                {
                    return !process.HasExited;
                }
                finally
                {
                    process.Dispose();
                }
            });
        }
        catch
        {
            return false;
        }
    }

    public static WindowInfo? WaitFor(HelperConfig config, TimeSpan timeout, FileLogger logger)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var window = Find(config);
            if (window is not null)
            {
                logger.Info($"PWA window found: HWND=0x{window.Handle.ToInt64():X}, title=\"{window.Title}\", class=\"{window.ClassName}\", process=\"{window.ProcessName}\".");
                return window;
            }

            Thread.Sleep(250);
        }

        return Find(config);
    }

    public static bool Close(WindowInfo window, FileLogger logger)
    {
        var requested = IsWindow(window.Handle) && PostMessage(window.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        var closed = false;
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            Thread.Sleep(250);
            closed = !IsWindow(window.Handle) || !IsWindowVisible(window.Handle);
            if (closed)
            {
                break;
            }
        }

        logger.Info(
            $"Close PWA window: HWND=0x{window.Handle.ToInt64():X}, title=\"{window.Title}\", " +
            $"class=\"{window.ClassName}\", process=\"{window.ProcessName}\", " +
            $"post_message={requested}, closed={closed}.");
        return requested && closed;
    }

    public static bool Activate(WindowInfo window, bool maximize, FileLogger logger)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var hwnd = window.Handle;
            var restored = false;
            var shown = false;
            var broughtToTop = false;
            var foregroundRequested = false;
            var topmostRaised = false;
            var topmostCleared = false;
            var attached = new List<(uint Source, uint Target)>();

            try
            {
                if (!IsWindow(hwnd))
                {
                    logger.Warn($"Activation attempt {attempt}/{maxAttempts}: HWND=0x{hwnd.ToInt64():X} is no longer a window.");
                    return false;
                }

                if (IsIconic(hwnd))
                {
                    restored = ShowWindow(hwnd, SwRestore);
                }

                shown = ShowWindow(hwnd, SwShow);
                if (maximize)
                {
                    ShowWindow(hwnd, SwMaximize);
                }

                var currentThread = GetCurrentThreadId();
                var foreground = GetForegroundWindow();
                var foregroundThread = foreground == IntPtr.Zero
                    ? 0u
                    : GetWindowThreadProcessId(foreground, out _);
                var targetThread = GetWindowThreadProcessId(hwnd, out _);
                AttachIfNeeded(currentThread, targetThread, attached);
                AttachIfNeeded(foregroundThread, currentThread, attached);

                broughtToTop = BringWindowToTop(hwnd);
                topmostRaised = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
                topmostCleared = SetWindowPos(hwnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
                foregroundRequested = SetForegroundWindow(hwnd);
                SetFocus(hwnd);
                BringWindowToTop(hwnd);
                foregroundRequested = SetForegroundWindow(hwnd) || foregroundRequested;
            }
            finally
            {
                for (var index = attached.Count - 1; index >= 0; index--)
                {
                    AttachThreadInput(attached[index].Source, attached[index].Target, false);
                }
            }

            Thread.Sleep(120);
            var visible = IsWindowVisible(hwnd);
            var iconic = IsIconic(hwnd);
            var foregroundWindow = GetForegroundWindow();
            var isForeground = foregroundWindow == hwnd;
            var success = visible && !iconic && isForeground;
            logger.Info(
                $"Activation attempt {attempt}/{maxAttempts}: HWND=0x{hwnd.ToInt64():X}, " +
                $"title=\"{window.Title}\", class=\"{window.ClassName}\", process=\"{window.ProcessName}\", " +
                $"restored={restored}, shown={shown}, brought_to_top={broughtToTop}, " +
                $"topmost_raise={topmostRaised}, topmost_clear={topmostCleared}, " +
                $"foreground_requested={foregroundRequested}, visible={visible}, iconic={iconic}, " +
                $"foreground=0x{foregroundWindow.ToInt64():X}, verified={success}.");

            if (success)
            {
                return true;
            }

            if (attempt < maxAttempts)
            {
                Thread.Sleep(120);
            }
        }

        logger.Warn($"Activation failed after {maxAttempts} attempts: HWND=0x{window.Handle.ToInt64():X}, title=\"{window.Title}\", class=\"{window.ClassName}\", process=\"{window.ProcessName}\".");
        return false;
    }

    private static void AttachIfNeeded(uint source, uint target, ICollection<(uint Source, uint Target)> attached)
    {
        if (source != 0 && source != target && AttachThreadInput(source, target, true))
        {
            attached.Add((source, target));
        }
    }

    private static WindowInfo ReadWindow(IntPtr hwnd)
    {
        var titleLength = GetWindowTextLength(hwnd);
        var titleBuffer = new StringBuilder(Math.Max(titleLength + 1, 1));
        GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
        var classBuffer = new StringBuilder(256);
        GetClassName(hwnd, classBuffer, classBuffer.Capacity);
        GetWindowThreadProcessId(hwnd, out var processId);
        return new WindowInfo(hwnd, titleBuffer.ToString(), classBuffer.ToString(), GetProcessName(processId));
    }

    private static string GetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return "";
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var length = (uint)buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref length)
                ? Path.GetFileName(buffer.ToString())
                : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string Normalise(string value) => (value ?? "").Trim().ToUpperInvariant();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr processHandle, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
