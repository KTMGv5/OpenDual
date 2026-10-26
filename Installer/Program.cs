using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace OpenDualSetup
{
    public class Program
    {
        private const string AppName = "OpenDual";
        private const string AppFullName = "OpenDual PlayStation Controller Suite";
        private const string AppVersion = "1.6.0";
        private const string AppPublisher = "OpenDual Team";

        [STAThread]
        public static void Main(string[] args)
        {
            bool isUninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)) ||
                               Process.GetCurrentProcess().ProcessName.Contains("uninstall", StringComparison.OrdinalIgnoreCase);
            bool isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("-q", StringComparison.OrdinalIgnoreCase));

            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenDual");

            if (isUninstall)
            {
                RunUninstallation(installDir, isSilent);
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
                        $"Welcome to {AppFullName} Setup!\n\nThis will install OpenDual into:\n{targetDir}\n\n• High-performance 1000Hz direct HID driver\n• Precision touchpad mouse & audio controls\n• Full system theme & accent color integration\n• Registered in Windows Installed Apps\n\nDo you want to continue?",
                        "OpenDual Setup",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (prompt != MessageBoxResult.Yes) return;
                }

                // 1. Kill any existing instances
                StopProcesses();

                // 2. Prepare target directory
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string sourceDir = AppDomain.CurrentDomain.BaseDirectory;

                // 3. Copy files to target
                string[] filesToCopy = new string[]
                {
                    "OpenDualHub.exe",
                    "OpenDualHub.dll",
                    "OpenDualHub.deps.json",
                    "OpenDualHub.runtimeconfig.json",
                    "OpenDualHub.pdb",
                    "OpenDualDriver.exe"
                };

                foreach (var file in filesToCopy)
                {
                    string src = Path.Combine(sourceDir, file);
                    SafeCopyFile(src, Path.Combine(targetDir, file));
                }

                // Copy Assets directory if present
                string srcAssets = Path.Combine(sourceDir, "Assets");
                string dstAssets = Path.Combine(targetDir, "Assets");
                if (Directory.Exists(srcAssets))
                {
                    if (!Directory.Exists(dstAssets)) Directory.CreateDirectory(dstAssets);
                    foreach (var f in Directory.GetFiles(srcAssets))
                    {
                        SafeCopyFile(f, Path.Combine(dstAssets, Path.GetFileName(f)));
                    }
                }

                // Copy self as OpenDualUninstaller.exe into target
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string uninstallerDst = Path.Combine(targetDir, "OpenDualUninstaller.exe");
                if (File.Exists(currentExe))
                {
                    try { SafeCopyFile(currentExe, uninstallerDst); } catch { }
                }

                // Create standalone uninstall.bat script as backup uninstaller
                CreateUninstallScript(targetDir);

                // 4. Calculate total size in KB
                long totalBytes = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f).Length).Sum();
                int estimatedSizeKb = (int)(totalBytes / 1024);

                // 5. Register in Windows Registry (HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenDual)
                RegisterInRegistry(targetDir, estimatedSizeKb);

                // 6. Register App Paths for Win+R / cmd "opendual" launch
                RegisterAppPaths(targetDir);

                // 7. Create Shortcuts (Start Menu & Desktop)
                string mainExe = Path.Combine(targetDir, "OpenDualHub.exe");
                string driverExe = Path.Combine(targetDir, "OpenDualDriver.exe");
                CreateShortcuts(mainExe);

                // 8. Relaunch OpenDual Driver and Hub
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

                if (File.Exists(mainExe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = mainExe,
                            WorkingDirectory = targetDir,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }

                if (!silent)
                {
                    MessageBox.Show(
                        $"{AppFullName} has been installed and updated successfully!\n\nAll previous driver instances were cleanly replaced and the suite has been restarted.\nShortcuts created on Desktop and Start Menu.",
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

        private static void RunUninstallation(string targetDir, bool silent)
        {
            try
            {
                if (!silent)
                {
                    var confirm = MessageBox.Show(
                        $"Are you sure you want to completely uninstall {AppFullName} from your system?",
                        "OpenDual Uninstaller",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirm != MessageBoxResult.Yes) return;
                }

                // 1. Terminate running driver and hub
                StopProcesses();

                // 2. Remove Shortcuts
                RemoveShortcuts();

                // 3. Remove Registry entries
                UnregisterFromRegistry();

                // 4. Remove installation directory via detached self-deleting batch
                string tempBatch = Path.Combine(Path.GetTempPath(), "opendual_cleanup.bat");
                string cleanupScript =
                    "@echo off\r\n" +
                    "timeout /t 1 /nobreak >nul\r\n" +
                    $"rmdir /s /q \"{targetDir}\"\r\n" +
                    $"del /f /q \"{tempBatch}\"\r\n";
                File.WriteAllText(tempBatch, cleanupScript);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{tempBatch}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

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
                    MessageBox.Show($"Uninstallation failed: {ex.Message}", "OpenDual Uninstaller Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void StopProcesses()
        {
            string[] procNames = new string[] { "OpenDualDriver", "OpenDualHub" };
            
            // 1. Force terminate via Process API
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

            // 2. Force terminate via Windows taskkill command line
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

            System.Threading.Thread.Sleep(300);
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
                    System.Threading.Thread.Sleep(200 * attempt);
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
            key.SetValue("URLInfoAbout", "https://github.com/OpenDual/OpenDual");
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

        private static void CreateUninstallScript(string installDir)
        {
            string uninstallBat = Path.Combine(installDir, "uninstall.bat");
            string content =
                "@echo off\r\n" +
                "echo =========================================================\r\n" +
                "echo   OpenDual PlayStation Controller Suite - Uninstaller\r\n" +
                "echo =========================================================\r\n" +
                "if exist \"%~dp0OpenDualUninstaller.exe\" (\r\n" +
                "    start \"\" \"%~dp0OpenDualUninstaller.exe\"\r\n" +
                ") else (\r\n" +
                "    echo Stopping OpenDual services...\r\n" +
                "    taskkill /f /im OpenDualHub.exe >nul 2>&1\r\n" +
                "    taskkill /f /im OpenDualDriver.exe >nul 2>&1\r\n" +
                "    reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\OpenDual\" /f >nul 2>&1\r\n" +
                "    reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\OpenDualHub.exe\" /f >nul 2>&1\r\n" +
                "    reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\OpenDual.exe\" /f >nul 2>&1\r\n" +
                "    del /f /q \"%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\OpenDual.lnk\" >nul 2>&1\r\n" +
                "    del /f /q \"%USERPROFILE%\\Desktop\\OpenDual.lnk\" >nul 2>&1\r\n" +
                "    echo Uninstall complete.\r\n" +
                "    pause\r\n" +
                ")\r\n";
            File.WriteAllText(uninstallBat, content);
        }
    }
}
