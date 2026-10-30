using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OpenDualHub
{
    public partial class VolumeHudWindow : Window
    {
        private readonly DispatcherTimer _dismissTimer;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_EXSTYLE = -20;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public VolumeHudWindow()
        {
            InitializeComponent();

            _dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1600)
            };
            _dismissTimer.Tick += (s, e) =>
            {
                _dismissTimer.Stop();
                FadeOut();
            };

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            Reposition();
        }

        private void Reposition()
        {
            double screenW = SystemParameters.PrimaryScreenWidth;
            Left = (screenW - Width) / 2;
            Top = 28;
        }

        public void ShowVolume(float volumeScalar, bool isMuted = false, string? deviceName = null)
        {
            Dispatcher.Invoke(() =>
            {
                Reposition();
                _dismissTimer.Stop();

                int pct = (int)Math.Round(Math.Clamp(volumeScalar, 0.0f, 1.0f) * 100f);

                if (isMuted || pct == 0)
                {
                    TxtIcon.Text = "🔇";
                    TxtVolumePct.Text = "MUTED";
                    TxtVolumePct.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                    ProgressVolume.Value = 0;
                    ProgressVolume.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                    HudRoot.BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 179, 0));
                }
                else
                {
                    TxtIcon.Text = pct > 65 ? "🔊" : (pct > 25 ? "🔉" : "🔈");
                    TxtVolumePct.Text = $"{pct}%";
                    TxtVolumePct.Foreground = new SolidColorBrush(Color.FromRgb(0, 245, 212));
                    ProgressVolume.Value = pct;
                    ProgressVolume.Foreground = new SolidColorBrush(Color.FromRgb(0, 245, 212));
                    HudRoot.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 245, 212));
                }

                if (!string.IsNullOrEmpty(deviceName) && TxtOutputDevice != null)
                {
                    TxtOutputDevice.Text = deviceName.Length > 16 ? deviceName.Substring(0, 14) + ".." : deviceName;
                }

                if (!IsVisible)
                {
                    Show();
                }

                FadeIn();
                _dismissTimer.Start();
            });
        }

        private void FadeIn()
        {
            var anim = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            HudRoot.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void FadeOut()
        {
            var anim = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (s, e) =>
            {
                if (HudRoot.Opacity == 0.0)
                {
                    Hide();
                }
            };
            HudRoot.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}