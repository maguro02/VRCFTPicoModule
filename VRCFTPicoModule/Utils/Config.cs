using System.Globalization;
using Microsoft.Extensions.Logging;

namespace VRCFTPicoModule.Utils;

public class Config
{
    public bool DisableEye { get; private set; }
    public bool DisableExpression { get; private set; }
    public float EyeGainX { get; private set; } = 1.0f;
    public float EyeGainY { get; private set; } = 1.0f;
    public bool LogRaw { get; private set; }
    public string LogFile { get; private set; } = "PicoRawLog.csv";
    public int LogIntervalMs { get; private set; } = 50;
    public bool LogIncludeVisemes { get; private set; }
    public string ModuleDirectory { get; private set; } = "";

    public static Config Load(string moduleDirectory, ILogger logger)
    {
        var config = new Config { ModuleDirectory = moduleDirectory };

        if (File.Exists(Path.Combine(moduleDirectory, ".disable_eye")))
        {
            config.DisableEye = true;
            logger.LogInformation("Legacy .disable_eye file detected; eye tracking disabled.");
        }
        if (File.Exists(Path.Combine(moduleDirectory, ".disable_expression")))
        {
            config.DisableExpression = true;
            logger.LogInformation("Legacy .disable_expression file detected; expression tracking disabled.");
        }

        var iniPath = Path.Combine(moduleDirectory, "config.ini");
        if (!File.Exists(iniPath))
        {
            logger.LogInformation("config.ini not found; writing default template to {IniPath}", iniPath);
            try
            {
                File.WriteAllText(iniPath, DefaultIniTemplate());
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to write default config.ini: {Message}", ex.Message);
            }
            return config;
        }

        try
        {
            foreach (var raw in File.ReadAllLines(iniPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
                var separator = line.IndexOf(':');
                if (separator < 0) continue;
                var key = line[..separator].Trim().ToLowerInvariant();
                var value = line[(separator + 1)..].Trim();
                config.ApplyKey(key, value);
            }

            logger.LogInformation(
                "config.ini loaded: eye={Eye} expression={Expression} eye_gain=({Gx},{Gy}) log-raw={LogRaw} log-interval-ms={IntervalMs}",
                !config.DisableEye,
                !config.DisableExpression,
                config.EyeGainX,
                config.EyeGainY,
                config.LogRaw,
                config.LogIntervalMs);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to parse config.ini: {Message}. Falling back to defaults.", ex.Message);
        }

        return config;
    }

    private void ApplyKey(string key, string value)
    {
        switch (key)
        {
            case "eye-tracking":
                if (ParseBoolEnable(value) == false) DisableEye = true;
                break;
            case "expression-tracking":
                if (ParseBoolEnable(value) == false) DisableExpression = true;
                break;
            case "eye_gain":
                var parts = value.Split(',');
                if (parts.Length >= 1 && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var gx))
                    EyeGainX = gx;
                if (parts.Length >= 2 && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var gy))
                    EyeGainY = gy;
                break;
            case "log-raw":
                LogRaw = ParseBoolEnable(value) ?? false;
                break;
            case "log-file":
                if (value.Length > 0) LogFile = value;
                break;
            case "log-interval-ms":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) && iv >= 0)
                    LogIntervalMs = iv;
                break;
            case "log-include-visemes":
                LogIncludeVisemes = ParseBoolEnable(value) ?? false;
                break;
        }
    }

    private static bool? ParseBoolEnable(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "enable" or "enabled" or "true" or "1" or "on" or "yes" => true,
            "disable" or "disabled" or "false" or "0" or "off" or "no" => false,
            _ => null,
        };
    }

    public string ResolveLogPath()
    {
        return Path.IsPathRooted(LogFile) ? LogFile : Path.Combine(ModuleDirectory, LogFile);
    }

    private static string DefaultIniTemplate() =>
        """
        # VRCFTPicoModule config.ini
        # Lines starting with '#' or ';' are comments. Each entry uses `key: value` format.

        # Enable or disable tracking channels.
        # If set to `disable`, VRCFT will not receive that stream from this module.
        # The legacy `.disable_eye` / `.disable_expression` flag files are still honored for
        # backwards compatibility.
        eye-tracking: enable
        expression-tracking: enable

        # Eye gaze multiplier applied after the (LookIn - LookOut) / (LookUp - LookDown) diff.
        # 1.0 = pass-through (identical to upstream lonelyicer behavior).
        # Increase if your gaze feels too weak; decrease if it overshoots.
        # Format: X, Y
        eye_gain: 1.0, 1.0

        # Raw value CSV logger. When enabled, one row per received packet is written to `log-file`
        # (rate-limited by log-interval-ms). Useful for observing what PICO Connect is actually
        # sending, and for deciding what adjustments are needed.
        log-raw: disable

        # Path to the log CSV. Relative paths resolve next to the module DLL. Absolute paths are
        # honored as-is.
        log-file: PicoRawLog.csv

        # Minimum interval between logged rows in milliseconds. 0 = log every packet.
        log-interval-ms: 50

        # Include the 20 Pico visemes (index 52..71) in the CSV. These are not consumed by the
        # module today, but the values still arrive from PICO Connect.
        log-include-visemes: disable
        """;
}
