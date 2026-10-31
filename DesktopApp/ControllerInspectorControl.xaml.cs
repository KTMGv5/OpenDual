using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class ControllerInspectorControl : UserControl
    {
        public MainWindow? MainWindow { get; set; }

        private static readonly SolidColorBrush s_ringBrush = CreateFrozenBrush(Color.FromArgb(40, 255, 255, 255));
        private static readonly SolidColorBrush s_gridBrush = CreateFrozenBrush(Color.FromArgb(30, 255, 255, 255));
        private static readonly SolidColorBrush s_dotLeftBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_dotRightBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x00, 0x7F));

        private Ellipse? _ringL, _dotL;
        private Line? _hLineL, _vLineL;

        private Ellipse? _ringR, _dotR;
        private Line? _hLineR, _vLineR;

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }

        public ControllerInspectorControl()
        {
            InitializeComponent();
        }

        public void UpdateTelemetry(ControllerTelemetry t)
        {
            if (t == null) return;

            // Overview & Metrics
            TxtModelTitle.Text = t.ModelName;
            TxtHzVal.Text = $"{t.PollingRateHz} Hz";
            TxtLatencyVal.Text = $"{t.LatencyMs:F1} ms";
            TxtConnectionInfo.Text = $"Connected via {t.ConnectionName} • Ultra Low-Latency Direct HID (1000Hz)";

            // Thumbsticks
            TxtCoordsL.Text = $"X: {t.LX:+0.00;-0.00;0.00}  Y: {t.LY:+0.00;-0.00;0.00}";
            TxtCoordsR.Text = $"X: {t.RX:+0.00;-0.00;0.00}  Y: {t.RY:+0.00;-0.00;0.00}";

            DrawStickPoint(CanvasStickL, ref _ringL, ref _hLineL, ref _vLineL, ref _dotL, t.LX, t.LY, s_dotLeftBrush);
            DrawStickPoint(CanvasStickR, ref _ringR, ref _hLineR, ref _vLineR, ref _dotR, t.RX, t.RY, s_dotRightBrush);

            // Triggers
            double l2Pct = Math.Clamp(t.L2 * 100.0, 0.0, 100.0);
            double r2Pct = Math.Clamp(t.R2 * 100.0, 0.0, 100.0);
            BarTriggerL.Value = l2Pct;
            TxtTriggerLVal.Text = $"{l2Pct:F0}%";
            BarTriggerR.Value = r2Pct;
            TxtTriggerRVal.Text = $"{r2Pct:F0}%";

            // Directional D-Pad
            var pillBg = (Brush)FindResource("PillBg");
            var actSig = (Brush)FindResource("AccentSignature");

            BtnDpadUp.Background = t.DPadUp ? actSig : pillBg;
            BtnDpadLeft.Background = t.DPadLeft ? actSig : pillBg;
            BtnDpadRight.Background = t.DPadRight ? actSig : pillBg;
            BtnDpadDown.Background = t.DPadDown ? actSig : pillBg;

            // Action Buttons
            BtnTriangle.Background = t.Triangle ? (Brush)FindResource("AccentGreen") : pillBg;
            BtnSquare.Background = t.Square ? (Brush)FindResource("AccentMagenta") : pillBg;
            BtnCircle.Background = t.Circle ? (Brush)FindResource("AccentRed") : pillBg;
            BtnCross.Background = t.Cross ? (Brush)FindResource("AccentBlue") : pillBg;
        }

        private static void DrawStickPoint(
            Canvas canvas,
            ref Ellipse? ring,
            ref Line? hLine,
            ref Line? vLine,
            ref Ellipse? dot,
            float x,
            float y,
            SolidColorBrush dotBrush)
        {
            if (canvas == null) return;

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 110;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 110;
            double cx = w * 0.5, cy = h * 0.5, r = cx - 6;

            if (dot == null)
            {
                ring = new Ellipse
                {
                    Stroke = s_ringBrush,
                    StrokeThickness = 1
                };
                hLine = new Line { Stroke = s_gridBrush };
                vLine = new Line { Stroke = s_gridBrush };
                dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = dotBrush,
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5
                };

                canvas.Children.Clear();
                canvas.Children.Add(ring);
                canvas.Children.Add(hLine);
                canvas.Children.Add(vLine);
                canvas.Children.Add(dot);
            }

            ring!.Width = r * 2;
            ring.Height = r * 2;
            Canvas.SetLeft(ring, cx - r);
            Canvas.SetTop(ring, cy - r);

            hLine!.X1 = 0; hLine.X2 = w; hLine.Y1 = cy; hLine.Y2 = cy;
            vLine!.X1 = cx; vLine.X2 = cx; vLine.Y1 = 0; vLine.Y2 = h;

            double px = cx + x * r;
            double py = cy - y * r;
            Canvas.SetLeft(dot, px - 5);
            Canvas.SetTop(dot, py - 5);
        }

        public void FocusItem(string itemId)
        {
            var defaultBorder = (Brush)FindResource("BorderColor");
            var glowBorder = (Brush)FindResource("AccentSignature");

            CardOverview.BorderBrush = defaultBorder;
            CardLiveInput.BorderBrush = defaultBorder;
            CardOverclock.BorderBrush = defaultBorder;

            switch (itemId)
            {
                case "ctrl_status":
                    CardOverview.BorderBrush = glowBorder;
                    CardOverview.BringIntoView();
                    break;
                case "ctrl_input":
                    CardLiveInput.BorderBrush = glowBorder;
                    CardLiveInput.BringIntoView();
                    break;
                case "ctrl_overclock":
                    CardOverclock.BorderBrush = (Brush)FindResource("AccentBlue");
                    CardOverclock.BringIntoView();
                    break;
                default:
                    CardOverview.BringIntoView();
                    break;
            }
        }

        private void BtnLaunchOverclock_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow != null)
            {
                MainWindow.OpenOverclockAssistant();
            }
            else
            {
                try
                {
                    var w = new OverclockAssistantWindow();
                    w.Owner = Window.GetWindow(this);
                    w.Show();
                }
                catch
                {
                    new OverclockAssistantWindow().Show();
                }
            }
        }
    }
}
