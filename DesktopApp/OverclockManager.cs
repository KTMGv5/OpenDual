using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenDualHub
{
    public class PollingBenchmark
    {
        private readonly List<float> _latencyHistory = new();
        private readonly object _lock = new();
        private const int MaxSamples = 600; // ~10 seconds at 60Hz IPC updates

        public int CurrentHz { get; private set; }
        public float CurrentLatencyMs { get; private set; }
        public float MinLatencyMs { get; private set; } = 999.0f;
        public float MaxLatencyMs { get; private set; } = 0.0f;
        public float AvgLatencyMs { get; private set; } = 0.0f;
        public int TotalSamples { get; private set; } = 0;
        public bool IsEsportsReady => CurrentHz >= 900;

        public void AddSample(int hz, float latencyMs)
        {
            lock (_lock)
            {
                CurrentHz = hz;
                CurrentLatencyMs = latencyMs;
                TotalSamples++;

                if (latencyMs > 0.01f)
                {
                    if (latencyMs < MinLatencyMs) MinLatencyMs = latencyMs;
                    if (latencyMs > MaxLatencyMs) MaxLatencyMs = latencyMs;

                    _latencyHistory.Add(latencyMs);
                    if (_latencyHistory.Count > MaxSamples)
                    {
                        _latencyHistory.RemoveAt(0);
                    }

                    float sum = 0;
                    for (int i = 0; i < _latencyHistory.Count; i++) sum += _latencyHistory[i];
                    AvgLatencyMs = sum / _latencyHistory.Count;
                }
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _latencyHistory.Clear();
                MinLatencyMs = 999.0f;
                MaxLatencyMs = 0.0f;
                AvgLatencyMs = 0.0f;
                TotalSamples = 0;
            }
        }
    }

    public static class OverclockManager
    {
        private const string HidusbfZipUrl = "https://raw.githubusercontent.com/LordOfMice/hidusbf/master/hidusbf.zip";
        private const string DualSenseVidPid = "VID_054C&PID_0CE6";

        public static string GetHidusbfDirectory()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localApp, "OpenDual", "tools", "hidusbf");
        }

        public static string GetSetupExePath()
        {
            return Path.Combine(GetHidusbfDirectory(), "DRIVER", "Setup.exe");
        }

        public static string GetCertPath()
        {
            return Path.Combine(GetHidusbfDirectory(), "SweetLow.CER");
        }

        public static bool IsHidusbfDownloaded()
        {
            return File.Exists(GetSetupExePath());
        }

        public static bool IsHidusbfServiceInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\hidusbf");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsMemoryIntegrityEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                if (key != null)
                {
                    object? val = key.GetValue("Enabled");
                    if (val is int intVal && intVal == 1) return true;
                }
            }
            catch { }
            return false;
        }

        public static (bool isFiltered, string details) CheckDualSenseFilterStatus()
        {
            try
            {
                using var usbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                if (usbKey == null) return (false, "USB Enum registry key not accessible");

                foreach (string subKeyName in usbKey.GetSubKeyNames())
                {
                    if (subKeyName.IndexOf(DualSenseVidPid, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        using var devKey = usbKey.OpenSubKey(subKeyName);
                        if (devKey == null) continue;

                        foreach (string instanceName in devKey.GetSubKeyNames())
                        {
                            using var instKey = devKey.OpenSubKey(instanceName);
                            if (instKey == null) continue;

                            object? lower = instKey.GetValue("LowerFilters");
                            if (lower is string[] arr && Array.Exists(arr, f => f.Equals("hidusbf", StringComparison.OrdinalIgnoreCase)))
                            {
                                return (true, $"Active (Device: {instanceName})");
                            }
                            if (lower is string s && s.Equals("hidusbf", StringComparison.OrdinalIgnoreCase))
                            {
                                return (true, $"Active (Device: {instanceName})");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Check error: {ex.Message}");
            }

            return (false, "Default Driver (Not Filtered)");
        }

        public static async Task<(bool success, string message)> DownloadAndExtractAsync(IProgress<int>? progress = null)
        {
            try
            {
                string targetDir = GetHidusbfDirectory();
                Directory.CreateDirectory(targetDir);

                string zipPath = Path.Combine(targetDir, "hidusbf.zip");

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "OpenDualHub");
                    using var response = await client.GetAsync(HidusbfZipUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using var sourceStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    byte[] buffer = new byte[8192];
                    long totalRead = 0;
                    int read;

                    while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (totalBytes > 0 && progress != null)
                        {
                            int pct = (int)((totalRead * 100) / totalBytes);
                            progress.Report(pct);
                        }
                    }
                }

                progress?.Report(100);

                // Extract package
                ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);

                try { File.Delete(zipPath); } catch { }

                if (IsHidusbfDownloaded())
                {
                    return (true, "SweetLow HIDUSBF package downloaded and extracted successfully.");
                }
                return (false, "Downloaded archive but Setup.exe was not found in DRIVER folder.");
            }
            catch (Exception ex)
            {
                return (false, $"Download failed: {ex.Message}");
            }
        }

        public static (bool success, string message) LaunchSweetLowSetup()
        {
            try
            {
                string setupPath = GetSetupExePath();
                if (!File.Exists(setupPath))
                {
                    return (false, "SweetLow Setup.exe not found. Please click Download first.");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = setupPath,
                    WorkingDirectory = Path.GetDirectoryName(setupPath),
                    UseShellExecute = true,
                    Verb = "runas" // Prompt for UAC Administrator elevation
                };

                Process.Start(psi);
                return (true, "SweetLow Setup launched successfully with Administrator rights.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to launch Setup.exe: {ex.Message}");
            }
        }

        public static async Task<(bool success, string message)> InstallCertificateAsync()
        {
            try
            {
                string certPath = GetCertPath();
                if (!File.Exists(certPath))
                {
                    return (false, "SweetLow.CER certificate file not found. Please download HIDUSBF first.");
                }

                // Run certutil -addstore -f "Root" SweetLow.CER
                // and certutil -addstore -f "TrustedPublisher" SweetLow.CER
                string cmdArgs = $"/c certutil -addstore -f \"Root\" \"{certPath}\" && certutil -addstore -f \"TrustedPublisher\" \"{certPath}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode == 0)
                    {
                        return (true, "SweetLow certificate added to Windows Trusted Root & Trusted Publishers stores.");
                    }
                    return (false, $"certutil exited with code {proc.ExitCode}");
                }
                return (false, "Could not start certutil process.");
            }
            catch (Exception ex)
            {
                return (false, $"Certificate installation failed: {ex.Message}");
            }
        }

        public static void OpenCoreIsolationSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("windowsdefender://coreisolation") { UseShellExecute = true });
            }
            catch
            {
                // Fallback to general settings
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:windowsdefender") { UseShellExecute = true });
                }
                catch { }
            }
        }
    }
}
