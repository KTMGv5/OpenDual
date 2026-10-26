using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class CaptureBarWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly DispatcherTimer _autoDismissTimer = new();
        private readonly DispatcherTimer _actionDismissTimer = new();
        private int _selectedTile = 1; // Default to Save Recent Clip (Tile 1)
        private double _currentScale = 1.0;

        private static readonly int[] ClipDurations = { 15, 30, 60, 180, 300 };
        private static readonly string[] ClipDurationLabels = { "Last 15s", "Last 30s", "Last 1 min", "Last 3 min", "Last 5 min" };
        private int _clipDurationIndex = 1; // 30s default

        // Rate-limiting navigation state
        private bool _lastDpadLeft, _lastDpadRight, _lastDpadUp, _lastDpadDown, _lastCross, _lastCircle, _lastShare;
        private DateTime _dpadHeldSince = DateTime.MinValue;
        private DateTime _lastDpadTick = DateTime.MinValue;
        private int _lastHeldDirection = 0; // -1: Left, +1: Right

        public Func<string>? GetActiveGameName { get; set; }
        public bool IsVisibleToUser => Visibility == Visibility.Visible && Opacity > 0.05;

        public CaptureBarWindow()
        {
            InitializeComponent();

            _autoDismissTimer.Interval = TimeSpan.FromSeconds(8);
            _autoDismissTimer.Tick += (s, e) =>
            {
                _autoDismissTimer.Stop();
                HideBar();
            };

            _actionDismissTimer.Interval = TimeSpan.FromMilliseconds(1300);
            _actionDismissTimer.Tick += (s, e) =>
            {
                _actionDismissTimer.Stop();
                HideBar();
            };

            // Detect current duration from registry
            int currentSec = GameCaptureManager.GetHistoricalBufferLengthSeconds();
            for (int i = 0; i < ClipDurations.Length; i++)
            {
                if (ClipDurations[i] == currentSec)
                {
                    _clipDurationIndex = i;
                    break;
                }
            }

            UpdateTileHighlights();
            UpdateDurationDisplay();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void SetBarScale(double scale)
        {
            _currentScale = Math.Clamp(scale, 0.75, 1.50);
            if (BarScaleTransform != null)
            {
                BarScaleTransform.ScaleX = _currentScale;
                BarScaleTransform.ScaleY = _currentScale;
            }
            Reposition();
        }

        private void Reposition()
        {
            UpdateLayout();
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;

            double actW = ActualWidth > 0 ? ActualWidth : (920 * _currentScale);
            double actH = ActualHeight > 0 ? ActualHeight : (170 * _currentScale);

            Left = Math.Max(0, (screenW - actW) / 2.0);
            Top = Math.Max(0, screenH - actH - 35);
        }

        public void ToggleOrShow()
        {
            if (IsVisibleToUser)
            {
                HideBar();
            }
            else
            {
                ShowBar();
            }
        }

        public void ShowBar()
        {
            if (GetActiveGameName != null)
            {
                string game = GetActiveGameName();
                TxtActiveGame.Text = string.IsNullOrWhiteSpace(game) ? "Windows Desktop" : game;
            }

            TxtNotification.Text = "";
            UpdateTileHighlights();
            UpdateDurationDisplay();

            Reposition();
            Visibility = Visibility.Visible;
            Opacity = 1.0;

            _autoDismissTimer.Stop();
            _autoDismissTimer.Start();
        }

        public void HideBar()
        {
            _autoDismissTimer.Stop();
            _actionDismissTimer.Stop();
            Visibility = Visibility.Collapsed;
        }

        public void ShowInstantNotification(string message)
        {
            TxtActiveGame.Text = message;
            TxtNotification.Text = message;
            Visibility = Visibility.Visible;
            Opacity = 1.0;
            Reposition();

            _autoDismissTimer.Stop();
            _actionDismissTimer.Stop();
            _actionDismissTimer.Start();
        }

        public void HandleControllerNavigation(ControllerTelemetry t)
        {
            if (!IsVisibleToUser) return;

            DateTime now = DateTime.UtcNow;

            // Handle Circle or Share: dismiss
            if ((t.Circle && !_lastCircle) || (t.Share && !_lastShare))
            {
                HideBar();
                _lastCircle = t.Circle;
                _lastShare = t.Share;
                return;
            }

            bool dpadUp = (t.DPad == 0 || t.DPad == 1 || t.DPad == 7);
            bool dpadRight = (t.DPad == 1 || t.DPad == 2 || t.DPad == 3);
            bool dpadDown = (t.DPad == 3 || t.DPad == 4 || t.DPad == 5);
            bool dpadLeft = (t.DPad == 5 || t.DPad == 6 || t.DPad == 7);

            // Left / Right Navigation (D-Pad or Stick)
            bool navLeft = dpadLeft || (t.LX < 0.35f);
            bool navRight = dpadRight || (t.LX > 0.65f);

            int currentDirection = 0;
            if (navLeft) currentDirection = -1;
            else if (navRight) currentDirection = +1;

            if (currentDirection != 0)
            {
                bool canTrigger = false;
                if (currentDirection != _lastHeldDirection)
                {
                    canTrigger = true;
                    _dpadHeldSince = now;
                    _lastDpadTick = now;
                    _lastHeldDirection = currentDirection;
                }
                else
                {
                    double heldMs = (now - _dpadHeldSince).TotalMilliseconds;
                    double sinceLastTick = (now - _lastDpadTick).TotalMilliseconds;
                    if (heldMs >= 320 && sinceLastTick >= 130)
                    {
                        canTrigger = true;
                        _lastDpadTick = now;
                    }
                }

                if (canTrigger)
                {
                    _selectedTile = Math.Clamp(_selectedTile + currentDirection, 0, 4);
                    UpdateTileHighlights();
                    _autoDismissTimer.Stop();
                    _autoDismissTimer.Start();
                }
            }
            else
            {
                _lastHeldDirection = 0;
            }

            // Up / Down Navigation: On Tile 1, cycles duration
            bool navUp = dpadUp || (t.LY < 0.35f);
            bool navDown = dpadDown || (t.LY > 0.65f);

            if (_selectedTile == 1)
            {
                if (navUp && !_lastDpadUp)
                {
                    _clipDurationIndex = Math.Min(_clipDurationIndex + 1, ClipDurations.Length - 1);
                    UpdateDurationDisplay();
                    GameCaptureManager.SetHistoricalBufferLengthSeconds(ClipDurations[_clipDurationIndex]);
                    _autoDismissTimer.Stop();
                    _autoDismissTimer.Start();
                }
                else if (navDown && !_lastDpadDown)
                {
                    _clipDurationIndex = Math.Max(_clipDurationIndex - 1, 0);
                    UpdateDurationDisplay();
                    GameCaptureManager.SetHistoricalBufferLengthSeconds(ClipDurations[_clipDurationIndex]);
                    _autoDismissTimer.Stop();
                    _autoDismissTimer.Start();
                }
            }

            // Cross (X): Execute
            if (t.Cross && !_lastCross)
            {
                ExecuteAction(_selectedTile);
            }

            _lastDpadLeft = navLeft;
            _lastDpadRight = navRight;
            _lastDpadUp = navUp;
            _lastDpadDown = navDown;
            _lastCross = t.Cross;
            _lastCircle = t.Circle;
            _lastShare = t.Share;
        }

        private void UpdateDurationDisplay()
        {
            string label = ClipDurationLabels[_clipDurationIndex];
            TxtClipDuration.Text = $"{label} (▲/▼ change)";
            TxtBufferNotice.Text = $"Instant Replay: {ClipDurations[_clipDurationIndex]}s";
        }

        private void UpdateTileHighlights()
        {
            HighlightTile(Tile0, _selectedTile == 0);
            HighlightTile(Tile1, _selectedTile == 1);
            HighlightTile(Tile2, _selectedTile == 2);
            HighlightTile(Tile3, _selectedTile == 3);
            HighlightTile(Tile4, _selectedTile == 4);
        }

        private void HighlightTile(Border tile, bool active)
        {
            if (tile == null) return;
            tile.Background = (SolidColorBrush)FindResource(active ? "BgActive" : "BgCard");
            tile.BorderBrush = (SolidColorBrush)FindResource(active ? "BorderActive" : "BorderNormal");
            tile.BorderThickness = new Thickness(active ? 2 : 1);
        }

        public void ExecuteAction(int index)
        {
            switch (index)
            {
                case 0: // Screenshot
                    GameCaptureManager.TakeScreenshot();
                    TxtNotification.Text = "📸 Screenshot Captured (Win+Alt+PrtScn)";
                    break;

                case 1: // Save Recent Clip
                    int sec = ClipDurations[_clipDurationIndex];
                    GameCaptureManager.SaveRecentClip(sec);
                    TxtNotification.Text = $"⏱️ Saved Last {sec}s Clip (Win+Alt+G)";
                    break;

                case 2: // Start / Stop Recording
                    GameCaptureManager.ToggleRecording();
                    TxtNotification.Text = "🔴 Recording Toggled (Win+Alt+R)";
                    break;

                case 3: // Xbox Game Bar
                    GameCaptureManager.OpenXboxGameBar();
                    TxtNotification.Text = "🎮 Opening Xbox Game Bar...";
                    break;

                case 4: // Gallery
                    GameCaptureManager.OpenCapturesFolder();
                    TxtNotification.Text = "📁 Opening Captures Folder...";
                    break;
            }

            _autoDismissTimer.Stop();
            _actionDismissTimer.Stop();
            _actionDismissTimer.Start();
        }

        private void Tile0_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ExecuteAction(0);
        private void Tile1_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ExecuteAction(1);
        private void Tile2_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ExecuteAction(2);
        private void Tile3_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ExecuteAction(3);
        private void Tile4_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ExecuteAction(4);
    }
}
