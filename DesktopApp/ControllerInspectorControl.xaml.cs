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

            DrawStickPoint(CanvasStickL, t.LX, t.LY, Color.FromRgb(0x00, 0xF5, 0xD4));
            DrawStickPoint(CanvasStickR, t.RX, t.RY, Color.FromRgb(0xFF, 0x00, 0x7F));

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

        private void DrawStickPoint(Canvas canvas, float x, float y, Color dotColor)
        {
            canvas.Children.Clear();
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 110;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 110;
            double cx = w * 0.5, cy = h * 0.5, r = cx - 6;

            // Concentric Ring & Crosshairs
            var circle = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 1,
                Margin = new Thickness(cx - r, cy - r, 0, 0)
            };
            canvas.Children.Add(circle);

            canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = cy, Y2 = cy, Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) });
            canvas.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = 0, Y2 = h, Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) });

            // Moving Stick Point
            double px = cx + x * r;
            double py = cy - y * r;

            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(dotColor),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1.5,
                Margin = new Thickness(px - 5, py - 5, 0, 0)
            };
            canvas.Children.Add(dot);
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
