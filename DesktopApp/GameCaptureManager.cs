using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenDualHub
{
    public static class GameCaptureManager
    {
        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_MENU = 0x12; // Alt
        private const ushort VK_SNAPSHOT = 0x2C; // Print Screen
        private const ushort VK_G = 0x47;
        private const ushort VK_R = 0x52;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)]
            public int type;
            [FieldOffset(8)]
            public KEYBDINPUT ki;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static string GetCapturesDirectory()
        {
            string myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string captures = Path.Combine(myVideos, "Captures");
            if (!Directory.Exists(captures))
            {
                try { Directory.CreateDirectory(captures); } catch { }
            }
            return captures;
        }

        public static bool IsGameDvrEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                if (key != null)
                {
                    object? appCapture = key.GetValue("AppCaptureEnabled");
                    if (appCapture is int val && val == 1) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool IsHistoricalCaptureEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                if (key != null)
                {
                    object? hist = key.GetValue("HistoricalCaptureEnabled");
                    if (hist is int val && val == 1) return true;
                }
            }
            catch { }
            return false;
        }

        public static int GetHistoricalBufferLengthSeconds()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                if (key != null)
                {
                    object? len = key.GetValue("HistoricalBufferLength");
                    if (len is int sec && sec > 0) return sec;
                }
            }
            catch { }
            return 30; // Default Windows Game Bar duration
        }

        public static void SetHistoricalBufferLengthSeconds(int seconds)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                if (key != null)
                {
                    key.SetValue("HistoricalBufferLength", seconds, RegistryValueKind.DWord);
                    key.SetValue("HistoricalBufferLengthUnit", 1, RegistryValueKind.DWord);
                    key.SetValue("HistoricalCaptureEnabled", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        public static void EnableGameDvr()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                if (key != null)
                {
                    key.SetValue("AppCaptureEnabled", 1, RegistryValueKind.DWord);
                    key.SetValue("HistoricalCaptureEnabled", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        public static void SaveRecentClip(int? customSeconds = null)
        {
            if (customSeconds.HasValue && customSeconds.Value > 0)
            {
                SetHistoricalBufferLengthSeconds(customSeconds.Value);
            }

            // Send Win + Alt + G to trigger Windows Game Bar "Record That"
            SendKeyCombo(new ushort[] { VK_LWIN, VK_MENU, VK_G });
        }

        public static void ToggleRecording()
        {
            // Send Win + Alt + R to start or stop Windows Game Bar recording
            SendKeyCombo(new ushort[] { VK_LWIN, VK_MENU, VK_R });
        }

        public static void TakeScreenshot()
        {
            // Send Win + Alt + PrtScn to take Game Bar screenshot
            SendKeyCombo(new ushort[] { VK_LWIN, VK_MENU, VK_SNAPSHOT });
        }

        public static void OpenXboxGameBar()
        {
            // Send Win + G to summon Xbox Game Bar overlay
            SendKeyCombo(new ushort[] { VK_LWIN, VK_G });
        }

        public static void OpenCapturesFolder()
        {
            try
            {
                string path = GetCapturesDirectory();
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public static void OpenGameDvrSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:gaming-gamedvr") { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:gaming") { UseShellExecute = true });
                }
                catch { }
            }
        }

        private static void SendKeyCombo(ushort[] keys)
        {
            int inputCount = keys.Length * 2;
            var inputs = new INPUT[inputCount];

            // Press keys down in order
            for (int i = 0; i < keys.Length; i++)
            {
                inputs[i] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT
                    {
                        wVk = keys[i],
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                };
            }

            // Release keys up in reverse order
            for (int i = 0; i < keys.Length; i++)
            {
                inputs[keys.Length + i] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT
                    {
                        wVk = keys[keys.Length - 1 - i],
                        wScan = 0,
                        dwFlags = KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                };
            }

            int structSize = Marshal.SizeOf(typeof(INPUT));
            SendInput((uint)inputCount, inputs, structSize);
        }
    }
}
