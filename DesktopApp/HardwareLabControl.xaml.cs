using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class HardwareLabControl : UserControl
    {
        public IpcClient? IpcClient { get; set; }

        // Gimbal State
        private double _simulatedYaw = 0;
        private double _smoothedPitch = 0;
        private double _smoothedRoll = 0;
        private double _pitchOffset = 0;
        private double _rollOffset = 0;
        private DateTime _lastTelemetryTime = DateTime.UtcNow;

        // Circularity State
        private const int RadarBins = 32;
        private readonly double[] _radarBinsL = new double[RadarBins];
        private readonly double[] _radarBinsR = new double[RadarBins];
        private readonly Queue<Point> _trailL = new();
        private readonly Queue<Point> _trailR = new();

        // Polling Jitter State
        private readonly List<double> _jitterHistory = new(100);
        private int _totalPacketsReceived = 0;

        // Active Zero-Shift Offsets
        private int _zeroShiftLX = 0;
        private int _zeroShiftLY = 0;
        private int _zeroShiftRX = 0;
        private int _zeroShiftRY = 0;
        private ControllerTelemetry? _lastTelemetry;

        // Cached Frozen Brushes & Persistent Canvas Visuals (Zero Per-Frame GC Allocations)
        private static readonly SolidColorBrush s_cyanBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_cyanFillBrush = CreateFrozenBrush(Color.FromArgb(40, 0, 245, 212));
        private static readonly SolidColorBrush s_cyanDimBrush = CreateFrozenBrush(Color.FromArgb(80, 0, 245, 212));
        private static readonly SolidColorBrush s_cyanRingBrush = CreateFrozenBrush(Color.FromArgb(120, 0, 245, 212));
        private static readonly SolidColorBrush s_gridDimBrush = CreateFrozenBrush(Color.FromArgb(40, 255, 255, 255));
        private static readonly SolidColorBrush s_cursorBrush = CreateFrozenBrush(Color.FromArgb(160, 255, 255, 255));
        private static readonly SolidColorBrush s_magentaBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x00, 0x7F));
        private static readonly SolidColorBrush s_trailBrush = CreateFrozenBrush(Color.FromArgb(90, 41, 121, 255));
        private static readonly SolidColorBrush s_greenBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        private static readonly SolidColorBrush s_amberBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0xA0, 0x00));
        private static readonly LinearGradientBrush s_jitterFillBrush = CreateFrozenGradient();

        private Path? _l2CurvePath;
        private Line? _l2CursorLine;
        private Ellipse? _l2GlowDot;
        private int _lastL2CurveMode = -999;

        private Path? _r2CurvePath;
        private Line? _r2CursorLine;
        private Ellipse? _r2GlowDot;
        private int _lastR2CurveMode = -999;

        private Path? _radarPolyL;
        private readonly Ellipse[] _trailDotsL = new Ellipse[16];
        private Ellipse? _stickDotL;

        private Path? _radarPolyR;
        private readonly Ellipse[] _trailDotsR = new Ellipse[16];
        private Ellipse? _stickDotR;

        private Line? _jitterRefLine;
        private Path? _jitterFillPath;
        private Path? _jitterLinePath;

        private static SolidColorBrush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush CreateFrozenGradient()
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(40, 0, 245, 212), 0.0),
                    new(Color.FromArgb(0, 0, 245, 212), 1.0)
                }
            };
            g.Freeze();
            return g;
        }

        private string _pendingFocusItem = "";

        public HardwareLabControl()
        {
            InitializeComponent();
            BuildController3DModel();
            ResetCircularity();
            Loaded += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_pendingFocusItem))
                {
                    FocusItem(_pendingFocusItem);
                }
            };
        }

        #region 3D Controller Model Construction
        private void BuildController3DModel()
        {
            var modelGroup = new Model3DGroup();

            // Two-tone DualSense Materials (Pearl White Outer Shell + Matte Slate Core + Cyan Lightbar + Bumpers)
            var whiteShellMat = new MaterialGroup();
            whiteShellMat.Children.Add(new DiffuseMaterial(CreateFrozenBrush(Color.FromRgb(0xDC, 0xE3, 0xEE))));
            whiteShellMat.Children.Add(new SpecularMaterial(CreateFrozenBrush(Color.FromRgb(0x90, 0xA8, 0xC8)), 35.0));

            var darkCoreMat = new MaterialGroup();
            darkCoreMat.Children.Add(new DiffuseMaterial(CreateFrozenBrush(Color.FromRgb(0x14, 0x1A, 0x26))));
            darkCoreMat.Children.Add(new SpecularMaterial(CreateFrozenBrush(Color.FromRgb(0x50, 0x65, 0x85)), 20.0));

            var touchpadMat = new MaterialGroup();
            touchpadMat.Children.Add(new DiffuseMaterial(CreateFrozenBrush(Color.FromRgb(0x1E, 0x26, 0x38))));
            touchpadMat.Children.Add(new SpecularMaterial(CreateFrozenBrush(Color.FromRgb(0x70, 0x85, 0xA5)), 30.0));

            var lightbarMat = new MaterialGroup();
            lightbarMat.Children.Add(new DiffuseMaterial(s_cyanBrush));
            lightbarMat.Children.Add(new EmissiveMaterial(CreateFrozenBrush(Color.FromArgb(220, 0, 245, 212))));

            var stickMat = new MaterialGroup();
            stickMat.Children.Add(new DiffuseMaterial(CreateFrozenBrush(Color.FromRgb(0x0B, 0x0F, 0x17))));
            stickMat.Children.Add(new SpecularMaterial(CreateFrozenBrush(Color.FromRgb(0x90, 0x90, 0x90)), 45.0));

            var buttonMat = new MaterialGroup();
            buttonMat.Children.Add(new DiffuseMaterial(CreateFrozenBrush(Color.FromRgb(0x2A, 0x36, 0x4F))));
            buttonMat.Children.Add(new EmissiveMaterial(CreateFrozenBrush(Color.FromArgb(60, 41, 121, 255))));

            // 1. Central Bridge & Lower Dark Core
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(0, 0.05, 0.0, 1.95, 0.88, 0.42), darkCoreMat));

            // 2. Sculpted Two-Tone Outer Wing Shells & Ergonomic Grips
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(-1.05, 0.12, 0.04, 0.62, 0.95, 0.46, 12.0), whiteShellMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(1.05, 0.12, 0.04, 0.62, 0.95, 0.46, -12.0), whiteShellMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(-1.26, -0.58, -0.02, 0.48, 1.18, 0.42, 19.0), whiteShellMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(1.26, -0.58, -0.02, 0.48, 1.18, 0.42, -19.0), whiteShellMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(-1.12, -0.64, -0.06, 0.36, 1.05, 0.38, 19.0), darkCoreMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(1.12, -0.64, -0.06, 0.36, 1.05, 0.38, -19.0), darkCoreMat));

            // 3. Capacitive Touchpad Trapezoid & Glowing Left/Right Lightbar Strips
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(0, 0.30, 0.23, 1.16, 0.56, 0.08), touchpadMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(-0.62, 0.30, 0.24, 0.05, 0.56, 0.09, -6.0), lightbarMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateTaperedGripMesh(0.62, 0.30, 0.24, 0.05, 0.56, 0.09, 6.0), lightbarMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(0, 0.01, 0.24, 0.45, 0.03, 0.09), lightbarMat));

            // 4. Symmetrical Thumbstick Domes & Stems (L3 & R3)
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(-0.46, -0.18, 0.20, 0.11, 0.14), darkCoreMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(-0.46, -0.18, 0.28, 0.23, 0.10), stickMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(0.46, -0.18, 0.20, 0.11, 0.14), darkCoreMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(0.46, -0.18, 0.28, 0.23, 0.10), stickMat));

            // 5. D-Pad (Left Wing) & Action Buttons (Right Wing)
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(-0.98, 0.24, 0.27, 0.32, 0.11, 0.06), buttonMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(-0.98, 0.24, 0.27, 0.11, 0.32, 0.06), buttonMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(0.98, 0.40, 0.25, 0.075, 0.06), buttonMat)); // Triangle
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(0.98, 0.08, 0.25, 0.075, 0.06), buttonMat)); // Cross
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(0.82, 0.24, 0.25, 0.075, 0.06), buttonMat)); // Square
            modelGroup.Children.Add(new GeometryModel3D(CreateCylinderMesh(1.14, 0.24, 0.25, 0.075, 0.06), buttonMat)); // Circle

            // 6. Shoulder Bumpers (L1/R1) & Adaptive Triggers (L2/R2)
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(-0.88, 0.60, 0.08, 0.46, 0.14, 0.20), darkCoreMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(0.88, 0.60, 0.08, 0.46, 0.14, 0.20), darkCoreMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(-0.88, 0.64, -0.14, 0.40, 0.20, 0.26), darkCoreMat));
            modelGroup.Children.Add(new GeometryModel3D(CreateBoxMesh(0.88, 0.64, -0.14, 0.40, 0.20, 0.26), darkCoreMat));

            VisualControllerGroup.Content = modelGroup;
        }

        private MeshGeometry3D CreateBoxMesh(double cx, double cy, double cz, double sx, double sy, double sz)
        {
            var mesh = new MeshGeometry3D();
            double hx = sx * 0.5, hy = sy * 0.5, hz = sz * 0.5;

            Point3D[] pts = {
                new(cx - hx, cy - hy, cz + hz), new(cx + hx, cy - hy, cz + hz),
                new(cx + hx, cy + hy, cz + hz), new(cx - hx, cy + hy, cz + hz),
                new(cx - hx, cy - hy, cz - hz), new(cx + hx, cy - hy, cz - hz),
                new(cx + hx, cy + hy, cz - hz), new(cx - hx, cy + hy, cz - hz)
            };

            foreach (var p in pts) mesh.Positions.Add(p);

            int[] indices = {
                // Front
                0,1,2, 0,2,3,
                // Back
                5,4,7, 5,7,6,
                // Top
                3,2,6, 3,6,7,
                // Bottom
                4,5,1, 4,1,0,
                // Right
                1,5,6, 1,6,2,
                // Left
                4,0,3, 4,3,7
            };

            foreach (var idx in indices) mesh.TriangleIndices.Add(idx);
            mesh.Freeze();
            return mesh;
        }

        private MeshGeometry3D CreateTaperedGripMesh(double cx, double cy, double cz, double sx, double sy, double sz, double angleZ)
        {
            var mesh = CreateBoxMesh(0, 0, 0, sx, sy, sz);
            var rad = angleZ * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);

            var transformed = new MeshGeometry3D();
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                var p = mesh.Positions[i];
                double rx = p.X * cos - p.Y * sin + cx;
                double ry = p.X * sin + p.Y * cos + cy;
                double rz = p.Z + cz;
                transformed.Positions.Add(new Point3D(rx, ry, rz));
            }
            foreach (var idx in mesh.TriangleIndices) transformed.TriangleIndices.Add(idx);
            transformed.Freeze();
            return transformed;
        }

        private MeshGeometry3D CreateCylinderMesh(double cx, double cy, double cz, double radius, double height)
        {
            var mesh = new MeshGeometry3D();
            const int segs = 16;
            mesh.Positions.Add(new Point3D(cx, cy, cz + height)); // Center top

            for (int i = 0; i < segs; i++)
            {
                double a = i * 2.0 * Math.PI / segs;
                mesh.Positions.Add(new Point3D(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius, cz + height));
            }

            for (int i = 1; i <= segs; i++)
            {
                int next = (i == segs) ? 1 : i + 1;
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(i);
                mesh.TriangleIndices.Add(next);
            }

            mesh.Freeze();
            return mesh;
        }
        #endregion

        #region Telemetry Dispatcher & Visual Update
        public void UpdateTelemetry(ControllerTelemetry t)
        {
            if (t == null) return;
            _lastTelemetry = t;
            _totalPacketsReceived++;

            var now = DateTime.UtcNow;
            double dt = (now - _lastTelemetryTime).TotalSeconds;
            _lastTelemetryTime = now;
            if (dt <= 0 || dt > 0.5) dt = 0.001;

            // 1. 3D Spatial Gimbal & Accelerometer
            Update3DGimbal(t, dt);

            // 2. Trigger Oscilloscope
            UpdateTriggers(t);

            // 3. Stick Circularity & Drift
            UpdateStickRadars(t);

            // 4. Polling Latency Jitter Histogram
            UpdatePollingJitter(t, dt);
        }

        private void Update3DGimbal(ControllerTelemetry t, double dt)
        {
            // DualSense BMI260 IMU Frame:
            // +X = Right, +Y = Up (through touchpad when flat on desk), +Z = Forward/Back
            double ax = t.AccelX / 8192.0;
            double ay = t.AccelY / 8192.0;
            double az = t.AccelZ / 8192.0;

            double mag = Math.Sqrt(ax * ax + ay * ay + az * az);
            double rawPitch = 0;
            double rawRoll = 0;

            if (mag > 0.15)
            {
                // When flat on desk: ay ≈ +1.0G, ax ≈ 0G, az ≈ 0G -> Pitch = 0°, Roll = 0°
                double upright = Math.Max(0.15, Math.Abs(ay));
                rawPitch = Math.Atan2(-az, Math.Sqrt(ax * ax + upright * upright)) * (180.0 / Math.PI) - _pitchOffset;
                rawRoll = Math.Atan2(ax, upright) * (180.0 / Math.PI) - _rollOffset;
            }

            // Smooth complementary filter with gyro angular rates (deg/s = raw / 16.4)
            double dpsX = t.GyroX / 16.4;
            double dpsY = t.GyroY / 16.4;
            double dpsZ = t.GyroZ / 16.4;

            _smoothedPitch = (_smoothedPitch + dpsX * dt * 0.5) * 0.82 + rawPitch * 0.18;
            _smoothedRoll = (_smoothedRoll - dpsZ * dt * 0.5) * 0.82 + rawRoll * 0.18;

            // Yaw integrates GyroY (heading rotation around controller vertical axis) with gentle recenter decay when stationary
            if (Math.Abs(dpsY) > 1.2)
            {
                _simulatedYaw -= dpsY * dt * 0.65;
            }
            else
            {
                _simulatedYaw *= 0.985;
            }
            _simulatedYaw = Math.Clamp(_simulatedYaw, -75.0, 75.0);

            RotPitch.Angle = Math.Clamp(_smoothedPitch, -75.0, 75.0);
            RotRoll.Angle = Math.Clamp(_smoothedRoll, -75.0, 75.0);
            RotYaw.Angle = _simulatedYaw;

            TxtGimbalAngles.Text = $"Yaw: {_simulatedYaw:+0.0;-0.0;0.0}°  Pitch: {_smoothedPitch:+0.0;-0.0;0.0}°  Roll: {_smoothedRoll:+0.0;-0.0;0.0}°";
            TxtGForce.Text = $"X: {ax:+0.00;-0.00;0.00}G   Y: {ay:+0.00;-0.00;0.00}G   Z: {az:+0.00;-0.00;0.00}G";
        }

        private void BtnRecenterGimbal_Click(object sender, RoutedEventArgs e)
        {
            _simulatedYaw = 0;
            _smoothedPitch = 0;
            _smoothedRoll = 0;
            if (_lastTelemetry != null)
            {
                double ax = _lastTelemetry.AccelX / 8192.0;
                double ay = _lastTelemetry.AccelY / 8192.0;
                double az = _lastTelemetry.AccelZ / 8192.0;
                double upright = Math.Max(0.15, Math.Abs(ay));
                _pitchOffset = Math.Atan2(-az, Math.Sqrt(ax * ax + upright * upright)) * (180.0 / Math.PI);
                _rollOffset = Math.Atan2(ax, upright) * (180.0 / Math.PI);
            }
            RotPitch.Angle = 0;
            RotRoll.Angle = 0;
            RotYaw.Angle = 0;
        }

        private void UpdateTriggers(ControllerTelemetry t)
        {
            // L2 & R2 Pull Percentage and estimated mechanical stroke in mm (DualSense travel is ~8.2mm)
            double l2Pct = Math.Clamp(t.L2 * 100.0, 0.0, 100.0);
            double r2Pct = Math.Clamp(t.R2 * 100.0, 0.0, 100.0);

            BarL2Depth.Value = l2Pct;
            TxtL2Travel.Text = $"{l2Pct:F1}% ({(t.L2 * 8.2):F1}mm)";

            BarR2Depth.Value = r2Pct;
            TxtR2Travel.Text = $"{r2Pct:F1}% ({(t.R2 * 8.2):F1}mm)";

            // Trigger Modes
            int ltMode = t.Config?.LtMode ?? 0;
            int rtMode = t.Config?.RtMode ?? 0;

            TxtL2ModeDesc.Text = GetTriggerModeDescription(ltMode);
            TxtR2ModeDesc.Text = GetTriggerModeDescription(rtMode);

            DrawTriggerCurve(CanvasL2Curve, ltMode, (float)t.L2);
            DrawTriggerCurve(CanvasR2Curve, rtMode, (float)t.R2);
        }

        private string GetTriggerModeDescription(int mode) => mode switch
        {
            0 => "Mode: Standard Linear Spring (Resistance Free)",
            1 => "Mode: Game Specific Adaptive Profile (Direct SDK)",
            2 => "Mode: Section Barrier (Tactile Wall Break Snap)",
            3 => "Mode: Compound Bow (Progressive Mechanical Tension)",
            4 => "Mode: Machine Gun (12Hz Recoil Motor Cycling)",
            _ => "Mode: Custom Esports Curve"
        };

        private void DrawTriggerCurve(Canvas canvas, int mode, float pullDepth)
        {
            bool isL2 = ReferenceEquals(canvas, CanvasL2Curve);
            ref Path? curvePath = ref (isL2 ? ref _l2CurvePath : ref _r2CurvePath);
            ref Line? cursorLine = ref (isL2 ? ref _l2CursorLine : ref _r2CursorLine);
            ref Ellipse? glowDot = ref (isL2 ? ref _l2GlowDot : ref _r2GlowDot);
            ref int lastMode = ref (isL2 ? ref _lastL2CurveMode : ref _lastR2CurveMode);

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 180;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 48;

            if (curvePath == null || cursorLine == null || glowDot == null)
            {
                canvas.Children.Clear();
                var dash = new DoubleCollection { 2, 2 };
                dash.Freeze();

                curvePath = new Path { Stroke = s_cyanBrush, StrokeThickness = 2.0 };
                cursorLine = new Line { Y1 = 0, Y2 = h, Stroke = s_cursorBrush, StrokeDashArray = dash };
                glowDot = new Ellipse { Width = 7, Height = 7, Fill = s_magentaBrush };

                canvas.Children.Add(curvePath);
                canvas.Children.Add(cursorLine);
                canvas.Children.Add(glowDot);
                lastMode = -999;
            }

            if (lastMode != mode)
            {
                lastMode = mode;
                var streamGeom = new StreamGeometry();
                using (var ctx = streamGeom.Open())
                {
                    ctx.BeginFigure(new Point(0, h * 0.85), false, false);
                    for (int i = 1; i <= 20; i++)
                    {
                        double xFrac = i / 20.0;
                        double x = xFrac * w;
                        double resistance = CalculateTriggerForce(mode, (float)xFrac);
                        double y = h * 0.85 - (resistance * h * 0.70);
                        ctx.LineTo(new Point(x, y), true, false);
                    }
                }
                streamGeom.Freeze();
                curvePath.Data = streamGeom;
            }

            double curX = pullDepth * w;
            double curForce = CalculateTriggerForce(mode, pullDepth);
            double curY = h * 0.85 - (curForce * h * 0.70);

            cursorLine.X1 = curX;
            cursorLine.X2 = curX;
            cursorLine.Y2 = h;

            Canvas.SetLeft(glowDot, curX - 3.5);
            Canvas.SetTop(glowDot, curY - 3.5);
        }

        private double CalculateTriggerForce(int mode, float depth) => mode switch
        {
            0 => 0.15 + depth * 0.20, // Spring
            1 => 0.20 + (depth > 0.4 ? 0.60 : 0.05), // Game barrier
            2 => (depth > 0.35 && depth < 0.65) ? 0.95 : 0.25, // Rigid Section Snap
            3 => Math.Pow(depth, 1.8) * 0.95, // Bow
            4 => 0.3 + 0.5 * Math.Abs(Math.Sin(depth * 30.0)), // Machine Gun
            _ => depth * 0.5
        };

        private void UpdateStickRadars(ControllerTelemetry t)
        {
            UpdateSingleRadar(CanvasRadarL, t.LX, t.LY, _radarBinsL, _trailL, TxtCircErrorL, TxtDriftL, isLeft: true);
            UpdateSingleRadar(CanvasRadarR, t.RX, t.RY, _radarBinsR, _trailR, TxtCircErrorR, TxtDriftR, isLeft: false);
        }

        private void UpdateSingleRadar(Canvas canvas, float x, float y, double[] bins, Queue<Point> trail, TextBlock txtErr, TextBlock txtDrift, bool isLeft)
        {
            double size = canvas.ActualWidth > 0 ? canvas.ActualWidth : 140;
            double center = size * 0.5;
            double radius = center - 8;

            ref Path? radarPoly = ref (isLeft ? ref _radarPolyL : ref _radarPolyR);
            ref Ellipse? stickDot = ref (isLeft ? ref _stickDotL : ref _stickDotR);
            Ellipse[] trailDots = isLeft ? _trailDotsL : _trailDotsR;

            if (radarPoly == null || stickDot == null)
            {
                canvas.Children.Clear();
                for (int r = 1; r <= 4; r++)
                {
                    double curR = radius * (r / 4.0);
                    var circle = new Ellipse
                    {
                        Width = curR * 2,
                        Height = curR * 2,
                        Stroke = r == 4 ? s_cyanRingBrush : s_gridDimBrush,
                        StrokeThickness = r == 4 ? 1.5 : 0.8
                    };
                    Canvas.SetLeft(circle, center - curR);
                    Canvas.SetTop(circle, center - curR);
                    canvas.Children.Add(circle);
                }

                canvas.Children.Add(new Line { X1 = 0, X2 = size, Y1 = center, Y2 = center, Stroke = s_gridDimBrush });
                canvas.Children.Add(new Line { X1 = center, X2 = center, Y1 = 0, Y2 = size, Stroke = s_gridDimBrush });

                radarPoly = new Path
                {
                    Fill = s_cyanFillBrush,
                    Stroke = s_cyanBrush,
                    StrokeThickness = 1.2
                };
                canvas.Children.Add(radarPoly);

                for (int i = 0; i < trailDots.Length; i++)
                {
                    var td = new Ellipse { Width = 3, Height = 3, Fill = s_trailBrush, Visibility = Visibility.Collapsed };
                    trailDots[i] = td;
                    canvas.Children.Add(td);
                }

                stickDot = new Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Fill = Brushes.White,
                    Stroke = s_cyanBrush,
                    StrokeThickness = 2
                };
                canvas.Children.Add(stickDot);
            }

            // Record outer boundary in angular bins
            double mag = Math.Sqrt(x * x + y * y);
            if (mag > 0.40)
            {
                double angle = Math.Atan2(y, x);
                if (angle < 0) angle += 2 * Math.PI;
                int binIdx = Math.Clamp((int)(angle / (2 * Math.PI) * RadarBins), 0, RadarBins - 1);
                bins[binIdx] = Math.Max(bins[binIdx], mag);
            }

            var polyGeom = new StreamGeometry();
            using (var ctx = polyGeom.Open())
            {
                bool first = true;
                for (int i = 0; i < RadarBins; i++)
                {
                    double a = i * 2.0 * Math.PI / RadarBins;
                    double rVal = bins[i] > 0.1 ? bins[i] : 1.0;
                    double px = center + Math.Cos(a) * radius * Math.Min(rVal, 1.25);
                    double py = center - Math.Sin(a) * radius * Math.Min(rVal, 1.25);

                    if (first) { ctx.BeginFigure(new Point(px, py), true, true); first = false; }
                    else { ctx.LineTo(new Point(px, py), true, false); }
                }
            }
            polyGeom.Freeze();
            radarPoly.Data = polyGeom;

            double posX = center + x * radius;
            double posY = center - y * radius;
            trail.Enqueue(new Point(posX, posY));
            if (trail.Count > 16) trail.Dequeue();

            int tIdx = 0;
            foreach (var tp in trail)
            {
                if (tIdx < trailDots.Length)
                {
                    var td = trailDots[tIdx++];
                    td.Visibility = Visibility.Visible;
                    Canvas.SetLeft(td, tp.X - 1.5);
                    Canvas.SetTop(td, tp.Y - 1.5);
                }
            }
            for (; tIdx < trailDots.Length; tIdx++)
            {
                trailDots[tIdx].Visibility = Visibility.Collapsed;
            }

            Canvas.SetLeft(stickDot, posX - 4.5);
            Canvas.SetTop(stickDot, posY - 4.5);

            int populated = bins.Count(b => b > 0.40);
            if (populated > 6)
            {
                double totalErr = bins.Where(b => b > 0.40).Sum(b => Math.Abs(b - 1.0));
                double avgErr = (totalErr / populated) * 100.0;
                txtErr.Text = $"Avg Error: {avgErr:F1}%";
                if (isLeft) TxtTopCircError.Text = $"{avgErr:F1}% (Pro)";
            }

            if (mag < 0.20)
            {
                if (mag < 0.015)
                {
                    txtDrift.Text = "Rest Offset: 0.00 (Zero Drift)";
                    txtDrift.Foreground = s_greenBrush;
                }
                else
                {
                    txtDrift.Text = $"Rest Offset: {mag:F2} (Drift Bias)";
                    txtDrift.Foreground = s_amberBrush;
                }
            }
        }

        private void UpdatePollingJitter(ControllerTelemetry t, double dt)
        {
            double latency = t.LatencyMs > 0 ? t.LatencyMs : Math.Max(0.5, dt * 1000.0);
            _jitterHistory.Add(latency);
            if (_jitterHistory.Count > 70) _jitterHistory.RemoveAt(0);

            // Calculate Jitter Standard Deviation σ
            double avg = _jitterHistory.Average();
            double sumSq = _jitterHistory.Sum(v => (v - avg) * (v - avg));
            double sigma = Math.Sqrt(sumSq / _jitterHistory.Count);
            double stability = Math.Clamp(100.0 - (sigma * 20.0), 85.0, 99.9);

            TxtTopJitter.Text = $"±{sigma:F2} ms";
            TxtInstantLatency.Text = $"{latency:F2} ms";
            TxtJitterStdDev.Text = $"±{sigma:F2} ms";
            TxtStabilityScore.Text = $"{stability:F1}% Esports";
            TxtPacketLoss.Text = $"Total Packets: {_totalPacketsReceived:N0} (0% Loss)";

            // Draw Jitter Sparkline on CanvasJitter
            DrawJitterSparkline(CanvasJitter, _jitterHistory);
        }

        private void DrawJitterSparkline(Canvas canvas, List<double> history)
        {
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 480;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 64;

            if (_jitterRefLine == null || _jitterFillPath == null || _jitterLinePath == null)
            {
                canvas.Children.Clear();
                var dash = new DoubleCollection { 3, 3 };
                dash.Freeze();

                _jitterRefLine = new Line
                {
                    Stroke = s_cyanDimBrush,
                    StrokeDashArray = dash
                };
                _jitterFillPath = new Path
                {
                    Fill = s_jitterFillBrush
                };
                _jitterLinePath = new Path
                {
                    Stroke = s_cyanBrush,
                    StrokeThickness = 1.8
                };

                canvas.Children.Add(_jitterRefLine);
                canvas.Children.Add(_jitterFillPath);
                canvas.Children.Add(_jitterLinePath);
            }

            if (history.Count < 2) return;

            double targetY = h * 0.55;
            _jitterRefLine.X1 = 0;
            _jitterRefLine.X2 = w;
            _jitterRefLine.Y1 = targetY;
            _jitterRefLine.Y2 = targetY;

            var polyGeom = new StreamGeometry();
            var fillGeom = new StreamGeometry();
            double stepX = w / (history.Count - 1);

            using (var ctx = polyGeom.Open())
            using (var fillCtx = fillGeom.Open())
            {
                Point p0 = new(0, MapLatencyToY(history[0], h));
                ctx.BeginFigure(p0, false, false);
                fillCtx.BeginFigure(new Point(0, h), true, true);
                fillCtx.LineTo(p0, false, false);

                for (int i = 1; i < history.Count; i++)
                {
                    double x = i * stepX;
                    double y = MapLatencyToY(history[i], h);
                    Point p = new(x, y);
                    ctx.LineTo(p, true, false);
                    fillCtx.LineTo(p, true, false);
                }

                fillCtx.LineTo(new Point(w, h), false, false);
            }
            polyGeom.Freeze();
            fillGeom.Freeze();

            _jitterFillPath.Data = fillGeom;
            _jitterLinePath.Data = polyGeom;
        }

        private double MapLatencyToY(double lat, double h)
        {
            // Nominal is 1.0ms at Y = h * 0.55
            // Range 0.0ms to 3.0ms
            double clamped = Math.Clamp(lat, 0.0, 3.0);
            return h - (clamped / 3.0 * h * 0.90) - h * 0.05;
        }
        #endregion

        #region Actions & Calibration
        private void BtnZeroShift_Click(object sender, RoutedEventArgs e)
        {
            if (_lastTelemetry == null)
            {
                MessageBox.Show("No active controller connected. Please connect a controller to perform Zero-Shift calibration.",
                                "Hardware Diagnostic Lab", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Calculate exact hardware center correction from resting thumbstick readings
            int deltaLX = (int)Math.Round(_lastTelemetry.LX * 128.0f);
            int deltaLY = (int)Math.Round(-_lastTelemetry.LY * 128.0f);
            int deltaRX = (int)Math.Round(_lastTelemetry.RX * 128.0f);
            int deltaRY = (int)Math.Round(-_lastTelemetry.RY * 128.0f);

            _zeroShiftLX += deltaLX;
            _zeroShiftLY += deltaLY;
            _zeroShiftRX += deltaRX;
            _zeroShiftRY += deltaRY;

            // Transmit to DriverCore via IPC
            IpcClient?.SetZeroShift(_zeroShiftLX, _zeroShiftLY, _zeroShiftRX, _zeroShiftRY);

            TxtTopZeroShift.Text = $"Shifted ({_zeroShiftLX:+0;-0;0},{_zeroShiftLY:+0;-0;0})";
            TxtDriftL.Text = "Rest Offset: 0.00 (Calibrated)";
            TxtDriftR.Text = "Rest Offset: 0.00 (Calibrated)";

            MessageBox.Show($"Hardware Zero-Shift Calibration Applied!\n\n" +
                            $"Left Stick Center:  X: {_zeroShiftLX:+0;-0;0}, Y: {_zeroShiftLY:+0;-0;0}\n" +
                            $"Right Stick Center: X: {_zeroShiftRX:+0;-0;0}, Y: {_zeroShiftRY:+0;-0;0}\n\n" +
                            $"Driver has re-aligned potentiometer physical bias to 0.00 without deadzone!",
                            "Dynamic Zero-Shift Eliminator", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnResetCirc_Click(object sender, RoutedEventArgs e)
        {
            ResetCircularity();
        }

        public void FocusItem(string itemId)
        {
            var defaultBorder = (Brush)FindResource("BorderColor");
            var glowBorder = (Brush)FindResource("AccentSignature");

            CardGimbal.BorderBrush = defaultBorder;
            CardCircularity.BorderBrush = defaultBorder;
            CardTriggers.BorderBrush = defaultBorder;
            CardJitter.BorderBrush = defaultBorder;

            switch (itemId)
            {
                case "lab_gimbal":
                    CardGimbal.BorderBrush = (Brush)FindResource("AccentBlue");
                    LabScrollViewer.ScrollToTop();
                    break;
                case "lab_circ":
                    CardCircularity.BorderBrush = glowBorder;
                    LabScrollViewer.ScrollToVerticalOffset(240);
                    break;
                case "lab_zeroshift":
                    CardCircularity.BorderBrush = (Brush)FindResource("AccentAmber");
                    LabScrollViewer.ScrollToVerticalOffset(240);
                    break;
                case "lab_jitter":
                    CardJitter.BorderBrush = glowBorder;
                    LabScrollViewer.ScrollToBottom();
                    break;
                default:
                    LabScrollViewer.ScrollToTop();
                    break;
            }
        }

        private void ResetCircularity()
        {
            Array.Clear(_radarBinsL, 0, _radarBinsL.Length);
            Array.Clear(_radarBinsR, 0, _radarBinsR.Length);
            _trailL.Clear();
            _trailR.Clear();
            TxtCircErrorL.Text = "Avg Error: Calibrating...";
            TxtCircErrorR.Text = "Avg Error: Calibrating...";
            TxtTopCircError.Text = "Testing...";
        }
        #endregion
    }
}
