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

        private readonly ProfileManager _profileManager = new();
        private GameDetector? _gameDetector;
        private readonly UpdateChecker _updateChecker = new();
        private UpdateInfo? _latestUpdateInfo;
        private GameProfile _currentProfile = new();
        private bool _lowBatteryWarned = false;
        private bool _isProfileApplying = false;
        private OverclockAssistantWindow? _overclockAssistant;
        private CaptureBarWindow? _captureBar;
        private bool _lastShareState = false;
        private DateTime _sharePressStartTime = DateTime.MinValue;
        private bool _shareLongPressHandled = false;
        private DateTime _lastShareReleaseTime = DateTime.MinValue;

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

            _trayManager.Initialize(helper.Handle, "OpenDual - PlayStation Controller Suite");
            _trayManager.DoubleClick += RestoreFromTray;
            _trayManager.RightClick += ShowTrayContextMenu;

            _quickHud = new QuickHudWindow();
            _quickHud.OnCycleProfile = CycleProfile;
            _quickHud.OnAdjustTouchpadSens = AdjustTouchpadSensitivity;
            _quickHud.OnToggleTouchpad = ToggleTouchpadEnabled;
            _quickHud.OnAdjustGyroSens = AdjustGyroSensitivity;
            _quickHud.OnToggleGyro = ToggleGyroEnabled;
            _quickHud.OnToggleMic = ToggleMicMute;
            _quickHud.OnHudScaleChanged = OnHudScaleChangedFromHud;
            _quickHud.GetActiveGameName = () => _gameDetector?.CurrentProcessName ?? "";
            _quickHud.GetActiveProfileName = () => _currentProfile?.Name ?? "Default Profile";
            _captureBar = new CaptureBarWindow();
            _captureBar.GetActiveGameName = () => _gameDetector?.CurrentProcessName ?? "";
            RefreshGameDvrStatus();
            UpdateResponsiveLayout(ActualWidth);

            // Game-Specific Auto Profile Switching
            _gameDetector = new GameDetector(_profileManager);
            _gameDetector.OnGameChanged += GameDetector_OnGameChanged;
            _gameDetector.Start();

            PopulateProfilesList();

            // Background update check (non-blocking)
            _ = CheckForUpdatesSilentlyAsync();
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

            var itemHud = new MenuItem { Header = "Preview Quick HUD (PS Button Overlay)" };
            itemHud.Click += (s, e) => _quickHud?.ToggleOrRefresh();
            menu.Items.Add(itemHud);

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
            _gameDetector?.Dispose();
            _trayManager.Dispose();
            _quickHud?.Close();
            _overclockAssistant?.Close();
            _captureBar?.Close();
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

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }

        private void NavTabsScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                if (e.Delta < 0)
                    sv.LineRight();
                else
                    sv.LineLeft();
                e.Handled = true;
            }
        }

        private void EnsureNavTabVisible(RadioButton? tab)
        {
            tab?.BringIntoView();
        }

        private void UpdateResponsiveLayout(double width)
        {
            if (!_isInitialized) return;

            // 1. Clock visibility in header
            if (TxtClock != null)
            {
                TxtClock.Visibility = width < 780 ? Visibility.Collapsed : Visibility.Visible;
            }

            // 2. Dashboard 2-column vs 1-column stacked
            if (DashLeftCol != null && DashRightCol != null && DashColDefRight != null)
            {
                if (width < 920)
                {
                    Grid.SetRow(DashRightCol, 1);
                    Grid.SetColumn(DashRightCol, 0);
                    DashRightCol.Margin = new Thickness(0, 16, 0, 0);
                    DashLeftCol.Margin = new Thickness(0, 0, 0, 0);
                    DashColDefRight.Width = new GridLength(0);
                }
                else
                {
                    Grid.SetRow(DashRightCol, 0);
                    Grid.SetColumn(DashRightCol, 1);
                    DashRightCol.Margin = new Thickness(10, 0, 0, 0);
                    DashLeftCol.Margin = new Thickness(0, 0, 10, 0);
                    DashColDefRight.Width = new GridLength(1, GridUnitType.Star);
                }
            }

            // 3. Triggers 2-column vs 1-column stacked
            if (TriggersLeftCol != null && TriggersRightCol != null && TriggersColDefRight != null)
            {
                if (width < 880)
                {
                    Grid.SetRow(TriggersRightCol, 1);
                    Grid.SetColumn(TriggersRightCol, 0);
                    TriggersRightCol.Margin = new Thickness(0, 14, 0, 0);
                    TriggersLeftCol.Margin = new Thickness(0, 0, 0, 0);
                    TriggersColDefRight.Width = new GridLength(0);
                }
                else
                {
                    Grid.SetRow(TriggersRightCol, 0);
                    Grid.SetColumn(TriggersRightCol, 1);
                    TriggersRightCol.Margin = new Thickness(12, 0, 0, 0);
                    TriggersLeftCol.Margin = new Thickness(0, 0, 12, 0);
                    TriggersColDefRight.Width = new GridLength(1, GridUnitType.Star);
                }
            }

            // 4. RGB 2-column vs 1-column stacked
            if (RgbLeftCol != null && RgbRightCol != null && RgbColDefRight != null)
            {
                if (width < 880)
                {
                    Grid.SetRow(RgbRightCol, 1);
                    Grid.SetColumn(RgbRightCol, 0);
                    RgbRightCol.Margin = new Thickness(0, 18, 0, 0);
                    RgbLeftCol.Margin = new Thickness(0, 0, 0, 0);
                    RgbColDefRight.Width = new GridLength(0);
                }
                else
                {
                    Grid.SetRow(RgbRightCol, 0);
                    Grid.SetColumn(RgbRightCol, 1);
                    RgbRightCol.Margin = new Thickness(0, 0, 0, 0);
                    RgbLeftCol.Margin = new Thickness(0, 0, 24, 0);
                    RgbColDefRight.Width = new GridLength(1, GridUnitType.Star);
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
            _gameDetector?.Dispose();
            _ipc.Dispose();
            _trayManager.Dispose();
            _quickHud?.Close();
            _overclockAssistant?.Close();
            _captureBar?.Close();
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

                RefreshGameDvrStatus();
            });
        }

        #region Navigation
        private void HideAllViews()
        {
            if (ViewDashboard == null) return;
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewRgb.Visibility = Visibility.Collapsed;
            ViewTriggers.Visibility = Visibility.Collapsed;
            if (ViewGyro != null) ViewGyro.Visibility = Visibility.Collapsed;
            ViewTouchpad.Visibility = Visibility.Collapsed;
            ViewAudio.Visibility = Visibility.Collapsed;
            if (ViewProfiles != null) ViewProfiles.Visibility = Visibility.Collapsed;
            ViewDriver.Visibility = Visibility.Collapsed;
        }

        private void Nav_Dashboard_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewDashboard != null) ViewDashboard.Visibility = Visibility.Visible;
            EnsureNavTabVisible(TabController);
        }

        private void Nav_Rgb_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewRgb != null) ViewRgb.Visibility = Visibility.Visible;
            EnsureNavTabVisible(TabLightbar);
        }

        private void Nav_Triggers_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewTriggers != null) ViewTriggers.Visibility = Visibility.Visible;
            EnsureNavTabVisible(TabTriggers);
        }

        private void Nav_Gyro_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewGyro != null) ViewGyro.Visibility = Visibility.Visible;
            EnsureNavTabVisible(TabGyro);
        }

        private void Nav_Touchpad_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewTouchpad != null) ViewTouchpad.Visibility = Visibility.Visible;
            EnsureNavTabVisible(TabTouchpad);
        }

        private void Nav_Audio_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewAudio != null) ViewAudio.Visibility = Visibility.Visible;
            EnsureNavTabVisible(TabAudio);
        }

        private void Nav_Profiles_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewProfiles != null) ViewProfiles.Visibility = Visibility.Visible;
            EnsureNavTabVisible(TabProfiles);
        }

        private void Nav_Driver_Checked(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            if (ViewDriver != null)
            {
                ViewDriver.Visibility = Visibility.Visible;
                RefreshDriverStatus();
            }
            EnsureNavTabVisible(TabSystem);
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

                        if (t.PollingRateHz >= 900)
                        {
                            if (DashOverclockLabel != null) DashOverclockLabel.Text = "1000 Hz Active";
                            if (DashOverclockSub != null) DashOverclockSub.Text = "Esports 1.0ms";
                            if (BtnDashOverclock != null)
                            {
                                BtnDashOverclock.Background = new SolidColorBrush(Color.FromRgb(19, 51, 36));
                                BtnDashOverclock.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                            }
                            if (TxtOverclockStatus != null)
                            {
                                TxtOverclockStatus.Text = $"{t.PollingRateHz} Hz ({t.LatencyMs:F1} ms) • Esports Active";
                                TxtOverclockStatus.Foreground = (Brush)FindResource("AccentGreen");
                            }
                        }
                        else if (t.PollingRateHz > 0)
                        {
                            if (DashOverclockLabel != null) DashOverclockLabel.Text = "1000 Hz Assistant";
                            if (DashOverclockSub != null) DashOverclockSub.Text = "Boost to 1ms";
                            if (BtnDashOverclock != null)
                            {
                                BtnDashOverclock.Background = new SolidColorBrush(Color.FromRgb(34, 26, 60));
                                BtnDashOverclock.BorderBrush = new SolidColorBrush(Color.FromRgb(124, 77, 255));
                            }
                            if (TxtOverclockStatus != null)
                            {
                                TxtOverclockStatus.Text = $"{t.PollingRateHz} Hz ({t.LatencyMs:F1} ms) • Default";
                                TxtOverclockStatus.Foreground = (Brush)FindResource("AccentAmber");
                            }
                        }

                        _overclockAssistant?.UpdateTelemetry(t);

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
                        if (PillFnL != null) HighlightPill(PillFnL, t.FnLeft);
                        if (PillFnR != null) HighlightPill(PillFnR, t.FnRight);
                        if (PillPadL != null) HighlightPill(PillPadL, t.PaddleLeft);
                        if (PillPadR != null) HighlightPill(PillPadR, t.PaddleRight);

                        // Live Gyro IMU Telemetry
                        if (BadgeGyroAiming != null)
                        {
                            BadgeGyroAiming.Visibility = t.GyroActive ? Visibility.Visible : Visibility.Collapsed;
                        }
                        if (BarGyroZ != null) BarGyroZ.Value = Math.Clamp(Math.Abs((int)t.GyroZ), 0, 500);
                        if (BarGyroX != null) BarGyroX.Value = Math.Clamp(Math.Abs((int)t.GyroX), 0, 500);
                        if (BarGyroY != null) BarGyroY.Value = Math.Clamp(Math.Abs((int)t.GyroY), 0, 500);
                        if (TxtGyroYaw != null) TxtGyroYaw.Text = $"Yaw: {t.GyroZ:+0000;-0000;0}";
                        if (TxtGyroPitch != null) TxtGyroPitch.Text = $"Pitch: {t.GyroX:+0000;-0000;0}";
                        if (TxtGyroRoll != null) TxtGyroRoll.Text = $"Roll: {t.GyroY:+0000;-0000;0}";

                        // Battery Low Alert & Visual Badge
                        if (t.Battery <= 15 && !t.Charging)
                        {
                            if (DashBatteryLowBadge != null) DashBatteryLowBadge.Visibility = Visibility.Visible;
                            if (TxtBatteryHeader != null) TxtBatteryHeader.Foreground = (SolidColorBrush)FindResource("AccentRed");
                            if (!_lowBatteryWarned)
                            {
                                _lowBatteryWarned = true;
                                _trayManager.ShowNotification("OpenDual Battery Warning", $"DualSense battery is critical ({t.Battery}%). Please connect USB charging cable.");
                            }
                        }
                        else
                        {
                            if (DashBatteryLowBadge != null) DashBatteryLowBadge.Visibility = Visibility.Collapsed;
                            if (TxtBatteryHeader != null) TxtBatteryHeader.Foreground = (SolidColorBrush)FindResource("TextPrimary");
                            if (t.Battery > 20 || t.Charging) _lowBatteryWarned = false;
                        }

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

                        // Dynamic Audio Endpoint Names
                        if (!string.IsNullOrEmpty(t.RenderDev) && TxtRenderDev != null && TxtRenderDev.Text != t.RenderDev)
                            TxtRenderDev.Text = t.RenderDev;
                        if (!string.IsNullOrEmpty(t.CaptureDev) && TxtCaptureDev != null && TxtCaptureDev.Text != t.CaptureDev)
                            TxtCaptureDev.Text = t.CaptureDev;

                        // In-Game Quick HUD Update & PS Button Detection
                        if (_quickHud != null)
                        {
                            _quickHud.UpdateState(t);
                            if (_quickHud.IsVisibleToUser)
                            {
                                _quickHud.HandleControllerNavigation(t);
                            }
                            if (t.PS && !_lastPsState && ChkEnableQuickHud?.IsChecked == true)
                            {
                                if (_captureBar != null && _captureBar.IsVisibleToUser)
                                {
                                    _captureBar.HideBar();
                                }
                                _quickHud.ToggleOrRefresh();
                            }
                        }
                        _lastPsState = t.PS;

                        // In-Game Create / Capture Bar Update & Share Button Detection (PS5 Style)
                        if (_captureBar != null)
                        {
                            if (_captureBar.IsVisibleToUser)
                            {
                                _captureBar.HandleControllerNavigation(t);
                            }

                            if (ChkEnableShareCapture?.IsChecked != false)
                            {
                                if (t.Share && !_lastShareState)
                                {
                                    _sharePressStartTime = DateTime.UtcNow;
                                    _shareLongPressHandled = false;
                                }
                                else if (t.Share && _lastShareState)
                                {
                                    if (!_shareLongPressHandled && (DateTime.UtcNow - _sharePressStartTime).TotalMilliseconds >= 800)
                                    {
                                        _shareLongPressHandled = true;
                                        GameCaptureManager.TakeScreenshot();
                                        _captureBar.ShowInstantNotification("📸 Screenshot Captured (Win+Alt+PrtScn)");
                                    }
                                }
                                else if (!t.Share && _lastShareState)
                                {
                                    if (!_shareLongPressHandled)
                                    {
                                        DateTime now = DateTime.UtcNow;
                                        if ((now - _lastShareReleaseTime).TotalMilliseconds <= 350)
                                        {
                                            GameCaptureManager.SaveRecentClip();
                                            _captureBar.ShowInstantNotification("⏱️ Instant Clip Saved (Win+Alt+G)");
                                            _lastShareReleaseTime = DateTime.MinValue;
                                        }
                                        else
                                        {
                                            _lastShareReleaseTime = now;
                                            if (_quickHud != null && _quickHud.IsVisibleToUser)
                                            {
                                                _quickHud.HideHud();
                                            }
                                            _captureBar.ToggleOrShow();
                                        }
                                    }
                                }
                            }
                        }
                        _lastShareState = t.Share;
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
                        if (TxtOverclockStatus != null)
                        {
                            TxtOverclockStatus.Text = "No Controller Connected";
                            TxtOverclockStatus.Foreground = (Brush)FindResource("TextSecondary");
                        }
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

            string cutoffDesc = cutoff switch
            {
                <= 60 => "Sub-Bass",
                <= 100 => "Punchy",
                <= 160 => "Deep Bass",
                _ => "Full Low"
            };
            if (TxtHapticIntensityVal != null) TxtHapticIntensityVal.Text = $"{(int)(intensity * 100)}%";
            if (TxtHapticCutoffVal != null) TxtHapticCutoffVal.Text = $"{cutoff} Hz ({cutoffDesc})";
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

        private void BtnDashOverclock_Click(object sender, RoutedEventArgs e)
        {
            OpenOverclockAssistant();
        }

        private void BtnOpenOverclockAssistant_Click(object sender, RoutedEventArgs e)
        {
            OpenOverclockAssistant();
        }

        private void OpenOverclockAssistant()
        {
            if (_overclockAssistant == null || !_overclockAssistant.IsLoaded)
            {
                _overclockAssistant = new OverclockAssistantWindow
                {
                    Owner = this
                };
                _overclockAssistant.Closed += (s, ev) => _overclockAssistant = null;
                _overclockAssistant.Show();
            }
            else
            {
                _overclockAssistant.Activate();
            }
        }

        private void CmbHudScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _quickHud == null || CmbHudScale?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is string tagStr && double.TryParse(tagStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double scale))
            {
                _quickHud.SetHudScale(scale);
                _captureBar?.SetBarScale(scale);
            }
        }

        private void OnHudScaleChangedFromHud(double scale)
        {
            Dispatcher.Invoke(() =>
            {
                _captureBar?.SetBarScale(scale);
                if (CmbHudScale == null) return;
                for (int i = 0; i < CmbHudScale.Items.Count; i++)
                {
                    if (CmbHudScale.Items[i] is ComboBoxItem item && item.Tag is string tagStr &&
                        double.TryParse(tagStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) &&
                        Math.Abs(s - scale) < 0.05)
                    {
                        CmbHudScale.SelectedIndex = i;
                        break;
                    }
                }
            });
        }

        private void BtnPreviewQuickHud_Click(object sender, RoutedEventArgs e)
        {
            _quickHud?.ToggleOrRefresh();
        }

        private void BtnPreviewCaptureBar_Click(object sender, RoutedEventArgs e)
        {
            _captureBar?.ToggleOrShow();
        }

        private void CmbClipDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbClipDuration?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
            {
                GameCaptureManager.SetHistoricalBufferLengthSeconds(seconds);
                RefreshGameDvrStatus();
            }
        }

        private void BtnGameDvrSettings_Click(object sender, RoutedEventArgs e)
        {
            GameCaptureManager.OpenGameDvrSettings();
        }

        private void BtnOpenCapturesGallery_Click(object sender, RoutedEventArgs e)
        {
            GameCaptureManager.OpenCapturesFolder();
        }

        private void RefreshGameDvrStatus()
        {
            if (TxtGameDvrStatus == null) return;
            bool enabled = GameCaptureManager.IsGameDvrEnabled();
            int sec = GameCaptureManager.GetHistoricalBufferLengthSeconds();

            if (enabled)
            {
                TxtGameDvrStatus.Text = $"Active - Instant Replay Buffer: {sec}s (Win+Alt+G ready)";
                TxtGameDvrStatus.Foreground = (SolidColorBrush)FindResource("AccentGreen");
            }
            else
            {
                TxtGameDvrStatus.Text = "GameDVR Disabled in Windows Settings (Click Captures Settings to enable)";
                TxtGameDvrStatus.Foreground = (SolidColorBrush)FindResource("AccentAmber");
            }

            if (CmbClipDuration != null)
            {
                for (int i = 0; i < CmbClipDuration.Items.Count; i++)
                {
                    if (CmbClipDuration.Items[i] is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int val) && val == sec)
                    {
                        CmbClipDuration.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
        #endregion

        #region Gyro Motion Aiming
        private void Gyro_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || _isProfileApplying) return;

            bool enabled = ChkEnableGyro?.IsChecked == true;
            int mode = CmbGyroMode?.SelectedIndex ?? 1;
            float sensX = (float)(SliderGyroSensX?.Value ?? 1.0);
            float sensY = (float)(SliderGyroSensY?.Value ?? 1.0);
            bool invX = ChkGyroInvertX?.IsChecked == true;
            bool invY = ChkGyroInvertY?.IsChecked == true;
            int deadzone = (int)(SliderGyroDeadzone?.Value ?? 20);
            float smooth = (float)(SliderGyroSmooth?.Value ?? 0.25);

            if (TxtGyroSensXVal != null) TxtGyroSensXVal.Text = $"{sensX:F1}x";
            if (TxtGyroSensYVal != null) TxtGyroSensYVal.Text = $"{sensY:F1}x";
            if (TxtGyroDeadzoneVal != null) TxtGyroDeadzoneVal.Text = $"{deadzone}";
            if (TxtGyroSmoothVal != null) TxtGyroSmoothVal.Text = $"{(int)(smooth * 100)}%";

            _ipc.SetGyro(enabled, mode, sensX, sensY, invX, invY, deadzone, smooth);
        }

        private void BtnResetGyroDrift_Click(object sender, RoutedEventArgs e)
        {
            Gyro_Changed(sender, e);
            if (FooterStatus != null) FooterStatus.Text = "Gyro resting baseline recalibrated. Keep controller still for 1 second.";
        }
        #endregion

        #region Game Profiles & Presets
        private void PopulateProfilesList()
        {
            if (CmbProfiles == null) return;
            CmbProfiles.Items.Clear();

            foreach (var profile in _profileManager.Profiles)
            {
                var item = new ComboBoxItem
                {
                    Content = profile.Name + (profile.IsBuiltIn ? " (Built-in)" : ""),
                    Tag = profile.Id
                };
                CmbProfiles.Items.Add(item);
            }

            if (CmbProfiles.Items.Count > 0)
            {
                CmbProfiles.SelectedIndex = 0;
            }
        }

        private void CmbProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProfiles.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var prof = _profileManager.Profiles.FirstOrDefault(p => p.Id == id);
                if (prof != null)
                {
                    if (TxtProfileName != null) TxtProfileName.Text = prof.Name;
                    if (TxtProfileDesc != null) TxtProfileDesc.Text = prof.Description;
                    if (TxtProfileProcesses != null) TxtProfileProcesses.Text = prof.ProcessNames;
                    if (BtnDeleteProfile != null) BtnDeleteProfile.IsEnabled = !prof.IsBuiltIn;

                    if (TxtSummaryTriggers != null)
                        TxtSummaryTriggers.Text = $"L2: {FormatTriggerModeName(prof.Config.LtMode)} • R2: {FormatTriggerModeName(prof.Config.RtMode)}";
                    if (TxtSummaryHaptics != null)
                        TxtSummaryHaptics.Text = $"RGB: #{prof.Config.R:X2}{prof.Config.G:X2}{prof.Config.B:X2} • Haptics: {(int)(prof.Config.HapticIntensity * 100)}% {(prof.Config.AudioTriggerKick ? "• Recoil Kick" : "")}";
                    if (TxtSummaryGyro != null)
                        TxtSummaryGyro.Text = $"Gyro: {(prof.Config.Gyro ? "Enabled" : "Disabled")} • Touchpad Mouse: {(prof.Config.Mouse ? "On" : "Off")}";
                }
            }
        }

        private static string FormatTriggerModeName(int mode) => mode switch
        {
            1 => "Rigid Resistance",
            2 => "Pulse Vibration",
            5 => "Bow Draw Curve",
            6 => "TriggerStop (Hair)",
            _ => "Standard (Off)"
        };

        private void BtnApplyProfile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbProfiles.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var prof = _profileManager.Profiles.FirstOrDefault(p => p.Id == id);
                if (prof != null)
                {
                    ApplyProfile(prof);
                }
            }
        }

        private void ApplyProfile(GameProfile prof)
        {
            _currentProfile = prof;
            _isProfileApplying = true;
            try
            {
                // Send full config to driver
                _ipc.SendConfig(prof.Config);

                // Update UI controls to match profile
                if (TxtDashProfileName != null) TxtDashProfileName.Text = prof.Name;
                if (ChkEnableGyro != null) ChkEnableGyro.IsChecked = prof.Config.Gyro;
                if (CmbGyroMode != null) CmbGyroMode.SelectedIndex = prof.Config.GyroMode;
                if (SliderGyroSensX != null) SliderGyroSensX.Value = prof.Config.GyroSensX;
                if (SliderGyroSensY != null) SliderGyroSensY.Value = prof.Config.GyroSensY;
                if (ChkGyroInvertX != null) ChkGyroInvertX.IsChecked = prof.Config.GyroInvertX;
                if (ChkGyroInvertY != null) ChkGyroInvertY.IsChecked = prof.Config.GyroInvertY;
                if (SliderGyroDeadzone != null) SliderGyroDeadzone.Value = prof.Config.GyroDeadzone;
                if (SliderGyroSmooth != null) SliderGyroSmooth.Value = prof.Config.GyroSmooth;

                if (ChkAudioHaptics != null) ChkAudioHaptics.IsChecked = prof.Config.Haptic;
                if (SliderHapticIntensity != null) SliderHapticIntensity.Value = prof.Config.HapticIntensity;
                if (SliderHapticCutoff != null) SliderHapticCutoff.Value = prof.Config.HapticCutoff;
                if (ChkTriggerKick != null) ChkTriggerKick.IsChecked = prof.Config.AudioTriggerKick;
                if (SliderTriggerThreshold != null) SliderTriggerThreshold.Value = prof.Config.AudioTriggerThreshold;

                static int UnmapTriggerIndex(int triggerMode) => triggerMode switch
                {
                    6 => 1, // Hair Trigger
                    1 => 2, // Rigid Stop
                    2 => 3, // Pulse
                    5 => 4, // Bow
                    _ => 0  // Off
                };

                if (ComboLTMode != null) ComboLTMode.SelectedIndex = UnmapTriggerIndex(prof.Config.LtMode);
                if (ComboRTMode != null) ComboRTMode.SelectedIndex = UnmapTriggerIndex(prof.Config.RtMode);
                if (SliderR != null) SliderR.Value = prof.Config.R;
                if (SliderG != null) SliderG.Value = prof.Config.G;
                if (SliderB != null) SliderB.Value = prof.Config.B;
                if (ComboEffect != null) ComboEffect.SelectedIndex = Math.Clamp(prof.Config.Effect, 0, 3);

                if (FooterStatus != null) FooterStatus.Text = $"Active Profile: {prof.Name}";
            }
            finally
            {
                _isProfileApplying = false;
            }
        }

        private void CycleProfile(int direction)
        {
            Dispatcher.Invoke(() =>
            {
                var profiles = _profileManager.Profiles;
                if (profiles.Count == 0) return;
                int currentIndex = 0;
                if (_currentProfile != null)
                {
                    for (int i = 0; i < profiles.Count; i++)
                    {
                        if (profiles[i].Id == _currentProfile.Id)
                        {
                            currentIndex = i;
                            break;
                        }
                    }
                }
                int nextIndex = (currentIndex + direction + profiles.Count) % profiles.Count;
                ApplyProfile(profiles[nextIndex]);
                if (CmbProfiles != null && nextIndex < CmbProfiles.Items.Count)
                {
                    CmbProfiles.SelectedIndex = nextIndex;
                }
            });
        }

        private void AdjustTouchpadSensitivity(float delta)
        {
            Dispatcher.Invoke(() =>
            {
                if (SliderMouseSens != null)
                {
                    double current = SliderMouseSens.Value;
                    double next = Math.Clamp(Math.Round(current + delta, 1), 0.2, 3.0);
                    SliderMouseSens.Value = next;
                }
            });
        }

        private void ToggleTouchpadEnabled()
        {
            Dispatcher.Invoke(() =>
            {
                if (ChkMouseEnabled != null)
                {
                    ChkMouseEnabled.IsChecked = !(ChkMouseEnabled.IsChecked == true);
                }
            });
        }

        private void AdjustGyroSensitivity(float delta)
        {
            Dispatcher.Invoke(() =>
            {
                if (SliderGyroSensX != null)
                {
                    double current = SliderGyroSensX.Value;
                    double next = Math.Clamp(Math.Round(current + delta, 1), 0.2, 4.0);
                    SliderGyroSensX.Value = next;
                    if (SliderGyroSensY != null) SliderGyroSensY.Value = next;
                }
            });
        }

        private void ToggleGyroEnabled()
        {
            Dispatcher.Invoke(() =>
            {
                if (ChkEnableGyro != null)
                {
                    ChkEnableGyro.IsChecked = !(ChkEnableGyro.IsChecked == true);
                }
            });
        }

        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbProfiles.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var prof = _profileManager.Profiles.FirstOrDefault(p => p.Id == id);
                if (prof != null)
                {
                    if (prof.IsBuiltIn)
                    {
                        var copy = prof.Clone((TxtProfileName?.Text.Trim() ?? "Custom") + " (Custom)");
                        if (TxtProfileDesc != null) copy.Description = TxtProfileDesc.Text.Trim();
                        if (TxtProfileProcesses != null) copy.ProcessNames = TxtProfileProcesses.Text.Trim();
                        CaptureCurrentConfig(copy.Config);
                        _profileManager.SaveProfile(copy);
                        PopulateProfilesList();
                        SelectProfileInCombo(copy.Id);
                        MessageBox.Show($"Built-in preset saved as new custom profile: '{copy.Name}'", "Profile Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        if (TxtProfileName != null) prof.Name = TxtProfileName.Text.Trim();
                        if (TxtProfileDesc != null) prof.Description = TxtProfileDesc.Text.Trim();
                        if (TxtProfileProcesses != null) prof.ProcessNames = TxtProfileProcesses.Text.Trim();
                        CaptureCurrentConfig(prof.Config);
                        _profileManager.SaveProfile(prof);
                        PopulateProfilesList();
                        SelectProfileInCombo(prof.Id);
                        MessageBox.Show($"Profile '{prof.Name}' updated successfully.", "Profile Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void CaptureCurrentConfig(ConfigDto cfg)
        {
            cfg.R = (int)(SliderR?.Value ?? 0);
            cfg.G = (int)(SliderG?.Value ?? 120);
            cfg.B = (int)(SliderB?.Value ?? 255);
            cfg.Effect = ComboEffect?.SelectedIndex ?? 0;

            static int MapTriggerMode(int idx) => idx switch
            {
                1 => 6, // Hair Trigger
                2 => 1, // Rigid Stop
                3 => 2, // Pulse
                4 => 5, // Bow
                _ => 0  // Off
            };

            cfg.LtMode = MapTriggerMode(ComboLTMode?.SelectedIndex ?? 0);
            cfg.RtMode = MapTriggerMode(ComboRTMode?.SelectedIndex ?? 0);
            cfg.Haptic = ChkAudioHaptics?.IsChecked == true;
            cfg.HapticIntensity = (float)(SliderHapticIntensity?.Value ?? 1.0);
            cfg.HapticCutoff = (int)(SliderHapticCutoff?.Value ?? 100);
            cfg.AudioTriggerKick = ChkTriggerKick?.IsChecked == true;
            cfg.AudioTriggerThreshold = (float)(SliderTriggerThreshold?.Value ?? 0.45);
            cfg.Gyro = ChkEnableGyro?.IsChecked == true;
            cfg.GyroMode = CmbGyroMode?.SelectedIndex ?? 1;
            cfg.GyroSensX = (float)(SliderGyroSensX?.Value ?? 1.0);
            cfg.GyroSensY = (float)(SliderGyroSensY?.Value ?? 1.0);
            cfg.GyroInvertX = ChkGyroInvertX?.IsChecked == true;
            cfg.GyroInvertY = ChkGyroInvertY?.IsChecked == true;
            cfg.GyroDeadzone = (int)(SliderGyroDeadzone?.Value ?? 20);
            cfg.GyroSmooth = (float)(SliderGyroSmooth?.Value ?? 0.25);
            cfg.IdleTimeout = CmbIdleTimeout?.SelectedIndex switch { 1 => 5, 2 => 10, 3 => 15, 4 => 30, _ => 0 };
            cfg.LowBatWarn = ChkLowBatteryWarn?.IsChecked == true;
        }

        private void SelectProfileInCombo(string id)
        {
            for (int i = 0; i < CmbProfiles.Items.Count; i++)
            {
                if (CmbProfiles.Items[i] is ComboBoxItem ci && ci.Tag as string == id)
                {
                    CmbProfiles.SelectedIndex = i;
                    break;
                }
            }
        }

        private void BtnDuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbProfiles.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var prof = _profileManager.Profiles.FirstOrDefault(p => p.Id == id);
                if (prof != null)
                {
                    var copy = prof.Clone(prof.Name + " (Copy)");
                    _profileManager.SaveProfile(copy);
                    PopulateProfilesList();
                    SelectProfileInCombo(copy.Id);
                }
            }
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbProfiles.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var prof = _profileManager.Profiles.FirstOrDefault(p => p.Id == id);
                if (prof != null && !prof.IsBuiltIn)
                {
                    if (MessageBox.Show($"Are you sure you want to delete profile '{prof.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _profileManager.DeleteProfile(id);
                        PopulateProfilesList();
                    }
                }
            }
        }

        private void BtnExportProfile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbProfiles.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var prof = _profileManager.Profiles.FirstOrDefault(p => p.Id == id);
                if (prof != null)
                {
                    var sfd = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "OpenDual Profile (*.json)|*.json",
                        FileName = $"{prof.Name.Replace(" ", "_")}.json"
                    };
                    if (sfd.ShowDialog() == true)
                    {
                        string json = _profileManager.ExportProfile(prof);
                        System.IO.File.WriteAllText(sfd.FileName, json);
                        MessageBox.Show("Profile exported successfully!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void BtnImportProfile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "OpenDual Profile (*.json)|*.json"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string json = System.IO.File.ReadAllText(ofd.FileName);
                    var imported = _profileManager.ImportProfile(json);
                    PopulateProfilesList();
                    SelectProfileInCombo(imported.Id);
                    MessageBox.Show($"Profile '{imported.Name}' imported successfully!", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import profile: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ChkAutoSwitchProfile_Changed(object sender, RoutedEventArgs e)
        {
            if (_gameDetector != null)
            {
                _gameDetector.AutoSwitchEnabled = ChkAutoSwitchProfile?.IsChecked == true;
            }
        }

        private void GameDetector_OnGameChanged(string exeName, GameProfile? matchedProfile)
        {
            Dispatcher.Invoke(() =>
            {
                if (matchedProfile != null)
                {
                    ApplyProfile(matchedProfile);
                    SelectProfileInCombo(matchedProfile.Id);
                    _quickHud?.ShowNotification($"Profile: {matchedProfile.Name}", $"Linked Executable: {exeName}");
                    if (FooterStatus != null) FooterStatus.Text = $"Auto-Switched to {matchedProfile.Name} ({exeName})";
                }
                else
                {
                    var def = _profileManager.Profiles.FirstOrDefault(p => p.Id == "builtin-default");
                    if (def != null)
                    {
                        ApplyProfile(def);
                        SelectProfileInCombo(def.Id);
                        if (FooterStatus != null) FooterStatus.Text = $"Restored Default Profile ({exeName})";
                    }
                }
            });
        }
        #endregion

        #region Power Management
        private void PowerSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || _isProfileApplying) return;

            int timeoutIdx = CmbIdleTimeout?.SelectedIndex ?? 2;
            int idleMins = timeoutIdx switch
            {
                0 => 0,
                1 => 5,
                2 => 10,
                3 => 15,
                4 => 30,
                _ => 10
            };
            bool lowBatWarn = ChkLowBatteryWarn?.IsChecked == true;

            _ipc.SetPowerManagement(idleMins, lowBatWarn);
        }
        #endregion

        #region In-App Automatic Updater
        private async Task CheckForUpdatesSilentlyAsync()
        {
            try
            {
                _latestUpdateInfo = await _updateChecker.CheckForUpdateAsync();
                Dispatcher.Invoke(() =>
                {
                    if (_latestUpdateInfo != null && _latestUpdateInfo.UpdateAvailable)
                    {
                        if (TxtUpdateStatus != null)
                        {
                            TxtUpdateStatus.Text = $"New version v{_latestUpdateInfo.LatestVersion} is available!";
                            TxtUpdateStatus.Foreground = (SolidColorBrush)FindResource("AccentGreen");
                        }
                        if (TxtNewVersionTitle != null) TxtNewVersionTitle.Text = $"Update Available: OpenDual v{_latestUpdateInfo.LatestVersion}";
                        if (TxtNewVersionNotes != null) TxtNewVersionNotes.Text = string.IsNullOrWhiteSpace(_latestUpdateInfo.ReleaseNotes) ? "Includes enhanced controller performance, gyro aiming, and bug fixes." : _latestUpdateInfo.ReleaseNotes;
                        if (PanelUpdateAvailable != null) PanelUpdateAvailable.Visibility = Visibility.Visible;
                    }
                });
            }
            catch { }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (BtnCheckUpdates != null) BtnCheckUpdates.IsEnabled = false;
            if (TxtUpdateStatus != null) TxtUpdateStatus.Text = "Checking GitHub repository for latest releases...";
            try
            {
                _latestUpdateInfo = await _updateChecker.CheckForUpdateAsync();
                if (_latestUpdateInfo != null && _latestUpdateInfo.UpdateAvailable)
                {
                    if (TxtUpdateStatus != null)
                    {
                        TxtUpdateStatus.Text = $"New version v{_latestUpdateInfo.LatestVersion} is available!";
                        TxtUpdateStatus.Foreground = (SolidColorBrush)FindResource("AccentGreen");
                    }
                    if (TxtNewVersionTitle != null) TxtNewVersionTitle.Text = $"Update Available: OpenDual v{_latestUpdateInfo.LatestVersion}";
                    if (TxtNewVersionNotes != null) TxtNewVersionNotes.Text = string.IsNullOrWhiteSpace(_latestUpdateInfo.ReleaseNotes) ? "Includes enhanced controller performance, gyro aiming, and bug fixes." : _latestUpdateInfo.ReleaseNotes;
                    if (PanelUpdateAvailable != null) PanelUpdateAvailable.Visibility = Visibility.Visible;
                }
                else
                {
                    if (TxtUpdateStatus != null)
                    {
                        TxtUpdateStatus.Text = $"You are on the latest release (v{UpdateChecker.CurrentAppVersion}).";
                        TxtUpdateStatus.Foreground = (SolidColorBrush)FindResource("AccentSignature");
                    }
                    if (PanelUpdateAvailable != null) PanelUpdateAvailable.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"OpenDual Suite is fully up to date!\n\nCurrent Version: v{UpdateChecker.CurrentAppVersion}", "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (TxtUpdateStatus != null) TxtUpdateStatus.Text = $"Failed to check updates: {ex.Message}";
            }
            finally
            {
                if (BtnCheckUpdates != null) BtnCheckUpdates.IsEnabled = true;
            }
        }

        private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_latestUpdateInfo == null || string.IsNullOrWhiteSpace(_latestUpdateInfo.DownloadUrl))
            {
                if (!string.IsNullOrWhiteSpace(_latestUpdateInfo?.ReleaseUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _latestUpdateInfo.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
                return;
            }

            if (BtnInstallUpdate != null) BtnInstallUpdate.IsEnabled = false;
            if (ProgressUpdate != null)
            {
                ProgressUpdate.Visibility = Visibility.Visible;
                ProgressUpdate.Value = 0;
            }

            var progress = new Progress<int>(pct =>
            {
                if (ProgressUpdate != null) ProgressUpdate.Value = pct;
                if (TxtNewVersionTitle != null) TxtNewVersionTitle.Text = $"Downloading update... {pct}%";
            });

            string? downloadedSetup = await _updateChecker.DownloadInstallerAsync(_latestUpdateInfo.DownloadUrl, progress);
            if (!string.IsNullOrEmpty(downloadedSetup) && System.IO.File.Exists(downloadedSetup))
            {
                if (TxtNewVersionTitle != null) TxtNewVersionTitle.Text = "Update downloaded. Launching installer...";
                UpdateChecker.LaunchInstallerAndExit(downloadedSetup, silent: false);
            }
            else
            {
                if (BtnInstallUpdate != null) BtnInstallUpdate.IsEnabled = true;
                if (ProgressUpdate != null) ProgressUpdate.Visibility = Visibility.Collapsed;
                MessageBox.Show("Failed to download installer package. Opening release webpage.", "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (!string.IsNullOrWhiteSpace(_latestUpdateInfo?.ReleaseUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _latestUpdateInfo.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
            }
        }
        #endregion
    }
}
