using System;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly TrayIconManager _trayManager = new();
        private readonly MicToastWindow _micToast = new();
        private readonly VolumeHudWindow _volumeHud = new();

        private bool _enableVolumeOverlay = false; // Default: DISABLED per user requirement
        public bool EnableVolumeOverlay { get => _enableVolumeOverlay; set => _enableVolumeOverlay = value; }

        private readonly TouchpadGestureEngine _gestureEngine = new();
        public TouchpadGestureEngine GestureEngine => _gestureEngine;

        private GyroCalibratorWindow? _gyroCalibrator;
        private bool? _lastMicMuted = null;
        private float? _lastMasterVolume = null;
        private QuickHudWindow? _quickHud;
        private bool _lastPsState = false;
        private bool _hasShownTrayBalloon = false;
        private readonly ProfileManager _profileManager = new();
        private GameDetector? _gameDetector;
        private readonly UpdateChecker _updateChecker = new();
        private UpdateInfo? _latestUpdateInfo;
        private GameProfile _currentProfile = new();
        private bool _lastChargingState = false;
        private bool _hasInitializedBatteryState = false;
        private bool _fullChargeNotified = false;
        private bool _lowBatteryWarned15 = false;
        private bool _criticalBatteryWarned5 = false;
        private OverclockAssistantWindow? _overclockAssistant;
        private CaptureBarWindow? _captureBar;
        private bool _lastShareState = false;
        private DateTime _sharePressStartTime = DateTime.MinValue;
        private bool _shareLongPressHandled = false;
        private DateTime _lastShareReleaseTime = DateTime.MinValue;

        public bool ClockUseMilitaryTime { get; set; } = false;
        public string ClockDateFormat { get; set; } = "ddd dd/MM";
        public bool ShowNerdStats { get; set; } = false;

        public string FormatCurrentClock(bool includeDate = true)
        {
            string timeFmt = ClockUseMilitaryTime ? "HH:mm" : "h:mm tt";
            string timeStr = DateTime.Now.ToString(timeFmt);
            if (!includeDate || string.IsNullOrEmpty(ClockDateFormat) || ClockDateFormat == "None")
            {
                return timeStr;
            }
            return $"{DateTime.Now.ToString(ClockDateFormat)}  {timeStr}";
        }

        public void UpdateClockDisplay()
        {
            if (XmbView != null && XmbView.TxtXmbClock != null)
            {
                XmbView.TxtXmbClock.Text = FormatCurrentClock(includeDate: true);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            ThemeManager.Initialize(Resources);
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) => UpdateClockDisplay();
            _clockTimer.Start();
            UpdateClockDisplay();

            _ipc.OnTelemetryReceived += OnTelemetryReceived;
            _ipc.OnConnectionChanged += OnIpcConnectionChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Load Persistent App & UI Preferences
            var settings = AppSettingsManager.Load();
            ShowNerdStats = settings.ShowNerdStats;
            ClockUseMilitaryTime = settings.ClockUseMilitaryTime;
            ClockDateFormat = string.IsNullOrEmpty(settings.ClockDateFormat) ? "ddd dd/MM" : settings.ClockDateFormat;
            _enableVolumeOverlay = settings.EnableVolumeOverlay;
            _gestureEngine.GesturesEnabled = settings.TouchpadGesturesEnabled;
            _gestureEngine.LoadSavedMappings(settings.GestureMappings);

            if (WaveBackground != null)
            {
                WaveBackground.LightweightMode = settings.LightweightBackground;
            }

            // 2. Hook Win32 messages to auto-update when Windows theme or accent color changes
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
            ThemeManager.ApplySystemTheme(Resources);

            // 3. Auto-start driver engine if not running
            if (!DriverManager.IsDriverEngineRunning())
            {
                _driver.StartDriverEngine();
            }

            _ipc.Start();
            if (XmbView != null)
            {
                XmbView.SetIpcClient(_ipc);
                XmbView.SetMainWindow(this);
                XmbView.SetNerdStatsVisible(ShowNerdStats);
                XmbView.CategoryInspector?.SyncSettingsUi(settings);
            }
            UpdateClockDisplay();

            // 4. System Tray & In-Game Overlays
            _trayManager.Initialize(helper.Handle, "OpenDual - Controller Suite");
            _trayManager.DoubleClick += RestoreFromTray;
            _trayManager.RightClick += ShowTrayContextMenu;

            _quickHud = new QuickHudWindow();
            _quickHud.OnCycleProfile = CycleProfile;
            _quickHud.OnAdjustTouchpadSens = AdjustTouchpadSensitivity;
            _quickHud.OnToggleTouchpad = ToggleTouchpadEnabled;
            _quickHud.OnAdjustGyroSens = AdjustGyroSensitivity;
            _quickHud.OnToggleGyro = ToggleGyroEnabled;
            _quickHud.OnToggleMic = ToggleMicMute;
            _quickHud.OnOpenHub = () => Dispatcher.Invoke(RestoreFromTray);
            _quickHud.OnToggleGestures = () =>
            {
                _gestureEngine.GesturesEnabled = !_gestureEngine.GesturesEnabled;
                AppSettingsManager.Current.TouchpadGesturesEnabled = _gestureEngine.GesturesEnabled;
                AppSettingsManager.Save();
                XmbView?.CategoryInspector?.SyncSettingsUi(AppSettingsManager.Current);
                _quickHud?.ShowNotification("Touchpad Gestures", _gestureEngine.GesturesEnabled ? "Custom Gestures: ENABLED" : "Custom Gestures: DISABLED");
            };
            _quickHud.OnHudScaleChanged = OnHudScaleChangedFromHud;
            _quickHud.GetActiveGameName = () => _gameDetector?.CurrentProcessName ?? "";
            _quickHud.GetActiveProfileName = () => _currentProfile?.Name ?? "Standard Balanced";

            _captureBar = new CaptureBarWindow();
            _captureBar.GetActiveGameName = () => _gameDetector?.CurrentProcessName ?? "";

            if (settings.HudScale > 0.4 && settings.HudScale <= 2.5)
            {
                _quickHud.SetHudScale(settings.HudScale);
                _captureBar.SetBarScale(settings.HudScale);
            }

            // 5. Game-Specific Auto Profile Switching & Initial Profile Restore
            _gameDetector = new GameDetector(_profileManager);
            _gameDetector.OnGameChanged += GameDetector_OnGameChanged;
            _gameDetector.Start();

            var restoredProf = _profileManager.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId)
                               ?? _profileManager.Profiles.FirstOrDefault(p => p.Id == "builtin-default")
                               ?? _profileManager.Profiles[0];
            if (settings.SavedHardwareConfig != null)
            {
                restoredProf.Config = settings.SavedHardwareConfig;
            }
            ApplyProfile(restoredProf, savePreference: false);

            // 6. Background update check (non-blocking)
            _ = CheckForUpdatesSilentlyAsync();

            // 7. Automated UI test & screenshot harness
            string[] args = Environment.GetCommandLineArgs();
            int targetCat = -1;
            int targetItem = -1;
            string? screenshotPath = null;
            bool targetQuickHud = false;
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--category" || args[i] == "--cat") && i + 1 < args.Length && int.TryParse(args[i + 1], out int c)) targetCat = c;
                if ((args[i] == "--item" || args[i] == "--itm") && i + 1 < args.Length && int.TryParse(args[i + 1], out int itm)) targetItem = itm;
                if (args[i] == "--screenshot" && i + 1 < args.Length) screenshotPath = args[i + 1];
                if (args[i] == "--quickhud") targetQuickHud = true;
            }

            if (args.Contains("--test-ui"))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(async () =>
                {
                    try
                    {
                        System.IO.File.WriteAllText("ui_test_result.log", "Starting UI button click test...\n");
                        XmbView?.Xmb.SelectCategory(3);
                        UpdateLayout();
                        await Task.Delay(100);
                        var insp = XmbView?.CategoryInspector;
                        if (insp != null)
                        {
                            System.IO.File.AppendAllText("ui_test_result.log", "Clicking Lightbar Effect buttons...\n");
                            insp.BtnLedEff0.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnLedEff1.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnLedEff2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnLedEff3.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnLedEff4.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", "Clicking Player LED buttons...\n");
                            insp.BtnPled1.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnPledAll.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        }

                        // Test Triggers: Rigid Wall, Click-Snap, Compound Bow, Machine Gun
                        XmbView?.Xmb.SelectCategory(2);
                        UpdateLayout();
                        await Task.Delay(100);
                        if (insp != null)
                        {
                            System.IO.File.AppendAllText("ui_test_result.log", "Clicking Trigger buttons (Rigid, Snap, Bow, Machine Gun)...\n");
                            insp.BtnL2_0.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnL2_1.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnL2_2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnL2_3.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnL2_4.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnR2_1.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnR2_2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnR2_3.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnR2_4.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.SliderSnapTension.Value = 92;
                            System.IO.File.AppendAllText("ui_test_result.log", $"Triggers verified: L2={insp.TxtL2ModeBadge.Text}, R2={insp.TxtR2ModeBadge.Text}, Tension={insp.TxtSnapTensionVal.Text}\n");
                        }

                        // Test Profiles: Standard, Esports, FPS, Racing (Repeated switching)
                        XmbView?.Xmb.SelectCategory(7);
                        UpdateLayout();
                        await Task.Delay(100);
                        if (insp != null)
                        {
                            System.IO.File.AppendAllText("ui_test_result.log", "Testing Profiles repeated activation...\n");
                            insp.BtnActDefault.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Profile 1: {_currentProfile?.Name}\n");
                            insp.BtnActEsports.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Profile 2: {_currentProfile?.Name}\n");
                            insp.BtnActFps.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Profile 3: {_currentProfile?.Name}\n");
                            insp.BtnActRacing.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Profile 4: {_currentProfile?.Name}\n");
                            insp.BtnActDefault.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Profile 1 re-activated: {_currentProfile?.Name}\n");
                        }

                        // Test Settings & Persistence: Nerd stats, military time, date formats, lightweight background
                        XmbView?.Xmb.SelectCategory(8);
                        UpdateLayout();
                        await Task.Delay(100);
                        if (insp != null)
                        {
                            System.IO.File.AppendAllText("ui_test_result.log", "Testing Settings toggles & persistence...\n");
                            insp.ToggleNerdStats.IsChecked = true;
                            insp.ToggleNerdStats.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Nerd Stats: {ShowNerdStats}, Pill: {XmbView?.PillNerdStats?.Visibility}\n");

                            insp.ToggleMilitaryTime.IsChecked = true;
                            insp.ToggleMilitaryTime.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnDateIso.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Clock Format: {FormatCurrentClock(true)}\n");

                            insp.ToggleLightweightBg.IsChecked = true;
                            insp.ToggleLightweightBg.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            System.IO.File.AppendAllText("ui_test_result.log", $"Lightweight Bg: {WaveBackground?.LightweightMode}\n");

                            var reloaded = AppSettingsManager.Load();
                            System.IO.File.AppendAllText("ui_test_result.log", $"Persisted Settings JSON Verified: NerdStats={reloaded.ShowNerdStats}, MilTime={reloaded.ClockUseMilitaryTime}, Date={reloaded.ClockDateFormat}, LightBg={reloaded.LightweightBackground}\n");

                            // Restore clean default preferences after test
                            insp.ToggleNerdStats.IsChecked = false;
                            insp.ToggleNerdStats.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.ToggleMilitaryTime.IsChecked = false;
                            insp.ToggleMilitaryTime.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnDateUk.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.ToggleLightweightBg.IsChecked = false;
                            insp.ToggleLightweightBg.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        }

                        XmbView?.Xmb.SelectCategory(5);
                        UpdateLayout();
                        await Task.Delay(100);
                        if (insp != null)
                        {
                            System.IO.File.AppendAllText("ui_test_result.log", "Clicking Gyro buttons...\n");
                            insp.BtnGyroMouse.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnGyroStick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnGyroTrigADS.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnGyroClickCrossSquare.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnGyroClickTriggers.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnGyroClickBumpers.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            insp.BtnGyroClickDisabled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        }

                        System.IO.File.AppendAllText("ui_test_result.log", "Testing HUD Gyro and Mic actions...\n");
                        if (_quickHud != null)
                        {
                            _quickHud.ShowHud();
                            _quickHud.SelectTile(2);
                            _quickHud.OnToggleGyro?.Invoke();
                            System.IO.File.AppendAllText("ui_test_result.log", $"HUD Gyro Toggled: {_quickHud.TxtTileGyroState.Text}\n");
                            _quickHud.SelectTile(1);
                            _quickHud.OnToggleMic?.Invoke();
                            System.IO.File.AppendAllText("ui_test_result.log", $"HUD Mic Toggled: {_quickHud.TxtTileMicBadge.Text}\n");
                            _quickHud.HideHud();
                            _gestureEngine.ExecuteGesture(TouchGestureType.SwipeLeft);
                            _gestureEngine.ExecuteGesture(TouchGestureType.SwipeDown);
                            if (_quickHud.IsVisibleToUser)
                            {
                                throw new InvalidOperationException("Quick HUD popped open on touchpad swipe!");
                            }

                            System.IO.File.AppendAllText("ui_test_result.log", "HUD Swipes verified: HUD remains safely closed on swipes.\n");
                        }

                        // Test Items 1, 2, 4 & 10 Deep Hardware Features
                        System.IO.File.AppendAllText("ui_test_result.log", "Testing Custom Profile Creator & Per-Game EXE Linker...\n");
                        if (insp != null)
                        {
                            insp.TxtCustomProfileName.Text = "Cyberpunk Custom Test";
                            insp.TxtCustomProfileExe.Text = "Cyberpunk2077.exe";
                            insp.BtnSaveCustomProfile_Click(insp.BtnSaveCustomProfile, new RoutedEventArgs());
                            var customs = GetCustomProfiles();
                            var created = customs.FirstOrDefault(p => p.Name == "Cyberpunk Custom Test");
                            if (created == null) throw new InvalidOperationException("Custom profile was not created!");
                            System.IO.File.AppendAllText("ui_test_result.log", $"Custom Profile Verified: Name={created.Name}, Exe={created.ProcessNames}\n");
                            DeleteCustomProfile(created.Id);
                            insp.RefreshCustomProfilesList();
                        }

                        System.IO.File.AppendAllText("ui_test_result.log", "Testing Custom Gesture Action Mapper persistence...\n");
                        _gestureEngine.SetMapping(TouchGestureType.LetterC, GestureAction.LaunchCalculator);
                        var gestureReloaded = AppSettingsManager.Load();
                        System.IO.File.AppendAllText("ui_test_result.log", $"Gesture Mapping Persisted: LetterC -> {gestureReloaded.GestureMappings["LetterC"]}\n");
                        _gestureEngine.SetMapping(TouchGestureType.LetterC, GestureAction.TakeScreenshot);

                        System.IO.File.AppendAllText("ui_test_result.log", "Testing 10 Deep Hardware Features in NovelFeaturesControl...\n");
                        XmbView?.Xmb.SelectCategory(2);
                        XmbView?.Xmb.SelectItem(5); // 10 Deep Hardware Modifiers
                        UpdateLayout();
                        await Task.Delay(100);
                        var novel = XmbView?.NovelControl;
                        if (novel != null)
                        {
                            var deepToggles = new[]
                            {
                                novel.ToggleDualStage, novel.ToggleRapidTrigger, novel.ToggleSprintShelf,
                                novel.ToggleGyroRatchet, novel.ToggleHapticDetent, novel.ToggleGShockGuard,
                                novel.TogglePlayerLedBar, novel.ToggleMicBeamforming, novel.ToggleRecoilAssist,
                                novel.ToggleThermalGuard
                            };
                            foreach (var tBtn in deepToggles)
                            {
                                tBtn.IsChecked = true;
                                tBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            }
                            var cfgAfterEnable = AppSettingsManager.Load().SavedHardwareConfig;
                            System.IO.File.AppendAllText("ui_test_result.log", $"All 10 Deep Features Enabled & Saved: DualStage={cfgAfterEnable?.DualStageTriggers}, Rapid={cfgAfterEnable?.HairTriggerRelease}, Sprint={cfgAfterEnable?.SprintShelfL3}, Thermal={cfgAfterEnable?.ThermalGuard}\n");

                            foreach (var tBtn in deepToggles)
                            {
                                tBtn.IsChecked = false;
                                tBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            }
                        }

                        // Restore Standard Balanced clean defaults after test
                        ApplyProfileByName("Standard Balanced");

                        System.IO.File.AppendAllText("ui_test_result.log", "SUCCESS: All button click tests completed without any crashes!\n");
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText("ui_test_result.log", $"CRASH IN TEST: {ex}\n");
                    }

                    Application.Current.Shutdown();
                    Environment.Exit(0);
                }));
                return;
            }

            if (!string.IsNullOrEmpty(screenshotPath))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(async () =>
                {
                    try
                    {
                        if (targetQuickHud && _quickHud != null)
                        {
                            _quickHud.ShowHud();
                            _quickHud.HudRoot.Opacity = 1.0;
                            _quickHud.UpdateLayout();
                            await Task.Delay(300);
                            _quickHud.CaptureToPng(screenshotPath);
                        }
                        else
                        {
                            if (targetCat >= 0 && XmbView != null) XmbView.Xmb.SelectCategory(targetCat);
                            if (targetItem >= 0 && XmbView != null) XmbView.Xmb.SelectItem(targetItem);
                            UpdateLayout();
                            await Task.Delay(300);
                            CaptureToPng(screenshotPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.WriteAllText("screenshot_err.log", ex.ToString());
                    }

                    Application.Current.Shutdown();
                    Environment.Exit(0);
                }));
                return;
            }
        }

        public void CaptureToPng(string outputPath)
        {
            UpdateLayout();
            int width = (int)ActualWidth;
            int height = (int)ActualHeight;
            if (width <= 0) width = 1100;
            if (height <= 0) height = 820;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            string? dir = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            using var stream = System.IO.File.Create(outputPath);
            encoder.Save(stream);
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
            itemRestart.Click += (s, e) => RestartDriverEngine();
            menu.Items.Add(itemRestart);
            var itemHud = new MenuItem { Header = "Preview Quick HUD (Controller Overlay)" };
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
            SystemHardwareMonitor.ToggleMicrophoneMute();
            bool isMuted = SystemHardwareMonitor.IsMicrophoneMuted();
            _micToast?.ShowStatus(isMuted);
            _quickHud?.UpdateMicStatus(isMuted);
        }

        private void ExitApplication()
        {
            _gameDetector?.Dispose();
            _trayManager.Dispose();
            _micToast?.Close();
            _quickHud?.Close();
            _overclockAssistant?.Close();
            _captureBar?.Close();
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && AppSettingsManager.Current.MinimizeToTray)
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

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (XmbView != null && XmbView.Visibility == Visibility.Visible)
            {
                if (e.Key == System.Windows.Input.Key.Left)
                {
                    XmbView.Xmb.NavigateCategory(-1);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Right)
                {
                    XmbView.Xmb.NavigateCategory(1);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Up)
                {
                    XmbView.Xmb.NavigateItem(-1);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Down)
                {
                    XmbView.Xmb.NavigateItem(1);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space)
                {
                    var item = XmbView.Xmb.ActiveItem;
                    if (item != null) item.OnActivate?.Invoke();
                    e.Handled = true;
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
                Dispatcher.Invoke(() => ThemeManager.ApplySystemTheme(Resources));
            }

            return IntPtr.Zero;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _clockTimer.Stop();
            _gameDetector?.Dispose();
            _ipc.Dispose();
            _trayManager.Dispose();
            _micToast?.Close();
            _quickHud?.Close();
            _overclockAssistant?.Close();
            _captureBar?.Close();
            Environment.Exit(0);
        }

        #region Telemetry & Live UI
        private void OnIpcConnectionChanged(bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                if (connected)
                {
                    _ipc.SendConfig(_currentProfile.Config);
                    XmbView?.CategoryInspector?.SyncFromConfig(_currentProfile.Config);
                }
            });
        }

        private ControllerTelemetry? _pendingTelemetry;
        private int _telemetryDispatchActive = 0;
        private long _lastTelemetryDispatchTicks = 0;

        private void OnTelemetryReceived(ControllerTelemetry t)
        {
            _pendingTelemetry = t;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double elapsedMs = (now - _lastTelemetryDispatchTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            // Throttle UI updates to smooth 60 FPS (16ms) so the UI message loop is never starved
            if (elapsedMs < 16.0) return;
            if (System.Threading.Interlocked.CompareExchange(ref _telemetryDispatchActive, 1, 0) == 0)
            {
                _lastTelemetryDispatchTicks = now;
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        var latest = _pendingTelemetry;
                        if (latest != null)
                        {
                            ApplyTelemetryToUi(latest);
                        }
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _telemetryDispatchActive, 0);
                    }
                }));
            }
        }

        private void ApplyTelemetryToUi(ControllerTelemetry t)
        {
            XmbView?.UpdateTelemetry(t);

            if (t.Connected)
            {
                _overclockAssistant?.UpdateTelemetry(t);

                // Mic Mute Transition & Toast
                if (_lastMicMuted.HasValue && _lastMicMuted.Value != t.MicMuted)
                {
                    _micToast?.ShowStatus(t.MicMuted);
                }
                _lastMicMuted = t.MicMuted;

                // Master Volume Transition & Floating HUD (Disabled by default per user request)
                if (_enableVolumeOverlay && _lastMasterVolume.HasValue && Math.Abs(_lastMasterVolume.Value - t.MasterVolume) > 0.015f)
                {
                    _volumeHud?.ShowVolume(t.MasterVolume, false, t.RenderDev);
                }
                _lastMasterVolume = t.MasterVolume;

                // Process capacitive touchpad unistroke gestures and custom shortcuts
                _gestureEngine.ProcessTelemetry(t);

                // Forward telemetry to interactive Gyro Calibrator
                _gyroCalibrator?.UpdateTelemetry(t);

                // Battery Level Alerts & Charging Notifications
                if (!_hasInitializedBatteryState)
                {
                    _lastChargingState = t.Charging;
                    _hasInitializedBatteryState = true;
                }
                else
                {
                    if (t.Charging && !_lastChargingState)
                    {
                        _lastChargingState = true;
                        _fullChargeNotified = (t.Battery >= 100);
                        _lowBatteryWarned15 = false;
                        _criticalBatteryWarned5 = false;
                        _trayManager.ShowNotification("OpenDual Charging Connected", $"Controller is now charging ({t.Battery}%).");
                    }
                    else if (!t.Charging && _lastChargingState)
                    {
                        _lastChargingState = false;
                        _fullChargeNotified = false;
                        _trayManager.ShowNotification("OpenDual Disconnected from Charger", $"Controller on battery ({t.Battery}% remaining).");
                    }
                    else if (t.Charging && t.Battery >= 100 && !_fullChargeNotified)
                    {
                        _fullChargeNotified = true;
                        _trayManager.ShowNotification("OpenDual Battery Full", "Controller battery is fully charged (100%).");
                    }
                    else if (t.Battery < 95)
                    {
                        _fullChargeNotified = false;
                    }

                    if (!t.Charging)
                    {
                        if (t.Battery <= 5 && !_criticalBatteryWarned5)
                        {
                            _criticalBatteryWarned5 = true;
                            _lowBatteryWarned15 = true;
                            _trayManager.ShowNotification("OpenDual Critical Battery", $"Controller battery is critically low ({t.Battery}%). Please connect USB charging cable.");
                        }
                        else if (t.Battery <= 15 && !_lowBatteryWarned15)
                        {
                            _lowBatteryWarned15 = true;
                            _trayManager.ShowNotification("OpenDual Battery Warning", $"Controller battery is low ({t.Battery}%). Please connect USB charging cable.");
                        }
                        else if (t.Battery > 20)
                        {
                            _lowBatteryWarned15 = false;
                            _criticalBatteryWarned5 = false;
                        }
                    }
                }

                // In-Game Quick HUD Update & Home Button Detection
                if (_quickHud != null)
                {
                    _quickHud.UpdateState(t);
                    if (_quickHud.IsVisibleToUser)
                    {
                        _quickHud.HandleControllerNavigation(t);
                    }

                    if (t.PS && !_lastPsState)
                    {
                        if (_captureBar != null && _captureBar.IsVisibleToUser)
                        {
                            _captureBar.HideBar();
                        }
                        _quickHud.ToggleOrRefresh();
                    }
                }
                _lastPsState = t.PS;

                // In-Game Create / Capture Bar Update & Share Button Detection
                if (_captureBar != null)
                {
                    if (_captureBar.IsVisibleToUser)
                    {
                        _captureBar.HandleControllerNavigation(t);
                    }

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
                _lastShareState = t.Share;
            }
            else
            {
                _hasInitializedBatteryState = false;
                _lastChargingState = false;
                _fullChargeNotified = false;
                _lowBatteryWarned15 = false;
                _criticalBatteryWarned5 = false;
            }
        }
        #endregion

        #region Driver & Assistants
        public void RestartDriverEngine()
        {
            _driver.StopDriverEngine();
            System.Threading.Thread.Sleep(300);
            _driver.StartDriverEngine();
        }

        public void OpenOverclockAssistant()
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

        private void OnHudScaleChangedFromHud(double scale)
        {
            Dispatcher.Invoke(() =>
            {
                _captureBar?.SetBarScale(scale);
                AppSettingsManager.Current.HudScale = scale;
                AppSettingsManager.Save();
            });
        }

        public void LaunchGyroCalibrator()
        {
            if (_gyroCalibrator == null || !_gyroCalibrator.IsLoaded)
            {
                _gyroCalibrator = new GyroCalibratorWindow();
                _gyroCalibrator.OnCalibrationApplied = HandleCalibrationApplied;
            }

            _gyroCalibrator.Show();
            _gyroCalibrator.Activate();
        }

        private void HandleCalibrationApplied(float sensX, float sensY, float deadzone)
        {
            Dispatcher.Invoke(() =>
            {
                var cfg = _currentProfile.Config;
                cfg.Gyro = true;
                cfg.GyroSensX = sensX;
                cfg.GyroSensY = sensY;
                cfg.GyroDeadzone = (int)Math.Round(deadzone);

                var insp = XmbView?.CategoryInspector;
                if (insp != null)
                {
                    insp.SetGyroEnabled(true);
                    insp.SetGyroSensitivity(sensX);
                }

                _ipc.SetGyro(true, cfg.GyroMode, cfg.GyroOutputMode, cfg.GyroSpace, sensX, sensY, cfg.GyroInvertX, cfg.GyroInvertY, cfg.GyroDeadzone, cfg.GyroSmooth, cfg.GyroClickMode);
                _trayManager.ShowNotification("OpenDual Gyro Calibration Applied", $"Aim calibration active: X={sensX:F2}x, Y={sensY:F2}x, Deadzone={deadzone:F2}");
            });
        }
        #endregion

        #region Game Profiles & Presets
        public ConfigDto CurrentConfig => _currentProfile.Config;
        public string ActiveProfileName => _currentProfile?.Name ?? "Standard Balanced";

        public void PersistLiveHardwareConfig()
        {
            AppSettingsManager.Current.SavedHardwareConfig = _currentProfile.Config;
            if (!_currentProfile.IsBuiltIn)
            {
                _profileManager.SaveProfile(_currentProfile);
            }
            AppSettingsManager.Save();
        }

        public System.Collections.Generic.IReadOnlyList<GameProfile> GetCustomProfiles()
        {
            return _profileManager.Profiles.Where(p => !p.IsBuiltIn).ToList();
        }

        public void SaveAndActivateCustomProfile(string name, string exeNames)
        {
            var custom = _currentProfile.Clone(name);
            custom.ProcessNames = exeNames;
            custom.Description = string.IsNullOrWhiteSpace(exeNames)
                ? "Custom User Hardware Profile"
                : $"Auto-Switch Game Profile ({exeNames})";
            _profileManager.SaveProfile(custom);
            ApplyProfile(custom, savePreference: true);
            _quickHud?.ShowNotification($"Saved Profile: {custom.Name}", "Custom profile created & activated.");
            _trayManager.ShowNotification("Custom Profile Saved", $"Activated profile: {custom.Name}");
        }

        public void DeleteCustomProfile(string id)
        {
            bool wasActive = _currentProfile.Id == id;
            _profileManager.DeleteProfile(id);
            if (wasActive)
            {
                var def = _profileManager.Profiles.FirstOrDefault(p => p.Id == "builtin-default") ?? _profileManager.Profiles[0];
                ApplyProfile(def, savePreference: true);
            }
        }

        private void ApplyProfile(GameProfile prof, bool savePreference = true)
        {
            _currentProfile = prof;
            _ipc.SendConfig(prof.Config);
            XmbView?.CategoryInspector?.SyncFromConfig(prof.Config);
            XmbView?.NovelControl?.SyncFromConfig(prof.Config);
            XmbView?.CategoryInspector?.UpdateActiveProfileCard(prof.Name);
            XmbView?.UpdateProfileBadge(prof.Name);

            if (savePreference)
            {
                AppSettingsManager.Current.ActiveProfileId = prof.Id;
                AppSettingsManager.Current.SavedHardwareConfig = prof.Config;
                AppSettingsManager.Save();
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
            });
        }

        private void AdjustTouchpadSensitivity(float delta)
        {
            Dispatcher.Invoke(() =>
            {
                var cfg = _currentProfile?.Config ?? new ConfigDto();
                float current = cfg.MouseSens > 0.05f ? cfg.MouseSens : 1.2f;
                float next = (float)Math.Clamp(Math.Round(current + delta, 1), 0.2, 3.0);
                cfg.MouseSens = next;
                _ipc.SetTouchpad(cfg.Mouse, next, cfg.ScrollSens > 0 ? cfg.ScrollSens : 1.0f, true, cfg.VolGesture);
            });
        }

        private void ToggleTouchpadEnabled()
        {
            Dispatcher.Invoke(() =>
            {
                var cfg = _currentProfile?.Config ?? new ConfigDto();
                bool newEnabled = !cfg.Mouse;
                cfg.Mouse = newEnabled;
                _ipc.SetTouchpad(newEnabled, cfg.MouseSens > 0 ? cfg.MouseSens : 1.2f, cfg.ScrollSens > 0 ? cfg.ScrollSens : 1.0f, true, cfg.VolGesture);
            });
        }

        private void AdjustGyroSensitivity(float delta)
        {
            Dispatcher.Invoke(() =>
            {
                var cfg = _currentProfile?.Config ?? new ConfigDto();
                float current = cfg.GyroSensX > 0.05f ? cfg.GyroSensX : 1.2f;
                float next = (float)Math.Clamp(Math.Round(current + delta, 1), 0.2, 4.0);
                cfg.GyroSensX = next;
                cfg.GyroSensY = next;
                var insp = XmbView?.CategoryInspector;
                if (insp != null)
                {
                    insp.SetGyroSensitivity(next);
                }

                int mode = cfg.GyroMode >= 0 ? cfg.GyroMode : 1;
                int outMode = cfg.GyroOutputMode >= 0 ? cfg.GyroOutputMode : 0;
                int space = cfg.GyroSpace >= 0 ? cfg.GyroSpace : 0;
                int deadzone = cfg.GyroDeadzone >= 0 ? cfg.GyroDeadzone : 3;
                float smooth = cfg.GyroSmooth >= 0 ? cfg.GyroSmooth : 0.20f;
                _ipc.SetGyro(cfg.Gyro, mode, outMode, space, next, next, cfg.GyroInvertX, cfg.GyroInvertY, deadzone, smooth, cfg.GyroClickMode);
                _quickHud?.UpdateGyroTile(cfg.Gyro, next);
            });
        }

        private void ToggleGyroEnabled()
        {
            Dispatcher.Invoke(() =>
            {
                var cfg = _currentProfile?.Config ?? new ConfigDto();
                bool newEnabled = !cfg.Gyro;
                cfg.Gyro = newEnabled;

                var insp = XmbView?.CategoryInspector;
                if (insp != null)
                {
                    insp.SetGyroEnabled(newEnabled);
                }

                float sensX = cfg.GyroSensX > 0.05f ? cfg.GyroSensX : 1.2f;
                float sensY = cfg.GyroSensY > 0.05f ? cfg.GyroSensY : 1.2f;
                int mode = cfg.GyroMode >= 0 ? cfg.GyroMode : 1;
                int outMode = cfg.GyroOutputMode >= 0 ? cfg.GyroOutputMode : 0;
                int space = cfg.GyroSpace >= 0 ? cfg.GyroSpace : 0;
                int deadzone = cfg.GyroDeadzone >= 0 ? cfg.GyroDeadzone : 3;
                float smooth = cfg.GyroSmooth >= 0 ? cfg.GyroSmooth : 0.20f;
                _ipc.SetGyro(newEnabled, mode, outMode, space, sensX, sensY, cfg.GyroInvertX, cfg.GyroInvertY, deadzone, smooth, cfg.GyroClickMode);
                _quickHud?.UpdateGyroTile(newEnabled, sensX);
            });
        }

        private void GameDetector_OnGameChanged(string exeName, GameProfile? matchedProfile)
        {
            Dispatcher.Invoke(() =>
            {
                if (matchedProfile != null)
                {
                    ApplyProfile(matchedProfile, savePreference: false);
                    _quickHud?.ShowNotification($"Profile: {matchedProfile.Name}", $"Linked Executable: {exeName}");
                }
                else
                {
                    var savedId = AppSettingsManager.Current.ActiveProfileId;
                    var def = _profileManager.Profiles.FirstOrDefault(p => p.Id == savedId)
                              ?? _profileManager.Profiles.FirstOrDefault(p => p.Id == "builtin-default");
                    if (def != null)
                    {
                        ApplyProfile(def, savePreference: false);
                    }
                }
            });
        }

        public void ApplyProfileByName(string name)
        {
            var prof = _profileManager.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? _profileManager.Profiles.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                       ?? _profileManager.Profiles.FirstOrDefault(p => p.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? (name.Contains("Esports", StringComparison.OrdinalIgnoreCase) ? _profileManager.Profiles.FirstOrDefault(p => p.Name.Contains("Esports", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Competitive", StringComparison.OrdinalIgnoreCase)) : null)
                       ?? (name.Contains("FPS", StringComparison.OrdinalIgnoreCase) ? _profileManager.Profiles.FirstOrDefault(p => p.Name.Contains("FPS", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Competitive", StringComparison.OrdinalIgnoreCase)) : null)
                       ?? (name.Contains("Racing", StringComparison.OrdinalIgnoreCase) ? _profileManager.Profiles.FirstOrDefault(p => p.Name.Contains("Racing", StringComparison.OrdinalIgnoreCase)) : null);

            if (prof != null)
            {
                ApplyProfile(prof, savePreference: true);
                _quickHud?.ShowNotification($"Profile: {prof.Name}", "Settings synchronized across system.");
                _trayManager.ShowNotification("Profile Activated", $"Settings applied for: {prof.Name}");
            }
        }
        #endregion

        #region In-App Automatic Updater
        private async Task CheckForUpdatesSilentlyAsync()
        {
            try
            {
                _latestUpdateInfo = await _updateChecker.CheckForUpdateAsync();
            }
            catch { }
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                _latestUpdateInfo = await _updateChecker.CheckForUpdateAsync();
                return _latestUpdateInfo;
            }
            catch
            {
                return null;
            }
        }

        public async Task DownloadAndInstallUpdateAsync(IProgress<int>? progress)
        {
            if (_latestUpdateInfo == null || string.IsNullOrWhiteSpace(_latestUpdateInfo.DownloadUrl)) return;
            string? setup = await _updateChecker.DownloadInstallerAsync(_latestUpdateInfo.DownloadUrl, progress);
            if (!string.IsNullOrEmpty(setup) && System.IO.File.Exists(setup))
            {
                UpdateChecker.LaunchInstallerAndExit(setup, silent: false);
            }
        }
        #endregion
    }
}
