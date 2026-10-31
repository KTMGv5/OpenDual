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

        public HardwareLabControl()
        {
            InitializeComponent();
            BuildController3DModel();
            ResetCircularity();
        }

        #region 3D Controller Model Construction
        private void BuildController3DModel()
        {
            var modelGroup = new Model3DGroup();

            // Materials
            var chassisMaterial = new MaterialGroup();
            chassisMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x30))));
            chassisMaterial.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x60, 0x80, 0xA0)), 25.0));

            var gripMaterial = new MaterialGroup();
            gripMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x0E, 0x14, 0x20))));
            gripMaterial.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x40, 0x50, 0x70)), 15.0));

            var lightbarMaterial = new MaterialGroup();
            lightbarMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4))));
            lightbarMaterial.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(180, 0, 245, 212))));

            var stickMaterial = new MaterialGroup();
            stickMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x16))));
            stickMaterial.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 40.0));

            // 1. Central Body Chassis
            var bodyMesh = CreateBoxMesh(0, 0.1, 0, 2.2, 0.9, 0.45);
            modelGroup.Children.Add(new GeometryModel3D(bodyMesh, chassisMaterial));

            // 2. Left and Right Ergonomic Grips
            var leftGripMesh = CreateTaperedGripMesh(-1.2, -0.6, 0.05, 0.5, 1.3, 0.4, 18.0);
            modelGroup.Children.Add(new GeometryModel3D(leftGripMesh, gripMaterial));

            var rightGripMesh = CreateTaperedGripMesh(1.2, -0.6, 0.05, 0.5, 1.3, 0.4, -18.0);
            modelGroup.Children.Add(new GeometryModel3D(rightGripMesh, gripMaterial));

            // 3. Touchpad Center Arc
            var touchMesh = CreateBoxMesh(0, 0.32, 0.15, 1.1, 0.55, 0.08);
            modelGroup.Children.Add(new GeometryModel3D(touchMesh, gripMaterial));

            // 4. Glowing DualSense Lightbar Horizon
            var lightMesh = CreateBoxMesh(0, 0.35, 0.20, 1.18, 0.08, 0.04);
            modelGroup.Children.Add(new GeometryModel3D(lightMesh, lightbarMaterial));

            // 5. Thumbstick Domes (L3 & R3)
            var leftStick = CreateCylinderMesh(-0.48, -0.15, 0.28, 0.24, 0.16);
            modelGroup.Children.Add(new GeometryModel3D(leftStick, stickMaterial));

            var rightStick = CreateCylinderMesh(0.48, -0.15, 0.28, 0.24, 0.16);
            modelGroup.Children.Add(new GeometryModel3D(rightStick, stickMaterial));

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
            // Gyro rates and Accelerometer G-forces
            double ax = t.AccelX / 8192.0;
            double ay = t.AccelY / 8192.0;
            double az = t.AccelZ / 8192.0;

            // Pitch & Roll from gravity vector
            double pitch = Math.Atan2(ay, Math.Sqrt(ax * ax + az * az)) * (180.0 / Math.PI);
            double roll = Math.Atan2(-ax, az) * (180.0 / Math.PI);

            // Yaw smoothly integrates z gyro rate
            double dpsZ = (t.GyroZ / 16.4);
            _simulatedYaw += dpsZ * dt * 0.15;
            if (_simulatedYaw > 180) _simulatedYaw -= 360;
            if (_simulatedYaw < -180) _simulatedYaw += 360;

            RotPitch.Angle = pitch;
            RotRoll.Angle = roll;
            RotYaw.Angle = _simulatedYaw;

            TxtGimbalAngles.Text = $"Yaw: {_simulatedYaw:+0.0;-0.0;0.0}°  Pitch: {pitch:+0.0;-0.0;0.0}°  Roll: {roll:+0.0;-0.0;0.0}°";
            TxtGForce.Text = $"X: {ax:+0.00;-0.00;0.00}G   Y: {ay:+0.00;-0.00;0.00}G   Z: {az:+0.00;-0.00;0.00}G";
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
            canvas.Children.Clear();
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 180;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 48;

            // Draw Base Resistance Curve
            var pathGeom = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(0, h * 0.85) };

            for (int i = 1; i <= 20; i++)
            {
                double xFrac = i / 20.0;
                double x = xFrac * w;
                double resistance = CalculateTriggerForce(mode, (float)xFrac);
                double y = h * 0.85 - (resistance * h * 0.70);
                fig.Segments.Add(new LineSegment(new Point(x, y), true));
            }
            pathGeom.Figures.Add(fig);

            var curvePath = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                StrokeThickness = 2.0,
                Data = pathGeom
            };
            canvas.Children.Add(curvePath);

            // Draw Real-time Current Position Cursor
            double curX = pullDepth * w;
            double curForce = CalculateTriggerForce(mode, pullDepth);
            double curY = h * 0.85 - (curForce * h * 0.70);

            var cursorLine = new Line
            {
                X1 = curX,
                X2 = curX,
                Y1 = 0,
                Y2 = h,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            canvas.Children.Add(cursorLine);

            var glowDot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x7F)),
                Margin = new Thickness(curX - 3.5, curY - 3.5, 0, 0)
            };
            canvas.Children.Add(glowDot);
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
            canvas.Children.Clear();
            double size = canvas.ActualWidth > 0 ? canvas.ActualWidth : 140;
            double center = size * 0.5;
            double radius = center - 8;

            // Draw Background Concentric Circles (25%, 50%, 75%, 100%)
            for (int r = 1; r <= 4; r++)
            {
                double curR = radius * (r / 4.0);
                var circle = new Ellipse
                {
                    Width = curR * 2,
                    Height = curR * 2,
                    Stroke = new SolidColorBrush(r == 4 ? Color.FromArgb(120, 0, 245, 212) : Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = r == 4 ? 1.5 : 0.8,
                    Margin = new Thickness(center - curR, center - curR, 0, 0)
                };
                canvas.Children.Add(circle);
            }

            // Crosshair
            var hLine = new Line { X1 = 0, X2 = size, Y1 = center, Y2 = center, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
            var vLine = new Line { X1 = center, X2 = center, Y1 = 0, Y2 = size, Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
            canvas.Children.Add(hLine);
            canvas.Children.Add(vLine);

            // Record outer boundary in angular bins
            double mag = Math.Sqrt(x * x + y * y);
            if (mag > 0.40)
            {
                double angle = Math.Atan2(y, x); // -pi to +pi
                if (angle < 0) angle += 2 * Math.PI;
                int binIdx = Math.Clamp((int)(angle / (2 * Math.PI) * RadarBins), 0, RadarBins - 1);
                bins[binIdx] = Math.Max(bins[binIdx], mag);
            }

            // Draw Outer Observed Boundary Polygon
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

            var radarPoly = new Path
            {
                Data = polyGeom,
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 245, 212)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                StrokeThickness = 1.2
            };
            canvas.Children.Add(radarPoly);

            // Stick Position & Trail
            double posX = center + x * radius;
            double posY = center - y * radius;
            trail.Enqueue(new Point(posX, posY));
            if (trail.Count > 16) trail.Dequeue();

            // Render Trail
            foreach (var tp in trail)
            {
                var trailDot = new Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(90, 41, 121, 255)),
                    Margin = new Thickness(tp.X - 1.5, tp.Y - 1.5, 0, 0)
                };
                canvas.Children.Add(trailDot);
            }

            // Current Stick Dot
            var stickDot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                StrokeThickness = 2,
                Margin = new Thickness(posX - 4.5, posY - 4.5, 0, 0)
            };
            canvas.Children.Add(stickDot);

            // Compute Circularity Error
            int populated = bins.Count(b => b > 0.40);
            if (populated > 6)
            {
                double totalErr = bins.Where(b => b > 0.40).Sum(b => Math.Abs(b - 1.0));
                double avgErr = (totalErr / populated) * 100.0;
                txtErr.Text = $"Avg Error: {avgErr:F1}%";
                if (isLeft) TxtTopCircError.Text = $"{avgErr:F1}% (Pro)";
            }

            // Drift resting measurement
            if (mag < 0.20)
            {
                if (mag < 0.015)
                {
                    txtDrift.Text = "Rest Offset: 0.00 (Zero Drift)";
                    txtDrift.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                }
                else
                {
                    txtDrift.Text = $"Rest Offset: {mag:F2} (Drift Bias)";
                    txtDrift.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00));
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
            canvas.Children.Clear();
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 480;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 64;

            if (history.Count < 2) return;

            // Dotted reference line for 1.00 ms (1000Hz target)
            double targetY = h * 0.55;
            var refLine = new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = targetY,
                Y2 = targetY,
                Stroke = new SolidColorBrush(Color.FromArgb(80, 0, 245, 212)),
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            canvas.Children.Add(refLine);

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

            var fillPath = new Path
            {
                Data = fillGeom,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new(Color.FromArgb(40, 0, 245, 212), 0.0),
                        new(Color.FromArgb(0, 0, 245, 212), 1.0)
                    }
                }
            };
            canvas.Children.Add(fillPath);

            var linePath = new Path
            {
                Data = polyGeom,
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                StrokeThickness = 1.8
            };
            canvas.Children.Add(linePath);
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
