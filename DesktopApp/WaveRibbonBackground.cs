using System;
using System.Windows;
using System.Windows.Media;

namespace OpenDualHub
{
    public class WaveRibbonBackground : FrameworkElement
    {
        private double _phase1 = 0;
        private double _phase2 = 1.5;
        private double _phase3 = 3.0;

        private readonly Pen _glowPen1;
        private readonly Pen _pen1;
        private readonly Pen _glowPen2;
        private readonly Pen _pen2;
        private readonly Pen _pen3;

        private struct StarParticle
        {
            public double X;
            public double Y;
            public double Radius;
            public double SpeedY;
            public double SpeedX;
            public double BaseAlpha;
            public double TwinklePhase;
            public double TwinkleSpeed;
        }

        private readonly StarParticle[] _particles = new StarParticle[20];
        private readonly Random _rand = new(1337);

        private static readonly Brush _bgBrush;
        private static readonly SolidColorBrush[] _particleBrushes;
        private long _lastRenderTicks = 0;
        private bool _isHooked = false;

        private bool _lightweightMode = false;
        public bool LightweightMode
        {
            get => _lightweightMode;
            set
            {
                if (_lightweightMode != value)
                {
                    _lightweightMode = value;
                    UpdateHookState();
                    InvalidateVisual();
                }
            }
        }

        static WaveRibbonBackground()
        {
            var bgBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.40),
                GradientOrigin = new Point(0.5, 0.40),
                RadiusX = 0.90,
                RadiusY = 0.90
            };
            bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x11, 0x18, 0x2A), 0.0));
            bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x07, 0x0A, 0x13), 0.65));
            bgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x03, 0x04, 0x08), 1.0));
            bgBrush.Freeze();
            _bgBrush = bgBrush;

            _particleBrushes = new SolidColorBrush[32];
            for (int i = 0; i < 32; i++)
            {
                byte a = (byte)(i * 200 / 31);
                var b = new SolidColorBrush(Color.FromArgb(a, 0x00, 0xF5, 0xD4));
                b.Freeze();
                _particleBrushes[i] = b;
            }
        }

        public WaveRibbonBackground()
        {
            IsHitTestVisible = false;

            // Initialize Lightweight Stardust Particles
            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i] = new StarParticle
                {
                    X = _rand.NextDouble(),
                    Y = _rand.NextDouble(),
                    Radius = 0.8 + _rand.NextDouble() * 2.0,
                    SpeedX = (_rand.NextDouble() - 0.4) * 0.0003,
                    SpeedY = -0.0002 - _rand.NextDouble() * 0.0005,
                    BaseAlpha = 0.2 + _rand.NextDouble() * 0.5,
                    TwinklePhase = _rand.NextDouble() * Math.PI * 2,
                    TwinkleSpeed = 0.02 + _rand.NextDouble() * 0.04
                };
            }

            // Glowing harmonic ethereal wave brushes
            var gradGlow1 = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            gradGlow1.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 80, 200), 0.0));
            gradGlow1.GradientStops.Add(new GradientStop(Color.FromArgb(45, 0, 245, 212), 0.45));
            gradGlow1.GradientStops.Add(new GradientStop(Color.FromArgb(60, 41, 121, 255), 0.75));
            gradGlow1.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 80, 200), 1.0));
            gradGlow1.Freeze();
            _glowPen1 = new Pen(gradGlow1, 9.0);
            _glowPen1.Freeze();

            var grad1 = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 112, 209), 0.0));
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0, 245, 212), 0.45));
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(180, 41, 121, 255), 0.75));
            grad1.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 112, 209), 1.0));
            grad1.Freeze();
            _pen1 = new Pen(grad1, 2.5);
            _pen1.Freeze();

            var gradGlow2 = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            gradGlow2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 41, 121, 255), 0.0));
            gradGlow2.GradientStops.Add(new GradientStop(Color.FromArgb(30, 0, 245, 212), 0.5));
            gradGlow2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 112, 209), 1.0));
            gradGlow2.Freeze();
            _glowPen2 = new Pen(gradGlow2, 7.0);
            _glowPen2.Freeze();

            var grad2 = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            grad2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 41, 121, 255), 0.0));
            grad2.GradientStops.Add(new GradientStop(Color.FromArgb(100, 0, 245, 212), 0.5));
            grad2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 112, 209), 1.0));
            grad2.Freeze();
            _pen2 = new Pen(grad2, 1.8);
            _pen2.Freeze();

            var grad3 = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            grad3.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 245, 212), 0.0));
            grad3.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 255, 255), 0.5));
            grad3.GradientStops.Add(new GradientStop(Color.FromArgb(0, 41, 121, 255), 1.0));
            grad3.Freeze();
            _pen3 = new Pen(grad3, 1.2);
            _pen3.Freeze();

            Loaded += (s, e) => UpdateHookState();
            Unloaded += (s, e) => UnhookRendering();
            IsVisibleChanged += (s, e) => UpdateHookState();
        }

        private void UpdateHookState()
        {
            if (IsLoaded && IsVisible && !_lightweightMode)
            {
                if (!_isHooked)
                {
                    CompositionTarget.Rendering += OnRendering;
                    _isHooked = true;
                }
            }
            else
            {
                UnhookRendering();
            }
        }

        private void UnhookRendering()
        {
            if (_isHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isHooked = false;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (_lightweightMode || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0) return;

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double elapsedMs = (now - _lastRenderTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs < 33.3) return; // Cap at smooth 30 FPS to minimize GPU/CPU power on all hardware
            _lastRenderTicks = now;

            _phase1 += 0.012;
            _phase2 += 0.008;
            _phase3 += 0.015;

            if (_phase1 > Math.PI * 2000) _phase1 = 0;
            if (_phase2 > Math.PI * 2000) _phase2 = 0;
            if (_phase3 > Math.PI * 2000) _phase3 = 0;

            // Update floating stardust
            for (int i = 0; i < _particles.Length; i++)
            {
                _particles[i].Y += _particles[i].SpeedY;
                _particles[i].X += _particles[i].SpeedX;
                _particles[i].TwinklePhase += _particles[i].TwinkleSpeed;

                if (_particles[i].Y < -0.05) { _particles[i].Y = 1.05; _particles[i].X = _rand.NextDouble(); }
                if (_particles[i].X < -0.05) _particles[i].X = 1.05;
                if (_particles[i].X > 1.05) _particles[i].X = -0.05;
            }

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Deep Piano Black with Charcoal Slate vignette (Pre-created & frozen)
            dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, w, h));

            if (_lightweightMode)
            {
                // Lightweight static ribbons: Rendered once with zero per-frame animation loops
                DrawRibbon(dc, _glowPen1, w, h * 0.58, h * 0.14, 0.8, 1.2, 0.0025, steps: 16);
                DrawRibbon(dc, _pen1, w, h * 0.58, h * 0.14, 0.8, 1.2, 0.0025, steps: 16);
                DrawRibbon(dc, _pen2, w, h * 0.62, h * 0.18, 2.2, 0.9, 0.0018, steps: 16);
                return;
            }

            // Floating Stardust Particles (Zero-allocation frozen brush palette)
            for (int i = 0; i < _particles.Length; i++)
            {
                double px = _particles[i].X * w;
                double py = _particles[i].Y * h;
                double tw = (Math.Sin(_particles[i].TwinklePhase) + 1.0) * 0.5;
                double alpha = Math.Clamp(_particles[i].BaseAlpha * (0.6 + tw * 0.4), 0.0, 1.0);

                int brushIdx = Math.Clamp((int)(alpha * 31), 0, 31);
                dc.DrawEllipse(_particleBrushes[brushIdx], null, new Point(px, py), _particles[i].Radius, _particles[i].Radius);
            }

            // Wave Ribbon 1 (Primary Ribbon Wave + Ambient Glow Aura)
            DrawRibbon(dc, _glowPen1, w, h * 0.58, h * 0.16, _phase1, 1.2, 0.0025, steps: 18);
            DrawRibbon(dc, _pen1, w, h * 0.58, h * 0.16, _phase1, 1.2, 0.0025, steps: 18);

            // Wave Ribbon 2 (Harmonic Under-wave + Aura)
            DrawRibbon(dc, _glowPen2, w, h * 0.62, h * 0.20, _phase2, 0.9, 0.0018, steps: 18);
            DrawRibbon(dc, _pen2, w, h * 0.62, h * 0.20, _phase2, 0.9, 0.0018, steps: 18);

            // Wave Ribbon 3 (Shimmer Crest)
            DrawRibbon(dc, _pen3, w, h * 0.55, h * 0.12, _phase3, 1.5, 0.0032, steps: 18);
        }

        private void DrawRibbon(DrawingContext dc, Pen pen, double width, double baseY, double amplitude, double phase, double freqFactor, double k, int steps = 18)
        {
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                double stepX = width / steps;

                double startY = baseY + Math.Sin(phase) * amplitude + Math.Cos(phase * 0.7) * (amplitude * 0.35);
                ctx.BeginFigure(new Point(0, startY), false, false);

                Point p0 = new(0, startY);
                for (int i = 1; i <= steps; i++)
                {
                    double x = i * stepX;
                    double angle = x * k * freqFactor + phase;
                    double y = baseY + Math.Sin(angle) * amplitude + Math.Cos(angle * 0.65) * (amplitude * 0.4);
                    Point p1 = new(x, y);

                    // Smooth Bezier segment
                    Point cp1 = new(p0.X + stepX * 0.5, p0.Y);
                    Point cp2 = new(p1.X - stepX * 0.5, p1.Y);
                    ctx.BezierTo(cp1, cp2, p1, true, true);
                    p0 = p1;
                }
            }
            geom.Freeze();
            dc.DrawGeometry(null, pen, geom);
        }
    }
}
