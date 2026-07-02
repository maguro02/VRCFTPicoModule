# VRCFTPicoModule

[![GitHub Release](https://img.shields.io/github/v/release/lonelyicer/VRCFTPicoModule)](https://github.com/lonelyicer/VRCFTPicoModule/releases/)
[![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/lonelyicer/VRCFTPicoModule/total)](https://github.com/lonelyicer/VRCFTPicoModule/releases/latest)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/lonelyicer/VRCFTPicoModule/ci.yml)](https://github.com/lonelyicer/VRCFTPicoModule/actions/workflows/ci.yml)


| **English** | [简体中文](./README.zh.md) |

VRCFTPicoModule is an extension module that adds support for PICO 4 Pro / Enterprise to [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking).

## Getting Started
### 1.Download  
Download the latest module (VRCFTPicoModule.zip) and one step setup script (SetupPICOConnect.ps1) from [here](https://github.com/lonelyicer/VRCFTPicoModule/releases/latest).  
Download the CI build from [here](https://github.com/lonelyicer/VRCFTPicoModule/actions/workflows/ci.yml).

### 2.Run one step setup script (optional)  
Right Click the `SetupPICOConnect.ps1`, and select `Run with Powershell`.

> [!NOTE]  
> You may need change the execution policy in PowerShell  
> Start PowerShell with administrator privilege and run the command below:  
> ``` 
> Set-ExecutionPolicy RemoteSigned 
> ```

### 3.Install module
Start `VRCFaceTracking` and Click the `Module Registry` tab.  
Then click the `Install Module from .zip` button.  
Select the file named `VRCFTPicoModule.zip`.  

Done! You have successfully installed the module.

> [!IMPORTANT]  
> If you are using `PICO Connect`.  
> You will need to manually change the protocol version or run a one-step setup script.

> [!NOTE]  
> To manual change protocol version,
> you will need change the value of `faceTrackingTransferProtocol` in the `settings.json` file located in the `%AppData%/PICO Connect/` directory to `2` or `1`.

## Configuration

On first run the module drops a `config.ini` next to the DLL
(`%APPDATA%\VRCFaceTracking\CustomLibs\config.ini`). Edit it and restart
VRCFaceTracking to apply. See [`VRCFTPicoModule/config.ini.example`](VRCFTPicoModule/config.ini.example)
for the full annotated template.

Available keys:

| Key | Default | Purpose |
|---|---|---|
| `eye-tracking` | `enable` | Turn the eye stream on/off |
| `expression-tracking` | `enable` | Turn the expression stream on/off |
| `eye_gain` | `1.0, 1.0` | Multiplier applied to gaze X, Y after the diff. `1.0` = pass-through. Final gaze values are clamped to `[-1, 1]` |
| `eye_gain_y_up` | `1.0` | Extra gain applied only when `Gaze.y >= 0` (looking up). PICO Connect under-reports upward gaze; try `2.5`-`3.0`. Final `Gaze.y` is clamped to `[-1, 1]`. |
| `eye_gain_y_down` | `1.0` | Extra gain applied only when `Gaze.y < 0` (looking down). Usually leave at `1.0` for PICO. |
| `wink_latch` | `enable` | Hold the last non-collapsed blink pair while PICO's per-eye blink estimator falls back to a symmetric half-lidded value (typically triggered by winking + looking to the FOV edge). Disable for raw PICO output. See `config.ini.example` for the tuning knobs. |
| `log-raw` | `disable` | Enable CSV logging of the raw 52-blendshape packet + computed eye state |
| `log-file` | `PicoRawLog.csv` | Log file path (relative resolves next to the DLL) |
| `log-interval-ms` | `50` | Minimum interval between logged rows |
| `log-include-visemes` | `disable` | Also log the 20 Pico visemes (index 52..71) |

The legacy `.disable_eye` / `.disable_expression` flag files are still honored.

### Debugging what PICO Connect is sending

Set `log-raw: enable` and open the resulting CSV. Each row contains:

- `wallclock_ms` — receive timestamp on the PC
- The 52 ARKit-style blendshapes (index 0..51) by name, plus optionally the 20 visemes
- `Openness_L`, `GazeX_L`, `GazeY_L`, `Openness_R`, `GazeX_R`, `GazeY_R` — the values
  actually written into `UnifiedTracking.Data.Eye` after this module's computation

> [!NOTE]
> When `eye-tracking: disable` (or a `.disable_eye` flag file is present), this module does
> not update `UnifiedTracking.Data.Eye`, so the `Openness_*` / `Gaze*` columns reflect
> whatever another module last wrote (or the default `0`), not this module's input. Only
> the raw blendshape columns are meaningful in that case.
>
> The `log-include-visemes: enable` option is silently ignored on the legacy protocol
> (port `29763`) because those packets only carry the 52 ARKit shapes.

This lets you observe the raw Pico input and the module's derived eye state side-by-side
without patching the DLL or running Wireshark.