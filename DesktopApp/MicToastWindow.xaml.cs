using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OpenDualHub
{
    public partial class MicToastWindow : Window
    {
        private readonly DispatcherTimer _dismissTimer;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_EXSTYLE = -20;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public MicToastWindow()
        {
            InitializeComponent();

            _dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1800)
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

        public void ShowStatus(bool isMuted)
        {
            Dispatcher.Invoke(() =>
            {
                Reposition();

                if (isMuted)
                {
                    ToastRoot.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 167, 38)); // Amber
                    BadgeIcon.Background = new SolidColorBrush(Color.FromArgb(50, 255, 152, 0));
                    BadgeIcon.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 167, 38));
                    if (PathIcon != null) PathIcon.Fill = new SolidColorBrush(Color.FromRgb(255, 167, 38));
                    TxtStatus.Text = "MICROPHONE MUTED";
                    TxtSub.Text = "Hardware & Windows mic silenced";
                    PillState.Background = new SolidColorBrush(Color.FromArgb(50, 255, 152, 0));
                    PillState.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 167, 38));
                    TxtBadge.Text = "MUTED";
                    TxtBadge.Foreground = new SolidColorBrush(Color.FromRgb(255, 167, 38));
                }
                else
                {
                    ToastRoot.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green
                    BadgeIcon.Background = new SolidColorBrush(Color.FromArgb(40, 0, 230, 118));
                    BadgeIcon.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 230, 118));
                    if (PathIcon != null) PathIcon.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    TxtStatus.Text = "MICROPHONE UNMUTED";
                    TxtSub.Text = "Microphone is live and active";
                    PillState.Background = new SolidColorBrush(Color.FromArgb(40, 0, 230, 118));
                    PillState.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    TxtBadge.Text = "ACTIVE";
                    TxtBadge.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }

                if (!IsVisible)
                {
                    Show();
                }

                _dismissTimer.Stop();

                var fadeIn = new DoubleAnimation(ToastRoot.Opacity, 1.0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ToastRoot.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                _dismissTimer.Start();
            });
        }

        private void FadeOut()
        {
            var fadeOut = new DoubleAnimation(ToastRoot.Opacity, 0.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                if (ToastRoot.Opacity <= 0.01)
                {
                    Hide();
                }
            };
            ToastRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}
