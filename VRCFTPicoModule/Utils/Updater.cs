using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFTPicoModule.Data;
using static VRCFTPicoModule.Utils.Localization;

namespace VRCFTPicoModule.Utils
{
    public class Updater()
    {
        private readonly UdpClient? _udpClient;
        private readonly ILogger? _logger;
        private readonly bool _isLegacy;
        private readonly (bool, bool) _trackingAvailable;
        private readonly Config _config = new();
        private readonly RawValueLogger? _rawLogger;

        public Updater(UdpClient udpClient, ILogger logger, bool isLegacy, (bool, bool) trackingAvailable, Config config, RawValueLogger? rawLogger) : this()
        {
            _udpClient = udpClient;
            _logger = logger;
            _isLegacy = isLegacy;
            _trackingAvailable = trackingAvailable;
            _config = config;
            _rawLogger = rawLogger;
        }
        
        private int _timeOut;
        private float _lastMouthLeft;
        private float _lastMouthRight;
        private const float SmoothingFactor = 0.5f;
        private ModuleState _moduleState;

        private bool _winkLatchArmed;
        private float _latchedBlinkL;
        private float _latchedBlinkR;

        public void Update(ModuleState state)
        {
            if (_udpClient == null)
                return;
            
            if (_logger == null)
                return;
            
            _udpClient.Client.ReceiveTimeout = 100;
            _moduleState = state;
            
            if (_moduleState != ModuleState.Active) return;

            try
            {
                var endPoint = new IPEndPoint(IPAddress.Any, 0);
                var data = _udpClient.Receive(ref endPoint);
                var pShape = ParseData(data, _isLegacy);

                if (_trackingAvailable.Item1)
                    UpdateEye(pShape);

                if (_trackingAvailable.Item2)
                    UpdateExpression(pShape);

                if (_rawLogger != null && pShape.Length > 0)
                {
                    var eye = UnifiedTracking.Data.Eye;
                    _rawLogger.Enqueue(pShape,
                        eye.Left.Openness, eye.Left.Gaze.x, eye.Left.Gaze.y,
                        eye.Right.Openness, eye.Right.Gaze.x, eye.Right.Gaze.y);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                if (++_timeOut > 600)
                {
                    _logger.LogWarning(T("update-timeout"));
                    _timeOut = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(T("update-failed"), ex);
            }
        }

        private static float[] ParseData(byte[] data, bool isLegacy)
        {
            if (isLegacy && data.Length >= Marshal.SizeOf<LegacyDataPacket.DataPackBody>())
                return DataPacketHelpers.ByteArrayToStructure<LegacyDataPacket.DataPackBody>(data).blendShapeWeight;

            if (data.Length <
                Marshal.SizeOf<DataPacket.DataPackHeader>() + Marshal.SizeOf<DataPacket.DataPackBody>()) return [];
            var header = DataPacketHelpers.ByteArrayToStructure<DataPacket.DataPackHeader>(data);
            return header.trackingType == 2 ? DataPacketHelpers.ByteArrayToStructure<DataPacket.DataPackBody>(data, Marshal.SizeOf<DataPacket.DataPackHeader>()).blendShapeWeight : [];
        }

        private void UpdateEye(float[] pShape)
        {
            var eye = UnifiedTracking.Data.Eye;

            var (blinkL, blinkR) = ApplyWinkLatch(
                pShape[(int)BlendShape.Index.EyeBlink_L],
                pShape[(int)BlendShape.Index.EyeBlink_R]);

            #region LeftEye
            eye.Left.Openness = 1f - blinkL;
            eye.Left.Gaze.x = Math.Clamp(
                (pShape[(int)BlendShape.Index.EyeLookIn_L] - pShape[(int)BlendShape.Index.EyeLookOut_L]) * _config.EyeGainX,
                -1f, 1f);
            eye.Left.Gaze.y = ComputeGazeY(
                pShape[(int)BlendShape.Index.EyeLookUp_L],
                pShape[(int)BlendShape.Index.EyeLookDown_L],
                _config);
            #endregion

            #region RightEye
            eye.Right.Openness = 1f - blinkR;
            eye.Right.Gaze.x = Math.Clamp(
                (pShape[(int)BlendShape.Index.EyeLookOut_R] - pShape[(int)BlendShape.Index.EyeLookIn_R]) * _config.EyeGainX,
                -1f, 1f);
            eye.Right.Gaze.y = ComputeGazeY(
                pShape[(int)BlendShape.Index.EyeLookUp_R],
                pShape[(int)BlendShape.Index.EyeLookDown_R],
                _config);
            #endregion
            
            #region Brow
            SetParam(pShape, BlendShape.Index.BrowInnerUp, UnifiedExpressions.BrowInnerUpLeft);
            SetParam(pShape, BlendShape.Index.BrowInnerUp, UnifiedExpressions.BrowInnerUpRight);
            SetParam(pShape, BlendShape.Index.BrowOuterUp_L, UnifiedExpressions.BrowOuterUpLeft);
            SetParam(pShape, BlendShape.Index.BrowOuterUp_R, UnifiedExpressions.BrowOuterUpRight);
            SetParam(pShape, BlendShape.Index.BrowDown_L, UnifiedExpressions.BrowLowererLeft);
            SetParam(pShape, BlendShape.Index.BrowDown_L, UnifiedExpressions.BrowPinchLeft);
            SetParam(pShape, BlendShape.Index.BrowDown_R, UnifiedExpressions.BrowLowererRight);
            SetParam(pShape, BlendShape.Index.BrowDown_R, UnifiedExpressions.BrowPinchRight);
            #endregion

            #region Eye
            SetParam(pShape, BlendShape.Index.EyeSquint_L, UnifiedExpressions.EyeSquintLeft);
            SetParam(pShape, BlendShape.Index.EyeSquint_R, UnifiedExpressions.EyeSquintRight);
            SetParam(pShape, BlendShape.Index.EyeWide_L, UnifiedExpressions.EyeWideLeft);
            SetParam(pShape, BlendShape.Index.EyeWide_R, UnifiedExpressions.EyeWideRight);
            #endregion
        }

        private void UpdateExpression(float[] pShape)
        {
            // Calibrated inputs: (raw - floor) * gain, clamped to [0, 1]. Reused across regions.
            var jawOpen = Calibrate(pShape[(int)BlendShape.Index.JawOpen], _config.JawOpenFloor, _config.JawOpenGain);
            var mouthFrownLeft = Calibrate(pShape[(int)BlendShape.Index.MouthFrown_L], _config.MouthFrownFloor, _config.MouthFrownGain);
            var mouthFrownRight = Calibrate(pShape[(int)BlendShape.Index.MouthFrown_R], _config.MouthFrownFloor, _config.MouthFrownGain);
            var mouthSmileLeft = Calibrate(pShape[(int)BlendShape.Index.MouthSmile_L], 0f, _config.MouthSmileGain);
            var mouthSmileRight = Calibrate(pShape[(int)BlendShape.Index.MouthSmile_R], 0f, _config.MouthSmileGain);
            var mouthPucker = Calibrate(pShape[(int)BlendShape.Index.MouthPucker], _config.MouthPuckerFloor, _config.MouthPuckerGain);
            var mouthFunnel = Calibrate(pShape[(int)BlendShape.Index.MouthFunnel], _config.MouthFunnelFloor, 1f);
            var mouthRollLower = Calibrate(pShape[(int)BlendShape.Index.MouthRollLower], _config.MouthRollLowerFloor, 1f);
            var mouthRollUpper = Calibrate(pShape[(int)BlendShape.Index.MouthRollUpper], _config.MouthRollUpperFloor, 1f);
            var cheekPuff = _config.CheekPuffEnabled
                ? Calibrate(pShape[(int)BlendShape.Index.CheekPuff], _config.CheekPuffFloor, _config.CheekPuffGain)
                : 0f;
            var mouthPressLeft = pShape[(int)BlendShape.Index.MouthPress_L];
            var mouthPressRight = pShape[(int)BlendShape.Index.MouthPress_R];

            // PICO fires MouthLeft and MouthRight simultaneously; keep only the dominant side.
            var mouthLeftRaw = pShape[(int)BlendShape.Index.MouthLeft];
            var mouthRightRaw = pShape[(int)BlendShape.Index.MouthRight];
            var (mouthLeft, mouthRight) = _config.MouthLeftRightDifferential
                ? (Math.Max(0f, mouthLeftRaw - mouthRightRaw), Math.Max(0f, mouthRightRaw - mouthLeftRaw))
                : (mouthLeftRaw, mouthRightRaw);

            #region Jaw
            SetParam(jawOpen, UnifiedExpressions.JawOpen);
            SetParam(pShape, BlendShape.Index.JawLeft, UnifiedExpressions.JawLeft);
            SetParam(pShape, BlendShape.Index.JawRight, UnifiedExpressions.JawRight);
            SetParam(pShape, BlendShape.Index.JawForward, UnifiedExpressions.JawForward);
            SetParam(pShape, BlendShape.Index.MouthClose, UnifiedExpressions.MouthClosed);
            #endregion

            #region Cheek
            SetParam(_config.CheekSquintEnabled ? pShape[(int)BlendShape.Index.CheekSquint_L] : 0f, UnifiedExpressions.CheekSquintLeft);
            SetParam(_config.CheekSquintEnabled ? pShape[(int)BlendShape.Index.CheekSquint_R] : 0f, UnifiedExpressions.CheekSquintRight);

            // Default: balanced puff. When cheek_puff is enabled AND clearly above the cross
            // threshold, a mouth clearly shifted one way is treated as the OPPOSITE cheek doing
            // the real puff (differential smoothed mouth L/R feeds the boost).
            float cheekPuffLeft = cheekPuff, cheekPuffRight = cheekPuff;
            if (_config.CheekPuffEnabled && cheekPuff > _config.CheekPuffCrossThreshold)
            {
                var smoothedLeft = SmoothValue(mouthLeft, ref _lastMouthLeft);
                var smoothedRight = SmoothValue(mouthRight, ref _lastMouthRight);
                const float diffThreshold = 0.15f;
                if (smoothedLeft > smoothedRight + diffThreshold)
                    cheekPuffRight = Math.Min(1f, cheekPuff + smoothedLeft);
                else if (smoothedRight > smoothedLeft + diffThreshold)
                    cheekPuffLeft = Math.Min(1f, cheekPuff + smoothedRight);
            }
            SetParam(cheekPuffLeft, UnifiedExpressions.CheekPuffLeft);
            SetParam(cheekPuffRight, UnifiedExpressions.CheekPuffRight);
            #endregion

            #region Nose
            SetParam(pShape, BlendShape.Index.NoseSneer_L, UnifiedExpressions.NoseSneerLeft);
            SetParam(pShape, BlendShape.Index.NoseSneer_R, UnifiedExpressions.NoseSneerRight);
            #endregion

            #region Mouth
            SetParam(pShape, BlendShape.Index.MouthUpperUp_L, UnifiedExpressions.MouthUpperUpLeft);
            SetParam(pShape, BlendShape.Index.MouthUpperUp_R, UnifiedExpressions.MouthUpperUpRight);
            SetParam(pShape, BlendShape.Index.MouthLowerDown_L, UnifiedExpressions.MouthLowerDownLeft);
            SetParam(pShape, BlendShape.Index.MouthLowerDown_R, UnifiedExpressions.MouthLowerDownRight);

            SetParam(mouthFrownLeft, UnifiedExpressions.MouthFrownLeft);
            SetParam(mouthFrownRight, UnifiedExpressions.MouthFrownRight);

            SetParam(pShape, BlendShape.Index.MouthDimple_L, UnifiedExpressions.MouthDimpleLeft);
            SetParam(pShape, BlendShape.Index.MouthDimple_R, UnifiedExpressions.MouthDimpleRight);
            SetParam(mouthLeft, UnifiedExpressions.MouthUpperLeft);
            SetParam(mouthLeft, UnifiedExpressions.MouthLowerLeft);
            SetParam(mouthRight, UnifiedExpressions.MouthUpperRight);
            SetParam(mouthRight, UnifiedExpressions.MouthLowerRight);
            SetParam(mouthPressLeft, UnifiedExpressions.MouthPressLeft);
            SetParam(mouthPressRight, UnifiedExpressions.MouthPressRight);
            SetParam(pShape, BlendShape.Index.MouthShrugLower, UnifiedExpressions.MouthRaiserLower);
            SetParam(pShape, BlendShape.Index.MouthShrugUpper, UnifiedExpressions.MouthRaiserUpper);

            // Both sides use calibrated Smile minus calibrated RollLower. At rest both are 0,
            // so the previous "negative clamp erases the smile" bug is gone; during a real
            // lip-roll (pucker) the smile still gets suppressed. Slant intentionally mirrors
            // Pull now that the rest-baseline is subtracted at the source — the upstream code
            // used a second `- RollLower` on Slant Left only, which looked like a copy-paste
            // typo (Slant Right did not do the same).
            var cornerPullLeft = Math.Max(0f, mouthSmileLeft - mouthRollLower);
            var cornerPullRight = Math.Max(0f, mouthSmileRight - mouthRollLower);
            SetParam(cornerPullLeft, UnifiedExpressions.MouthCornerPullLeft);
            SetParam(cornerPullLeft, UnifiedExpressions.MouthCornerSlantLeft);
            SetParam(cornerPullRight, UnifiedExpressions.MouthCornerPullRight);
            SetParam(cornerPullRight, UnifiedExpressions.MouthCornerSlantRight);

            SetParam(pShape, BlendShape.Index.MouthStretch_L, UnifiedExpressions.MouthStretchLeft);
            SetParam(pShape, BlendShape.Index.MouthStretch_R, UnifiedExpressions.MouthStretchRight);
            #endregion

            #region Lip
            // Prefer calibrated pucker over funnel for LipFunnel unless the same side is
            // pressed shut (raw MouthPress kept — press has no observed baseline). Note that
            // the 0.3f threshold is applied to the *calibrated* pucker, so raising
            // mouth_pucker_floor also effectively raises this activation point.
            var funnelActive = mouthPucker > 0.3f;
            var funnelLeft = funnelActive && mouthPressLeft < 0.2f ? mouthPucker : mouthFunnel;
            var funnelRight = funnelActive && mouthPressRight < 0.2f ? mouthPucker : mouthFunnel;
            SetParam(funnelLeft, UnifiedExpressions.LipFunnelUpperLeft);
            SetParam(funnelRight, UnifiedExpressions.LipFunnelUpperRight);
            SetParam(funnelLeft, UnifiedExpressions.LipFunnelLowerLeft);
            SetParam(funnelRight, UnifiedExpressions.LipFunnelLowerRight);

            SetParam(mouthPucker, UnifiedExpressions.LipPuckerUpperLeft);
            SetParam(mouthPucker, UnifiedExpressions.LipPuckerUpperRight);
            SetParam(mouthPucker, UnifiedExpressions.LipPuckerLowerLeft);
            SetParam(mouthPucker, UnifiedExpressions.LipPuckerLowerRight);
            SetParam(mouthRollUpper, UnifiedExpressions.LipSuckUpperLeft);
            SetParam(mouthRollUpper, UnifiedExpressions.LipSuckUpperRight);
            SetParam(mouthRollLower, UnifiedExpressions.LipSuckLowerLeft);
            SetParam(mouthRollLower, UnifiedExpressions.LipSuckLowerRight);
            #endregion

            #region Tongue
            SetParam(pShape, BlendShape.Index.TongueOut, UnifiedExpressions.TongueOut);
            #endregion
        }

        private static float Calibrate(float raw, float floor, float gain)
            => Math.Clamp((raw - floor) * gain, 0f, 1f);

        private static float SmoothValue(float newValue, ref float lastValue)
        {
            lastValue += (newValue - lastValue) * SmoothingFactor;
            return lastValue;
        }

        private (float blinkL, float blinkR) ApplyWinkLatch(float bl, float br)
        {
            if (!_config.WinkLatchEnabled)
                return (bl, br);

            var high = _config.WinkLatchTriggerHigh;
            var low = _config.WinkLatchTriggerLow;

            if ((bl >= high && br <= low) || (br >= high && bl <= low))
                _winkLatchArmed = true;
            else if ((bl <= low && br <= low) || (bl >= high && br >= high))
                _winkLatchArmed = false;

            var inCollapse = _winkLatchArmed
                          && bl >= _config.WinkLatchCollapseLow && bl < high
                          && br >= _config.WinkLatchCollapseLow && br < high
                          && MathF.Abs(bl - br) < _config.WinkLatchSymmetryBand;

            if (inCollapse)
                return (_latchedBlinkL, _latchedBlinkR);

            _latchedBlinkL = bl;
            _latchedBlinkR = br;
            return (bl, br);
        }

        private static float ComputeGazeY(float lookUp, float lookDown, Config config)
        {
            var raw = lookUp - lookDown;
            var y = raw * (raw >= 0f ? config.EyeGainYUp : config.EyeGainYDown) * config.EyeGainY;
            return Math.Clamp(y, -1f, 1f);
        }

        private static void SetParam(float[] pShape, BlendShape.Index index, UnifiedExpressions outputType)
        {
            UnifiedTracking.Data.Shapes[(int)outputType].Weight = pShape[(int)index];
        }

        private static void SetParam(float param, UnifiedExpressions outputType)
        {
            UnifiedTracking.Data.Shapes[(int)outputType].Weight = param;
        }
    }
}