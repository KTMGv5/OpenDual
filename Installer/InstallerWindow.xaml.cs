using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenDualSetup
{
    public partial class InstallerWindow : Window
    {
        private readonly string _defaultInstallDir;
        private readonly string _appDataDir;
        private readonly bool _startInUninstallMode;
        private bool _isCompleted = false;
        private bool _wasUninstallAction = false;

        public InstallerWindow(string defaultInstallDir, string appDataDir, bool uninstallMode)
        {
            InitializeComponent();
            _defaultInstallDir = defaultInstallDir;
            _appDataDir = appDataDir;
            _startInUninstallMode = uninstallMode;

            TxtInstallDir.Text = _defaultInstallDir;
            CheckExistingInstallation();

            if (_startInUninstallMode)
            {
                ConfigureForUninstallPrompt();
            }
        }

        private void CheckExistingInstallation()
        {
            string existingHub = Path.Combine(_defaultInstallDir, "OpenDualHub.exe");
            if (File.Exists(existingHub))
            {
                TxtDetectTitle.Text = "Existing OpenDual Installation Detected";
                TxtDetectSub.Text = "Click 'Update / Repair Suite' to upgrade to v1.7.0 while preserving your saved profiles.";
                BtnPrimaryAction.Content = "Update / Repair Suite →";
                BtnUninstallExisting.Visibility = Visibility.Visible;
            }
        }

        private void ConfigureForUninstallPrompt()
        {
            TxtWindowTitle.Text = "OPENDUAL SUITE UNINSTALLER";
            TxtDetectTitle.Text = "Uninstall OpenDual Controller Suite";
            TxtDetectTitle.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x4B));
            TxtDetectSub.Text = "This will stop background services, remove installed files, and unregister shortcuts.";
            BannerDetected.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x4B));
            BtnPrimaryAction.Content = "🗑 Confirm Uninstall";
            BtnPrimaryAction.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x4B));
            BtnPrimaryAction.Foreground = Brushes.White;
            BtnPrimaryAction.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x4B));
            BtnUninstallExisting.Visibility = Visibility.Collapsed;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void BtnResetDir_Click(object sender, RoutedEventArgs e)
        {
            TxtInstallDir.Text = _defaultInstallDir;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteUninstallWorkflowAsync();
        }

        private async void BtnPrimaryAction_Click(object sender, RoutedEventArgs e)
        {
            if (_isCompleted)
            {
                if (!_wasUninstallAction && ToggleLaunchAfter.IsChecked == true)
                {
                    Program.LaunchInstalledSuite(TxtInstallDir.Text.Trim());
                }
                Close();
                return;
            }

            if (_startInUninstallMode)
            {
                await ExecuteUninstallWorkflowAsync();
            }
            else
            {
                await ExecuteInstallWorkflowAsync();
            }
        }

        private void AppendLog(string message, bool isSuccess = true)
        {
            Dispatcher.Invoke(() =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock
                {
                    Text = isSuccess ? "✓" : "•",
                    Foreground = isSuccess
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))
                        : new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xEC, 0xFF)),
                    FontSize = 11.5
                });
                PanelStepLogs.Children.Add(row);
                LogScroll.ScrollToEnd();
            });
        }

        private void SetProgress(int pct, string stepText)
        {
            Dispatcher.Invoke(() =>
            {
                BarInstallProgress.Value = pct;
                TxtProgressPct.Text = $"{pct}%";
                TxtProgressStep.Text = stepText;
            });
        }

        private async Task ExecuteInstallWorkflowAsync()
        {
            string targetDir = TxtInstallDir.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
                targetDir = _defaultInstallDir;

            bool autoStart = ToggleAutoStart.IsChecked == true;
            bool checkVigem = ToggleViGEm.IsChecked == true;
            bool createShortcuts = ToggleShortcuts.IsChecked == true;

            PageConfigure.Visibility = Visibility.Collapsed;
            PageProgress.Visibility = Visibility.Visible;
            BtnPrimaryAction.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            BtnUninstallExisting.Visibility = Visibility.Collapsed;

            try
            {
                SetProgress(10, "Stopping running driver and desktop hub instances...");
                AppendLog("Stopping active OpenDual processes...", false);
                await Task.Run(() => Program.StopProcesses());
                AppendLog("Stopped active processes cleanly.");

                SetProgress(30, "Creating installation directories & extracting signed binaries...");
                await Task.Run(() => Program.ExtractSuiteFiles(targetDir));
                AppendLog("Extracted OpenDualDriver.exe (1000Hz C++20 Direct HID Engine).");
                AppendLog("Extracted OpenDualHub.exe (Cross-Media Bar Desktop Hub).");

                if (checkVigem)
                {
                    SetProgress(55, "Verifying ViGEmBus Virtual Gamepad Bus driver...");
                    await Task.Run(() => Program.EnsureViGEmBusInstalled());
                    AppendLog("Verified ViGEmBus virtual gamepad bus driver.");
                }

                SetProgress(80, "Registering Windows Installed Apps & Startup Services...");
                await Task.Run(() => Program.ConfigureRegistryAndShortcuts(targetDir, autoStart, createShortcuts));
                AppendLog("Registered OpenDual Suite in Windows Installed Apps.");
                if (autoStart) AppendLog("Configured 1000Hz Direct HID Driver auto-start on login.");
                if (createShortcuts) AppendLog("Created Desktop and Start Menu shortcuts.");

                SetProgress(100, "Installation complete!");
                await Task.Delay(350);

                _isCompleted = true;
                _wasUninstallAction = false;
                PageProgress.Visibility = Visibility.Collapsed;
                PageFinished.Visibility = Visibility.Visible;

                TxtFinishTitle.Text = "OpenDual v1.7.0 Installed Successfully!";
                TxtFinishSub.Text = $"Installed to: {targetDir}";
                TxtFinishSummary.Text =
                    "✓ 1000Hz C++20 Direct HID Kernel Driver (OpenDualDriver.exe)\n" +
                    "✓ Cross-Media Bar Desktop Hub & Initial Setup Wizard (OpenDualHub.exe)\n" +
                    "✓ Registered in Windows Installed Apps & Startup Services";

                BtnPrimaryAction.IsEnabled = true;
                BtnPrimaryAction.Content = ToggleLaunchAfter.IsChecked == true ? "Launch OpenDual Hub ✓" : "Finish ✓";
            }
            catch (Exception ex)
            {
                AppendLog($"Error during setup: {ex.Message}", false);
                BtnCancel.IsEnabled = true;
            }
        }

        private async Task ExecuteUninstallWorkflowAsync()
        {
            string targetDir = TxtInstallDir.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
                targetDir = _defaultInstallDir;

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            bool isRunningFromTarget = !string.IsNullOrEmpty(currentExe) &&
                                       currentExe.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase);

            if (isRunningFromTarget)
            {
                Program.RelaunchUninstallerFromTemp(currentExe, targetDir, _appDataDir, false);
                return;
            }

            PageConfigure.Visibility = Visibility.Collapsed;
            PageProgress.Visibility = Visibility.Visible;
            TxtProgressHeadline.Text = "Uninstalling OpenDual Controller Suite...";
            BtnPrimaryAction.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            BtnUninstallExisting.Visibility = Visibility.Collapsed;

            try
            {
                SetProgress(25, "Terminating running driver and hub processes...");
                await Task.Run(() => Program.StopProcesses());
                AppendLog("Stopped OpenDualDriver and OpenDualHub processes.");

                SetProgress(60, "Removing shortcuts and Windows Registry keys...");
                await Task.Run(() =>
                {
                    Program.RemoveShortcuts();
                    Program.UnregisterFromRegistry();
                });
                AppendLog("Removed Desktop/Start Menu shortcuts and Registry keys.");

                SetProgress(90, "Deleting installation directory...");
                await Task.Run(() => Program.DeleteDirectoriesCleanly(targetDir, _appDataDir));
                AppendLog("Deleted installation files cleanly.");

                SetProgress(100, "Uninstallation complete!");
                await Task.Delay(300);

                _isCompleted = true;
                _wasUninstallAction = true;
                PageProgress.Visibility = Visibility.Collapsed;
                PageFinished.Visibility = Visibility.Visible;

                TxtFinishTitle.Text = "OpenDual Uninstalled Cleanly";
                TxtFinishSub.Text = "All services, shortcuts, and binaries have been removed from your PC.";
                TxtFinishSummary.Text =
                    "✓ Background 1000Hz HID driver stopped & unregistered\n" +
                    "✓ Desktop & Start Menu shortcuts removed\n" +
                    "✓ Application directory cleaned";

                BtnPrimaryAction.IsEnabled = true;
                BtnPrimaryAction.Content = "Close Uninstaller ✓";
                BtnPrimaryAction.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
                BtnPrimaryAction.Foreground = new SolidColorBrush(Color.FromRgb(0x08, 0x0B, 0x13));
            }
            catch (Exception ex)
            {
                AppendLog($"Uninstall error: {ex.Message}", false);
                BtnCancel.IsEnabled = true;
            }
        }

        public void CaptureToPng(string path)
        {
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            int width = Math.Max(1, (int)(ActualWidth * dpi.DpiScaleX));
            int height = Math.Max(1, (int)(ActualHeight * dpi.DpiScaleY));
            var rtb = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(this);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            enc.Save(fs);
        }
    }
}
