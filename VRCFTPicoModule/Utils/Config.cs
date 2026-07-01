using System.Globalization;
using Microsoft.Extensions.Logging;

namespace VRCFTPicoModule.Utils;

public class Config
{
    public bool DisableEye { get; private set; }
    public bool DisableExpression { get; private set; }
    public float EyeGainX { get; private set; } = 1.0f;
    public float EyeGainY { get; private set; } = 1.0f;
    public float EyeGainYUp { get; private set; } = 1.0f;
    public float EyeGainYDown { get; private set; } = 1.0f;
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
                config.ApplyKey(key, value, logger);
            }

            logger.LogInformation(
                "config.ini loaded: eye={Eye} expression={Expression} eye_gain=({Gx},{Gy}) eye_gain_y=(up={GyUp},down={GyDown}) log-raw={LogRaw} log-interval-ms={IntervalMs}",
                !config.DisableEye,
                !config.DisableExpression,
                config.EyeGainX,
                config.EyeGainY,
                config.EyeGainYUp,
                config.EyeGainYDown,
                config.LogRaw,
                config.LogIntervalMs);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to parse config.ini: {Message}. Falling back to defaults.", ex.Message);
        }

        return config;
    }

    private void ApplyKey(string key, string value, ILogger logger)
    {
        switch (key)
        {
            case "eye-tracking":
                {
                    var parsed = ParseBoolEnable(value);
                    if (parsed is null)
                        logger.LogWarning("config.ini {Key}='{Value}' is not a recognized enable/disable value; keeping default", key, value);
                    else if (parsed == false) DisableEye = true;
                }
                break;
            case "expression-tracking":
                {
                    var parsed = ParseBoolEnable(value);
                    if (parsed is null)
                        logger.LogWarning("config.ini {Key}='{Value}' is not a recognized enable/disable value; keeping default", key, value);
                    else if (parsed == false) DisableExpression = true;
                }
                break;
            case "eye_gain":
                var parts = value.Split(',');
                if (parts.Length >= 1)
                    EyeGainX = ParseFloat("eye_gain X component", parts[0].Trim(), EyeGainX, logger);
                if (parts.Length >= 2)
                    EyeGainY = ParseFloat("eye_gain Y component", parts[1].Trim(), EyeGainY, logger);
                break;
            case "eye_gain_y_up":
                EyeGainYUp = ParseFloat("eye_gain_y_up", value, EyeGainYUp, logger, min: 0f);
                break;
            case "eye_gain_y_down":
                EyeGainYDown = ParseFloat("eye_gain_y_down", value, EyeGainYDown, logger, min: 0f);
                break;
            case "log-raw":
                {
                    var parsed = ParseBoolEnable(value);
                    if (parsed is null)
                        logger.LogWarning("config.ini {Key}='{Value}' is not a recognized enable/disable value; keeping default", key, value);
                    else LogRaw = parsed.Value;
                }
                break;
            case "log-file":
                if (value.Length > 0) LogFile = value;
                break;
            case "log-interval-ms":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) && iv >= 0)
                    LogIntervalMs = iv;
                else
                    logger.LogWarning("config.ini log-interval-ms '{Value}' is not a non-negative integer; keeping default {Default}",
                        value, LogIntervalMs);
                break;
            case "log-include-visemes":
                {
                    var parsed = ParseBoolEnable(value);
                    if (parsed is null)
                        logger.LogWarning("config.ini {Key}='{Value}' is not a recognized enable/disable value; keeping default", key, value);
                    else LogIncludeVisemes = parsed.Value;
                }
                break;
            default:
                logger.LogWarning("config.ini contains unknown key '{Key}'; ignoring", key);
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

    private static float ParseFloat(string label, string value, float @default, ILogger logger, float min = float.NegativeInfinity)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= min)
            return parsed;
        var kind = min > float.NegativeInfinity ? $"a number >= {min}" : "a number";
        logger.LogWarning("config.ini {Label} '{Value}' is not {Kind}; keeping default {Default}",
            label, value, kind, @default);
        return @default;
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

        # Asymmetric gain applied to the Y (up/down) axis *before* eye_gain's Y component.
        # Only the sign-matching side is multiplied:
        #   Gaze.y >= 0  ->  multiplied by eye_gain_y_up
        #   Gaze.y <  0  ->  multiplied by eye_gain_y_down
        # The final Gaze.y is clamped to [-1.0, +1.0].
        #
        # PICO Connect tends to under-report upward gaze (raw Up channel saturates ~0.25
        # while Down reaches ~0.65), so the upward eye motion feels weak on the avatar.
        # Recommended for PICO 4 Pro / Enterprise: eye_gain_y_up around 2.5 - 3.0,
        # eye_gain_y_down left at 1.0. Increase gradually; a value that makes forward
        # gaze look upward means the gain (or eye_gain Y) is too high.
        eye_gain_y_up: 1.0
        eye_gain_y_down: 1.0

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
