using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public class IpcClient : IDisposable
    {
        private const string PipeName = "OpenDualDriver";
        private NamedPipeClientStream? _pipeClient;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public event Action<ControllerTelemetry>? OnTelemetryReceived;
        public event Action<bool>? OnConnectionChanged;

        public bool IsConnected => _pipeClient?.IsConnected == true;

        public void Start()
        {
            if (_cts != null) return;

            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => WorkerLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _pipeClient?.Close(); } catch { }
            try { _readTask?.Wait(500); } catch { }
            _cts?.Dispose();
            _cts = null;
        }

        private async Task WorkerLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipeClient.ConnectAsync(1000, token);

                    OnConnectionChanged?.Invoke(true);

                    using var reader = new StreamReader(_pipeClient, Encoding.UTF8, false, 8192, leaveOpen: true);
                    while (!token.IsCancellationRequested && _pipeClient.IsConnected)
                    {
                        var line = await reader.ReadLineAsync(token);
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var telemetry = JsonSerializer.Deserialize<ControllerTelemetry>(line);
                            if (telemetry != null)
                            {
                                OnTelemetryReceived?.Invoke(telemetry);
                            }
                        }
                        catch
                        {
                            // Skip malformed packet
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Pipe connection failed or dropped; wait and retry
                    OnConnectionChanged?.Invoke(false);
                }
                finally
                {
                    try { _pipeClient?.Dispose(); } catch { }
                    _pipeClient = null;
                }

                if (!token.IsCancellationRequested)
                {
                    await Task.Delay(500, token);
                }
            }
        }

        public void SendCommand(string json)
        {
            var client = _pipeClient;
            if (client == null || !client.IsConnected) return;

            // Non-blocking fire-and-forget: dispatch to ThreadPool to guarantee 0ms UI delay
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                try
                {
                    // Try to acquire send lock within 200ms
                    if (!await _sendLock.WaitAsync(200).ConfigureAwait(false)) return;
                    try
                    {
                        var target = _pipeClient;
                        if (target != null && target.IsConnected)
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(json);
                            await target.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                            // CRITICAL: NEVER call target.Flush() on Windows NamedPipeClientStream.
                            // Named pipe Flush() invokes Win32 FlushFileBuffers, which blocks the thread
                            // until the remote driver consumes all buffer bytes, causing UI freezes/deadlocks.
                        }
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch
                {
                    // Ignore write failures; client reconnects automatically
                }
            });
        }

        public void SetRgbColor(int r, int g, int b, int effect = 0, int playerLeds = 4, float effectSpeed = 1.0f)
        {
            var json = $"{{\"ledR\":{r},\"ledG\":{g},\"ledB\":{b},\"rgbEffect\":{effect},\"playerLeds\":{playerLeds},\"effectSpeed\":{effectSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
            SendCommand(json);
        }

        public void SetAdaptiveTriggers(int ltMode, int ltStart, int ltForce, int ltFreq,
                                       int rtMode, int rtStart, int rtForce, int rtFreq)
        {
            var json = $"{{\"ltMode\":{ltMode},\"ltStart\":{ltStart},\"ltForce\":{ltForce},\"ltFreq\":{ltFreq}," +
                       $"\"rtMode\":{rtMode},\"rtStart\":{rtStart},\"rtForce\":{rtForce},\"rtFreq\":{rtFreq}}}";
            SendCommand(json);
        }

        public void SetTouchpad(bool enabled, float mouseSens, float scrollSens, bool tapToClick, bool touchpadVolumeGesture = true)
        {
            var json = $"{{\"mouseEnabled\":{(enabled ? "true" : "false")}," +
                       $"\"mouseSens\":{mouseSens.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"scrollSens\":{scrollSens.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"tapToClick\":{(tapToClick ? "true" : "false")}," +
                       $"\"touchpadVolumeGesture\":{(touchpadVolumeGesture ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void SetMicSync(bool sync)
        {
            var json = $"{{\"syncMicMute\":{(sync ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void SetAudioHaptics(bool enabled, float intensity, int cutoffHz, bool stereo, bool triggerKick = false, float triggerThreshold = 0.45f)
        {
            var json = $"{{\"audioHaptics\":{(enabled ? "true" : "false")}," +
                       $"\"hapticIntensity\":{intensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"hapticCutoff\":{cutoffHz}," +
                       $"\"hapticStereo\":{(stereo ? "true" : "false")}," +
                       $"\"audioTriggerKick\":{(triggerKick ? "true" : "false")}," +
                       $"\"audioTriggerThreshold\":{triggerThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
            SendCommand(json);
        }

        public void SetSpeakerOutput(bool enabled)
        {
            var json = $"{{\"speakerEnabled\":{(enabled ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void SetGyro(bool enabled, int mode, int outputMode, int space, float sensX, float sensY, bool invertX, bool invertY, int deadzone, float smooth, int clickMode = 0)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var json = $"{{\"gyroEnabled\":{(enabled ? "true" : "false")}," +
                       $"\"gyroMode\":{mode}," +
                       $"\"gyroOutputMode\":{outputMode}," +
                       $"\"gyroSpace\":{space}," +
                       $"\"gyroSensX\":{sensX.ToString(inv)}," +
                       $"\"gyroSensY\":{sensY.ToString(inv)}," +
                       $"\"gyroInvertX\":{(invertX ? "true" : "false")}," +
                       $"\"gyroInvertY\":{(invertY ? "true" : "false")}," +
                       $"\"gyroDeadzone\":{deadzone}," +
                       $"\"gyroSmooth\":{smooth.ToString(inv)}," +
                       $"\"gyroClickMode\":{clickMode}}}";
            SendCommand(json);
        }

        public void SetPowerManagement(int idleTimeoutMinutes, bool lowBatteryWarning)
        {
            var json = $"{{\"idleTimeout\":{idleTimeoutMinutes},\"lowBatteryWarning\":{(lowBatteryWarning ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void SendConfig(ConfigDto cfg)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var json = $"{{\"ledR\":{cfg.R},\"ledG\":{cfg.G},\"ledB\":{cfg.B},\"rgbEffect\":{cfg.Effect},\"playerLeds\":{cfg.PlayerLeds},\"effectSpeed\":{cfg.Speed.ToString(inv)}," +
                       $"\"mouseEnabled\":{(cfg.Mouse ? "true" : "false")},\"mouseSens\":{cfg.MouseSens.ToString(inv)},\"scrollSens\":{cfg.ScrollSens.ToString(inv)},\"touchpadVolumeGesture\":{(cfg.VolGesture ? "true" : "false")},\"syncMicMute\":{(cfg.SyncMic ? "true" : "false")}," +
                       $"\"ltMode\":{cfg.LtMode},\"ltStart\":{cfg.LtStart},\"ltForce\":{cfg.LtForce},\"ltFreq\":{cfg.LtFreq},\"rtMode\":{cfg.RtMode},\"rtStart\":{cfg.RtStart},\"rtForce\":{cfg.RtForce},\"rtFreq\":{cfg.RtFreq}," +
                       $"\"audioHaptics\":{(cfg.Haptic ? "true" : "false")},\"hapticIntensity\":{cfg.HapticIntensity.ToString(inv)},\"hapticCutoff\":{cfg.HapticCutoff},\"hapticStereo\":{(cfg.HapticStereo ? "true" : "false")}," +
                       $"\"audioTriggerKick\":{(cfg.AudioTriggerKick ? "true" : "false")},\"audioTriggerThreshold\":{cfg.AudioTriggerThreshold.ToString(inv)},\"speakerEnabled\":{(cfg.SpeakerEnabled ? "true" : "false")}," +
                       $"\"gyroEnabled\":{(cfg.Gyro ? "true" : "false")},\"gyroMode\":{cfg.GyroMode},\"gyroOutputMode\":{cfg.GyroOutputMode},\"gyroSpace\":{cfg.GyroSpace},\"gyroSensX\":{cfg.GyroSensX.ToString(inv)},\"gyroSensY\":{cfg.GyroSensY.ToString(inv)},\"gyroInvertX\":{(cfg.GyroInvertX ? "true" : "false")},\"gyroInvertY\":{(cfg.GyroInvertY ? "true" : "false")},\"gyroDeadzone\":{cfg.GyroDeadzone},\"gyroSmooth\":{cfg.GyroSmooth.ToString(inv)},\"gyroClickMode\":{cfg.GyroClickMode}," +
                       $"\"idleTimeout\":{cfg.IdleTimeout},\"lowBatteryWarning\":{(cfg.LowBatWarn ? "true" : "false")}," +
                       $"\"dualStageTriggers\":{(cfg.DualStageTriggers ? "true" : "false")},\"hairTriggerRelease\":{(cfg.HairTriggerRelease ? "true" : "false")}," +
                       $"\"sprintShelfL3\":{(cfg.SprintShelfL3 ? "true" : "false")},\"gyroRatcheting\":{(cfg.GyroRatcheting ? "true" : "false")}," +
                       $"\"hapticDetent\":{(cfg.HapticDetent ? "true" : "false")},\"gShockGuard\":{(cfg.GShockGuard ? "true" : "false")}," +
                       $"\"playerLedTelemetry\":{(cfg.PlayerLedTelemetry ? "true" : "false")},\"micBeamforming\":{(cfg.MicBeamforming ? "true" : "false")}," +
                       $"\"recoilAssist\":{(cfg.RecoilAssist ? "true" : "false")},\"thermalGuard\":{(cfg.ThermalGuard ? "true" : "false")}," +
                       $"\"virtualPadMode\":{cfg.VirtualPadMode},\"activeControllerSlot\":{cfg.ActiveControllerSlot}," +
                       $"\"leftStickCurve\":{cfg.LeftStickCurve},\"rightStickCurve\":{cfg.RightStickCurve}," +
                       $"\"antiDeadzoneL\":{cfg.AntiDeadzoneL.ToString(inv)},\"antiDeadzoneR\":{cfg.AntiDeadzoneR.ToString(inv)},\"axialDeadzoneShape\":{cfg.AxialDeadzoneShape}," +
                       $"\"flickStickEnabled\":{(cfg.FlickStickEnabled ? "true" : "false")},\"flickStickSens\":{cfg.FlickStickSens.ToString(inv)}," +
                       $"\"edgeShiftLayer\":{(cfg.EdgeShiftLayer ? "true" : "false")}," +
                       $"\"l2ActuationMin\":{cfg.L2ActuationMin},\"l2ActuationMax\":{cfg.L2ActuationMax}," +
                       $"\"r2ActuationMin\":{cfg.R2ActuationMin},\"r2ActuationMax\":{cfg.R2ActuationMax}," +
                       $"\"rapidFireMacro\":{(cfg.RapidFireMacro ? "true" : "false")},\"rapidFireHz\":{cfg.RapidFireHz}," +
                       $"\"zeroShiftLX\":{cfg.ZeroShiftLX},\"zeroShiftLY\":{cfg.ZeroShiftLY},\"zeroShiftRX\":{cfg.ZeroShiftRX},\"zeroShiftRY\":{cfg.ZeroShiftRY}}}";
            SendCommand(json);
        }

        public void PowerOffControllerNow()
        {
            SendCommand("{\"forcePowerOff\":true}");
        }

        public void SetDeepFeature(string featureKey, bool enabled)
        {
            var json = $"{{\"{featureKey}\":{(enabled ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void SetZeroShift(int zLX, int zLY, int zRX, int zRY)
        {
            var json = $"{{\"zeroShiftLX\":{zLX},\"zeroShiftLY\":{zLY},\"zeroShiftRX\":{zRX},\"zeroShiftRY\":{zRY}}}";
            SendCommand(json);
        }

        public void Dispose()
        {
            Stop();
            _sendLock.Dispose();
        }
    }
}
