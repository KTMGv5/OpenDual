using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class QuickHudWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private readonly DispatcherTimer _hideTimer;
        private bool _isVisibleToUser = false;
        private int _selectedTileIndex = 0;

        // Controller input edge detection & repeat debouncing
        private int _lastDpad = 8;
        private bool _lastCross = false;
        private bool _lastCircle = false;
        private bool _lastSquare = false;
        private DateTime _lastDpadActionTime = DateTime.MinValue;
        private bool _dpadInRepeatMode = false;

        // Public callbacks & data hooks
        public bool IsVisibleToUser => _isVisibleToUser;
        public Action<int>? OnCycleProfile { get; set; }
        public Action<float>? OnAdjustTouchpadSens { get; set; }
        public Action? OnToggleTouchpad { get; set; }
        public Action<float>? OnAdjustGyroSens { get; set; }
        public Action? OnToggleGyro { get; set; }
        public Action? OnToggleMic { get; set; }
        public Func<string>? GetActiveGameName { get; set; }
        public Func<string>? GetActiveProfileName { get; set; }

        public QuickHudWindow()
        {
            InitializeComponent();

            // Force handle creation and configure non-activating topmost toolwindow
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();

            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            // Timer for ephemeral notifications only (never active for persistent Control Center HUD)
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.5)
            };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                HideHud();
            };

            SelectTile(0);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void SelectTile(int index)
        {
            _selectedTileIndex = Math.Clamp(index, 0, 4);
            UpdateTileVisuals();
            UpdateNavGuideText();
        }

        private void UpdateTileVisuals()
        {
            var selectedBg = (Brush)FindResource("TileSelectedBg");
            var selectedBorder = (Brush)FindResource("TileSelectedBorder");
            var normalBg = (Brush)FindResource("TileNormalBg");
            var normalBorder = (Brush)FindResource("TileNormalBorder");

            var tiles = new[] { Tile0, Tile1, Tile2, Tile3, Tile4 };
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                bool isSel = (i == _selectedTileIndex);
                tiles[i].Background = isSel ? selectedBg : normalBg;
                tiles[i].BorderBrush = isSel ? selectedBorder : normalBorder;
                tiles[i].BorderThickness = isSel ? new Thickness(2) : new Thickness(1.5);
            }
        }

        private void UpdateNavGuideText()
        {
            if (TxtNavGuide == null) return;

            TxtNavGuide.Text = _selectedTileIndex switch
            {
                0 => "D-Pad ◄► Select Tile   •   ▲▼ Cycle Profile   •   ✕ Apply Profile   •   □ Mic Mute   •   ○ / PS Close",
                1 => "D-Pad ◄► Select Tile   •   ▲▼ Master Volume ±5%   •   ✕ Master Mute   •   □ Mic Mute   •   ○ / PS Close",
                2 => "D-Pad ◄► Select Tile   •   ▲▼ Gyro Sens ±0.1x   •   ✕ Toggle Gyro   •   □ Mic Mute   •   ○ / PS Close",
                3 => "D-Pad ◄► Select Tile   •   ▲▼ Refresh PC Stats   •   ✕ Refresh Now   •   □ Mic Mute   •   ○ / PS Close",
                4 => "D-Pad ◄► Select Tile   •   ▲▼ Touchpad Sens ±0.1x   •   ✕ Toggle Touchpad   •   □ Mic Mute   •   ○ / PS Close",
                _ => "D-Pad ◄► Select Tile   •   ▲▼ Adjust Setting   •   ✕ Action   •   □ Mic Mute   •   ○ / PS Close"
            };
        }

        public void HandleControllerNavigation(ControllerTelemetry t)
        {
            if (!_isVisibleToUser || t == null) return;

            var now = DateTime.UtcNow;

            // Decode D-Pad directions (0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8=Neutral)
            bool dpadUp = (t.DPad == 0 || t.DPad == 1 || t.DPad == 7);
            bool dpadRight = (t.DPad == 1 || t.DPad == 2 || t.DPad == 3);
            bool dpadDown = (t.DPad == 3 || t.DPad == 4 || t.DPad == 5);
            bool dpadLeft = (t.DPad == 5 || t.DPad == 6 || t.DPad == 7);
            bool dpadNeutral = (t.DPad == 8);

            bool lastDpadNeutral = (_lastDpad == 8);
            bool isNewDpadPress = !dpadNeutral && (lastDpadNeutral || t.DPad != _lastDpad);

            bool canRepeat = false;
            if (!dpadNeutral)
            {
                double elapsed = (now - _lastDpadActionTime).TotalMilliseconds;
                if (isNewDpadPress)
                {
                    canRepeat = true;
                    _dpadInRepeatMode = false;
                }
                else if (!_dpadInRepeatMode && elapsed >= 320)
                {
                    canRepeat = true;
                    _dpadInRepeatMode = true;
                    _lastDpadActionTime = now;
                }
                else if (_dpadInRepeatMode && elapsed >= 130)
                {
                    canRepeat = true;
                    _lastDpadActionTime = now;
                }
            }
            else
            {
                _dpadInRepeatMode = false;
            }

            if (canRepeat)
            {
                _lastDpadActionTime = now;
                if (dpadLeft)
                {
                    SelectTile((_selectedTileIndex - 1 + 5) % 5);
                }
                else if (dpadRight)
                {
                    SelectTile((_selectedTileIndex + 1) % 5);
                }
                else if (dpadUp)
                {
                    AdjustSelectedTile(+1);
                }
                else if (dpadDown)
                {
                    AdjustSelectedTile(-1);
                }
            }

            // Leading edge for Cross (✕)
            if (t.Cross && !_lastCross)
            {
                ActionSelectedTile();
            }

            // Leading edge for Square (□) -> Quick Mic Mute Toggle
            if (t.Square && !_lastSquare)
            {
                OnToggleMic?.Invoke();
            }

            // Leading edge for Circle (○) -> Dismiss HUD
            if (t.Circle && !_lastCircle)
            {
                HideHud();
            }

            _lastDpad = t.DPad;
            _lastCross = t.Cross;
            _lastCircle = t.Circle;
            _lastSquare = t.Square;
        }

        private void AdjustSelectedTile(int direction)
        {
            switch (_selectedTileIndex)
            {
                case 0: // Game & Profile
                    OnCycleProfile?.Invoke(direction);
                    break;
                case 1: // Master Volume
                    SystemHardwareMonitor.AdjustMasterVolume(direction * 5);
                    UpdateAudioTileDisplay();
                    break;
                case 2: // Gyro Aiming
                    OnAdjustGyroSens?.Invoke(direction * 0.1f);
                    break;
                case 3: // PC Hardware
                    UpdateHardwareTileDisplay();
                    break;
                case 4: // Touchpad Sensitivity
                    OnAdjustTouchpadSens?.Invoke(direction * 0.1f);
                    break;
            }
        }

        private void ActionSelectedTile()
        {
            switch (_selectedTileIndex)
            {
                case 0: // Profile: cycle to next
                    OnCycleProfile?.Invoke(1);
                    break;
                case 1: // Master Audio: toggle mute
                    SystemHardwareMonitor.ToggleMasterMute();
                    UpdateAudioTileDisplay();
                    break;
                case 2: // Gyro: toggle enable
                    OnToggleGyro?.Invoke();
                    break;
                case 3: // PC Hardware: refresh
                    UpdateHardwareTileDisplay();
                    break;
                case 4: // Touchpad: toggle enable
                    OnToggleTouchpad?.Invoke();
                    break;
            }
        }

        private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string tagStr && int.TryParse(tagStr, out int idx))
            {
                SelectTile(idx);
            }
        }

        private void UpdateAudioTileDisplay()
        {
            try
            {
                int vol = SystemHardwareMonitor.GetMasterVolumePercent();
                bool muted = SystemHardwareMonitor.IsMasterMuted();
                if (TxtTileVolumePercent != null)
                    TxtTileVolumePercent.Text = muted ? "MUTED" : $"{vol}%";
                if (BarTileVolume != null)
                    BarTileVolume.Value = muted ? 0 : vol;
            }
            catch { }
        }

        private void UpdateHardwareTileDisplay()
        {
            try
            {
                int cpu = SystemHardwareMonitor.GetCpuUsagePercent();
                if (TxtTileCpuUsage != null)
                    TxtTileCpuUsage.Text = $"CPU: {cpu}%";

                var (ramLoad, usedGb, totalGb) = SystemHardwareMonitor.GetMemoryStats();
                if (TxtTileRamUsage != null)
                    TxtTileRamUsage.Text = $"RAM: {ramLoad}% ({usedGb:F1} GB)";
                if (TxtTileRamTotal != null)
                    TxtTileRamTotal.Text = $"Total: {totalGb:F1} GB DDR";
            }
            catch { }
        }

        public void UpdateState(ControllerTelemetry t)
        {
            if (t == null) return;

            Dispatcher.Invoke(() =>
            {
                // Top Status Bar: Controller & Connection
                if (TxtControllerName != null)
                    TxtControllerName.Text = string.IsNullOrEmpty(t.ModelName) ? "DualSense Wireless Controller" : t.ModelName;

                if (TxtConnection != null)
                {
                    string connType = t.Connection == 1 ? "USB" : "Bluetooth";
                    string latStr = t.LatencyMs > 0 ? t.LatencyMs.ToString("F1") : "1.0";
                    TxtConnection.Text = $"{t.PollingRateHz}Hz ({latStr}ms) • {connType}";
                }

                // Clock
                if (TxtClock != null)
                    TxtClock.Text = DateTime.Now.ToString("h:mm tt");

                // Battery
                if (TxtBatteryPercent != null)
                {
                    if (t.Battery <= 15 && !t.Charging)
                    {
                        TxtBatteryPercent.Text = $"{t.Battery}% [LOW]";
                        TxtBatteryPercent.Foreground = (Brush)new BrushConverter().ConvertFromString("#FF334B")!;
                    }
                    else
                    {
                        TxtBatteryPercent.Text = $"{t.Battery}%";
                        if (t.Battery > 50)
                            TxtBatteryPercent.Foreground = (Brush)FindResource("AccentGreen");
                        else
                            TxtBatteryPercent.Foreground = (Brush)FindResource("AccentAmber");
                    }
                }
                if (TxtChargingBadge != null)
                    TxtChargingBadge.Visibility = t.Charging ? Visibility.Visible : Visibility.Collapsed;

                // Mini Stereo Tactile Rumble Meters
                if (BarMiniHapticL != null) BarMiniHapticL.Value = Math.Clamp((int)t.HapticL, 0, 255);
                if (BarMiniHapticR != null) BarMiniHapticR.Value = Math.Clamp((int)t.HapticR, 0, 255);

                // TILE 0: Active Game & Profile
                if (TxtTileGameName != null)
                {
                    string activeGame = GetActiveGameName?.Invoke() ?? "";
                    TxtTileGameName.Text = string.IsNullOrEmpty(activeGame) ? "Desktop (No Game)" : activeGame;
                }
                if (TxtTileProfileName != null)
                {
                    string profileName = GetActiveProfileName?.Invoke() ?? "Default Profile";
                    TxtTileProfileName.Text = profileName;
                }
                if (TxtTileTriggersMode != null && t.Config != null)
                {
                    string lMode = FormatTriggerMode(t.Config.LtMode);
                    string rMode = FormatTriggerMode(t.Config.RtMode);
                    TxtTileTriggersMode.Text = $"L2: {lMode} • R2: {rMode}";
                }

                // TILE 1: Master Audio & Mic
                UpdateAudioTileDisplay();
                if (TxtTileAudioDevice != null)
                {
                    TxtTileAudioDevice.Text = !string.IsNullOrEmpty(t.RenderDev) ? t.RenderDev : "Default Audio Device";
                }
                if (TxtTileMicBadge != null)
                {
                    if (t.MicMuted)
                    {
                        TxtTileMicBadge.Text = "🎙️ MIC: MUTED";
                        TxtTileMicBadge.Foreground = (Brush)FindResource("AccentAmber");
                    }
                    else
                    {
                        TxtTileMicBadge.Text = "🎙️ MIC: ACTIVE";
                        TxtTileMicBadge.Foreground = (Brush)FindResource("AccentGreen");
                    }
                }

                // TILE 2: 6-Axis Gyro Aiming
                if (t.Config != null)
                {
                    bool gyroOn = t.Config.Gyro;
                    if (TxtTileGyroState != null)
                    {
                        TxtTileGyroState.Text = gyroOn ? "ACTIVE (ADS)" : "DISABLED";
                        TxtTileGyroState.Foreground = gyroOn ? (Brush)FindResource("TextPrimary") : (Brush)FindResource("TextSecondary");
                    }
                    if (TxtTileGyroSens != null)
                    {
                        TxtTileGyroSens.Text = $"Sens: {t.Config.GyroSensX:F1}x (Yaw/Pitch)";
                    }
                    if (TxtTileGyroStatus != null)
                    {
                        TxtTileGyroStatus.Text = gyroOn ? "Sub-Pixel Mouse (Active)" : "Press ✕ to Enable";
                    }
                }

                // TILE 3: PC Hardware
                UpdateHardwareTileDisplay();

                // TILE 4: Controller & Touchpad
                if (t.Config != null)
                {
                    bool mouseOn = t.Config.Mouse;
                    if (TxtTileTouchpadSens != null)
                    {
                        TxtTileTouchpadSens.Text = mouseOn ? $"Touchpad: {t.Config.MouseSens:F1}x" : "Touchpad: DISABLED";
                    }
                    if (TxtTileTouchpadMode != null)
                    {
                        TxtTileTouchpadMode.Text = "Precision Notch: ON";
                    }
                    if (TxtTileHapticsSummary != null)
                    {
                        int intensity = (int)(t.Config.HapticIntensity * 100);
                        TxtTileHapticsSummary.Text = $"Haptics: {intensity}% (Punchy)";
                    }
                }
            });
        }

        private static string FormatTriggerMode(int mode) => mode switch
        {
            1 => "Rigid",
            2 => "Pulse",
            5 => "Bow",
            6 => "TriggerStop",
            _ => "Standard"
        };

        public void ShowNotification(string title, string details)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtTileGameName != null) TxtTileGameName.Text = title;
                if (TxtTileProfileName != null) TxtTileProfileName.Text = details;

                ShowHud();

                // Toast notifications auto-dismiss after 3.5 seconds
                _hideTimer.Stop();
                _hideTimer.Start();
            });
        }

        public void ToggleOrRefresh()
        {
            if (_isVisibleToUser)
            {
                _hideTimer.Stop();
                HideHud();
            }
            else
            {
                ShowHud();
            }
        }

        public void ShowHud()
        {
            _isVisibleToUser = true;

            // Pinned at bottom center of primary screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - Width) / 2.0;
            Top = screenHeight - Height - 50.0;

            if (!IsVisible)
            {
                Show();
            }

            var helper = new WindowInteropHelper(this);
            SetWindowPos(helper.Handle, HWND_TOPMOST, (int)Left, (int)Top, (int)Width, (int)Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

            // Smooth fade-in
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            HudRoot.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Persistent: HUD stays open until explicitly closed by user (no hideTimer)
            _hideTimer.Stop();
        }

        public void HideHud()
        {
            _isVisibleToUser = false;
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                if (!_isVisibleToUser)
                {
                    Hide();
                }
            };
            HudRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}
