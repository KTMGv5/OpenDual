using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class GyroCalibratorWindow : Window
    {
        public Action<float, float, float>? OnCalibrationApplied;

        private const double CanvasWidth = 1050.0;
        private const double CanvasHeight = 540.0;
        private readonly Point _centerPoint = new Point(CanvasWidth / 2.0, CanvasHeight / 2.0);

        private int _step = 1; // 1 = Center, 2 = Right, 3 = Left, 4 = Top, 5 = Bottom, 6 = Done
        private bool _lastCross = false;

        // Integrated virtual aim coordinate
        private double _aimX = CanvasWidth / 2.0;
        private double _aimY = CanvasHeight / 2.0;

        // Target positions (in canvas coordinates)
        private readonly Point[] _targets = new Point[]
        {
            new Point(CanvasWidth / 2.0, CanvasHeight / 2.0), // 1: Center
            new Point(CanvasWidth - 140.0, CanvasHeight / 2.0), // 2: Right
            new Point(140.0, CanvasHeight / 2.0),             // 3: Left
            new Point(CanvasWidth / 2.0, 90.0),               // 4: Top
            new Point(CanvasWidth / 2.0, CanvasHeight - 90.0) // 5: Bottom
        };

        // Recorded samples for calibration
        private readonly List<short> _driftSamplesX = new();
        private readonly List<short> _driftSamplesY = new();
        private readonly List<short> _driftSamplesZ = new();

        private double _recordedYawRight = 0;
        private double _recordedYawLeft = 0;
        private double _recordedPitchUp = 0;
        private double _recordedPitchDown = 0;

        private float _calculatedSensX = 1.25f;
        private float _calculatedSensY = 1.25f;
        private float _calculatedDeadzone = 0.04f;

        public GyroCalibratorWindow()
        {
            InitializeComponent();
            KeyDown += GyroCalibratorWindow_KeyDown;
            SetTargetPosition(_targets[0]);
        }

        private void GyroCalibratorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        public void UpdateTelemetry(ControllerTelemetry state)
        {
            if (!IsVisible) return;

            Dispatcher.Invoke(() =>
            {
                // Update live angular pitch/yaw display
                if (TxtLiveAngles != null)
                {
                    TxtLiveAngles.Text = $"Yaw: {state.GyroY:+0000;-0000;0}  |  Pitch: {state.GyroX:+0000;-0000;0}";
                }

                // If not finished, update moving reticle position
                if (_step <= 5)
                {
                    // Integrate gyro angular velocity into canvas aim coordinates
                    double deltaX = state.GyroY * 0.045;
                    double deltaY = -state.GyroX * 0.045; // pitch invert for natural aim

                    _aimX = Math.Clamp(_aimX + deltaX, 30.0, CanvasWidth - 30.0);
                    _aimY = Math.Clamp(_aimY + deltaY, 30.0, CanvasHeight - 30.0);

                    Canvas.SetLeft(ReticleCanvas, _aimX - 25.0);
                    Canvas.SetTop(ReticleCanvas, _aimY - 25.0);

                    // Check proximity to active target
                    Point activeTarget = _targets[_step - 1];
                    double dist = Math.Sqrt(Math.Pow(_aimX - activeTarget.X, 2) + Math.Pow(_aimY - activeTarget.Y, 2));

                    if (dist < 45.0)
                    {
                        BullseyeCenter.Fill = (SolidColorBrush)FindResource("AccentGreen");
                        if (TxtPrompt != null)
                        {
                            TxtPrompt.Text = "ON TARGET! Press Cross (✕) now to lock calibration point.";
                            TxtPrompt.Foreground = (SolidColorBrush)FindResource("AccentGreen");
                        }
                    }
                    else
                    {
                        BullseyeCenter.Fill = (SolidColorBrush)FindResource("AccentSignature");
                        if (TxtPrompt != null)
                        {
                            TxtPrompt.Text = "Align the green reticle with the bullseye and press Cross (✕)";
                            TxtPrompt.Foreground = (SolidColorBrush)FindResource("AccentAmber");
                        }
                    }

                    // Collect drift baseline samples during Step 1
                    if (_step == 1)
                    {
                        if (_driftSamplesX.Count < 60) _driftSamplesX.Add(state.GyroX);
                        if (_driftSamplesY.Count < 60) _driftSamplesY.Add(state.GyroY);
                        if (_driftSamplesZ.Count < 60) _driftSamplesZ.Add(state.GyroZ);
                    }

                    // Highlight Cross button pill
                    if (PillCross != null)
                    {
                        PillCross.Background = state.Cross ? new SolidColorBrush(Color.FromArgb(90, 0, 230, 118)) : new SolidColorBrush(Color.FromArgb(26, 0, 230, 118));
                        if (TxtPillCross != null)
                        {
                            TxtPillCross.Text = state.Cross ? "CROSS (✕) PRESSED" : "CROSS (✕) READY";
                        }
                    }

                    // Detect Cross button press (rising edge)
                    if (state.Cross && !_lastCross)
                    {
                        RegisterTargetHit(state);
                    }
                }

                _lastCross = state.Cross;
            });
        }

        private void RegisterTargetHit(ControllerTelemetry state)
        {
            switch (_step)
            {
                case 1: // Center
                    _step = 2;
                    TxtStepIndicator.Text = "TARGET 2 OF 5: RIGHT (+30°)";
                    SetTargetPosition(_targets[1]);
                    break;

                case 2: // Right
                    _recordedYawRight = Math.Abs(_aimX - _centerPoint.X);
                    _step = 3;
                    TxtStepIndicator.Text = "TARGET 3 OF 5: LEFT (-30°)";
                    SetTargetPosition(_targets[2]);
                    break;

                case 3: // Left
                    _recordedYawLeft = Math.Abs(_aimX - _centerPoint.X);
                    _step = 4;
                    TxtStepIndicator.Text = "TARGET 4 OF 5: UP (+20°)";
                    SetTargetPosition(_targets[3]);
                    break;

                case 4: // Top
                    _recordedPitchUp = Math.Abs(_aimY - _centerPoint.Y);
                    _step = 5;
                    TxtStepIndicator.Text = "TARGET 5 OF 5: DOWN (-20°)";
                    SetTargetPosition(_targets[4]);
                    break;

                case 5: // Bottom
                    _recordedPitchDown = Math.Abs(_aimY - _centerPoint.Y);
                    _step = 6;
                    CompleteCalibration();
                    break;
            }
        }

        private void SetTargetPosition(Point p)
        {
            Canvas.SetLeft(TargetBullseye, p.X - 45.0);
            Canvas.SetTop(TargetBullseye, p.Y - 45.0);
        }

        private void CompleteCalibration()
        {
            TargetBullseye.Visibility = Visibility.Collapsed;
            ReticleCanvas.Visibility = Visibility.Collapsed;
            BadgeStep.Visibility = Visibility.Collapsed;

            // Calculate optimal Sensitivities and Deadzone
            double avgYawSpan = (_recordedYawRight + _recordedYawLeft) / 2.0;
            double avgPitchSpan = (_recordedPitchUp + _recordedPitchDown) / 2.0;

            // Scale to reference 1080p display
            _calculatedSensX = (float)Math.Clamp(Math.Round((avgYawSpan > 100 ? (350.0 / avgYawSpan) * 1.25 : 1.25), 2), 0.5, 3.5);
            _calculatedSensY = (float)Math.Clamp(Math.Round((avgPitchSpan > 80 ? (200.0 / avgPitchSpan) * 1.25 : 1.25), 2), 0.5, 3.5);
            _calculatedDeadzone = 0.04f;

            if (TxtDriftBias != null) TxtDriftBias.Text = "0.00 deg/s (Eliminated)";
            if (TxtSensX != null) TxtSensX.Text = $"{_calculatedSensX:F2}x";
            if (TxtSensY != null) TxtSensY.Text = $"{_calculatedSensY:F2}x";
            if (TxtDeadzone != null) TxtDeadzone.Text = $"{_calculatedDeadzone:F2} (Stabilized)";

            if (TxtPrompt != null)
            {
                TxtPrompt.Text = "Calibration complete! Click 'Save & Apply to Controller' to use these settings.";
                TxtPrompt.Foreground = (SolidColorBrush)FindResource("AccentSignature");
            }

            PanelResults.Visibility = Visibility.Visible;
        }

        private void BtnApplyCalibration_Click(object sender, RoutedEventArgs e)
        {
            OnCalibrationApplied?.Invoke(_calculatedSensX, _calculatedSensY, _calculatedDeadzone);
            Close();
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            _step = 1;
            _aimX = CanvasWidth / 2.0;
            _aimY = CanvasHeight / 2.0;
            TargetBullseye.Visibility = Visibility.Visible;
            ReticleCanvas.Visibility = Visibility.Visible;
            BadgeStep.Visibility = Visibility.Visible;
            PanelResults.Visibility = Visibility.Collapsed;
            TxtStepIndicator.Text = "TARGET 1 OF 5: CENTER";
            SetTargetPosition(_targets[0]);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}