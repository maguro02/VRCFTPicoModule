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

    // Mouth calibration defaults derived from PICO 4 Pro raw-log observations at rest.
    // Each shape uses `output = clamp01((raw - floor) * gain)` before downstream logic.
    public float JawOpenFloor { get; private set; } = 0.05f;
    public float JawOpenGain { get; private set; } = 1.0f;
    public float MouthFrownFloor { get; private set; } = 0.14f;
    public float MouthFrownGain { get; private set; } = 2.0f;
    public float MouthSmileGain { get; private set; } = 1.0f;
    public float MouthPuckerFloor { get; private set; } = 0.10f;
    public float MouthPuckerGain { get; private set; } = 1.0f;
    public float MouthFunnelFloor { get; private set; } = 0.05f;
    public float MouthRollLowerFloor { get; private set; } = 0.16f;
    public float MouthRollUpperFloor { get; private set; } = 0.05f;

    // PICO Connect keeps CheekPuff pegged near its noise floor even during a real cheek puff,
    // and CheekSquint is emitted as a constant. Disabled by default; can be re-enabled for
    // users on hardware/firmware that produces usable signal.
    public bool CheekPuffEnabled { get; private set; }
    public float CheekPuffFloor { get; private set; } = 0.14f;
    public float CheekPuffGain { get; private set; } = 1.0f;
    public float CheekPuffCrossThreshold { get; private set; } = 0.25f;
    public bool CheekSquintEnabled { get; private set; }

    // PICO Connect fires MouthLeft and MouthRight together when the mouth shifts to one side.
    // Differential mode passes only the dominant side, which prevents the opposite-cheek bleed.
    public bool MouthLeftRightDifferential { get; private set; } = true;

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
            logger.LogInformation(
                "mouth calibration: jaw=(floor={JawFloor},gain={JawGain}) frown=(floor={FrownFloor},gain={FrownGain}) smile-gain={SmileGain} pucker=(floor={PuckerFloor},gain={PuckerGain}) funnel-floor={FunnelFloor} roll=(lower-floor={RollLowerFloor},upper-floor={RollUpperFloor}) cheek-puff={CheekPuff} cheek-squint={CheekSquint} mouth-lr-differential={LrDiff}",
                config.JawOpenFloor,
                config.JawOpenGain,
                config.MouthFrownFloor,
                config.MouthFrownGain,
                config.MouthSmileGain,
                config.MouthPuckerFloor,
                config.MouthPuckerGain,
                config.MouthFunnelFloor,
                config.MouthRollLowerFloor,
                config.MouthRollUpperFloor,
                config.CheekPuffEnabled,
                config.CheekSquintEnabled,
                config.MouthLeftRightDifferential);
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
                EyeGainYUp = ParseFloat(key, value, EyeGainYUp, logger, min: 0f);
                break;
            case "eye_gain_y_down":
                EyeGainYDown = ParseFloat(key, value, EyeGainYDown, logger, min: 0f);
                break;
            case "jaw_open_floor":
                JawOpenFloor = ParseFloat(key, value, JawOpenFloor, logger, min: 0f);
                break;
            case "jaw_open_gain":
                JawOpenGain = ParseFloat(key, value, JawOpenGain, logger, min: 0f);
                break;
            case "mouth_frown_floor":
                MouthFrownFloor = ParseFloat(key, value, MouthFrownFloor, logger, min: 0f);
                break;
            case "mouth_frown_gain":
                MouthFrownGain = ParseFloat(key, value, MouthFrownGain, logger, min: 0f);
                break;
            case "mouth_smile_gain":
                MouthSmileGain = ParseFloat(key, value, MouthSmileGain, logger, min: 0f);
                break;
            case "mouth_pucker_floor":
                MouthPuckerFloor = ParseFloat(key, value, MouthPuckerFloor, logger, min: 0f);
                break;
            case "mouth_pucker_gain":
                MouthPuckerGain = ParseFloat(key, value, MouthPuckerGain, logger, min: 0f);
                break;
            case "mouth_funnel_floor":
                MouthFunnelFloor = ParseFloat(key, value, MouthFunnelFloor, logger, min: 0f);
                break;
            case "mouth_roll_lower_floor":
                MouthRollLowerFloor = ParseFloat(key, value, MouthRollLowerFloor, logger, min: 0f);
                break;
            case "mouth_roll_upper_floor":
                MouthRollUpperFloor = ParseFloat(key, value, MouthRollUpperFloor, logger, min: 0f);
                break;
            case "cheek_puff":
                CheekPuffEnabled = ParseBoolOr(key, value, CheekPuffEnabled, logger);
                break;
            case "cheek_puff_floor":
                CheekPuffFloor = ParseFloat(key, value, CheekPuffFloor, logger, min: 0f);
                break;
            case "cheek_puff_gain":
                CheekPuffGain = ParseFloat(key, value, CheekPuffGain, logger, min: 0f);
                break;
            case "cheek_puff_cross_threshold":
                CheekPuffCrossThreshold = ParseFloat(key, value, CheekPuffCrossThreshold, logger, min: 0f);
                break;
            case "cheek_squint":
                CheekSquintEnabled = ParseBoolOr(key, value, CheekSquintEnabled, logger);
                break;
            case "mouth_lr_differential":
                MouthLeftRightDifferential = ParseBoolOr(key, value, MouthLeftRightDifferential, logger);
                break;
            case "log-raw":
                LogRaw = ParseBoolOr(key, value, LogRaw, logger);
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
                LogIncludeVisemes = ParseBoolOr(key, value, LogIncludeVisemes, logger);
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

    private static bool ParseBoolOr(string key, string value, bool @default, ILogger logger)
    {
        var parsed = ParseBoolEnable(value);
        if (parsed is not null) return parsed.Value;
        logger.LogWarning("config.ini {Key}='{Value}' is not a recognized enable/disable value; keeping default", key, value);
        return @default;
    }

    private static float ParseFloat(string label, string value, float @default, ILogger logger, float min = float.NegativeInfinity)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && float.IsFinite(parsed) && parsed >= min)
            return parsed;
        var kind = min > float.NegativeInfinity ? $"a finite number >= {min}" : "a finite number";
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
        # Final Gaze.x and Gaze.y are clamped to [-1.0, +1.0] regardless of this multiplier,
        # so values above 1.0 will saturate at the edges rather than extending the range.
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

        # ---- Mouth calibration --------------------------------------------------
        # PICO Connect emits several mouth blendshapes with a persistent non-zero baseline at
        # rest (e.g. MouthFrown ~ 0.14 in a neutral face on PICO 4 Pro), which shows up on the
        # avatar as a permanent frown / puff / lip-roll. Each calibrated shape below is
        # transformed as:
        #   output = clamp01((raw - floor) * gain)
        # so the floor cancels the resting bias and the gain restores expressive range.
        # Defaults are derived from observed PICO 4 Pro logs; adjust per user/hardware by
        # watching the raw CSV (see log-raw below).
        jaw_open_floor: 0.05
        jaw_open_gain: 1.0
        mouth_frown_floor: 0.14
        mouth_frown_gain: 2.0
        mouth_smile_gain: 1.0
        mouth_pucker_floor: 0.10
        mouth_pucker_gain: 1.0
        mouth_funnel_floor: 0.05
        mouth_roll_lower_floor: 0.16
        mouth_roll_upper_floor: 0.05

        # ---- Cheek channels ----------------------------------------------------
        # PICO Connect does not reliably drive CheekPuff or CheekSquint on tested firmware,
        # so both are disabled by default. Re-enable if your hardware/firmware differs.
        cheek_puff: disable
        cheek_puff_floor: 0.14
        cheek_puff_gain: 1.0
        cheek_puff_cross_threshold: 0.25
        cheek_squint: disable

        # Differential mode for MouthLeft/Right: PICO fires both simultaneously when the mouth
        # shifts to one side, so this passes only the dominant side to prevent opposite-side
        # bleed. Disable only if your avatar rig expects the raw simultaneous values.
        mouth_lr_differential: enable

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
