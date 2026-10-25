using System;
using System.Runtime.InteropServices;
using System.Windows;
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

        public QuickHudWindow()
        {
            InitializeComponent();

            // Force handle creation and configure non-activating topmost toolwindow
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();

            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.5)
            };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                HideHud();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void UpdateState(ControllerTelemetry t)
        {
            if (t == null) return;

            Dispatcher.Invoke(() =>
            {
                TxtControllerName.Text = t.ModelName;
                TxtConnection.Text = $"Direct 1000Hz HID • {t.ConnectionName}";

                // Battery
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
                TxtChargingBadge.Visibility = t.Charging ? Visibility.Visible : Visibility.Collapsed;

                // Mic
                if (t.MicMuted)
                {
                    BadgeMicMuted.Visibility = Visibility.Visible;
                    BadgeMicActive.Visibility = Visibility.Collapsed;
                }
                else
                {
                    BadgeMicMuted.Visibility = Visibility.Collapsed;
                    BadgeMicActive.Visibility = Visibility.Visible;
                }

                // Triggers & Haptics
                string trigText = "Triggers: Default";
                if (t.Config != null)
                {
                    string lMode = FormatTriggerMode(t.Config.LtMode);
                    string rMode = FormatTriggerMode(t.Config.RtMode);
                    bool haptic = t.Config.Haptic;
                    bool kick = t.Config.AudioTriggerKick;

                    trigText = $"L2: {lMode} • R2: {rMode}";
                    if (kick) trigText += " • Recoil Kick Active";
                    else if (haptic) trigText += " • Audio Haptics Active";
                }
                TxtTriggerProfile.Text = trigText;
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
                TxtControllerName.Text = title;
                TxtTriggerProfile.Text = details;
                ShowHud();
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

            // Position at bottom center of primary screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - Width) / 2.0;
            Top = screenHeight - Height - 55.0; // 55px above bottom edge / taskbar

            // Ensure window is in WPF visual tree composition without activating
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

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        public void HideHud()
        {
            _isVisibleToUser = false;
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(220))
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
