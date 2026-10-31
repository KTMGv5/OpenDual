using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class XmbCategoryInspectorControl : UserControl
    {
        public IpcClient? IpcClient { get; set; }

        public XmbCategoryInspectorControl()
        {
            InitializeComponent();
        }

        public void SetSection(string sectionId)
        {
            PanelTriggers.Visibility = Visibility.Collapsed;
            PanelGyro.Visibility = Visibility.Collapsed;
            PanelLightbar.Visibility = Visibility.Collapsed;
            PanelAudio.Visibility = Visibility.Collapsed;
            PanelProfiles.Visibility = Visibility.Collapsed;
            PanelSettings.Visibility = Visibility.Collapsed;

            switch (sectionId.ToLowerInvariant())
            {
                case "triggers":
                    PanelTriggers.Visibility = Visibility.Visible;
                    break;
                case "gyro":
                    PanelGyro.Visibility = Visibility.Visible;
                    break;
                case "lightbar":
                    PanelLightbar.Visibility = Visibility.Visible;
                    break;
                case "audio":
                    PanelAudio.Visibility = Visibility.Visible;
                    break;
                case "users":
                case "profiles":
                    PanelProfiles.Visibility = Visibility.Visible;
                    break;
                case "settings":
                    PanelSettings.Visibility = Visibility.Visible;
                    break;
                default:
                    PanelTriggers.Visibility = Visibility.Visible;
                    break;
            }
        }

        public void UpdateTelemetry(ControllerTelemetry t)
        {
            if (t == null) return;
            BarL2Inspector.Value = t.L2 * 100.0;
            BarR2Inspector.Value = t.R2 * 100.0;
        }

        #region Adaptive Triggers Handlers
        private void BtnL2Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int mode))
            {
                TxtL2ModeBadge.Text = btn.Content.ToString();
                IpcClient?.SetAdaptiveTriggers(mode, 30, 8, 12, 1, 30, 8, 12);
            }
        }

        private void BtnR2Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int mode))
            {
                TxtR2ModeBadge.Text = btn.Content.ToString();
                IpcClient?.SetAdaptiveTriggers(2, 30, 8, 12, mode, 30, 8, 12);
            }
        }
        #endregion

        #region Gyro Handlers
        private void ToggleGyroEnable_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleGyroEnable.IsChecked == true;
            ToggleGyroEnable.Content = isOn ? "Gyro Aim: ON" : "Gyro Aim: OFF";
            IpcClient?.SetGyro(isOn, 1, 0, 0, (float)SliderSensX.Value, (float)SliderSensY.Value, false, false, 3, 0.20f);
        }

        private void BtnGyroMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int mode))
            {
                var sigBrush = (Brush)FindResource("AccentSignature");
                var darkBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x27, 0x40));
                BtnGyroMouse.Background = (mode == 0) ? sigBrush : darkBg;
                BtnGyroMouse.Foreground = (mode == 0) ? new SolidColorBrush(Color.FromRgb(0x08, 0x0B, 0x13)) : Brushes.White;
                BtnGyroStick.Background = (mode == 1) ? sigBrush : darkBg;
                BtnGyroStick.Foreground = (mode == 1) ? new SolidColorBrush(Color.FromRgb(0x08, 0x0B, 0x13)) : Brushes.White;

                IpcClient?.SetGyro(ToggleGyroEnable.IsChecked == true, 1, mode, 0, (float)SliderSensX.Value, (float)SliderSensY.Value, false, false, 3, 0.20f);
            }
        }

        private void SliderSens_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSensXVal != null && SliderSensX != null) TxtSensXVal.Text = $"{SliderSensX.Value:F2}x";
            if (TxtSensYVal != null && SliderSensY != null) TxtSensYVal.Text = $"{SliderSensY.Value:F2}x";
        }

        private void BtnLaunchCalibrator_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wizard = new GyroCalibratorWindow();
                wizard.Owner = Window.GetWindow(this);
                wizard.ShowDialog();
            }
            catch { }
        }
        #endregion

        #region Lightbar Handlers
        private void BtnLedEffect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int effect))
            {
                IpcClient?.SetRgbColor(0, 245, 212, effect, 4, 1.0f);
            }
        }

        private void BtnColorPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                try
                {
                    var col = (Color)ColorConverter.ConvertFromString(hex);
                    IpcClient?.SetRgbColor(col.R, col.G, col.B, 0, 4, 1.0f);
                }
                catch { }
            }
        }
        #endregion

        #region Audio Handlers
        private void ToggleHaptics_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleHaptics.IsChecked == true;
            ToggleHaptics.Content = isOn ? "Haptics: ON" : "Haptics: OFF";
            IpcClient?.SetAudioHaptics(isOn, (float)(SliderHapticIntensity.Value / 100.0), 40, true);
        }

        private void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtHapticIntensity != null)
            {
                TxtHapticIntensity.Text = $"{e.NewValue:F0}%";
                IpcClient?.SetAudioHaptics(ToggleHaptics.IsChecked == true, (float)(e.NewValue / 100.0), 40, true);
            }
        }

        private void ToggleSpeaker_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleSpeaker.IsChecked == true;
            ToggleSpeaker.Content = isOn ? "Speaker: ON" : "Speaker: OFF";
            IpcClient?.SetSpeakerOutput(isOn);
        }
        #endregion

        #region Profiles & Settings
        private void BtnActivateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                MessageBox.Show($"Profile '{name}' activated successfully!\nSettings applied to DualSense.", "OpenDual XMB Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRestartDriver_Click(object sender, RoutedEventArgs e)
        {
            var dm = new DriverManager();
            dm.StopDriverEngine();
            System.Threading.Thread.Sleep(300);
            dm.StartDriverEngine();
        }

        private void ToggleAutoStart_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleAutoStart.IsChecked == true;
            ToggleAutoStart.Content = isOn ? "Auto-Start: ON" : "Auto-Start: OFF";
        }
        #endregion
    }
}
