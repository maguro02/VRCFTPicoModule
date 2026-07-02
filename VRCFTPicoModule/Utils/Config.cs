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

    // PICO's eye-tracking estimator collapses to a symmetric fallback when one eye is
    // winked while the other is at an extreme gaze angle: both EyeBlink channels
    // converge to the same mid value (~0.4-0.6), so the avatar goes half-lidded on
    // both sides. The latch arms on a clean wink (one side >= trigger_high, other side
    // <= trigger_low), then re-emits the last non-collapsed pair while the raw values sit in
    // the symmetric-collapse zone. It releases on either eyes-open or a real double
    // blink so ordinary blinking is untouched.
    public bool WinkLatchEnabled { get; private set; } = true;
    public float WinkLatchTriggerHigh { get; private set; } = 0.65f;
    public float WinkLatchTriggerLow { get; private set; } = 0.30f;
    public float WinkLatchCollapseLow { get; private set; } = 0.35f;
    public float WinkLatchSymmetryBand { get; private set; } = 0.10f;

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
            logger.LogInformation(
                "wink latch: enabled={Enabled} trigger=(high={High},low={Low}) collapse-low={CollapseLow} symmetry-band={SymBand}",
                config.WinkLatchEnabled,
                config.WinkLatchTriggerHigh,
                config.WinkLatchTriggerLow,
                config.WinkLatchCollapseLow,
                config.WinkLatchSymmetryBand);
            // Blink values live in [0, 1]; thresholds outside that range or ordered wrong
            // never match, silently turning the latch into a no-op.
            if (config.WinkLatchEnabled
                && (config.WinkLatchTriggerHigh > 1f
                    || config.WinkLatchTriggerLow >= config.WinkLatchTriggerHigh
                    || config.WinkLatchCollapseLow <= config.WinkLatchTriggerLow
                    || config.WinkLatchCollapseLow >= config.WinkLatchTriggerHigh
                    || config.WinkLatchSymmetryBand <= 0f))
                logger.LogWarning(
                    "config.ini wink latch thresholds are inconsistent (expected 0 <= trigger_low < collapse_low < trigger_high <= 1 and symmetry_band > 0); the latch may never engage");
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
            case "wink_latch":
                WinkLatchEnabled = ParseBoolOr(key, value, WinkLatchEnabled, logger);
                break;
            case "wink_latch_trigger_high":
                WinkLatchTriggerHigh = ParseFloat(key, value, WinkLatchTriggerHigh, logger, min: 0f);
                break;
            case "wink_latch_trigger_low":
                WinkLatchTriggerLow = ParseFloat(key, value, WinkLatchTriggerLow, logger, min: 0f);
                break;
            case "wink_latch_collapse_low":
                WinkLatchCollapseLow = ParseFloat(key, value, WinkLatchCollapseLow, logger, min: 0f);
                break;
            case "wink_latch_symmetry_band":
                WinkLatchSymmetryBand = ParseFloat(key, value, WinkLatchSymmetryBand, logger, min: 0f);
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
        # On first run this file is auto-created next to the module DLL
        # (%APPDATA%\VRCFaceTracking\CustomLibs\config.ini). Edit and restart VRCFT.
        #
        # VRCFTPicoModule config.ini
        # `#` または `;` で始まる行はコメント。各項目は `key: value` 形式。
        # 初回起動時にモジュールDLLの隣 (%APPDATA%\VRCFaceTracking\CustomLibs\config.ini)
        # に自動生成される。編集後は VRCFT を再起動して反映。

        # Enable or disable tracking channels. The legacy `.disable_eye` /
        # `.disable_expression` flag files are still honored for backwards compatibility.
        #
        # トラッキングチャネルの有効/無効。旧来の `.disable_eye` / `.disable_expression`
        # フラグファイルも後方互換のため引き続き有効。
        eye-tracking: enable
        expression-tracking: enable

        # Eye gaze multiplier applied after the (LookIn - LookOut) / (LookUp - LookDown)
        # diff. 1.0 = pass-through. Final Gaze.x/y are clamped to [-1.0, +1.0], so values
        # > 1.0 saturate at the edges rather than extending the range. Format: X, Y
        #
        # 視線倍率。(LookIn - LookOut) / (LookUp - LookDown) 差分の後に適用。
        # 1.0 でそのまま。最終値は [-1.0, +1.0] にクランプされるため、1.0超の値は
        # 範囲を広げるのではなく端で飽和する。フォーマット: X, Y
        eye_gain: 1.0, 1.0

        # Asymmetric Y-axis gain applied *before* eye_gain's Y component. Only the
        # sign-matching side is multiplied (Gaze.y >= 0 uses _up, < 0 uses _down); the
        # final Gaze.y is still clamped to [-1.0, +1.0]. PICO Connect under-reports
        # upward gaze (raw Up saturates ~0.25 vs Down ~0.65), so recommended for
        # PICO 4 Pro / Enterprise: eye_gain_y_up = 2.5-3.0, eye_gain_y_down = 1.0.
        #
        # Y軸(上下)の非対称ゲイン。eye_gain の Y 成分より *前* に適用され、符号が一致
        # する側のみ倍される (Gaze.y >= 0 は _up、< 0 は _down)。最終 Gaze.y は
        # [-1.0, +1.0] にクランプされる。PICO Connect は上方向視線を過小報告する
        # (raw Up は ~0.25 で飽和、Down は ~0.65)ため、PICO 4 Pro / Enterprise 推奨値:
        # eye_gain_y_up = 2.5〜3.0、eye_gain_y_down = 1.0。
        eye_gain_y_up: 1.0
        eye_gain_y_down: 1.0

        # ---- Mouth calibration ----------------------------------------------------
        # PICO Connect emits several mouth blendshapes with a non-zero baseline at rest
        # (e.g. MouthFrown ~ 0.14 on PICO 4 Pro), producing a permanent frown/puff on
        # the avatar. Each calibrated shape is transformed as:
        #   output = clamp01((raw - floor) * gain)
        # Defaults come from PICO 4 Pro logs; tune per user by watching the raw CSV
        # (see log-raw below).
        #
        # ---- 口周りキャリブレーション ---------------------------------------------
        # PICO Connect の一部の口 blendshape は無表情時にも非ゼロ値を出力し
        # (例: PICO 4 Pro の MouthFrown ~ 0.14)、アバターが常に不機嫌な顔になる。
        # 各シェイプは以下の式で補正:
        #   output = clamp01((raw - floor) * gain)
        # デフォルトは PICO 4 Pro のログ由来。個別調整は log-raw の生値CSVを参照。

        # JawOpen ticks at ~0.05-0.08 at rest; floor kills idle wobble.
        # JawOpen は無表情時に ~0.05-0.08 で揺れる。floor でアイドル揺らぎを除去。
        jaw_open_floor: 0.05
        jaw_open_gain: 1.0

        # MouthFrown raw peaks ~0.23 for an intentional frown, so gain 2.0 restores
        # a visible range after subtracting the ~0.14 rest floor.
        #
        # MouthFrown は意図的にしかめても raw のピークが ~0.23 しかないため、
        # ~0.14 の静止バイアスを引いた上で gain 2.0 により表現範囲を回復する。
        mouth_frown_floor: 0.14
        mouth_frown_gain: 2.0

        # MouthSmile has no meaningful rest floor. Bump the gain if smiles feel weak.
        # MouthSmile は静止バイアスなし。笑顔が弱いと感じたら gain を上げる。
        mouth_smile_gain: 1.0

        # MouthPucker sits around 0.10-0.12 at rest.
        # MouthPucker は無表情時 ~0.10-0.12。
        mouth_pucker_floor: 0.10
        mouth_pucker_gain: 1.0

        # MouthFunnel and both MouthRoll channels have small but consistent floors.
        # MouthFunnel と MouthRoll (上下) は小さいが安定した底値をもつ。
        mouth_funnel_floor: 0.05
        mouth_roll_lower_floor: 0.16
        mouth_roll_upper_floor: 0.05

        # ---- Cheek channels -------------------------------------------------------
        # PICO Connect doesn't reliably drive CheekPuff (pinned near noise floor even
        # during a real puff) or CheekSquint (emitted as a constant). Disabled by
        # default; re-enable if your hardware/firmware differs.
        #
        # ---- 頬チャネル -----------------------------------------------------------
        # PICO Connect は CheekPuff (実際に頬を膨らませてもノイズ底に張り付く) と
        # CheekSquint (定数出力) を正しく駆動しない。既定で無効。動作するハードや
        # ファームでは有効化して構わない。
        cheek_puff: disable
        cheek_puff_floor: 0.14
        cheek_puff_gain: 1.0

        # When cheek_puff is enabled and calibrated CheekPuff exceeds this threshold,
        # a heuristic re-routes the puff to the OPPOSITE cheek from a clearly-shifted
        # mouth. Raise if a plain mouth-shift accidentally triggers a puff.
        #
        # cheek_puff 有効時、補正後の CheekPuff がこの閾値を超えると、口のシフト方向
        # と逆側の頬に puff を振り替えるヒューリスティックが働く。単なる口のシフトで
        # puff が誤発火する場合は値を上げる。
        cheek_puff_cross_threshold: 0.25

        cheek_squint: disable

        # PICO Connect fires MouthLeft and MouthRight together when the mouth shifts,
        # causing bleed onto the opposite side. Differential mode passes only the
        # dominant side: max(0, MouthLeft - MouthRight). Disable only if your avatar
        # rig expects the raw simultaneous values.
        #
        # PICO Connect は口が片側にシフトすると MouthLeft と MouthRight を同時に
        # 出力し、反対側にブリードが出る。差分モードは優勢側のみ通す:
        # max(0, MouthLeft - MouthRight)。アバターリグが raw の同時値を期待する
        # 場合のみ無効化。
        mouth_lr_differential: enable

        # ---- Wink latch -----------------------------------------------------------
        # When one eye is winked while the other looks toward the FOV edge, PICO's
        # blink estimator collapses to a symmetric fallback (~0.4-0.6 on both eyes),
        # half-lidding the avatar. The latch arms on a clean wink and re-emits the
        # last non-collapsed EyeBlink_L / EyeBlink_R while raw values sit in the
        # symmetric-collapse zone. It releases on eyes-open or a real double blink,
        # so ordinary blinking is untouched. Disable if your firmware doesn't exhibit
        # the fallback or you prefer raw PICO output.
        #
        # ---- Wink ラッチ ----------------------------------------------------------
        # 片眼をウィンクしつつもう片方が FOV 端を見ると、PICO のまばたき推定が対称
        # フォールバック (両眼 ~0.4-0.6) に落ちてアバターが両目とも半目になる。
        # ラッチはクリーンなウィンクで動作し、raw が対称崩壊域にある間は直前の
        # 非崩壊な EyeBlink_L / EyeBlink_R を再送出。両眼開きまたは通常の両眼
        # まばたきで解除されるため、普段のまばたきは影響を受けない。この症状が
        # 出ないファーム、または PICO の raw 出力を優先したい場合は無効化。
        wink_latch: enable

        # trigger_high = closed enough to arm as the winking side (also the ceiling
        #                of the symmetric-collapse override zone).
        # trigger_low  = opposite eye must be at or below this to count as open.
        # collapse_low = lower bound of the symmetric-collapse zone; keep it slightly
        #                above trigger_low so genuinely opening eyes pass through.
        # symmetry_band = |EyeBlink_L - EyeBlink_R| tolerance inside the collapse zone.
        #
        # trigger_high = ウィンク側として認定するための閉眼の下限 (対称崩壊上書き域の
        #                上限も兼ねる)。
        # trigger_low  = 反対側の眼が開いていると認定するための上限。
        # collapse_low = 対称崩壊域の下限。trigger_low よりわずかに上に置いて、
        #                本当に開こうとしている眼はそのまま通す。
        # symmetry_band = 崩壊域内で許容する |EyeBlink_L - EyeBlink_R| の幅。
        wink_latch_trigger_high: 0.65
        wink_latch_trigger_low: 0.30
        wink_latch_collapse_low: 0.35
        wink_latch_symmetry_band: 0.10

        # Raw value CSV logger. Writes one row per received packet to `log-file`
        # (rate-limited by log-interval-ms). Useful for observing what PICO Connect
        # actually sends and deciding what adjustments are needed.
        #
        # 生値CSVロガー。受信パケット1件につき1行を `log-file` に書き込む
        # (log-interval-ms でレート制限)。PICO Connect が実際に送っている値の確認と
        # 調整方針の判断に有用。
        log-raw: disable

        # Path to the log CSV. Relative paths resolve next to the module DLL.
        # Absolute paths are honored as-is.
        #
        # ログCSVのパス。相対パスはモジュールDLL隣で解決。絶対パスはそのまま使用。
        log-file: PicoRawLog.csv

        # Minimum interval between logged rows in milliseconds. 0 = log every packet.
        # ログ行の最小間隔(ミリ秒)。0 で全パケット記録。
        log-interval-ms: 50

        # Include the 20 Pico visemes (index 52..71) in the CSV. Not consumed by the
        # module today, but the values still arrive from PICO Connect.
        #
        # CSV に PICO の20個の viseme (index 52..71) を含める。現状モジュールは未使用
        # だが、PICO Connect からは値が到達している。
        log-include-visemes: disable
        """;
}
