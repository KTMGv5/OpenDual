using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class OverclockAssistantWindow : Window
    {
        private readonly PollingBenchmark _benchmark = new();
        private bool _celebrated = false;

        public OverclockAssistantWindow()
        {
            InitializeComponent();
            RefreshFilterStatus();
        }

        public void UpdateTelemetry(ControllerTelemetry state)
        {
            Dispatcher.Invoke(() =>
            {
                _benchmark.AddSample(state.PollingRateHz, state.LatencyMs);

                BenchHz.Text = $"{state.PollingRateHz} Hz";
                BenchLatency.Text = $"{state.LatencyMs:F1} ms";

                float intervalMs = state.PollingRateHz > 0 ? (1000.0f / state.PollingRateHz) : 4.0f;
                BenchInterval.Text = $"{intervalMs:F1} ms interval";

                if (_benchmark.MinLatencyMs < 900.0f)
                {
                    BenchMinMax.Text = $"{_benchmark.MinLatencyMs:F1} / {_benchmark.MaxLatencyMs:F1} ms";
                    BenchAvg.Text = $"Avg: {_benchmark.AvgLatencyMs:F1} ms";
                }

                if (state.PollingRateHz >= 900)
                {
                    // Esports Ready / Overclocked
                    PillLiveRate.Background = new SolidColorBrush(Color.FromRgb(14, 51, 32));
                    PillLiveRate.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    DotRateIndicator.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    TxtRateHeaderStatus.Text = $"{state.PollingRateHz} Hz ({state.LatencyMs:F1} ms) • Esports Ready!";
                    TxtRateHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));

                    BenchLatencyDiff.Text = "Esports Target Achieved";
                    BenchLatencyDiff.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));

                    if (!_celebrated)
                    {
                        _celebrated = true;
                        TxtActionStatus.Text = "🎉 SUCCESS: Overclock verified! Your DualSense is now running at 1000 Hz (1.0 ms delay).";
                        TxtActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                        RefreshFilterStatus();
                    }
                }
                else
                {
                    // Standard factory polling
                    PillLiveRate.Background = new SolidColorBrush(Color.FromRgb(38, 28, 8));
                    PillLiveRate.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 160, 0));
                    DotRateIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 160, 0));
                    TxtRateHeaderStatus.Text = $"{state.PollingRateHz} Hz ({state.LatencyMs:F1} ms) • Default";
                    TxtRateHeaderStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 160, 0));

                    BenchLatencyDiff.Text = "Esports target: 1.0 ms";
                    BenchLatencyDiff.Foreground = (Brush)FindResource("TextSecondary");
                }
            });
        }

        public void RefreshFilterStatus()
        {
            var (isFiltered, details) = OverclockManager.CheckDualSenseFilterStatus();
            bool svcInstalled = OverclockManager.IsHidusbfServiceInstalled();

            if (isFiltered)
            {
                BenchFilterStatus.Text = "Filtered (Active)";
                BenchFilterStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                BenchDeviceFilter.Text = details;
            }
            else if (svcInstalled)
            {
                BenchFilterStatus.Text = "Service Ready";
                BenchFilterStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 245, 212));
                BenchDeviceFilter.Text = "Device Not Filtered Yet";
            }
            else
            {
                BenchFilterStatus.Text = "Not Installed";
                BenchFilterStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 160, 0));
                BenchDeviceFilter.Text = "Default Windows Driver";
            }
        }

        private async void BtnDownloadHidusbf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnDownloadHidusbf.IsEnabled = false;
                ProgressDownload.Visibility = Visibility.Visible;
                ProgressDownload.Value = 0;
                TxtActionStatus.Text = "Downloading official SweetLow HIDUSBF package from GitHub...";
                TxtActionStatus.Foreground = (Brush)FindResource("TextSecondary");

                var progress = new Progress<int>(pct => ProgressDownload.Value = pct);
                var (success, msg) = await OverclockManager.DownloadAndExtractAsync(progress);

                if (success)
                {
                    TxtActionStatus.Text = "Download complete! SweetLow package ready in tools directory.";
                    TxtActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }
                else
                {
                    TxtActionStatus.Text = $"Download failed: {msg}";
                    TxtActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 51, 75));
                }
            }
            finally
            {
                BtnDownloadHidusbf.IsEnabled = true;
                ProgressDownload.Visibility = Visibility.Collapsed;
                RefreshFilterStatus();
            }
        }

        private async void BtnInstallCert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnInstallCert.IsEnabled = false;
                TxtActionStatus.Text = "Installing SweetLow driver certificate to Windows Trusted Stores (elevated)...";
                TxtActionStatus.Foreground = (Brush)FindResource("TextSecondary");

                var (success, msg) = await OverclockManager.InstallCertificateAsync();
                if (success)
                {
                    TxtActionStatus.Text = "Certificate installed successfully! Windows will accept the filter driver.";
                    TxtActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }
                else
                {
                    TxtActionStatus.Text = msg;
                    TxtActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 160, 0));
                }
            }
            finally
            {
                BtnInstallCert.IsEnabled = true;
            }
        }

        private void BtnLaunchSetup_Click(object sender, RoutedEventArgs e)
        {
            if (!OverclockManager.IsHidusbfDownloaded())
            {
                BtnDownloadHidusbf_Click(sender, e);
                return;
            }

            var (success, msg) = OverclockManager.LaunchSweetLowSetup();
            if (success)
            {
                TxtActionStatus.Text = "SweetLow Setup launched. Follow steps 3 and 4 in the guide below.";
                TxtActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 245, 212));
            }
            else
            {
                TxtActionStatus.Text = msg;
                TxtActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 51, 75));
            }
        }

        private void BtnCoreIsolation_Click(object sender, RoutedEventArgs e)
        {
            OverclockManager.OpenCoreIsolationSettings();
            TxtActionStatus.Text = "Opened Windows Core Isolation settings. Ensure Memory Integrity is set appropriately if driver fails to load.";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
