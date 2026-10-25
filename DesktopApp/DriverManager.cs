using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenDualHub
{
    public class DriverManager
    {
        private const string ViGEmInstallerUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
        private Process? _driverProcess;

        public static bool IsViGEmBusInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsDriverEngineRunning()
        {
            var processes = Process.GetProcessesByName("OpenDualDriver");
            return processes.Length > 0;
        }

        public bool StartDriverEngine()
        {
            if (IsDriverEngineRunning()) return true;

            // Locate OpenDualDriver.exe
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(basePath, "OpenDualDriver.exe");

            if (!File.Exists(exePath))
            {
                // Check relative development build paths
                string devPath = Path.GetFullPath(Path.Combine(basePath, @"..\..\..\..\build_core\Release\OpenDualDriver.exe"));
                if (File.Exists(devPath))
                {
                    exePath = devPath;
                }
            }

            if (!File.Exists(exePath))
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? basePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                _driverProcess = Process.Start(psi);
                return _driverProcess != null && !_driverProcess.HasExited;
            }
            catch
            {
                return false;
            }
        }

        public void StopDriverEngine()
        {
            var processes = Process.GetProcessesByName("OpenDualDriver");
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(1000);
                }
                catch { }
            }
            _driverProcess = null;
        }

        public async Task<bool> InstallViGEmBusAsync(Action<string>? statusCallback = null)
        {
            try
            {
                statusCallback?.Invoke("Downloading ViGEmBus installer...");
                string tempFile = Path.Combine(Path.GetTempPath(), "ViGEmBus_Installer.exe");

                using var httpClient = new HttpClient();
                var data = await httpClient.GetByteArrayAsync(ViGEmInstallerUrl);
                await File.WriteAllBytesAsync(tempFile, data);

                statusCallback?.Invoke("Launching installer (Administrator prompt will appear)...");
                var psi = new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true,
                    Verb = "runas" // Prompt UAC elevation
                };

                var proc = Process.Start(psi);
                if (proc == null) return false;

                statusCallback?.Invoke("Installing ViGEmBus driver...");
                await proc.WaitForExitAsync();

                return IsViGEmBusInstalled();
            }
            catch (Exception ex)
            {
                statusCallback?.Invoke($"Installation failed: {ex.Message}");
                return false;
            }
        }
    }
}
