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
        private static readonly SolidColorBrush s_svgIdleBrush = CreateFrozenBrush(Color.FromRgb(0x22, 0x2E, 0x46));
        private static readonly SolidColorBrush s_svgBumperIdleBrush = CreateFrozenBrush(Color.FromRgb(0x1B, 0x26, 0x3B));
        private static readonly SolidColorBrush s_svgTrigIdleBrush = CreateFrozenBrush(Color.FromRgb(0x16, 0x20, 0x32));
        private static readonly SolidColorBrush s_svgStickIdleBrush = CreateFrozenBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        private static readonly SolidColorBrush s_amberBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0xA0, 0x00));
        private static readonly SolidColorBrush s_greenBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        private static readonly SolidColorBrush s_redBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x33, 0x4B));
        private static readonly SolidColorBrush s_blueBrush = CreateFrozenBrush(Color.FromRgb(0x29, 0x79, 0xFF));

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
            BtnTriangle.Background = t.Triangle ? s_greenBrush : pillBg;
            BtnSquare.Background = t.Square ? s_dotRightBrush : pillBg;
            BtnCircle.Background = t.Circle ? s_redBrush : pillBg;
            BtnCross.Background = t.Cross ? s_blueBrush : pillBg;

            // Update Interactive Controller Hardware Schematic
            if (SvgL2 != null) SvgL2.Fill = t.L2 > 0.08f ? s_dotLeftBrush : s_svgTrigIdleBrush;
            if (SvgR2 != null) SvgR2.Fill = t.R2 > 0.08f ? s_dotRightBrush : s_svgTrigIdleBrush;
            if (SvgL1 != null) SvgL1.Fill = t.L1 ? s_dotLeftBrush : s_svgBumperIdleBrush;
            if (SvgR1 != null) SvgR1.Fill = t.R1 ? s_dotRightBrush : s_svgBumperIdleBrush;

            if (SvgDpadUp != null) SvgDpadUp.Fill = t.DPadUp ? s_dotLeftBrush : s_svgIdleBrush;
            if (SvgDpadDown != null) SvgDpadDown.Fill = t.DPadDown ? s_dotLeftBrush : s_svgIdleBrush;
            if (SvgDpadLeft != null) SvgDpadLeft.Fill = t.DPadLeft ? s_dotLeftBrush : s_svgIdleBrush;
            if (SvgDpadRight != null) SvgDpadRight.Fill = t.DPadRight ? s_dotLeftBrush : s_svgIdleBrush;

            if (SvgBtnTriangle != null) SvgBtnTriangle.Fill = t.Triangle ? s_greenBrush : s_svgIdleBrush;
            if (SvgBtnSquare != null) SvgBtnSquare.Fill = t.Square ? s_dotRightBrush : s_svgIdleBrush;
            if (SvgBtnCircle != null) SvgBtnCircle.Fill = t.Circle ? s_redBrush : s_svgIdleBrush;
            if (SvgBtnCross != null) SvgBtnCross.Fill = t.Cross ? s_blueBrush : s_svgIdleBrush;

            if (SvgShareBtn != null) SvgShareBtn.Fill = t.Share ? s_dotLeftBrush : s_svgIdleBrush;
            if (SvgOptionsBtn != null) SvgOptionsBtn.Fill = t.Options ? s_dotLeftBrush : s_svgIdleBrush;
            if (SvgHomeBtn != null) SvgHomeBtn.Fill = t.PS ? s_dotLeftBrush : s_svgIdleBrush;
            if (SvgMicBtn != null) SvgMicBtn.Fill = (t.MicBtn || t.MicMuted) ? s_amberBrush : s_svgIdleBrush;

            if (SvgStickCapL != null)
            {
                Canvas.SetLeft(SvgStickCapL, 165.0 + t.LX * 7.5);
                Canvas.SetTop(SvgStickCapL, 93.0 - t.LY * 7.5);
                SvgStickCapL.Fill = t.L3 ? s_dotLeftBrush : s_svgStickIdleBrush;
            }
            if (SvgStickCapR != null)
            {
                Canvas.SetLeft(SvgStickCapR, 271.0 + t.RX * 7.5);
                Canvas.SetTop(SvgStickCapR, 93.0 - t.RY * 7.5);
                SvgStickCapR.Fill = t.R3 ? s_dotRightBrush : s_svgStickIdleBrush;
            }

            if (SvgTouchDot1 != null)
            {
                if (t.Touch1.On)
                {
                    SvgTouchDot1.Visibility = Visibility.Visible;
                    Canvas.SetLeft(SvgTouchDot1, 180.0 + (t.Touch1.X / 1920.0) * 96.0);
                    Canvas.SetTop(SvgTouchDot1, 45.0 + (t.Touch1.Y / 1080.0) * 30.0);
                }
                else
                {
                    SvgTouchDot1.Visibility = Visibility.Collapsed;
                }
            }
            if (SvgTouchDot2 != null)
            {
                if (t.Touch2.On)
                {
                    SvgTouchDot2.Visibility = Visibility.Visible;
                    Canvas.SetLeft(SvgTouchDot2, 180.0 + (t.Touch2.X / 1920.0) * 96.0);
                    Canvas.SetTop(SvgTouchDot2, 45.0 + (t.Touch2.Y / 1080.0) * 30.0);
                }
                else
                {
                    SvgTouchDot2.Visibility = Visibility.Collapsed;
                }
            }
            if (TxtSchematicTouchInfo != null)
            {
                TxtSchematicTouchInfo.Text = t.Touch1.On
                    ? $"Touchpad Active: ({t.Touch1.X}, {t.Touch1.Y}) • Multi-Touch Live"
                    : "Touchpad: Idle • Bumpers, Sticks & Face Buttons Live";
            }
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
