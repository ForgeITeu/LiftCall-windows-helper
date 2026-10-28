using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiftCall.WindowsHelper;

internal enum ConfigLoadState
{
    Missing,
    Invalid,
    Valid,
}

internal sealed record ConfigLoadResult(
    ConfigLoadState State,
    HelperConfig Configuration,
    string? ErrorMessage)
{
    public bool IsReady => State == ConfigLoadState.Valid;
}

internal sealed class HelperConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 32145;

    [JsonPropertyName("window_title_contains")]
    public string WindowTitleContains { get; set; } = "LiftCall";

    [JsonPropertyName("window_class")]
    public string WindowClass { get; set; } = "";

    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("maximize")]
    public bool Maximize { get; set; } = true;

    [JsonPropertyName("launch_pwa_on_start")]
    public bool LaunchPwaOnStart { get; set; } = true;

    [JsonPropertyName("close_pwa_on_exit")]
    public bool ClosePwaOnExit { get; set; } = true;

    [JsonPropertyName("pwa_shortcut_path")]
    public string PwaShortcutPath { get; set; } = "";

    [JsonPropertyName("allowed_origins")]
    public List<string> AllowedOrigins { get; set; } = new()
    {
        "https://liftcall.forge-it.eu",
    };

    public string? GetAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin.Contains('\r') || origin.Contains('\n'))
        {
            return null;
        }

        var candidate = origin.Trim();
        return AllowedOrigins.Any(allowed => string.Equals(allowed?.Trim(), candidate, StringComparison.Ordinal))
            ? candidate
            : null;
    }

    public static ConfigLoadResult Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ConfigLoadResult(ConfigLoadState.Missing, new HelperConfig(), null);
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var config = JsonSerializer.Deserialize<HelperConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (config is null)
            {
                return Invalid("config.json musi zawierać obiekt konfiguracji.");
            }

            config.AllowedOrigins ??= new List<string>();
            config.Validate();
            // Keep the legacy JSON property readable, but always bind HTTP to loopback.
            config.Host = "127.0.0.1";
            return new ConfigLoadResult(ConfigLoadState.Valid, config, null);
        }
        catch (Exception exception)
        {
            return Invalid(exception.Message);
        }
    }

    private static ConfigLoadResult Invalid(string message)
    {
        return new ConfigLoadResult(ConfigLoadState.Invalid, new HelperConfig(), message);
    }

    public void ApplyFrom(HelperConfig source)
    {
        Host = "127.0.0.1";
        Port = source.Port;
        WindowTitleContains = source.WindowTitleContains;
        WindowClass = source.WindowClass;
        ProcessName = source.ProcessName;
        Maximize = source.Maximize;
        LaunchPwaOnStart = source.LaunchPwaOnStart;
        ClosePwaOnExit = source.ClosePwaOnExit;
        PwaShortcutPath = source.PwaShortcutPath;
        AllowedOrigins = source.AllowedOrigins.ToList();
    }

    public void Save(string path)
    {
        Host = "127.0.0.1";
        Validate();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(temporaryPath, json + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    public void Validate()
    {
        if (Host is not ("127.0.0.1" or "localhost"))
        {
            throw new InvalidOperationException("host musi wskazywać localhost (127.0.0.1).");
        }

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("port musi być liczbą od 1 do 65535.");
        }

        if (string.IsNullOrWhiteSpace(WindowTitleContains)
            && string.IsNullOrWhiteSpace(WindowClass)
            && string.IsNullOrWhiteSpace(ProcessName))
        {
            throw new InvalidOperationException("W config.json ustaw co najmniej jeden selektor okna.");
        }

        if (AllowedOrigins.Any(origin => string.IsNullOrWhiteSpace(origin) || origin.Contains('\r') || origin.Contains('\n')))
        {
            throw new InvalidOperationException("allowed_origins zawiera pusty lub niepoprawny wpis.");
        }
    }
}
