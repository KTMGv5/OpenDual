using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class MainWindow : Window
    {
        private readonly IpcClient _ipc = new();
        private readonly DriverManager _driver = new();
        private readonly DispatcherTimer _clockTimer = new();
        private bool _isUpdatingUi = false;
        private bool _isInitialized = false;

        private readonly TrayIconManager _trayManager = new();
        private QuickHudWindow? _quickHud;
        private bool _lastPsState = false;
        private bool _hasShownTrayBalloon = false;
        private DispatcherTimer? _hapticDebounceTimer;
        private DateTime _lastHapticSendTime = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();
            _isInitialized = true;

            ThemeManager.Initialize(Resources);

            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) =>
            {
                if (TxtClock != null)
                {
                    TxtClock.Text = DateTime.Now.ToString("h:mm tt");
                }
            };
            _clockTimer.Start();
            if (TxtClock != null)
            {
                TxtClock.Text = DateTime.Now.ToString("h:mm tt");
            }

            _ipc.OnTelemetryReceived += OnTelemetryReceived;
            _ipc.OnConnectionChanged += OnIpcConnectionChanged;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Hook Win32 messages to auto-update when Windows theme or accent color changes
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);

            ThemeManager.ApplySystemTheme(Resources);

            // Auto-start driver engine if not running
            if (!DriverManager.IsDriverEngineRunning())
            {
                _driver.StartDriverEngine();
            }

            _ipc.Start();
            RefreshDriverStatus();

            _trayManager.Initialize(helper.Handle, "OpenDual — PlayStation Controller Suite");
            _trayManager.DoubleClick += RestoreFromTray;
            _trayManager.RightClick += ShowTrayContextMenu;

            _quickHud = new QuickHudWindow();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }

        private void ShowTrayContextMenu()
        {
            var menu = new ContextMenu();
            var itemOpen = new MenuItem { Header = "Open OpenDual Hub" };
            itemOpen.Click += (s, e) => RestoreFromTray();
            menu.Items.Add(itemOpen);

            var itemRestart = new MenuItem { Header = "Restart Driver Engine" };
            itemRestart.Click += (s, e) => BtnRestartEngine_Click(this, new RoutedEventArgs());
            menu.Items.Add(itemRestart);

            var itemMute = new MenuItem { Header = "Toggle Microphone Mute" };
            itemMute.Click += (s, e) => ToggleMicMute();
            menu.Items.Add(itemMute);

            menu.Items.Add(new Separator());

            var itemExit = new MenuItem { Header = "Exit OpenDual" };
            itemExit.Click += (s, e) => ExitApplication();
            menu.Items.Add(itemExit);

            menu.IsOpen = true;
        }

        private void ToggleMicMute()
        {
            if (ChkSyncMic != null)
            {
                bool newSync = !(ChkSyncMic.IsChecked == true);
                ChkSyncMic.IsChecked = newSync;
                _ipc.SetMicSync(newSync);
            }
        }

        private void ExitApplication()
        {
            _trayManager.Dispose();
            _quickHud?.Close();
            Application.Current.Shutdown();
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && ChkMinimizeToTray?.IsChecked == true)
            {
                Hide();
                ShowInTaskbar = false;
                if (!_hasShownTrayBalloon)
                {
                    _hasShownTrayBalloon = true;
                    _trayManager.ShowNotification("OpenDual Suite", "OpenDual is minimized to the system tray. Click icon to restore.");
                }
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_trayManager.ProcessWindowMessage(msg, lParam))
            {
                handled = true;
                return IntPtr.Zero;
            }

            const int WM_SETTINGCHANGE = 0x001A;
            const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
            if (msg == WM_SETTINGCHANGE || msg == WM_DWMCOLORIZATIONCOLORCHANGED)
            {
                Dispatcher.Invoke(() =>
                {
                    ThemeManager.ApplySystemTheme(Resources);
                    RefreshDriverStatus();
                });
            }
            return IntPtr.Zero;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _clockTimer.Stop();
            _ipc.Dispose();
            _trayManager.Dispose();
            _quickHud?.Close();
        }

        private void RefreshDriverStatus()
        {
            Dispatcher.Invoke(() =>
            {
                bool viInstalled = DriverManager.IsViGEmBusInstalled();
                if (TxtViGEmStatus != null)
                {
                    TxtViGEmStatus.Text = viInstalled ? "Installed & Active" : "Not Installed (Optional for XInput)";
                    TxtViGEmStatus.Foreground = viInstalled ? (SolidColorBrush)FindResource("AccentGreen") : (SolidColorBrush)FindResource("AccentRed");
                }

                bool engRunning = DriverManager.IsDriverEngineRunning();
                if (TxtEngineStatus != null)
                {
                    TxtEngineStatus.Text = engRunning ? "Running (1000Hz Active)" : "Stopped";
                    TxtEngineStatus.Foreground = engRunning ? (SolidColorBrush)FindResource("AccentGreen") : (SolidColorBrush)FindResource("AccentRed");
                }

                if (FooterEngineStatus != null)
                {
                    FooterEngineStatus.Text = engRunning ? "Engine: Active (1000Hz)" : "Engine: Stopped";
                    FooterEngineStatus.Foreground = engRunning ? (SolidColorBrush)FindResource("AccentGreen") : (SolidColorBrush)FindResource("AccentRed");
                }

                // Show only appropriate contextual buttons
                if (BtnStartEngine != null && BtnRestartEngine != null && BtnStopEngine != null)
                {
                    if (engRunning)
                    {
                        BtnStartEngine.Visibility = Visibility.Collapsed;
                        BtnRestartEngine.Visibility = Visibility.Visible;
                        BtnStopEngine.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        BtnStartEngine.Visibility = Visibility.Visible;
                        BtnRestartEngine.Visibility = Visibility.Collapsed;
                        BtnStopEngine.Visibility = Visibility.Collapsed;
                    }
                }
            });
        }

        #region Navigation
        private void HideAllViews()
        {
            if (ViewDashboard == null) return;
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewRgb.Visibility = Visibility.Collapsed;
            ViewTriggers.Visibility = Visibility.Collapsed;
            ViewTouchpad.Visibility = Visibility.Collapsed;
            ViewAudio.Visibility = Visibility.Collapsed;
            ViewDriver.Visibility = Visibility.Collapsed;
        }

        private void Nav_Dashboard_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewDashboard != null) ViewDashboard.Visibility = Visibility.Visible;
        }

        private void Nav_Rgb_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewRgb != null) ViewRgb.Visibility = Visibility.Visible;
        }

        private void Nav_Triggers_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewTriggers != null) ViewTriggers.Visibility = Visibility.Visible;
        }

        private void Nav_Touchpad_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewTouchpad != null) ViewTouchpad.Visibility = Visibility.Visible;
        }

        private void Nav_Audio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewAudio != null) ViewAudio.Visibility = Visibility.Visible;
        }

        private void Nav_Driver_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewDriver != null)
            {
                ViewDriver.Visibility = Visibility.Visible;
                RefreshDriverStatus();
            }
        }
        #endregion

        #region Telemetry & Live UI
        private void OnIpcConnectionChanged(bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                if (!connected)
                {
                    if (StatusDot != null) StatusDot.Fill = (SolidColorBrush)FindResource("AccentRed");
                    if (FooterStatus != null) FooterStatus.Text = "Driver Service Disconnected";
                    if (DashModelTitle != null) DashModelTitle.Text = "Waiting for OpenDual Service...";
                    if (DashConnInfo != null) DashConnInfo.Text = "Start driver engine in System tab to begin low-latency Direct HID polling.";
                }
                RefreshDriverStatus();
            });
        }

        private void OnTelemetryReceived(ControllerTelemetry t)
        {
            Dispatcher.Invoke(() =>
            {
                _isUpdatingUi = true;
                try
                {
                    if (t.Connected)
                    {
                        if (StatusDot != null) StatusDot.Fill = (SolidColorBrush)FindResource("AccentGreen");
                        if (FooterStatus != null) FooterStatus.Text = $"{t.ModelName} Connected ({t.ConnectionName})";
                        if (DashModelTitle != null) DashModelTitle.Text = t.ModelName;
                        if (DashConnInfo != null) DashConnInfo.Text = $"Connected via {t.ConnectionName} • Ultra Low-Latency Direct HID (1000Hz)";
                        if (TxtBatteryHeader != null) TxtBatteryHeader.Text = $"{t.Battery}%{(t.Charging ? " [CHG]" : "")}";

                        if (DashHzVal != null) DashHzVal.Text = $"{t.PollingRateHz} Hz";
                        if (DashLatencyVal != null) DashLatencyVal.Text = $"{t.LatencyMs:F1} ms";

                        if (DashMicBadge != null) DashMicBadge.Visibility = t.MicMuted ? Visibility.Visible : Visibility.Collapsed;

                        // Live Dynamic Color Box for Rainbow / Breathing / Multi-color effects
                        if (t.Config != null && ColorPreviewBox != null && TxtHexCode != null)
                        {
                            if (t.Config.Effect != 0)
                            {
                                var dynamicColor = Color.FromRgb((byte)t.Config.R, (byte)t.Config.G, (byte)t.Config.B);
                                ColorPreviewBox.Background = new SolidColorBrush(dynamicColor);
                                TxtHexCode.Text = $"#{t.Config.R:X2}{t.Config.G:X2}{t.Config.B:X2}";
                                if (TxtGlowSub != null)
                                {
                                    TxtGlowSub.Text = t.Config.Effect switch
                                    {
                                        1 => "Active Glow: Breathing Pulse",
                                        2 => "Active Glow: Rainbow 360° Cycle",
                                        3 => "Active Glow: Battery Level Gauge",
                                        _ => "Active Dynamic Effect"
                                    };
                                }
                            }
                        }

                        // Triggers
                        int l2Pct = (int)(t.L2 * 100);
                        int r2Pct = (int)(t.R2 * 100);
                        if (TxtL2Val != null) TxtL2Val.Text = $"{l2Pct}%";
                        if (BarL2 != null) BarL2.Value = l2Pct;
                        if (TxtR2Val != null) TxtR2Val.Text = $"{r2Pct}%";
                        if (BarR2 != null) BarR2.Value = r2Pct;

                        // Sticks (Canvas width 130, center 65, radius 65, dot width 26 -> range 0 to 104)
                        double lsX = 52 + (t.LX * 45);
                        double lsY = 52 - (t.LY * 45); // up is positive
                        if (DotLS != null)
                        {
                            Canvas.SetLeft(DotLS, Math.Clamp(lsX, 10, 94));
                            Canvas.SetTop(DotLS, Math.Clamp(lsY, 10, 94));
                        }
                        if (TxtLSCoord != null) TxtLSCoord.Text = $"X: {t.LX:+0.00;-0.00;0.00}  Y: {t.LY:+0.00;-0.00;0.00}";

                        double rsX = 52 + (t.RX * 45);
                        double rsY = 52 - (t.RY * 45);
                        if (DotRS != null)
                        {
                            Canvas.SetLeft(DotRS, Math.Clamp(rsX, 10, 94));
                            Canvas.SetTop(DotRS, Math.Clamp(rsY, 10, 94));
                        }
                        if (TxtRSCoord != null) TxtRSCoord.Text = $"X: {t.RX:+0.00;-0.00;0.00}  Y: {t.RY:+0.00;-0.00;0.00}";

                        // Buttons Highlight
                        if (PillL1 != null) HighlightPill(PillL1, t.L1);
                        if (PillR1 != null) HighlightPill(PillR1, t.R1);
                        if (PillShare != null) HighlightPill(PillShare, t.Share);
                        if (PillOpt != null) HighlightPill(PillOpt, t.Options);
                        if (PillPS != null) HighlightPill(PillPS, t.PS);
                        if (PillL3 != null) HighlightPill(PillL3, t.L3);
                        if (PillR3 != null) HighlightPill(PillR3, t.R3);
                        if (PillMic != null) HighlightPill(PillMic, t.MicBtn);

                        if (BtnTriangle != null) HighlightButton(BtnTriangle, t.Triangle, "#00E676");
                        if (BtnSquare != null) HighlightButton(BtnSquare, t.Square, "#FF3377");
                        if (BtnCircle != null) HighlightButton(BtnCircle, t.Circle, "#FF334B");
                        if (BtnCross != null) HighlightButton(BtnCross, t.Cross, "#3399FF");

                        // Touchpad (380x200 canvas from 1920x1080)
                        if (DotTouch1 != null)
                        {
                            if (t.Touch1.On)
                            {
                                double x1 = (t.Touch1.X / 1920.0) * 380 - 9;
                                double y1 = (t.Touch1.Y / 1080.0) * 200 - 9;
                                Canvas.SetLeft(DotTouch1, x1);
                                Canvas.SetTop(DotTouch1, y1);
                                DotTouch1.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                DotTouch1.Visibility = Visibility.Collapsed;
                            }
                        }

                        if (DotTouch2 != null)
                        {
                            if (t.Touch2.On)
                            {
                                double x2 = (t.Touch2.X / 1920.0) * 380 - 9;
                                double y2 = (t.Touch2.Y / 1080.0) * 200 - 9;
                                Canvas.SetLeft(DotTouch2, x2);
                                Canvas.SetTop(DotTouch2, y2);
                                DotTouch2.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                DotTouch2.Visibility = Visibility.Collapsed;
                            }
                        }

                        string t1Str = t.Touch1.On ? $"({t.Touch1.X}, {t.Touch1.Y})" : "Inactive";
                        string t2Str = t.Touch2.On ? $"({t.Touch2.X}, {t.Touch2.Y})" : "Inactive";
                        if (TxtTouchCoords != null) TxtTouchCoords.Text = $"Touch 1: {t1Str} | Touch 2: {t2Str}";

                        // Live Audio-Reactive Haptics Tactile Meters
                        if (BarHapticL != null) BarHapticL.Value = t.HapticL;
                        if (TxtHapticL != null) TxtHapticL.Text = t.HapticL.ToString();
                        if (BarHapticR != null) BarHapticR.Value = t.HapticR;
                        if (TxtHapticR != null) TxtHapticR.Text = t.HapticR.ToString();

                        // In-Game Quick HUD Update & PS Button Detection
                        if (_quickHud != null)
                        {
                            _quickHud.UpdateState(t);
                            if (t.PS && !_lastPsState && ChkEnableQuickHud?.IsChecked == true)
                            {
                                _quickHud.ToggleOrRefresh();
                            }
                        }
                        _lastPsState = t.PS;
                    }
                    else
                    {
                        if (StatusDot != null) StatusDot.Fill = (SolidColorBrush)FindResource("AccentRed");
                        if (FooterStatus != null) FooterStatus.Text = "Waiting for PS4 / PS5 Controller...";
                        if (TxtBatteryHeader != null) TxtBatteryHeader.Text = "--%";
                        if (DashModelTitle != null) DashModelTitle.Text = "No Controller Detected";
                        if (DashConnInfo != null) DashConnInfo.Text = "Connect a DualSense or DualShock 4 via USB or Bluetooth.";
                        if (DashHzVal != null) DashHzVal.Text = "0 Hz";
                        if (DashLatencyVal != null) DashLatencyVal.Text = "0.0 ms";
                    }
                }
                finally
                {
                    _isUpdatingUi = false;
                }
            });
        }

        private void HighlightPill(Border border, bool active)
        {
            border.Background = active ? (SolidColorBrush)FindResource("AccentBlue") : (SolidColorBrush)FindResource("PillBg");
        }

        private void HighlightButton(Border border, bool active, string hexColor)
        {
            if (active)
            {
                var col = (Color)ColorConverter.ConvertFromString(hexColor);
                border.Background = new SolidColorBrush(col);
            }
            else
            {
                border.Background = (SolidColorBrush)FindResource("PillBg");
            }
        }
        #endregion

        #region RGB Studio
        private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isUpdatingUi || TxtValR == null || SliderR == null || SliderG == null || SliderB == null || ColorPreviewBox == null || TxtHexCode == null) return;

            int r = (int)SliderR.Value;
            int g = (int)SliderG.Value;
            int b = (int)SliderB.Value;
            float speed = (float)(SliderEffectSpeed?.Value ?? 1.0);

            TxtValR.Text = r.ToString();
            TxtValG.Text = g.ToString();
            TxtValB.Text = b.ToString();

            int effect = ComboEffect?.SelectedIndex ?? 0;
            if (effect == 0) // Only override preview box manually in solid mode
            {
                var color = Color.FromRgb((byte)r, (byte)g, (byte)b);
                ColorPreviewBox.Background = new SolidColorBrush(color);
                TxtHexCode.Text = $"#{r:X2}{g:X2}{b:X2}";
                if (TxtGlowSub != null) TxtGlowSub.Text = "Active Solid Color";
            }

            int playerLeds = GetSelectedPlayerLeds();
            _ipc.SetRgbColor(r, g, b, effect, playerLeds, speed);
        }

        private void EffectSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isUpdatingUi || SliderEffectSpeed == null || TxtEffectSpeedVal == null) return;

            float speed = (float)SliderEffectSpeed.Value;
            TxtEffectSpeedVal.Text = $"{speed:F1}x";

            int r = (int)(SliderR?.Value ?? 0);
            int g = (int)(SliderG?.Value ?? 120);
            int b = (int)(SliderB?.Value ?? 255);
            int effect = ComboEffect?.SelectedIndex ?? 0;
            int playerLeds = GetSelectedPlayerLeds();

            _ipc.SetRgbColor(r, g, b, effect, playerLeds, speed);
        }

        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not string tag) return;

            var parts = tag.Split(',');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int r) &&
                int.TryParse(parts[1], out int g) &&
                int.TryParse(parts[2], out int b))
            {
                _isUpdatingUi = true;
                if (SliderR != null) SliderR.Value = r;
                if (SliderG != null) SliderG.Value = g;
                if (SliderB != null) SliderB.Value = b;
                if (TxtValR != null) TxtValR.Text = r.ToString();
                if (TxtValG != null) TxtValG.Text = g.ToString();
                if (TxtValB != null) TxtValB.Text = b.ToString();

                var color = Color.FromRgb((byte)r, (byte)g, (byte)b);
                if (ColorPreviewBox != null) ColorPreviewBox.Background = new SolidColorBrush(color);
                if (TxtHexCode != null) TxtHexCode.Text = $"#{r:X2}{g:X2}{b:X2}";
                if (TxtGlowSub != null) TxtGlowSub.Text = "Active Solid Color";
                if (ComboEffect != null) ComboEffect.SelectedIndex = 0; // Set to Solid
                _isUpdatingUi = false;

                float speed = (float)(SliderEffectSpeed?.Value ?? 1.0);
                _ipc.SetRgbColor(r, g, b, 0, GetSelectedPlayerLeds(), speed);
            }
        }

        private void ComboEffect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || SliderR == null || SliderG == null || SliderB == null || ComboEffect == null) return;
            int r = (int)SliderR.Value;
            int g = (int)SliderG.Value;
            int b = (int)SliderB.Value;
            int effect = ComboEffect.SelectedIndex;
            float speed = (float)(SliderEffectSpeed?.Value ?? 1.0);

            if (effect == 0 && ColorPreviewBox != null && TxtHexCode != null)
            {
                var color = Color.FromRgb((byte)r, (byte)g, (byte)b);
                ColorPreviewBox.Background = new SolidColorBrush(color);
                TxtHexCode.Text = $"#{r:X2}{g:X2}{b:X2}";
                if (TxtGlowSub != null) TxtGlowSub.Text = "Active Solid Color";
            }

            _ipc.SetRgbColor(r, g, b, effect, GetSelectedPlayerLeds(), speed);
        }

        private int GetSelectedPlayerLeds()
        {
            if (RadioP2?.IsChecked == true) return 0x0A; // Player 2
            if (RadioP3?.IsChecked == true) return 0x15; // Player 3
            if (RadioP4?.IsChecked == true) return 0x1B; // Player 4
            return 0x04; // Player 1 (Center)
        }

        private void PlayerLed_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || SliderR == null || SliderG == null || SliderB == null) return;
            int r = (int)SliderR.Value;
            int g = (int)SliderG.Value;
            int b = (int)SliderB.Value;
            int effect = ComboEffect?.SelectedIndex ?? 0;
            float speed = (float)(SliderEffectSpeed?.Value ?? 1.0);
            _ipc.SetRgbColor(r, g, b, effect, GetSelectedPlayerLeds(), speed);
        }
        #endregion

        #region Adaptive Triggers
        private void TriggerMode_Changed(object sender, EventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || ComboLTMode == null || ComboRTMode == null ||
                SliderLTStart == null || SliderLTForce == null || SliderLTFreq == null ||
                SliderRTStart == null || SliderRTForce == null || SliderRTFreq == null) return;

            static int MapMode(int idx) => idx switch
            {
                1 => 6,
                2 => 1,
                3 => 2,
                4 => 5,
                _ => 0
            };

            int ltMode = MapMode(ComboLTMode.SelectedIndex);
            int ltStart = (int)SliderLTStart.Value;
            int ltForce = (int)SliderLTForce.Value;
            int ltFreq = (int)SliderLTFreq.Value;

            int rtMode = MapMode(ComboRTMode.SelectedIndex);
            int rtStart = (int)SliderRTStart.Value;
            int rtForce = (int)SliderRTForce.Value;
            int rtFreq = (int)SliderRTFreq.Value;

            _ipc.SetAdaptiveTriggers(ltMode, ltStart, ltForce, ltFreq, rtMode, rtStart, rtForce, rtFreq);
        }
        #endregion

        #region Touchpad
        private void TouchpadConfig_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || ChkMouseEnabled == null || SliderMouseSens == null || SliderScrollSens == null || ChkTapClick == null) return;

            bool enabled = ChkMouseEnabled.IsChecked == true;
            float mouseSens = (float)SliderMouseSens.Value;
            float scrollSens = (float)SliderScrollSens.Value;
            bool tapClick = ChkTapClick.IsChecked == true;

            if (TxtMouseSensVal != null) TxtMouseSensVal.Text = $"{mouseSens:F1}x";
            if (TxtScrollSensVal != null) TxtScrollSensVal.Text = $"{scrollSens:F1}x";

            _ipc.SetTouchpad(enabled, mouseSens, scrollSens, tapClick);
        }
        #endregion

        #region Audio & Mic
        private void MicSync_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || ChkSyncMic == null) return;
            bool sync = ChkSyncMic.IsChecked == true;
            _ipc.SetMicSync(sync);
        }

        private void AudioHaptics_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || ChkAudioHaptics == null || SliderHapticIntensity == null || SliderHapticCutoff == null) return;

            bool enabled = ChkAudioHaptics.IsChecked == true;
            float intensity = (float)SliderHapticIntensity.Value;
            int cutoff = (int)SliderHapticCutoff.Value;
            bool stereo = RadioHapticStereo?.IsChecked == true;
            bool triggerKick = ChkTriggerKick?.IsChecked == true;
            float kickThreshold = SliderTriggerThreshold != null ? (float)SliderTriggerThreshold.Value : 0.45f;

            if (TxtHapticIntensityVal != null) TxtHapticIntensityVal.Text = $"{(int)(intensity * 100)}%";
            if (TxtHapticCutoffVal != null) TxtHapticCutoffVal.Text = $"{cutoff} Hz";
            if (TxtTriggerThresholdVal != null) TxtTriggerThresholdVal.Text = $"{(int)(kickThreshold * 100)}%";

            // Throttle / debounce IPC sends to prevent pipe spam during fast dragging
            var now = DateTime.UtcNow;
            if ((now - _lastHapticSendTime).TotalMilliseconds >= 40)
            {
                _lastHapticSendTime = now;
                _ipc.SetAudioHaptics(enabled, intensity, cutoff, stereo, triggerKick, kickThreshold);
            }
            else
            {
                _hapticDebounceTimer?.Stop();
                _hapticDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
                _hapticDebounceTimer.Tick += (s, args) =>
                {
                    _hapticDebounceTimer.Stop();
                    _lastHapticSendTime = DateTime.UtcNow;
                    _ipc.SetAudioHaptics(enabled, intensity, cutoff, stereo, triggerKick, kickThreshold);
                };
                _hapticDebounceTimer.Start();
            }
        }
        #endregion

        #region Driver Management
        private void BtnStartEngine_Click(object sender, RoutedEventArgs e)
        {
            _driver.StartDriverEngine();
            TxtDriverLog.Text = "OpenDualDriver engine launched in high-priority multimedia mode.";
            RefreshDriverStatus();
        }

        private void BtnRestartEngine_Click(object sender, RoutedEventArgs e)
        {
            _driver.StopDriverEngine();
            System.Threading.Thread.Sleep(300);
            _driver.StartDriverEngine();
            TxtDriverLog.Text = "OpenDualDriver engine restarted.";
            RefreshDriverStatus();
        }

        private void BtnStopEngine_Click(object sender, RoutedEventArgs e)
        {
            _driver.StopDriverEngine();
            TxtDriverLog.Text = "OpenDualDriver engine stopped.";
            RefreshDriverStatus();
        }

        private async void BtnInstallViGEm_Click(object sender, RoutedEventArgs e)
        {
            BtnInstallViGEm.IsEnabled = false;
            TxtDriverLog.Text = "Starting ViGEmBus installation...";

            bool success = await _driver.InstallViGEmBusAsync(msg =>
            {
                Dispatcher.Invoke(() => TxtDriverLog.Text = msg);
            });

            BtnInstallViGEm.IsEnabled = true;
            TxtDriverLog.Text = success ? "ViGEmBus Driver installed successfully!" : "ViGEmBus installation cancelled or failed.";
            RefreshDriverStatus();
        }
        #endregion
    }
}
