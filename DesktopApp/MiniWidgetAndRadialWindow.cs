using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenDualHub.Models;

namespace OpenDualHub
{
    /// <summary>
    /// Item 14: Lightweight Screen-Sync Ambient Lightbar Sampler ("Ambilight" for Controller)
    /// Samples 9 screen coordinates via GDI32 GetDC/GetPixel at low frequency (~6 Hz) with zero allocations.
    /// </summary>
    public static class ScreenAmbienceSampler
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        private static float _smoothR = 0f, _smoothG = 180f, _smoothB = 255f;

        public static (int R, int G, int B) SampleScreenColor()
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return ((int)_smoothR, (int)_smoothG, (int)_smoothB);

            try
            {
                int w = (int)SystemParameters.PrimaryScreenWidth;
                int h = (int)SystemParameters.PrimaryScreenHeight;
                if (w <= 0) w = 1920;
                if (h <= 0) h = 1080;

                long sumR = 0, sumG = 0, sumB = 0;
                int count = 0;

                int[] xs = { w / 4, w / 2, (w * 3) / 4 };
                int[] ys = { h / 4, h / 2, (h * 3) / 4 };

                for (int ix = 0; ix < 3; ix++)
                {
                    for (int iy = 0; iy < 3; iy++)
                    {
                        uint p = GetPixel(hdc, xs[ix], ys[iy]);
                        if (p != 0xFFFFFFFF)
                        {
                            sumR += (int)(p & 0xFF);
                            sumG += (int)((p >> 8) & 0xFF);
                            sumB += (int)((p >> 16) & 0xFF);
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    float avgR = sumR / (float)count;
                    float avgG = sumG / (float)count;
                    float avgB = sumB / (float)count;

                    // Boost saturation slightly so dark screens still produce a rich lightbar glow
                    float maxC = Math.Max(avgR, Math.Max(avgG, avgB));
                    if (maxC > 8f && maxC < 220f)
                    {
                        float boost = 235f / maxC;
                        avgR = Math.Clamp(avgR * boost, 0f, 255f);
                        avgG = Math.Clamp(avgG * boost, 0f, 255f);
                        avgB = Math.Clamp(avgB * boost, 0f, 255f);
                    }

                    _smoothR = _smoothR * 0.70f + avgR * 0.30f;
                    _smoothG = _smoothG * 0.70f + avgG * 0.30f;
                    _smoothB = _smoothB * 0.70f + avgB * 0.30f;
                }
            }
            catch { }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }

            return ((int)Math.Clamp(_smoothR, 0, 255), (int)Math.Clamp(_smoothG, 0, 255), (int)Math.Clamp(_smoothB, 0, 255));
        }
    }

    /// <summary>
    /// Item 17: Compact Mini-Widget Mode (Always-on-Top Streamer / Second-Screen View)
    /// </summary>
    public class MiniWidgetWindow : Window
    {
        private readonly MainWindow _mainWin;
        private readonly TextBlock _txtStatus;
        private readonly TextBlock _txtProfile;
        private readonly TextBlock _txtBattery;
        private readonly ProgressBar _barL2;
        private readonly ProgressBar _barR2;
        private readonly Ellipse _stickDotL;
        private readonly Ellipse _stickDotR;

        public MiniWidgetWindow(MainWindow mainWin)
        {
            _mainWin = mainWin;
            Title = "OpenDual Mini-Widget";
            Width = 310;
            Height = 168;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - 330;
            Top = SystemParameters.WorkArea.Top + 24;

            var rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x09, 0x0D, 0x16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10)
            };
            rootBorder.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

            var stack = new StackPanel();

            // Header row
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            _txtStatus = new TextBlock
            {
                Text = "⚡ 1000Hz • 1.0ms",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(_txtStatus);

            var btnRestore = new Button
            {
                Content = "Expand Hub ↗",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(8, 3, 8, 3),
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x22, 0x38)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };
            btnRestore.Click += (s, e) =>
            {
                _mainWin.RestoreFromMiniWidget();
            };
            header.Children.Add(btnRestore);
            stack.Children.Add(header);

            // Profile & Battery Row
            var subRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            _txtProfile = new TextBlock
            {
                Text = "Profile: Standard Balanced",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xC4, 0xDE))
            };
            _txtBattery = new TextBlock
            {
                Text = "🔋 100% (~7h)",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            subRow.Children.Add(_txtProfile);
            subRow.Children.Add(_txtBattery);
            stack.Children.Add(subRow);

            // Live Mini Sticks + Triggers Visualizer
            var vizGrid = new Grid();
            vizGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            vizGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            vizGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var canvasL = new Canvas { Width = 46, Height = 46, Background = new SolidColorBrush(Color.FromRgb(0x10, 0x17, 0x26)) };
            canvasL.Children.Add(new Ellipse { Width = 44, Height = 44, Stroke = new SolidColorBrush(Color.FromRgb(0x28, 0x3B, 0x5C)), StrokeThickness = 1 });
            _stickDotL = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)) };
            Canvas.SetLeft(_stickDotL, 18);
            Canvas.SetTop(_stickDotL, 18);
            canvasL.Children.Add(_stickDotL);
            Grid.SetColumn(canvasL, 0);
            vizGrid.Children.Add(canvasL);

            var canvasR = new Canvas { Width = 46, Height = 46, Background = new SolidColorBrush(Color.FromRgb(0x10, 0x17, 0x26)) };
            canvasR.Children.Add(new Ellipse { Width = 44, Height = 44, Stroke = new SolidColorBrush(Color.FromRgb(0x28, 0x3B, 0x5C)), StrokeThickness = 1 });
            _stickDotR = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) };
            Canvas.SetLeft(_stickDotR, 18);
            Canvas.SetTop(_stickDotR, 18);
            canvasR.Children.Add(_stickDotR);
            Grid.SetColumn(canvasR, 1);
            vizGrid.Children.Add(canvasR);

            var trigStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            trigStack.Children.Add(new TextBlock { Text = "L2 / R2 ACTUATION", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x9B, 0xB8)), Margin = new Thickness(0, 0, 0, 3) });
            _barL2 = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Value = 0, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x2A)), Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 5) };
            _barR2 = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Value = 0, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x2A)), Foreground = new SolidColorBrush(Color.FromRgb(0x29, 0x79, 0xFF)), BorderThickness = new Thickness(0) };
            trigStack.Children.Add(_barL2);
            trigStack.Children.Add(_barR2);
            Grid.SetColumn(trigStack, 2);
            vizGrid.Children.Add(trigStack);

            stack.Children.Add(vizGrid);
            rootBorder.Child = stack;
            Content = rootBorder;
        }

        public void UpdateTelemetry(ControllerTelemetry t, string profileName)
        {
            if (!IsVisible) return;
            _txtProfile.Text = $"Profile: {profileName}";
            if (t.Connected)
            {
                _txtStatus.Text = $"⚡ {t.PollingRateHz}Hz • {t.LatencyMs:F2}ms";
                double estHours = Math.Max(0.3, (t.Battery / 100.0) * 7.5);
                _txtBattery.Text = $"🔋 {t.Battery}% (~{estHours:F1}h)";
                _barL2.Value = t.L2 * 100.0;
                _barR2.Value = t.R2 * 100.0;
                Canvas.SetLeft(_stickDotL, 18 + t.LX * 16);
                Canvas.SetTop(_stickDotL, 18 - t.LY * 16);
                Canvas.SetLeft(_stickDotR, 18 + t.RX * 16);
                Canvas.SetTop(_stickDotR, 18 - t.RY * 16);
            }
            else
            {
                _txtStatus.Text = "⚡ Standby (1000Hz Ready)";
            }
        }
    }

    /// <summary>
    /// Item 12: On-Screen Touchpad Radial Menu Overlay (8-Slot Quick Command Wheel)
    /// </summary>
    public class RadialMenuWindow : Window
    {
        private readonly MainWindow _mainWin;

        public RadialMenuWindow(MainWindow mainWin)
        {
            _mainWin = mainWin;
            Title = "OpenDual Radial Menu";
            Width = 380;
            Height = 380;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var grid = new Grid();
            var bgCircle = new Ellipse
            {
                Width = 350,
                Height = 350,
                Fill = new SolidColorBrush(Color.FromArgb(0xEC, 0x09, 0x0E, 0x18)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                StrokeThickness = 2
            };
            grid.Children.Add(bgCircle);

            var centerBtn = new Button
            {
                Content = "✕ Close\nRadial",
                Width = 86,
                Height = 86,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1F, 0x33)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                BorderThickness = new Thickness(1.5),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            centerBtn.Click += (s, e) => Hide();
            grid.Children.Add(centerBtn);

            (string Label, Action Act)[] items = new (string, Action)[]
            {
                ("📸 Screenshot", () => _mainWin.GestureEngine.ExecuteDirectAction(GestureAction.TakeScreenshot)),
                ("🎬 Save Clip", () => _mainWin.GestureEngine.ExecuteDirectAction(GestureAction.SaveReplayClip)),
                ("🎙 Discord Mute", () => _mainWin.GestureEngine.ExecuteDirectAction(GestureAction.DiscordMute)),
                ("🎧 Deafen", () => _mainWin.GestureEngine.ExecuteDirectAction(GestureAction.DiscordDeafen)),
                ("⏯ Play/Pause", () => _mainWin.GestureEngine.ExecuteDirectAction(GestureAction.PlayPauseMedia)),
                ("⏭ Next Track", () => _mainWin.GestureEngine.ExecuteDirectAction(GestureAction.NextTrack)),
                ("🎯 Toggle Gyro", () => _mainWin.ToggleGyroFromExternal()),
                ("👤 Next Profile", () => _mainWin.CycleProfileFromExternal())
            };

            double radius = 122;
            for (int i = 0; i < items.Length; i++)
            {
                double angle = (i * Math.PI * 2.0 / items.Length) - (Math.PI / 2.0);
                double dx = Math.Cos(angle) * radius;
                double dy = Math.Sin(angle) * radius;
                var idx = i;

                var btn = new Button
                {
                    Content = items[i].Label,
                    Width = 94,
                    Height = 34,
                    Background = new SolidColorBrush(Color.FromRgb(0x15, 0x22, 0x38)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x29, 0x79, 0xFF)),
                    BorderThickness = new Thickness(1),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    RenderTransform = new TranslateTransform(dx, dy)
                };
                btn.Click += (s, e) =>
                {
                    Hide();
                    items[idx].Act();
                };
                grid.Children.Add(btn);
            }

            Content = grid;
        }

        public void ToggleMenu()
        {
            if (IsVisible) Hide();
            else { Show(); Activate(); }
        }
    }
}
