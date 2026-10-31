using System;
using System.Windows;
using System.Windows.Media;

namespace OpenDualHub
{
    public class Ps3WaveBackground : FrameworkElement
    {
        private double _phase1 = 0;
        private double _phase2 = 1.5;
        private double _phase3 = 3.0;

        private readonly Pen _pen1;
        private readonly Pen _pen2;
        private readonly Pen _pen3;
        private readonly Pen _fillPen1;

        public Ps3WaveBackground()
        {
            IsHitTestVisible = false;

            // Glowing PS3 ethereal wave brushes
            var grad1 = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 112, 209), 0.0));
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(90, 0, 245, 212), 0.45));
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(130, 41, 121, 255), 0.75));
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 112, 209), 1.0));
            grad1.Freeze();

            var grad2 = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            grad2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 41, 121, 255), 0.0));
            grad2.GradientStops.Add(new GradientStop(Color.FromArgb(60, 0, 245, 212), 0.5));
            grad2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 112, 209), 1.0));
            grad2.Freeze();

            var grad3 = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            grad3.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 245, 212), 0.0));
            grad3.GradientStops.Add(new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.5));
            grad3.GradientStops.Add(new GradientStop(Color.FromArgb(0, 41, 121, 255), 1.0));
            grad3.Freeze();

            _pen1 = new Pen(grad1, 2.5);
            _pen1.Freeze();

            _pen2 = new Pen(grad2, 1.8);
            _pen2.Freeze();

            _pen3 = new Pen(grad3, 1.2);
            _pen3.Freeze();

            var fillBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(20, 0, 112, 209), 0.0));
            fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 8, 11, 18), 1.0));
            fillBrush.Freeze();
            _fillPen1 = new Pen(fillBrush, 0);
            _fillPen1.Freeze();

            Loaded += (s, e) => CompositionTarget.Rendering += OnRendering;
            Unloaded += (s, e) => CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            _phase1 += 0.012;
            _phase2 += 0.008;
            _phase3 += 0.015;

            if (_phase1 > Math.PI * 2000) _phase1 = 0;
            if (_phase2 > Math.PI * 2000) _phase2 = 0;
            if (_phase3 > Math.PI * 2000) _phase3 = 0;

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Background Deep Piano Black with Charcoal Slate vignette
            var bgBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.45),
                GradientOrigin = new Point(0.5, 0.45),
                RadiusX = 0.85,
                RadiusY = 0.85
            };
            bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x17, 0x26), 0.0));
            bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x08, 0x0B, 0x13), 0.7));
            bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x04, 0x06, 0x0A), 1.0));
            dc.DrawRectangle(bgBrush, null, new Rect(0, 0, w, h));

            // Wave Ribbon 1 (Primary PS3 Wave)
            DrawRibbon(dc, _pen1, w, h * 0.58, h * 0.16, _phase1, 1.2, 0.0025);

            // Wave Ribbon 2 (Harmonic Under-wave)
            DrawRibbon(dc, _pen2, w, h * 0.62, h * 0.20, _phase2, 0.9, 0.0018);

            // Wave Ribbon 3 (Shimmer Crest)
            DrawRibbon(dc, _pen3, w, h * 0.55, h * 0.12, _phase3, 1.5, 0.0032);
        }

        private void DrawRibbon(DrawingContext dc, Pen pen, double width, double baseY, double amplitude, double phase, double freqFactor, double k)
        {
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                int steps = 28;
                double stepX = width / steps;

                double startY = baseY + Math.Sin(phase) * amplitude + Math.Cos(phase * 0.7) * (amplitude * 0.35);
                ctx.BeginFigure(new Point(0, startY), false, false);

                Point p0 = new Point(0, startY);
                for (int i = 1; i <= steps; i++)
                {
                    double x = i * stepX;
                    double angle = x * k * freqFactor + phase;
                    double y = baseY + Math.Sin(angle) * amplitude + Math.Cos(angle * 0.65) * (amplitude * 0.4);
                    Point p1 = new Point(x, y);

                    // Smooth Bezier segment
                    Point cp1 = new Point(p0.X + stepX * 0.5, p0.Y);
                    Point cp2 = new Point(p1.X - stepX * 0.5, p1.Y);
                    ctx.BezierTo(cp1, cp2, p1, true, true);
                    p0 = p1;
                }
            }
            geom.Freeze();
            dc.DrawGeometry(null, pen, geom);
        }
    }
}