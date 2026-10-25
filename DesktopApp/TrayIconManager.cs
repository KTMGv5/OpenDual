using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenDualHub
{
    public class TrayIconManager : IDisposable
    {
        public const int WM_APP = 0x8000;
        public const int WM_TRAYICON = WM_APP + 101;

        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_LBUTTONUP = 0x0202;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIM_SETVERSION = 0x00000004;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;

        private const int NIIF_INFO = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private IntPtr _hWnd;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _isAdded = false;

        public event Action? DoubleClick;
        public event Action? RightClick;

        public void Initialize(IntPtr hWnd, string tooltip)
        {
            _hWnd = hWnd;

            // Load app.ico if available
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/, 16, 16, 0x00000010 /*LR_LOADFROMFILE*/);
            }

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip
            };

            _isAdded = Shell_NotifyIcon(NIM_ADD, ref nid);
        }

        public void ShowNotification(string title, string message)
        {
            if (!_isAdded || _hWnd == IntPtr.Zero) return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_INFO,
                szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title,
                szInfo = message.Length > 255 ? message.Substring(0, 255) : message,
                dwInfoFlags = NIIF_INFO
            };

            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }

        public bool ProcessWindowMessage(int msg, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                int evt = lParam.ToInt32();
                if (evt == WM_LBUTTONDBLCLK || evt == WM_LBUTTONUP)
                {
                    DoubleClick?.Invoke();
                    return true;
                }
                else if (evt == WM_RBUTTONUP)
                {
                    RightClick?.Invoke();
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_isAdded && _hWnd != IntPtr.Zero)
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hWnd,
                    uID = 1001
                };
                Shell_NotifyIcon(NIM_DELETE, ref nid);
                _isAdded = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
    }
}
