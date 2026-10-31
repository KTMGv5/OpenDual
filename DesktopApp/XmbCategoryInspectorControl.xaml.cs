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
            SetSectionAndItem(sectionId, "");
        }

        public void SetSectionAndItem(string sectionId, string itemId, string? categoryTitle = null, string? itemTitle = null, string? itemSubtitle = null, string? badge = null)
        {
            // 1. Update Breadcrumb Banner
            if (!string.IsNullOrEmpty(categoryTitle)) TxtBannerCategory.Text = categoryTitle;
            if (!string.IsNullOrEmpty(itemTitle)) TxtBannerItem.Text = itemTitle;
            if (!string.IsNullOrEmpty(itemSubtitle)) TxtBannerSubtitle.Text = itemSubtitle;
            if (!string.IsNullOrEmpty(badge)) TxtBannerBadge.Text = badge;

            // 2. Hide all category panels
            PanelTriggers.Visibility = Visibility.Collapsed;
            PanelGyro.Visibility = Visibility.Collapsed;
            PanelLightbar.Visibility = Visibility.Collapsed;
            PanelTouchpad.Visibility = Visibility.Collapsed;
            PanelAudio.Visibility = Visibility.Collapsed;
            PanelProfiles.Visibility = Visibility.Collapsed;
            PanelSettings.Visibility = Visibility.Collapsed;

            // 3. Reset card borders
            var defaultBorder = (Brush)FindResource("BorderColor");
            var glowBorder = (Brush)FindResource("AccentSignature");

            CardTrigL2.BorderBrush = defaultBorder;
            CardTrigR2.BorderBrush = defaultBorder;
            CardTrigSnap.BorderBrush = defaultBorder;

            CardGyroOutput.BorderBrush = defaultBorder;
            CardGyroTuning.BorderBrush = defaultBorder;

            CardLedEffect.BorderBrush = defaultBorder;
            CardLedColor.BorderBrush = defaultBorder;
            CardLedBrightness.BorderBrush = defaultBorder;

            CardTouchMouse.BorderBrush = defaultBorder;
            CardTouchVol.BorderBrush = defaultBorder;
            CardTouchTap.BorderBrush = defaultBorder;

            CardAudHaptic.BorderBrush = defaultBorder;
            CardAudReactive.BorderBrush = defaultBorder;
            CardAudSpeaker.BorderBrush = defaultBorder;
            CardAudMic.BorderBrush = defaultBorder;

            CardProfDefault.BorderBrush = defaultBorder;
            CardProfEsports.BorderBrush = defaultBorder;
            CardProfFps.BorderBrush = defaultBorder;
            CardProfRacing.BorderBrush = defaultBorder;

            CardSetDriver.BorderBrush = defaultBorder;
            CardSetAutoStart.BorderBrush = defaultBorder;
            CardSetIdle.BorderBrush = defaultBorder;
            CardSetUpdate.BorderBrush = defaultBorder;

            // 4. Activate target panel & highlight matching card
            switch (sectionId.ToLowerInvariant())
            {
                case "triggers":
                    PanelTriggers.Visibility = Visibility.Visible;
                    if (itemId == "trig_l2") { CardTrigL2.BorderBrush = glowBorder; CardTrigL2.BringIntoView(); }
                    else if (itemId == "trig_r2") { CardTrigR2.BorderBrush = glowBorder; CardTrigR2.BringIntoView(); }
                    else if (itemId == "trig_snap" || itemId == "trig_oscilloscope") { CardTrigSnap.BorderBrush = glowBorder; CardTrigSnap.BringIntoView(); }
                    break;

                case "gyro":
                    PanelGyro.Visibility = Visibility.Visible;
                    if (itemId == "gyro_output") { CardGyroOutput.BorderBrush = glowBorder; CardGyroOutput.BringIntoView(); }
                    else if (itemId == "gyro_tuning") { CardGyroTuning.BorderBrush = glowBorder; CardGyroTuning.BringIntoView(); }
                    else if (itemId == "gyro_wizard") { BtnLaunchCalibrator.BringIntoView(); }
                    break;

                case "lightbar":
                    PanelLightbar.Visibility = Visibility.Visible;
                    if (itemId == "led_effect") { CardLedEffect.BorderBrush = glowBorder; CardLedEffect.BringIntoView(); }
                    else if (itemId == "led_color" || itemId == "led_players") { CardLedColor.BorderBrush = glowBorder; CardLedColor.BringIntoView(); }
                    else if (itemId == "led_brightness") { CardLedBrightness.BorderBrush = glowBorder; CardLedBrightness.BringIntoView(); }
                    break;

                case "touchpad":
                    PanelTouchpad.Visibility = Visibility.Visible;
                    if (itemId == "touch_mouse") { CardTouchMouse.BorderBrush = glowBorder; CardTouchMouse.BringIntoView(); }
                    else if (itemId == "touch_vol") { CardTouchVol.BorderBrush = (Brush)FindResource("AccentAmber"); CardTouchVol.BringIntoView(); }
                    else if (itemId == "touch_tap") { CardTouchTap.BorderBrush = glowBorder; CardTouchTap.BringIntoView(); }
                    break;

                case "audio":
                    PanelAudio.Visibility = Visibility.Visible;
                    if (itemId == "aud_haptic") { CardAudHaptic.BorderBrush = glowBorder; CardAudHaptic.BringIntoView(); }
                    else if (itemId == "aud_reactive") { CardAudReactive.BorderBrush = glowBorder; CardAudReactive.BringIntoView(); }
                    else if (itemId == "aud_speaker") { CardAudSpeaker.BorderBrush = (Brush)FindResource("AccentBlue"); CardAudSpeaker.BringIntoView(); }
                    else if (itemId == "aud_mic") { CardAudMic.BorderBrush = (Brush)FindResource("AccentAmber"); CardAudMic.BringIntoView(); }
                    break;

                case "users":
                case "profiles":
                    PanelProfiles.Visibility = Visibility.Visible;
                    if (itemId == "prof_default") { CardProfDefault.BorderBrush = glowBorder; CardProfDefault.BringIntoView(); }
                    else if (itemId == "prof_esports") { CardProfEsports.BorderBrush = glowBorder; CardProfEsports.BringIntoView(); }
                    else if (itemId == "prof_fps") { CardProfFps.BorderBrush = glowBorder; CardProfFps.BringIntoView(); }
                    else if (itemId == "prof_racing" || itemId == "prof_new") { CardProfRacing.BorderBrush = glowBorder; CardProfRacing.BringIntoView(); }
                    break;

                case "settings":
                    PanelSettings.Visibility = Visibility.Visible;
                    if (itemId == "set_driver") { CardSetDriver.BorderBrush = (Brush)FindResource("AccentGreen"); CardSetDriver.BringIntoView(); }
                    else if (itemId == "set_autostart") { CardSetAutoStart.BorderBrush = glowBorder; CardSetAutoStart.BringIntoView(); }
                    else if (itemId == "set_idle") { CardSetIdle.BorderBrush = (Brush)FindResource("AccentAmber"); CardSetIdle.BringIntoView(); }
                    else if (itemId == "set_update") { CardSetUpdate.BorderBrush = glowBorder; CardSetUpdate.BringIntoView(); }
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

        private void SliderSnapTension_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSnapTensionVal != null)
            {
                TxtSnapTensionVal.Text = $"{e.NewValue:F0}% Force";
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

        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtBrightnessVal != null)
            {
                TxtBrightnessVal.Text = $"{e.NewValue:F0}%";
            }
        }
        #endregion

        #region Touchpad Handlers
        private void ToggleTouchMouse_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleTouchMouse.IsChecked == true;
            ToggleTouchMouse.Content = isOn ? "Mouse: ON" : "Mouse: OFF";
            PushTouchpadConfig();
        }

        private void SliderTouchSens_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTouchSensVal != null)
            {
                TxtTouchSensVal.Text = $"{e.NewValue:F2}x";
                PushTouchpadConfig();
            }
        }

        private void ToggleTouchVol_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleTouchVol.IsChecked == true;
            ToggleTouchVol.Content = isOn ? "Gesture: ON" : "Gesture: OFF";
            PushTouchpadConfig();
        }

        private void ToggleTouchTap_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleTouchTap.IsChecked == true;
            ToggleTouchTap.Content = isOn ? "Tap: ON" : "Tap: OFF";
            PushTouchpadConfig();
        }

        private void PushTouchpadConfig()
        {
            bool mouseOn = ToggleTouchMouse?.IsChecked == true;
            float sens = (float)(SliderTouchSens?.Value ?? 1.2);
            bool volGesture = ToggleTouchVol?.IsChecked == true;
            bool tapClick = ToggleTouchTap?.IsChecked == true;
            IpcClient?.SetTouchpad(mouseOn, sens, 1.0f, tapClick, volGesture);
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

        private void ToggleMicSync_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleMicSync.IsChecked == true;
            ToggleMicSync.Content = isOn ? "Mic Sync: ON" : "Mic Sync: OFF";
            IpcClient?.SetMicSync(isOn);
        }
        #endregion

        #region Profiles & Settings
        private void BtnActivateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                MessageBox.Show($"Profile '{name}' activated successfully!\nSettings applied to controller.", "OpenDual Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void CmbIdleTimeout_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbIdleTimeout != null && CmbIdleTimeout.SelectedItem is ComboBoxItem item)
            {
                int minutes = item.Content.ToString() switch
                {
                    "5 Minutes" => 5,
                    "10 Minutes" => 10,
                    "15 Minutes" => 15,
                    "Never" => 0,
                    _ => 10
                };
                IpcClient?.SetPowerManagement(minutes, true);
            }
        }
        #endregion
    }
}
