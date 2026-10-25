using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace OpenDualHub
{
    public class GameDetector : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private readonly DispatcherTimer _timer;
        private readonly ProfileManager _profileManager;
        private string _lastProcessName = "";
        private string _lastActiveProfileId = "";
        private bool _autoSwitchEnabled = true;

        public bool AutoSwitchEnabled
        {
            get => _autoSwitchEnabled;
            set => _autoSwitchEnabled = value;
        }

        public string CurrentProcessName => _lastProcessName;

        public event Action<string, GameProfile?>? OnGameChanged;

        public GameDetector(ProfileManager profileManager)
        {
            _profileManager = profileManager;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (!_autoSwitchEnabled) return;

            string currentExe = GetActiveProcessName();
            if (string.Equals(currentExe, _lastProcessName, StringComparison.OrdinalIgnoreCase))
                return;

            _lastProcessName = currentExe;
            var matchedProfile = _profileManager.FindProfileForProcess(currentExe);

            // Avoid re-triggering same profile
            if (matchedProfile != null)
            {
                if (matchedProfile.Id != _lastActiveProfileId)
                {
                    _lastActiveProfileId = matchedProfile.Id;
                    OnGameChanged?.Invoke(currentExe, matchedProfile);
                }
            }
            else if (!string.IsNullOrEmpty(_lastActiveProfileId))
            {
                // Switched to an unmatched process (e.g. Explorer, Chrome)
                // Optionally revert to default profile
                _lastActiveProfileId = "";
                OnGameChanged?.Invoke(currentExe, null);
            }
        }

        private static string GetActiveProcessName()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return "";

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return "";

                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero) return "";

                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return Path.GetFileName(sb.ToString());
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch { }

            return "";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
