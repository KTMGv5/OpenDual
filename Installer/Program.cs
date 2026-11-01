using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OpenDualSetup
{
    public class Program
    {
        public const string AppName = "OpenDual";
        public const string AppFullName = "OpenDual Controller Suite";
        public const string AppVersion = "1.8.0";
        public const string AppPublisher = "OpenDual Team";

        [STAThread]
        public static void Main(string[] args)
        {
            bool isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("-q", StringComparison.OrdinalIgnoreCase));

            string? screenshotPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--screenshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    screenshotPath = args[i + 1];
                }
            }

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
                    if (isSilent)
                    {
                        RunTempCleanup(targetToWipe, appDataToWipe);
                    }
                    else
                    {
                        var app = new Application();
                        var win = new InstallerWindow(targetToWipe, appDataToWipe, uninstallMode: true);
                        app.Run(win);
                    }
                    return;
                }
            }

            // 2. Check if uninstallation request
            bool isUninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)) ||
                               Process.GetCurrentProcess().ProcessName.Contains("uninstall", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(screenshotPath))
            {
                var app = new Application();
                var win = new InstallerWindow(installDir, appDataDir, isUninstall);
                win.Loaded += async (s, e) =>
                {
                    await Task.Delay(250);
                    win.CaptureToPng(screenshotPath);
                    win.Close();
                    Environment.Exit(0);
                };
                app.Run(win);
                return;
            }

            if (isSilent)
            {
                if (isUninstall)
                {
                    RunSilentUninstallation(installDir, appDataDir);
                }
                else
                {
                    RunSilentInstallation(installDir);
                }
            }
            else
            {
                var app = new Application();
                var win = new InstallerWindow(installDir, appDataDir, isUninstall);
                app.Run(win);
            }
        }

        public static void ExtractSuiteFiles(string targetDir)
        {
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

            string driverExe = Path.Combine(targetDir, "OpenDualDriver.exe");
            string hubExe = Path.Combine(targetDir, "OpenDualHub.exe");
            string icoDst = Path.Combine(assetsDir, "app.ico");
            string logoDst = Path.Combine(assetsDir, "logo.png");

            ExtractResource("Embedded.OpenDualDriver.exe", driverExe, Path.Combine(sourceDir, "OpenDualDriver.exe"));
            ExtractResource("Embedded.OpenDualHub.exe", hubExe, Path.Combine(sourceDir, "OpenDualHub.exe"));
            ExtractResource("Embedded.app.ico", icoDst, Path.Combine(sourceDir, "Assets", "app.ico"));
            ExtractResource("Embedded.logo.png", logoDst, Path.Combine(sourceDir, "Assets", "logo.png"));

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string uninstallerDst = Path.Combine(targetDir, "OpenDualUninstaller.exe");
            if (File.Exists(currentExe))
            {
                try { SafeCopyFile(currentExe, uninstallerDst); } catch { }
            }
        }

        public static void ConfigureRegistryAndShortcuts(string targetDir, bool autoStart, bool createShortcuts)
        {
            string driverExe = Path.Combine(targetDir, "OpenDualDriver.exe");
            string hubExe = Path.Combine(targetDir, "OpenDualHub.exe");

            long totalBytes = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f).Length).Sum();
            int estimatedSizeKb = (int)(totalBytes / 1024);

            RegisterInRegistry(targetDir, estimatedSizeKb);
            RegisterAppPaths(targetDir);

            if (autoStart)
            {
                RegisterAutoStart(driverExe);
            }

            if (createShortcuts)
            {
                CreateShortcuts(hubExe);
            }
        }

        public static void LaunchInstalledSuite(string targetDir)
        {
            string driverExe = Path.Combine(targetDir, "OpenDualDriver.exe");
            string hubExe = Path.Combine(targetDir, "OpenDualHub.exe");

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
        }

        private static void RunSilentInstallation(string targetDir)
        {
            try
            {
                StopProcesses();
                ExtractSuiteFiles(targetDir);
                EnsureViGEmBusInstalled();
                ConfigureRegistryAndShortcuts(targetDir, autoStart: true, createShortcuts: true);
                LaunchInstalledSuite(targetDir);
            }
            catch { }
        }

        private static void RunSilentUninstallation(string targetDir, string appDataDir)
        {
            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                bool isRunningFromTarget = !string.IsNullOrEmpty(currentExe) &&
                                          currentExe.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase);

                if (isRunningFromTarget)
                {
                    RelaunchUninstallerFromTemp(currentExe, targetDir, appDataDir, true);
                    return;
                }

                RunTempCleanup(targetDir, appDataDir);
            }
            catch { }
        }

        public static void RelaunchUninstallerFromTemp(string currentExe, string targetDir, string appDataDir, bool silent)
        {
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

            Environment.Exit(0);
        }

        private static void RunTempCleanup(string targetDir, string appDataDir)
        {
            Thread.Sleep(600);
            StopProcesses();
            RemoveShortcuts();
            UnregisterFromRegistry();
            DeleteDirectoriesCleanly(targetDir, appDataDir);
        }

        public static void DeleteDirectoriesCleanly(string targetDir, string appDataDir)
        {
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

            if (Directory.Exists(appDataDir))
            {
                try
                {
                    Directory.Delete(appDataDir, true);
                }
                catch { }
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

        public static void EnsureViGEmBusInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
                if (key != null) return;

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

        public static void StopProcesses()
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
            key.SetValue("Comments", "High-Performance Direct HID Controller Driver & Desktop Suite");
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

        public static void UnregisterFromRegistry()
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

        public static void CreateShortcuts(string targetExe)
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

        public static void RemoveShortcuts()
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
