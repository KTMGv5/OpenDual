using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace OpenDualSetup
{
    public class Program
    {
        private const string AppName = "OpenDual";
        private const string AppFullName = "OpenDual PlayStation Controller Suite";
        private const string AppVersion = "1.6.6";
        private const string AppPublisher = "OpenDual Team";

        [STAThread]
        public static void Main(string[] args)
        {
            bool isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("-q", StringComparison.OrdinalIgnoreCase));

            string installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "OpenDual");

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenDual");

            // 1. Check if running detached cleanup from temp
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--cleanup-from-temp", StringComparison.OrdinalIgnoreCase) && i + 2 < args.Length)
                {
                    string targetToWipe = args[i + 1];
                    string appDataToWipe = args[i + 2];
                    RunTempCleanup(targetToWipe, appDataToWipe, isSilent);
                    return;
                }
            }

            // 2. Check if uninstallation request
            bool isUninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)) ||
                               Process.GetCurrentProcess().ProcessName.Contains("uninstall", StringComparison.OrdinalIgnoreCase);

            if (isUninstall)
            {
                RunUninstallation(installDir, appDataDir, isSilent);
            }
            else
            {
                RunInstallation(installDir, isSilent);
            }
        }

        private static void RunInstallation(string targetDir, bool silent)
        {
            try
            {
                if (!silent)
                {
                    var prompt = MessageBox.Show(
                        $"Welcome to {AppFullName} Setup!\n\nThis will install OpenDual into:\n{targetDir}\n\n- High-performance 1000Hz direct HID driver\n- Precision touchpad mouse & audio controls\n- DualSense 6-axis gyro motion aiming\n- Registered in Windows Installed Apps\n- Starts automatically with Windows\n\nDo you want to continue?",
                        "OpenDual Setup",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (prompt != MessageBoxResult.Yes) return;
                }

                // 1. Kill any existing running instances
                StopProcesses();

                // 2. Prepare target directories
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string assetsDir = Path.Combine(targetDir, "Assets");
                if (!Directory.Exists(assetsDir))
                {
                    Directory.CreateDirectory(assetsDir);
                }

                string sourceDir = AppDomain.CurrentDomain.BaseDirectory;

                // 3. Extract Embedded Binaries and Assets (or fallback to adjacent files)
                string driverExe = Path.Combine(targetDir, "OpenDualDriver.exe");
                string hubExe = Path.Combine(targetDir, "OpenDualHub.exe");
                string icoDst = Path.Combine(assetsDir, "app.ico");
                string logoDst = Path.Combine(assetsDir, "logo.png");

                ExtractResource("Embedded.OpenDualDriver.exe", driverExe, Path.Combine(sourceDir, "OpenDualDriver.exe"));
                ExtractResource("Embedded.OpenDualHub.exe", hubExe, Path.Combine(sourceDir, "OpenDualHub.exe"));
                ExtractResource("Embedded.app.ico", icoDst, Path.Combine(sourceDir, "Assets", "app.ico"));
                ExtractResource("Embedded.logo.png", logoDst, Path.Combine(sourceDir, "Assets", "logo.png"));

                // 4. Copy self as OpenDualUninstaller.exe into target
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string uninstallerDst = Path.Combine(targetDir, "OpenDualUninstaller.exe");
                if (File.Exists(currentExe))
                {
                    try { SafeCopyFile(currentExe, uninstallerDst); } catch { }
                }

                // 5. Ensure ViGEmBus driver is installed for Xbox 360 virtual gamepad emulation
                EnsureViGEmBusInstalled();

                // 6. Register in Windows Registry (Add/Remove Programs, App Paths, Run Auto-Start)
                long totalBytes = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f).Length).Sum();
                int estimatedSizeKb = (int)(totalBytes / 1024);

                RegisterInRegistry(targetDir, estimatedSizeKb);
                RegisterAppPaths(targetDir);
                RegisterAutoStart(driverExe);

                // 7. Create Start Menu and Desktop Shortcuts
                CreateShortcuts(hubExe);

                // 8. Launch OpenDual Driver Service (hidden) and Hub
                if (File.Exists(driverExe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = driverExe,
                            WorkingDirectory = targetDir,
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                    }
                    catch { }
                }

                if (File.Exists(hubExe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = hubExe,
                            WorkingDirectory = targetDir,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }

                if (!silent)
                {
                    MessageBox.Show(
                        $"{AppFullName} has been installed and started successfully!\n\nDriver service is running and configured to start with Windows.\nShortcuts created on Desktop and Start Menu.",
                        "OpenDual Setup",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show($"Setup failed with error: {ex.Message}", "OpenDual Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void RunUninstallation(string targetDir, string appDataDir, bool silent)
        {
            try
            {
                if (!silent)
                {
                    var confirm = MessageBox.Show(
                        $"Are you sure you want to completely uninstall {AppFullName} from your system?\n\nThis will remove all installed binaries, driver services, shortcuts, and AppData settings.",
                        "OpenDual Uninstaller",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirm != MessageBoxResult.Yes) return;
                }

                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                bool isRunningFromTarget = !string.IsNullOrEmpty(currentExe) &&
                                          currentExe.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase);

                if (isRunningFromTarget)
                {
                    // Copy self to %TEMP% and relaunch detached so targetDir is not locked
                    string tempExe = Path.Combine(Path.GetTempPath(), "OpenDualUninstaller.exe");
                    try
                    {
                        File.Copy(currentExe, tempExe, true);
                    }
                    catch { }

                    string silentArg = silent ? "--silent" : "";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempExe,
                        Arguments = $"--cleanup-from-temp \"{targetDir}\" \"{appDataDir}\" {silentArg}",
                        UseShellExecute = false
                    });

                    // Exit immediately to release file lock
                    Environment.Exit(0);
                    return;
                }

                // If not running from target (e.g. running from temp or standalone), run cleanup directly
                RunTempCleanup(targetDir, appDataDir, silent);
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show($"Uninstallation failed: {ex.Message}", "OpenDual Uninstaller Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void RunTempCleanup(string targetDir, string appDataDir, bool silent)
        {
            try
            {
                // Brief pause to allow the launching uninstaller process to fully exit
                Thread.Sleep(600);

                // 1. Terminate running driver and hub
                StopProcesses();

                // 2. Remove Shortcuts
                RemoveShortcuts();

                // 3. Remove Registry entries (Add/Remove programs, App Paths, Run auto-start)
                UnregisterFromRegistry();

                // 4. Delete Installation directory cleanly
                if (Directory.Exists(targetDir))
                {
                    for (int attempt = 1; attempt <= 6; attempt++)
                    {
                        try
                        {
                            Directory.Delete(targetDir, true);
                            break;
                        }
                        catch (IOException)
                        {
                            StopProcesses();
                            Thread.Sleep(300 * attempt);
                        }
                        catch { break; }
                    }
                }

                // 5. Delete AppData settings and profiles folder cleanly
                if (Directory.Exists(appDataDir))
                {
                    try
                    {
                        Directory.Delete(appDataDir, true);
                    }
                    catch { }
                }

                if (!silent)
                {
                    MessageBox.Show(
                        $"{AppFullName} has been completely uninstalled from your computer.",
                        "OpenDual Uninstaller",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show($"Cleanup failed: {ex.Message}", "OpenDual Uninstaller Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static bool ExtractResource(string resourceName, string targetPath, string fallbackSource)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null && stream.Length > 0)
                {
                    string? dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    stream.CopyTo(fs);
                    return true;
                }
            }
            catch { }

            // Fallback to disk copy if running before resource embedding
            if (File.Exists(fallbackSource))
            {
                try
                {
                    SafeCopyFile(fallbackSource, targetPath);
                    return true;
                }
                catch { }
            }

            return false;
        }

        private static void EnsureViGEmBusInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
                if (key != null) return; // Already installed and operational

                // Attempt 1: winget silent install
                var wingetPsi = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    Arguments = "install --id ViGEm.ViGEmBus --accept-package-agreements --accept-source-agreements -h",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var wingetProc = Process.Start(wingetPsi);
                wingetProc?.WaitForExit(35000);

                using var verifyKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
                if (verifyKey != null) return;

                // Attempt 2: Download official ViGEmBus installer and run silently
                string tempInstaller = Path.Combine(Path.GetTempPath(), "ViGEmBus_Setup.exe");
                using var client = new System.Net.Http.HttpClient();
                var data = client.GetByteArrayAsync("https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe").GetAwaiter().GetResult();
                File.WriteAllBytes(tempInstaller, data);

                var instPsi = new ProcessStartInfo
                {
                    FileName = tempInstaller,
                    Arguments = "/quiet /norestart",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var instProc = Process.Start(instPsi);
                instProc?.WaitForExit(35000);

                try { File.Delete(tempInstaller); } catch { }
            }
            catch { }
        }

        private static void StopProcesses()
        {
            string[] procNames = new string[] { "OpenDualDriver", "OpenDualHub" };

            foreach (var name in procNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        p.Kill(true);
                        p.WaitForExit(1500);
                    }
                    catch { }
                }
            }

            foreach (var name in procNames)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = $"/f /t /im {name}.exe",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var killProc = Process.Start(psi);
                    killProc?.WaitForExit(1000);
                }
                catch { }
            }

            Thread.Sleep(300);
        }

        private static void SafeCopyFile(string src, string dst)
        {
            if (!File.Exists(src)) return;

            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    File.Copy(src, dst, true);
                    return;
                }
                catch (IOException)
                {
                    if (attempt == 6) throw;
                    StopProcesses();
                    Thread.Sleep(200 * attempt);
                }
            }
        }

        private static void RegisterInRegistry(string installDir, int sizeKb)
        {
            string uninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenDual";
            using var key = Registry.CurrentUser.CreateSubKey(uninstallKeyPath);
            if (key == null) return;

            string hubExe = Path.Combine(installDir, "OpenDualHub.exe");
            string uninstallerExe = Path.Combine(installDir, "OpenDualUninstaller.exe");

            key.SetValue("DisplayName", AppFullName);
            key.SetValue("DisplayVersion", AppVersion);
            key.SetValue("Publisher", AppPublisher);
            key.SetValue("InstallLocation", installDir);
            key.SetValue("DisplayIcon", $"{hubExe},0");
            key.SetValue("UninstallString", $"\"{uninstallerExe}\"");
            key.SetValue("QuietUninstallString", $"\"{uninstallerExe}\" --silent");
            key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("Comments", "High-Performance Direct HID PlayStation Controller Driver & Desktop Suite");
            key.SetValue("URLInfoAbout", "https://github.com/KTMGv5/OpenDual");
        }

        private static void RegisterAppPaths(string installDir)
        {
            string hubExe = Path.Combine(installDir, "OpenDualHub.exe");

            string[] appNames = new string[] { "OpenDualHub.exe", "OpenDual.exe" };
            foreach (var name in appNames)
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{name}");
                if (key != null)
                {
                    key.SetValue("", hubExe);
                    key.SetValue("Path", installDir);
                }
            }
        }

        private static void RegisterAutoStart(string driverExe)
        {
            try
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (runKey != null && File.Exists(driverExe))
                {
                    runKey.SetValue("OpenDualDriver", $"\"{driverExe}\"");
                }
            }
            catch { }
        }

        private static void UnregisterFromRegistry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenDual", false);
            }
            catch { }

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\OpenDualHub.exe", false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\OpenDual.exe", false);
            }
            catch { }

            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                runKey?.DeleteValue("OpenDualDriver", false);
            }
            catch { }
        }

        private static void CreateShortcuts(string targetExe)
        {
            try
            {
                string startMenuPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs", "OpenDual.lnk");

                string desktopPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "OpenDual.lnk");

                CreateWscriptShortcut(startMenuPath, targetExe, Path.GetDirectoryName(targetExe) ?? "");
                CreateWscriptShortcut(desktopPath, targetExe, Path.GetDirectoryName(targetExe) ?? "");
            }
            catch { }
        }

        private static void RemoveShortcuts()
        {
            try
            {
                string startMenuPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs", "OpenDual.lnk");

                string desktopPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "OpenDual.lnk");

                if (File.Exists(startMenuPath)) File.Delete(startMenuPath);
                if (File.Exists(desktopPath)) File.Delete(desktopPath);
            }
            catch { }
        }

        private static void CreateWscriptShortcut(string shortcutPath, string targetPath, string workingDir)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                object? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                object? shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                if (shortcut == null) return;

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { AppFullName });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { $"{targetPath},0" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }
    }
}
