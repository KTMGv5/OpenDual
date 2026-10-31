using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class XmbCategoryInspectorControl : UserControl
    {
        public MainWindow? MainWindow { get; set; }
        public IpcClient? IpcClient { get; set; }

        // Adaptive Triggers State
        private int _l2Mode = 2; // Rigid Section Snap
        private int _r2Mode = 1; // Game Specific SDK
        private int _snapTension = 85;
        private int _l2Start = 30, _l2Force = 8, _l2Freq = 12;
        private int _r2Start = 30, _r2Force = 8, _r2Freq = 12;

        // Gyro Aim State
        private bool _gyroEnabled = false;
        private bool _configInitialized = false;
        private int _gyroTriggerMode = 1; // 1 = Hold L2 (ADS)
        private int _gyroOutputMode = 0;  // 0 = Mouse
        private int _gyroSpace = 0;       // 0 = Player Space
        private float _gyroSensX = 1.2f;
        private float _gyroSensY = 1.2f;
        private bool _gyroInvertX = false;
        private bool _gyroInvertY = false;
        private int _gyroDeadzone = 3;
        private float _gyroSmooth = 0.20f;
        private int _gyroClickMode = 0; // 0 = Cross (A) Left / Square (X) Right, 1 = R2/L2, 2 = R1/L1, 3 = Disabled

        // Lightbar State
        private int _activeLedEffect = 2; // Rainbow Wave
        private byte _colorR = 0, _colorG = 245, _colorB = 212;
        private byte _brightness = 255;
        private byte _playerLeds = 4; // Player 1 (Center)

        // Touchpad & Gesture State
        private bool _mouseEnabled = true;
        private float _touchSens = 1.2f;
        private bool _volGesture = false; // Default OFF
        private bool _tapClick = true;
        private bool _discreteScroll = true;
        private readonly List<Point> _recentStroke = new();

        public XmbCategoryInspectorControl()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                DrawTriggerOscilloscope(0, 0);
                DrawGyroCrosshair(0, 0, 0);
                SyncStartupRegistryState();
            };
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
            var defaultBorder = GetBrush("BorderColor", Color.FromArgb(0x35, 0x29, 0x79, 0xFF));
            var glowBorder = GetBrush("AccentSignature", Color.FromRgb(0x00, 0xF5, 0xD4));

            CardTrigL2.BorderBrush = defaultBorder;
            CardTrigR2.BorderBrush = defaultBorder;
            CardTrigSnap.BorderBrush = defaultBorder;

            CardGyroEnable.BorderBrush = defaultBorder;
            CardGyroOutput.BorderBrush = defaultBorder;
            CardGyroTriggerMode.BorderBrush = defaultBorder;
            CardGyroClick.BorderBrush = defaultBorder;
            CardGyroTuning.BorderBrush = defaultBorder;

            CardLedEffect.BorderBrush = defaultBorder;
            CardLedColor.BorderBrush = defaultBorder;
            CardLedPlayers.BorderBrush = defaultBorder;
            CardLedBrightness.BorderBrush = defaultBorder;

            CardTouchMouse.BorderBrush = defaultBorder;
            CardTouchNotch.BorderBrush = defaultBorder;
            CardTouchVol.BorderBrush = defaultBorder;
            CardTouchTap.BorderBrush = defaultBorder;
            CardTouchGestures.BorderBrush = defaultBorder;

            CardAudHaptic.BorderBrush = defaultBorder;
            CardAudReactive.BorderBrush = defaultBorder;
            CardAudSpeaker.BorderBrush = defaultBorder;
            CardAudMic.BorderBrush = defaultBorder;
            CardAudVolumeHud.BorderBrush = defaultBorder;

            CardProfDefault.BorderBrush = defaultBorder;
            CardProfEsports.BorderBrush = defaultBorder;
            CardProfFps.BorderBrush = defaultBorder;
            CardProfRacing.BorderBrush = defaultBorder;

            CardSetDriver.BorderBrush = defaultBorder;
            CardSetAutoStart.BorderBrush = defaultBorder;
            CardSetIdle.BorderBrush = defaultBorder;
            if (CardSetNerdStats != null) CardSetNerdStats.BorderBrush = defaultBorder;
            if (CardSetClock != null) CardSetClock.BorderBrush = defaultBorder;
            if (CardSetBackgroundMode != null) CardSetBackgroundMode.BorderBrush = defaultBorder;
            CardSetUpdate.BorderBrush = defaultBorder;

            // 4. Activate target panel & highlight matching card
            switch (sectionId.ToLowerInvariant())
            {
                case "triggers":
                    PanelTriggers.Visibility = Visibility.Visible;
                    if (itemId == "trig_l2") { CardTrigL2.BorderBrush = glowBorder; CardTrigL2.BringIntoView(); }
                    else if (itemId == "trig_r2") { CardTrigR2.BorderBrush = glowBorder; CardTrigR2.BringIntoView(); }
                    else if (itemId == "trig_snap" || itemId == "trig_oscilloscope") { CardTrigSnap.BorderBrush = glowBorder; CardTrigSnap.BringIntoView(); }
                    else { CardTrigL2.BringIntoView(); }
                    break;

                case "gyro":
                    PanelGyro.Visibility = Visibility.Visible;
                    if (itemId == "gyro_enable") { CardGyroEnable.BorderBrush = glowBorder; CardGyroEnable.BringIntoView(); }
                    else if (itemId == "gyro_output") { CardGyroOutput.BorderBrush = glowBorder; CardGyroOutput.BringIntoView(); }
                    else if (itemId == "gyro_click") { CardGyroClick.BorderBrush = glowBorder; CardGyroClick.BringIntoView(); }
                    else if (itemId == "gyro_tuning") { CardGyroTuning.BorderBrush = glowBorder; CardGyroTuning.BringIntoView(); }
                    else if (itemId == "gyro_wizard") { BtnLaunchCalibrator.BringIntoView(); }
                    else { CardGyroEnable.BringIntoView(); }
                    break;

                case "lightbar":
                    PanelLightbar.Visibility = Visibility.Visible;
                    if (itemId == "led_effect") { CardLedEffect.BorderBrush = glowBorder; CardLedEffect.BringIntoView(); }
                    else if (itemId == "led_color") { CardLedColor.BorderBrush = glowBorder; CardLedColor.BringIntoView(); }
                    else if (itemId == "led_players") { CardLedPlayers.BorderBrush = glowBorder; CardLedPlayers.BringIntoView(); }
                    else if (itemId == "led_brightness") { CardLedBrightness.BorderBrush = glowBorder; CardLedBrightness.BringIntoView(); }
                    else { CardLedEffect.BringIntoView(); }
                    break;

                case "touchpad":
                    PanelTouchpad.Visibility = Visibility.Visible;
                    if (itemId == "touch_mouse") { CardTouchMouse.BorderBrush = glowBorder; CardTouchMouse.BringIntoView(); }
                    else if (itemId == "touch_vol") { CardTouchVol.BorderBrush = GetBrush("AccentAmber", Color.FromRgb(0xFF, 0xB3, 0x00)); CardTouchVol.BringIntoView(); }
                    else if (itemId == "touch_tap") { CardTouchTap.BorderBrush = glowBorder; CardTouchTap.BringIntoView(); }
                    else { CardTouchGestures.BorderBrush = glowBorder; CardTouchGestures.BringIntoView(); }
                    break;

                case "audio":
                    PanelAudio.Visibility = Visibility.Visible;
                    if (itemId == "aud_haptic") { CardAudHaptic.BorderBrush = glowBorder; CardAudHaptic.BringIntoView(); }
                    else if (itemId == "aud_reactive") { CardAudReactive.BorderBrush = glowBorder; CardAudReactive.BringIntoView(); }
                    else if (itemId == "aud_speaker") { CardAudSpeaker.BorderBrush = GetBrush("AccentBlue", Color.FromRgb(0x00, 0x70, 0xD1)); CardAudSpeaker.BringIntoView(); }
                    else if (itemId == "aud_mic") { CardAudMic.BorderBrush = GetBrush("AccentAmber", Color.FromRgb(0xFF, 0xB3, 0x00)); CardAudMic.BringIntoView(); }
                    else { CardAudHaptic.BringIntoView(); }
                    break;

                case "users":
                case "profiles":
                    PanelProfiles.Visibility = Visibility.Visible;
                    if (itemId == "prof_default") { CardProfDefault.BorderBrush = glowBorder; CardProfDefault.BringIntoView(); }
                    else if (itemId == "prof_esports") { CardProfEsports.BorderBrush = glowBorder; CardProfEsports.BringIntoView(); }
                    else if (itemId == "prof_fps") { CardProfFps.BorderBrush = glowBorder; CardProfFps.BringIntoView(); }
                    else if (itemId == "prof_racing" || itemId == "prof_new") { CardProfRacing.BorderBrush = glowBorder; CardProfRacing.BringIntoView(); }
                    else { CardProfDefault.BringIntoView(); }
                    break;

                case "settings":
                    PanelSettings.Visibility = Visibility.Visible;
                    if (itemId == "set_driver") { CardSetDriver.BorderBrush = GetBrush("AccentGreen", Color.FromRgb(0x00, 0xE6, 0x76)); CardSetDriver.BringIntoView(); }
                    else if (itemId == "set_autostart") { CardSetAutoStart.BorderBrush = glowBorder; CardSetAutoStart.BringIntoView(); }
                    else if (itemId == "set_idle") { CardSetIdle.BorderBrush = GetBrush("AccentAmber", Color.FromRgb(0xFF, 0xB3, 0x00)); CardSetIdle.BringIntoView(); }
                    else if (itemId == "set_update") { CardSetUpdate.BorderBrush = glowBorder; CardSetUpdate.BringIntoView(); }
                    else { CardSetDriver.BringIntoView(); }
                    break;

                default:
                    PanelTriggers.Visibility = Visibility.Visible;
                    break;
            }
        }

        public void UpdateTelemetry(ControllerTelemetry t)
        {
            if (t == null) return;

            if (!_configInitialized && t.Config != null)
            {
                _configInitialized = true;
                SyncFromConfig(t.Config);
            }

            // 1. Triggers Depth & Motor Oscilloscope
            BarL2Inspector.Value = t.L2 * 100.0;
            BarR2Inspector.Value = t.R2 * 100.0;
            DrawTriggerOscilloscope(t.L2, t.R2);

            // 2. Gyro Crosshair Motion Reticle
            DrawGyroCrosshair(t.GyroX, t.GyroY, t.GyroZ);

            // 3. Touchpad Live Stroke Sandbox
            if (t.Touch1.On)
            {
                _recentStroke.Add(new Point(t.Touch1.X, t.Touch1.Y));
                if (_recentStroke.Count > 100) _recentStroke.RemoveAt(0);
                DrawGestureSandbox(_recentStroke);
            }
            else if (_recentStroke.Count > 0)
            {
                _recentStroke.Clear();
            }
        }

        #region Adaptive Triggers Handlers
        private static int MapXmbTriggerMode(int btnTag) => btnTag switch
        {
            1 => 1, // Rigid Wall
            2 => 6, // Click-Snap Barrier (Weapon / TriggerStop)
            3 => 5, // Compound Bow
            4 => 2, // Machine Gun Recoil (Pulse / Vibration)
            _ => 0  // Off
        };

        private static int UnmapXmbTriggerMode(int mode) => mode switch
        {
            1 => 1,
            6 => 2,
            5 => 3,
            2 => 4,
            _ => 0
        };

        private void BtnL2Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int btnTag))
            {
                _l2Mode = MapXmbTriggerMode(btnTag);
                TxtL2ModeBadge.Text = btn.Content.ToString();
                HighlightOptionButton(new[] { BtnL2_0, BtnL2_1, BtnL2_2, BtnL2_3, BtnL2_4 }, btn);

                if (_l2Mode == 2) { _l2Freq = 32; _l2Start = 1; }
                else if (_l2Mode == 5) { _l2Start = 1; }
                else if (_l2Mode == 6) { _l2Start = 2; }
                else if (_l2Mode == 1) { _l2Start = 2; }

                PushAdaptiveTriggers();
                DrawTriggerOscilloscope(BarL2Inspector.Value / 100.0, BarR2Inspector.Value / 100.0);
            }
        }

        private void BtnR2Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int btnTag))
            {
                _r2Mode = MapXmbTriggerMode(btnTag);
                TxtR2ModeBadge.Text = btn.Content.ToString();
                HighlightOptionButton(new[] { BtnR2_0, BtnR2_1, BtnR2_2, BtnR2_3, BtnR2_4 }, btn);

                if (_r2Mode == 2) { _r2Freq = 32; _r2Start = 1; }
                else if (_r2Mode == 5) { _r2Start = 1; }
                else if (_r2Mode == 6) { _r2Start = 2; }
                else if (_r2Mode == 1) { _r2Start = 2; }

                PushAdaptiveTriggers();
                DrawTriggerOscilloscope(BarL2Inspector.Value / 100.0, BarR2Inspector.Value / 100.0);
            }
        }

        private void SliderSnapTension_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _snapTension = (int)e.NewValue;
            if (TxtSnapTensionVal != null)
            {
                TxtSnapTensionVal.Text = $"{_snapTension}% Force";
            }
            // Scale accurately to DualSense 1..8 force strength
            _l2Force = Math.Clamp((int)Math.Round(_snapTension / 100.0 * 8.0), 1, 8);
            _r2Force = _l2Force;
            PushAdaptiveTriggers();
            DrawTriggerOscilloscope(0, 0);
        }

        private void PushAdaptiveTriggers()
        {
            IpcClient?.SetAdaptiveTriggers(_l2Mode, _l2Start, _l2Force, _l2Freq, _r2Mode, _r2Start, _r2Force, _r2Freq);
        }

        private void DrawTriggerOscilloscope(double l2Pos, double r2Pos)
        {
            if (CanvasTrigOscilloscope == null) return;
            CanvasTrigOscilloscope.Children.Clear();

            double w = CanvasTrigOscilloscope.ActualWidth > 0 ? CanvasTrigOscilloscope.ActualWidth : 580;
            double h = CanvasTrigOscilloscope.ActualHeight > 0 ? CanvasTrigOscilloscope.ActualHeight : 64;

            // Center grid baseline
            CanvasTrigOscilloscope.Children.Add(new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = h * 0.5,
                Y2 = h * 0.5,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeDashArray = new DoubleCollection { 2, 2 }
            });

            // L2 Waveform (Cyan)
            var geomL2 = new StreamGeometry();
            using (var ctx = geomL2.Open())
            {
                ctx.BeginFigure(new Point(0, h * 0.75), false, false);
                for (int x = 0; x < w * 0.5; x += 4)
                {
                    double norm = x / (w * 0.5);
                    double resistance = _l2Mode switch
                    {
                        0 => 0.0, // Linear off
                        1 => Math.Sin(norm * Math.PI * 4) * 0.3, // SDK
                        2 => norm > 0.4 ? 0.85 : 0.1, // Snap barrier
                        3 => norm * 0.9, // Bow tension
                        4 => (Math.Sin(norm * Math.PI * 8) > 0 ? 0.9 : 0.1), // Machine Gun
                        _ => 0.5
                    };
                    double y = h * 0.75 - (resistance * (h * 0.45));
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geomL2.Freeze();
            CanvasTrigOscilloscope.Children.Add(new Path
            {
                Data = geomL2,
                Stroke = GetBrush("AccentSignature", Color.FromRgb(0x00, 0xF5, 0xD4)),
                StrokeThickness = 1.8
            });

            // R2 Waveform (Magenta)
            var geomR2 = new StreamGeometry();
            using (var ctx = geomR2.Open())
            {
                ctx.BeginFigure(new Point(w * 0.5, h * 0.75), false, false);
                for (int x = (int)(w * 0.5); x < w; x += 4)
                {
                    double norm = (x - w * 0.5) / (w * 0.5);
                    double resistance = _r2Mode switch
                    {
                        0 => 0.0,
                        1 => Math.Sin(norm * Math.PI * 4) * 0.35,
                        2 => norm > 0.4 ? 0.85 : 0.1,
                        3 => norm * 0.9,
                        4 => (Math.Sin(norm * Math.PI * 10) > 0 ? 0.95 : 0.05),
                        _ => 0.5
                    };
                    double y = h * 0.75 - (resistance * (h * 0.45));
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geomR2.Freeze();
            CanvasTrigOscilloscope.Children.Add(new Path
            {
                Data = geomR2,
                Stroke = GetBrush("AccentMagenta", Color.FromRgb(0xE0, 0x40, 0xFB)),
                StrokeThickness = 1.8
            });

            // Live Trigger Pull Indicator Markers
            double markerL2X = Math.Clamp(l2Pos * (w * 0.5), 2, w * 0.5 - 2);
            CanvasTrigOscilloscope.Children.Add(new Line
            {
                X1 = markerL2X,
                X2 = markerL2X,
                Y1 = 0,
                Y2 = h,
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            });

            double markerR2X = w * 0.5 + Math.Clamp(r2Pos * (w * 0.5), 2, w * 0.5 - 2);
            CanvasTrigOscilloscope.Children.Add(new Line
            {
                X1 = markerR2X,
                X2 = markerR2X,
                Y1 = 0,
                Y2 = h,
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            });
        }
        #endregion

        public void SyncFromConfig(ConfigDto cfg)
        {
            if (cfg == null) return;

            // Audio Haptics
            if (ToggleHaptics != null)
            {
                ToggleHaptics.IsChecked = cfg.Haptic;
                ToggleHaptics.Content = cfg.Haptic ? "Haptics: ON" : "Haptics: OFF";
            }
            if (SliderHapticIntensity != null)
            {
                SliderHapticIntensity.Value = (int)(cfg.HapticIntensity * 100.0);
            }
            if (TxtHapticIntensity != null)
            {
                TxtHapticIntensity.Text = $"{(int)(cfg.HapticIntensity * 100.0)}%";
            }
            if (ToggleSpeaker != null)
            {
                ToggleSpeaker.IsChecked = cfg.SpeakerEnabled;
                ToggleSpeaker.Content = cfg.SpeakerEnabled ? "Speaker: ON" : "Speaker: OFF";
            }
            if (ToggleMicSync != null)
            {
                ToggleMicSync.IsChecked = cfg.SyncMic;
                ToggleMicSync.Content = cfg.SyncMic ? "Mic Sync: ON" : "Mic Sync: OFF";
            }

            // Gyro Aiming
            _gyroEnabled = cfg.Gyro;
            if (ToggleGyroEnable != null)
            {
                ToggleGyroEnable.IsChecked = cfg.Gyro;
                ToggleGyroEnable.Content = cfg.Gyro ? "Gyro Aim: ON" : "Gyro Aim: OFF";
            }
            _gyroSensX = cfg.GyroSensX;
            _gyroSensY = cfg.GyroSensY;
            if (SliderSensX != null) SliderSensX.Value = cfg.GyroSensX;
            if (SliderSensY != null) SliderSensY.Value = cfg.GyroSensY;
            if (TxtSensXVal != null) TxtSensXVal.Text = $"{cfg.GyroSensX:F2}x";
            if (TxtSensYVal != null) TxtSensYVal.Text = $"{cfg.GyroSensY:F2}x";
            _gyroOutputMode = cfg.GyroOutputMode;
            if (BtnGyroMouse != null && BtnGyroStick != null)
            {
                HighlightOptionButton(new[] { BtnGyroMouse, BtnGyroStick }, cfg.GyroOutputMode == 1 ? BtnGyroStick : BtnGyroMouse);
            }
            _gyroClickMode = cfg.GyroClickMode;
            if (BtnGyroClickCrossSquare != null && BtnGyroClickTriggers != null && BtnGyroClickBumpers != null && BtnGyroClickDisabled != null)
            {
                var clickBtn = cfg.GyroClickMode switch
                {
                    1 => BtnGyroClickTriggers,
                    2 => BtnGyroClickBumpers,
                    3 => BtnGyroClickDisabled,
                    _ => BtnGyroClickCrossSquare
                };
                HighlightOptionButton(new[] { BtnGyroClickCrossSquare, BtnGyroClickTriggers, BtnGyroClickBumpers, BtnGyroClickDisabled }, clickBtn);
            }

            // Gestures
            if (MainWindow != null && ToggleGestures != null)
            {
                ToggleGestures.IsChecked = MainWindow.GestureEngine.GesturesEnabled;
                ToggleGestures.Content = MainWindow.GestureEngine.GesturesEnabled ? "Gestures: ON" : "Gestures: OFF";
            }

            // Adaptive Triggers
            _l2Mode = cfg.LtMode;
            _r2Mode = cfg.RtMode;
            int l2Idx = UnmapXmbTriggerMode(_l2Mode);
            int r2Idx = UnmapXmbTriggerMode(_r2Mode);
            var l2Buttons = new[] { BtnL2_0, BtnL2_1, BtnL2_2, BtnL2_3, BtnL2_4 };
            var r2Buttons = new[] { BtnR2_0, BtnR2_1, BtnR2_2, BtnR2_3, BtnR2_4 };
            if (l2Idx >= 0 && l2Idx < l2Buttons.Length)
            {
                HighlightOptionButton(l2Buttons, l2Buttons[l2Idx]);
                if (TxtL2ModeBadge != null) TxtL2ModeBadge.Text = l2Buttons[l2Idx].Content.ToString();
            }
            if (r2Idx >= 0 && r2Idx < r2Buttons.Length)
            {
                HighlightOptionButton(r2Buttons, r2Buttons[r2Idx]);
                if (TxtR2ModeBadge != null) TxtR2ModeBadge.Text = r2Buttons[r2Idx].Content.ToString();
            }
        }

        #region Gyro Handlers
        public void SetGyroEnabled(bool enabled)
        {
            _gyroEnabled = enabled;
            if (ToggleGyroEnable != null)
            {
                ToggleGyroEnable.IsChecked = enabled;
                ToggleGyroEnable.Content = enabled ? "Gyro Aim: ON" : "Gyro Aim: OFF";
            }
        }

        public void SetGyroSensitivity(float sens)
        {
            _gyroSensX = sens;
            _gyroSensY = sens;
            if (SliderSensX != null) SliderSensX.Value = sens;
            if (SliderSensY != null) SliderSensY.Value = sens;
            if (TxtSensXVal != null) TxtSensXVal.Text = $"{sens:F2}x";
            if (TxtSensYVal != null) TxtSensYVal.Text = $"{sens:F2}x";
        }

        private void ToggleGyroEnable_Click(object sender, RoutedEventArgs e)
        {
            _gyroEnabled = ToggleGyroEnable.IsChecked == true;
            ToggleGyroEnable.Content = _gyroEnabled ? "Gyro Aim: ON" : "Gyro Aim: OFF";
            PushGyroConfig();
        }

        private void BtnGyroMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int mode))
            {
                _gyroOutputMode = mode;
                HighlightOptionButton(new[] { BtnGyroMouse, BtnGyroStick }, btn);
                PushGyroConfig();
            }
        }

        private void BtnGyroTriggerMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int trigMode))
            {
                _gyroTriggerMode = trigMode;
                HighlightOptionButton(new[] { BtnGyroTrigADS, BtnGyroTrigAlways, BtnGyroTrigTouch, BtnGyroTrigR2 }, btn);
                PushGyroConfig();
            }
        }

        private void SliderSens_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SliderSensX != null && SliderSensY != null)
            {
                _gyroSensX = (float)SliderSensX.Value;
                _gyroSensY = (float)SliderSensY.Value;
                if (TxtSensXVal != null) TxtSensXVal.Text = $"{_gyroSensX:F2}x";
                if (TxtSensYVal != null) TxtSensYVal.Text = $"{_gyroSensY:F2}x";
                PushGyroConfig();
            }
        }

        private void ChkGyroInvert_Click(object sender, RoutedEventArgs e)
        {
            _gyroInvertX = ChkGyroInvertX.IsChecked == true;
            _gyroInvertY = ChkGyroInvertY.IsChecked == true;
            PushGyroConfig();
        }

        private void SliderDeadzone_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _gyroDeadzone = (int)e.NewValue;
            if (TxtDeadzoneVal != null) TxtDeadzoneVal.Text = $"{_gyroDeadzone} deg/s";
            PushGyroConfig();
        }

        private void PushGyroConfig()
        {
            IpcClient?.SetGyro(_gyroEnabled, _gyroTriggerMode, _gyroOutputMode, _gyroSpace, _gyroSensX, _gyroSensY, _gyroInvertX, _gyroInvertY, _gyroDeadzone, _gyroSmooth, _gyroClickMode);
        }

        private void BtnGyroClick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int clickMode))
            {
                _gyroClickMode = clickMode;
                HighlightOptionButton(new[] { BtnGyroClickCrossSquare, BtnGyroClickTriggers, BtnGyroClickBumpers, BtnGyroClickDisabled }, btn);
                PushGyroConfig();
            }
        }

        private void BtnLaunchCalibrator_Click(object sender, RoutedEventArgs e)
        {
            MainWindow?.LaunchGyroCalibrator();
        }

        private void DrawGyroCrosshair(short gx, short gy, short gz)
        {
            if (CanvasGyroMotion == null) return;
            CanvasGyroMotion.Children.Clear();

            double w = CanvasGyroMotion.ActualWidth > 0 ? CanvasGyroMotion.ActualWidth : 580;
            double h = CanvasGyroMotion.ActualHeight > 0 ? CanvasGyroMotion.ActualHeight : 70;
            double cx = w * 0.5, cy = h * 0.5;

            // Crosshairs
            CanvasGyroMotion.Children.Add(new Line { X1 = cx - 40, X2 = cx + 40, Y1 = cy, Y2 = cy, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) });
            CanvasGyroMotion.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = cy - 25, Y2 = cy + 25, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) });

            // Reticle dot
            double dx = Math.Clamp(gz * 0.02, -cx + 10, cx - 10);
            double dy = Math.Clamp(-gx * 0.02, -cy + 10, cy - 10);

            CanvasGyroMotion.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = GetBrush("AccentSignature", Color.FromRgb(0x00, 0xF5, 0xD4)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Margin = new Thickness(cx + dx - 5, cy + dy - 5, 0, 0)
            });
        }
        #endregion

        #region Lightbar Handlers
        private void BtnLedEffect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int effect))
            {
                _activeLedEffect = effect;
                HighlightOptionButton(new[] { BtnLedEff0, BtnLedEff1, BtnLedEff2, BtnLedEff3, BtnLedEff4 }, btn);
                PushLightbarConfig();
            }
        }

        private void BtnColorPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                try
                {
                    var col = (Color)ColorConverter.ConvertFromString(hex);
                    _colorR = col.R;
                    _colorG = col.G;
                    _colorB = col.B;
                    _activeLedEffect = 0; // Set to Solid Color

                    ColorPreviewPill.Background = new SolidColorBrush(col);
                    TxtHexCode.Text = hex;
                    HighlightOptionButton(new[] { BtnLedEff0, BtnLedEff1, BtnLedEff2, BtnLedEff3, BtnLedEff4 }, BtnLedEff0);

                    PushLightbarConfig();
                }
                catch { }
            }
        }

        private void BtnPlayerLed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && byte.TryParse(tagStr, out byte pmask))
            {
                _playerLeds = pmask;
                HighlightOptionButton(new[] { BtnPled1, BtnPled2, BtnPled3, BtnPled4, BtnPledAll }, btn);
                PushLightbarConfig();
            }
        }

        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _brightness = (byte)Math.Clamp(e.NewValue * 2.55, 0, 255);
            if (TxtBrightnessVal != null) TxtBrightnessVal.Text = $"{e.NewValue:F0}%";
            PushLightbarConfig();
        }

        private void PushLightbarConfig()
        {
            float scale = _brightness / 255.0f;
            int r = Math.Clamp((int)(_colorR * scale), 0, 255);
            int g = Math.Clamp((int)(_colorG * scale), 0, 255);
            int b = Math.Clamp((int)(_colorB * scale), 0, 255);
            try { IpcClient?.SetRgbColor(r, g, b, _activeLedEffect, _playerLeds, 1.0f); } catch { }
        }
        #endregion

        #region Touchpad Handlers
        private void ToggleTouchMouse_Click(object sender, RoutedEventArgs e)
        {
            _mouseEnabled = ToggleTouchMouse.IsChecked == true;
            ToggleTouchMouse.Content = _mouseEnabled ? "Mouse: ON" : "Mouse: OFF";
            PushTouchpadConfig();
        }

        private void SliderTouchSens_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _touchSens = (float)e.NewValue;
            if (TxtTouchSensVal != null) TxtTouchSensVal.Text = $"{_touchSens:F2}x";
            PushTouchpadConfig();
        }

        private void ToggleDiscreteScroll_Click(object sender, RoutedEventArgs e)
        {
            _discreteScroll = ToggleDiscreteScroll.IsChecked == true;
            ToggleDiscreteScroll.Content = _discreteScroll ? "Notched: ON" : "Notched: OFF";
            PushTouchpadConfig();
        }

        private void ToggleTouchVol_Click(object sender, RoutedEventArgs e)
        {
            _volGesture = ToggleTouchVol.IsChecked == true;
            ToggleTouchVol.Content = _volGesture ? "Gesture: ON" : "Gesture: OFF";
            PushTouchpadConfig();
        }

        private void ToggleTouchTap_Click(object sender, RoutedEventArgs e)
        {
            _tapClick = ToggleTouchTap.IsChecked == true;
            ToggleTouchTap.Content = _tapClick ? "Tap: ON" : "Tap: OFF";
            PushTouchpadConfig();
        }

        private void ToggleGestures_Click(object sender, RoutedEventArgs e)
        {
            bool isGestures = ToggleGestures.IsChecked == true;
            ToggleGestures.Content = isGestures ? "Gestures: ON" : "Gestures: OFF";
            if (MainWindow != null) MainWindow.GestureEngine.GesturesEnabled = isGestures;
        }

        private void PushTouchpadConfig()
        {
            IpcClient?.SetTouchpad(_mouseEnabled, _touchSens, 1.0f, _tapClick, _volGesture);
        }

        private void DrawGestureSandbox(List<Point> pts)
        {
            if (CanvasGestureSandbox == null || pts.Count < 2) return;
            CanvasGestureSandbox.Children.Clear();

            double w = CanvasGestureSandbox.ActualWidth > 0 ? CanvasGestureSandbox.ActualWidth : 580;
            double h = CanvasGestureSandbox.ActualHeight > 0 ? CanvasGestureSandbox.ActualHeight : 90;

            // Touchpad normalized space: 0..1920 X, 0..1080 Y
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                Point p0 = new(pts[0].X / 1920.0 * w, pts[0].Y / 1080.0 * h);
                ctx.BeginFigure(p0, false, false);
                for (int i = 1; i < pts.Count; i++)
                {
                    Point pi = new(pts[i].X / 1920.0 * w, pts[i].Y / 1080.0 * h);
                    ctx.LineTo(pi, true, false);
                }
            }
            geom.Freeze();

            CanvasGestureSandbox.Children.Add(new Path
            {
                Data = geom,
                Stroke = GetBrush("AccentSignature", Color.FromRgb(0x00, 0xF5, 0xD4)),
                StrokeThickness = 2.5
            });
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

        private void ToggleVolumeOverlay_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleVolumeOverlay.IsChecked == true;
            ToggleVolumeOverlay.Content = isOn ? "Overlay: ON" : "Overlay: OFF";
            if (MainWindow != null) MainWindow.EnableVolumeOverlay = isOn;
        }
        #endregion

        #region Profiles Handlers
        public void UpdateActiveProfileCard(string name)
        {
            var defaultBorder = GetBrush("BorderColor", Color.FromArgb(0x35, 0x29, 0x79, 0xFF));
            var glowBorder = GetBrush("AccentSignature", Color.FromRgb(0x00, 0xF5, 0xD4));

            bool isDef = name.Equals("Standard Balanced", StringComparison.OrdinalIgnoreCase);
            bool isEsports = name.Contains("Esports", StringComparison.OrdinalIgnoreCase);
            bool isFps = name.Contains("FPS", StringComparison.OrdinalIgnoreCase);
            bool isRacing = name.Contains("Racing", StringComparison.OrdinalIgnoreCase);

            if (CardProfDefault != null) CardProfDefault.BorderBrush = isDef ? glowBorder : defaultBorder;
            if (CardProfEsports != null) CardProfEsports.BorderBrush = isEsports ? glowBorder : defaultBorder;
            if (CardProfFps != null) CardProfFps.BorderBrush = isFps ? glowBorder : defaultBorder;
            if (CardProfRacing != null) CardProfRacing.BorderBrush = isRacing ? glowBorder : defaultBorder;

            if (BtnActDefault != null) BtnActDefault.Content = isDef ? "✓ Active" : "Activate";
            if (BtnActEsports != null) BtnActEsports.Content = isEsports ? "✓ Active" : "Activate";
            if (BtnActFps != null) BtnActFps.Content = isFps ? "✓ Active" : "Activate";
            if (BtnActRacing != null) BtnActRacing.Content = isRacing ? "✓ Active" : "Activate";
        }

        private void BtnActivateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name && MainWindow != null)
            {
                MainWindow.ApplyProfileByName(name);
                UpdateActiveProfileCard(name);
            }
        }
        #endregion

        #region Settings & Updates Handlers
        private void BtnRestartDriver_Click(object sender, RoutedEventArgs e)
        {
            MainWindow?.RestartDriverEngine();
        }

        private void ToggleAutoStart_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleAutoStart.IsChecked == true;
            ToggleAutoStart.Content = isOn ? "Auto-Start: ON" : "Auto-Start: OFF";
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (runKey != null)
                {
                    string driverExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenDualDriver.exe");
                    if (isOn)
                        runKey.SetValue("OpenDualDriver", $"\"{driverExe}\"");
                    else
                        runKey.DeleteValue("OpenDualDriver", false);
                }
            }
            catch { }
        }

        private void SyncStartupRegistryState()
        {
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                bool exists = runKey?.GetValue("OpenDualDriver") != null;
                ToggleAutoStart.IsChecked = exists;
                ToggleAutoStart.Content = exists ? "Auto-Start: ON" : "Auto-Start: OFF";
            }
            catch { }
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

        private void ToggleNerdStats_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleNerdStats.IsChecked == true;
            ToggleNerdStats.Content = isOn ? "Nerd Stats: ON" : "Nerd Stats: OFF";
            if (MainWindow != null)
            {
                MainWindow.ShowNerdStats = isOn;
                MainWindow.XmbView?.SetNerdStatsVisible(isOn);
            }
        }

        private void ToggleMilitaryTime_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleMilitaryTime.IsChecked == true;
            ToggleMilitaryTime.Content = isOn ? "Military Time: ON" : "Military Time: OFF";
            if (MainWindow != null)
            {
                MainWindow.ClockUseMilitaryTime = isOn;
                MainWindow.UpdateClockDisplay();
            }
        }

        private void BtnDateFormat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fmt && MainWindow != null)
            {
                MainWindow.ClockDateFormat = fmt;
                MainWindow.UpdateClockDisplay();
                HighlightOptionButton(new[] { BtnDateUk, BtnDateUs, BtnDateIso, BtnDateNone }, btn);
            }
        }

        private void ToggleLightweightBg_Click(object sender, RoutedEventArgs e)
        {
            bool isOn = ToggleLightweightBg.IsChecked == true;
            ToggleLightweightBg.Content = isOn ? "Lightweight Art: ON" : "Lightweight Art: OFF";
            if (MainWindow?.WaveBackground != null)
            {
                MainWindow.WaveBackground.LightweightMode = isOn;
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            TxtXmbUpdateStatus.Text = "Connecting to GitHub releases repository...";

            if (MainWindow != null)
            {
                var info = await MainWindow.CheckForUpdatesAsync();
                if (info != null && info.UpdateAvailable)
                {
                    TxtXmbUpdateStatus.Text = $"Update available: OpenDual v{info.LatestVersion}!";
                    TxtXmbUpdateStatus.Foreground = GetBrush("AccentGreen", Color.FromRgb(0x00, 0xE6, 0x76));
                    TxtXmbNewVersionTitle.Text = $"OpenDual Suite v{info.LatestVersion} Ready";
                    TxtXmbNewVersionNotes.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "Includes enhanced controller performance, gyro aiming, and bug fixes." : info.ReleaseNotes;
                    PanelXmbUpdateDetails.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtXmbUpdateStatus.Text = "You are on the latest release (v1.6.6).";
                    TxtXmbUpdateStatus.Foreground = GetBrush("AccentSignature", Color.FromRgb(0x00, 0xF5, 0xD4));
                    PanelXmbUpdateDetails.Visibility = Visibility.Collapsed;
                    MessageBox.Show("OpenDual Suite is fully up to date!\n\nCurrent Version: v1.6.6", "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            BtnCheckUpdates.IsEnabled = true;
        }

        private async void BtnXmbInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow == null) return;
            BtnXmbInstallUpdate.IsEnabled = false;
            ProgressXmbUpdate.Visibility = Visibility.Visible;
            ProgressXmbUpdate.Value = 0;

            var progress = new Progress<int>(pct =>
            {
                ProgressXmbUpdate.Value = pct;
                TxtXmbNewVersionTitle.Text = $"Downloading update... {pct}%";
            });

            await MainWindow.DownloadAndInstallUpdateAsync(progress);
            BtnXmbInstallUpdate.IsEnabled = true;
        }
        #endregion

        private Brush GetBrush(string resourceKey, Color fallbackColor)
        {
            if (TryFindResource(resourceKey) is Brush b)
                return b;
            if (Application.Current != null)
            {
                try
                {
                    if (Application.Current.TryFindResource(resourceKey) is Brush appB)
                        return appB;
                }
                catch { }
            }
            return new SolidColorBrush(fallbackColor);
        }

        private void HighlightOptionButton(Button[] allButtons, Button selectedButton)
        {
            var sigBg = GetBrush("AccentSignature", Color.FromRgb(0x00, 0xF5, 0xD4));
            var darkBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x27, 0x40));
            var darkText = new SolidColorBrush(Color.FromRgb(0x08, 0x0B, 0x13));

            foreach (var b in allButtons)
            {
                if (b == null) continue;
                bool isSel = (b == selectedButton);
                b.Background = isSel ? sigBg : darkBg;
                b.Foreground = isSel ? darkText : Brushes.White;
            }
        }
    }
}
