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
        private readonly object _writeLock = new();

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
            if (!IsConnected || _pipeClient == null) return;

            lock (_writeLock)
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    _pipeClient.Write(bytes, 0, bytes.Length);
                    _pipeClient.Flush();
                }
                catch
                {
                    // Ignore write failures; client will reconnect
                }
            }
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

        public void SetTouchpad(bool enabled, float mouseSens, float scrollSens, bool tapToClick)
        {
            var json = $"{{\"mouseEnabled\":{(enabled ? "true" : "false")}," +
                       $"\"mouseSens\":{mouseSens.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"scrollSens\":{scrollSens.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"tapToClick\":{(tapToClick ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void SetMicSync(bool sync)
        {
            var json = $"{{\"syncMicMute\":{(sync ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void SetAudioHaptics(bool enabled, float intensity, int cutoffHz, bool stereo)
        {
            var json = $"{{\"audioHaptics\":{(enabled ? "true" : "false")}," +
                       $"\"hapticIntensity\":{intensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"\"hapticCutoff\":{cutoffHz}," +
                       $"\"hapticStereo\":{(stereo ? "true" : "false")}}}";
            SendCommand(json);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
