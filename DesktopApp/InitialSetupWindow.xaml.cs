using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenDualHub
{
    public partial class InitialSetupWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly IpcClient _ipc;
        private int _currentStep = 1;
        private bool _lightweightMode = false;
        private string _selectedPlaystyle = "Balanced";

        private static readonly SolidColorBrush s_selBg = new(Color.FromRgb(0x13, 0x22, 0x38));
        private static readonly SolidColorBrush s_unselBg = new(Color.FromRgb(0x10, 0x17, 0x26));
        private static readonly SolidColorBrush s_cyanBrush = new(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_borderBrush = new(Color.FromRgb(0x1E, 0x2B, 0x45));
        private static readonly SolidColorBrush s_dotInactive = new(Color.FromRgb(0x25, 0x35, 0x52));
        private static readonly SolidColorBrush s_mutedBrush = new(Color.FromRgb(0x8A, 0x9B, 0xB8));

        public InitialSetupWindow(MainWindow mainWindow, IpcClient ipc)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _ipc = ipc;

            LoadExistingPreferences();
            UpdateStepVisuals();
        }

        private void LoadExistingPreferences()
        {
            var settings = AppSettingsManager.Current;
            _lightweightMode = settings.LightweightBackground;
            UpdatePerfModeCards();

            ToggleSetupTray.IsChecked = settings.MinimizeToTray;
            ToggleSetupNerdStats.IsChecked = settings.ShowNerdStats;
            ToggleSetupMilitary.IsChecked = settings.ClockUseMilitaryTime;

            string fmt = settings.ClockDateFormat ?? "ddd dd/MM";
            for (int i = 0; i < CmbSetupDateFormat.Items.Count; i++)
            {
                if (CmbSetupDateFormat.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), fmt, StringComparison.OrdinalIgnoreCase))
                {
                    CmbSetupDateFormat.SelectedIndex = i;
                    break;
                }
            }

            var cfg = _mainWindow.CurrentConfig;
            ToggleSetupBatteryGuard.IsChecked = cfg.ThermalGuard;
            ToggleSetupHall.IsChecked = true;
            ToggleSetupAudioHaptics.IsChecked = cfg.Haptic;
            ToggleSetupTouchMouse.IsChecked = cfg.Mouse;

            bool driverUp = DriverManager.IsDriverEngineRunning();
            if (driverUp)
            {
                DotDriverStatus.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                TxtDriverHeadline.Text = "1000Hz Direct HID Kernel Engine Active";
                TxtDriverSub.Text = "Overlapped Win32 I/O pipe connected • Sub-millisecond input & output reports ready";
                TxtPollingPill.Text = "1000 Hz • 1.0 ms";
            }
            else
            {
                DotDriverStatus.Fill = s_cyanBrush;
                TxtDriverHeadline.Text = "Direct HID Kernel Engine Standby";
                TxtDriverSub.Text = "Background service will auto-connect as soon as your controller is plugged in or paired";
                TxtPollingPill.Text = "Ready";
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CardPerfFull_Click(object sender, MouseButtonEventArgs e)
        {
            _lightweightMode = false;
            UpdatePerfModeCards();
        }

        private void CardPerfLight_Click(object sender, MouseButtonEventArgs e)
        {
            _lightweightMode = true;
            UpdatePerfModeCards();
        }

        private void UpdatePerfModeCards()
        {
            if (!_lightweightMode)
            {
                CardPerfFull.Background = s_selBg;
                CardPerfFull.BorderBrush = s_cyanBrush;
                BadgePerfFull.Text = "SELECTED";
                BadgePerfFull.Foreground = s_cyanBrush;

                CardPerfLight.Background = s_unselBg;
                CardPerfLight.BorderBrush = s_borderBrush;
                BadgePerfLight.Text = "LOW GPU";
                BadgePerfLight.Foreground = s_mutedBrush;
            }
            else
            {
                CardPerfLight.Background = s_selBg;
                CardPerfLight.BorderBrush = s_cyanBrush;
                BadgePerfLight.Text = "SELECTED";
                BadgePerfLight.Foreground = s_cyanBrush;

                CardPerfFull.Background = s_unselBg;
                CardPerfFull.BorderBrush = s_borderBrush;
                BadgePerfFull.Text = "60 FPS";
                BadgePerfFull.Foreground = s_mutedBrush;
            }
        }

        private void CardPlaystyle_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border bd && bd.Tag is string tag)
            {
                _selectedPlaystyle = tag;
                CardPlayBalanced.Background = tag == "Balanced" ? s_selBg : s_unselBg;
                CardPlayBalanced.BorderBrush = tag == "Balanced" ? s_cyanBrush : s_borderBrush;

                CardPlayEsports.Background = tag == "Esports" ? s_selBg : s_unselBg;
                CardPlayEsports.BorderBrush = tag == "Esports" ? s_cyanBrush : s_borderBrush;

                CardPlayFps.Background = tag == "FPS" ? s_selBg : s_unselBg;
                CardPlayFps.BorderBrush = tag == "FPS" ? s_cyanBrush : s_borderBrush;

                CardPlayRacing.Background = tag == "Racing" ? s_selBg : s_unselBg;
                CardPlayRacing.BorderBrush = tag == "Racing" ? s_cyanBrush : s_borderBrush;
            }
        }

        private void BtnPrevStep_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepVisuals();
            }
        }

        private void BtnNextStep_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 3)
            {
                _currentStep++;
                UpdateStepVisuals();
            }
            else
            {
                ApplyAndFinishSetup();
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            AppSettingsManager.Current.HasCompletedInitialSetup = true;
            AppSettingsManager.Save();
            Close();
        }

        public void SetStep(int step)
        {
            _currentStep = Math.Clamp(step, 1, 3);
            UpdateStepVisuals();
        }

        private void UpdateStepVisuals()
        {
            PanelStep1.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep2.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep3.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

            TxtStepBadge.Text = $"STEP {_currentStep} OF 3";
            DotStep1.Fill = _currentStep >= 1 ? s_cyanBrush : s_dotInactive;
            DotStep2.Fill = _currentStep >= 2 ? s_cyanBrush : s_dotInactive;
            DotStep3.Fill = _currentStep >= 3 ? s_cyanBrush : s_dotInactive;

            BtnPrevStep.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
            BtnNextStep.Content = _currentStep < 3 ? "Next Step →" : "Finish & Launch Hub ✓";
        }

        private void ApplyAndFinishSetup()
        {
            var settings = AppSettingsManager.Current;
            settings.HasCompletedInitialSetup = true;
            settings.LightweightBackground = _lightweightMode;
            settings.MinimizeToTray = ToggleSetupTray.IsChecked == true;
            settings.ShowNerdStats = ToggleSetupNerdStats.IsChecked == true;
            settings.ClockUseMilitaryTime = ToggleSetupMilitary.IsChecked == true;

            if (CmbSetupDateFormat.SelectedItem is ComboBoxItem selDate && selDate.Tag is string dateTag)
            {
                settings.ClockDateFormat = dateTag;
            }

            // Apply chosen Playstyle preset first
            switch (_selectedPlaystyle)
            {
                case "Esports":
                    _mainWindow.ApplyProfileByName("Esports Ultra 1000Hz");
                    break;
                case "FPS":
                    _mainWindow.ApplyProfileByName("FPS Gyro Specialist");
                    break;
                case "Racing":
                    _mainWindow.ApplyProfileByName("Racing Force Feedback");
                    break;
                default:
                    _mainWindow.ApplyProfileByName("Standard Balanced");
                    break;
            }

            var cfg = _mainWindow.CurrentConfig;
            bool batteryGuard = ToggleSetupBatteryGuard.IsChecked == true;
            cfg.ThermalGuard = batteryGuard;
            cfg.Haptic = ToggleSetupAudioHaptics.IsChecked == true;
            cfg.Mouse = ToggleSetupTouchMouse.IsChecked == true;
            cfg.HapticDetent = ToggleSetupTouchMouse.IsChecked == true;

            if (_selectedPlaystyle == "Esports")
            {
                cfg.DualStageTriggers = true;
                cfg.HairTriggerRelease = true;
                cfg.SprintShelfL3 = true;
            }
            else if (_selectedPlaystyle == "FPS")
            {
                cfg.Gyro = true;
                cfg.GyroRatcheting = true;
                cfg.RecoilAssist = true;
            }

            settings.SavedHardwareConfig = cfg;
            AppSettingsManager.Save();

            // Apply immediately to MainWindow & IPC
            _mainWindow.ShowNerdStats = settings.ShowNerdStats;
            _mainWindow.ClockUseMilitaryTime = settings.ClockUseMilitaryTime;
            _mainWindow.ClockDateFormat = settings.ClockDateFormat;
            _mainWindow.SetLightweightBackground(settings.LightweightBackground);
            _mainWindow.UpdateClockDisplay();
            _mainWindow.XmbView?.SetNerdStatsVisible(settings.ShowNerdStats);
            _mainWindow.XmbView?.CategoryInspector?.SyncSettingsUi(settings);
            _mainWindow.XmbView?.CategoryInspector?.SyncFromConfig(cfg);
            _mainWindow.XmbView?.NovelControl?.SyncFromConfig(cfg);
            _ipc.SendConfig(cfg);

            Close();
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
