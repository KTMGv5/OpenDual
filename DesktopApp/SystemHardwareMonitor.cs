using System;
using System.Runtime.InteropServices;

namespace OpenDualHub
{
    public static class SystemHardwareMonitor
    {
        #region Memory (RAM)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static (int loadPercent, double usedGb, double totalGb) GetMemoryStats()
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGb = Math.Max(0.0, totalGb - availGb);
                return ((int)mem.dwMemoryLoad, usedGb, totalGb);
            }
            return (0, 0, 0);
        }
        #endregion

        #region CPU Usage
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
                                                  out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
                                                  out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        private static ulong _prevIdleTime;
        private static ulong _prevKernelTime;
        private static ulong _prevUserTime;
        private static DateTime _lastCpuSampleTime = DateTime.MinValue;
        private static int _cachedCpuUsage = 0;

        private static ulong FileTimeToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        }

        public static int GetCpuUsagePercent()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCpuSampleTime).TotalMilliseconds < 400 && _lastCpuSampleTime != DateTime.MinValue)
            {
                return _cachedCpuUsage;
            }

            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                return _cachedCpuUsage;

            ulong idle = FileTimeToUInt64(idleFt);
            ulong kernel = FileTimeToUInt64(kernelFt);
            ulong user = FileTimeToUInt64(userFt);

            if (_prevKernelTime != 0)
            {
                ulong kernelDelta = kernel - _prevKernelTime;
                ulong userDelta = user - _prevUserTime;
                ulong idleDelta = idle - _prevIdleTime;

                ulong totalSys = kernelDelta + userDelta;
                if (totalSys > 0 && totalSys >= idleDelta)
                {
                    ulong busy = totalSys - idleDelta;
                    _cachedCpuUsage = (int)Math.Clamp((busy * 100.0) / totalSys, 0.0, 100.0);
                }
            }

            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _lastCpuSampleTime = now;
            return _cachedCpuUsage;
        }
        #endregion

        #region Master Audio Volume (CoreAudio)
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate([In] ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint channelCount);
            [PreserveSig] int SetMasterVolumeLevel(float levelDB, ref Guid eventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDB);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
        }

        private static IAudioEndpointVolume? GetAudioEndpointVolume()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var dev) == 0 && dev != null)
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    if (dev.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var volObj) == 0 && volObj != null)
                    {
                        return (IAudioEndpointVolume)volObj;
                    }
                }
            }
            catch { }
            return null;
        }

        public static int GetMasterVolumePercent()
        {
            try
            {
                var vol = GetAudioEndpointVolume();
                if (vol != null && vol.GetMasterVolumeLevelScalar(out float scalar) == 0)
                {
                    return (int)Math.Round(scalar * 100.0f);
                }
            }
            catch { }
            return 75; // Fallback sensible default
        }

        public static void SetMasterVolumePercent(int percent)
        {
            try
            {
                var vol = GetAudioEndpointVolume();
                if (vol != null)
                {
                    float scalar = Math.Clamp(percent / 100.0f, 0.0f, 1.0f);
                    Guid guid = Guid.Empty;
                    vol.SetMasterVolumeLevelScalar(scalar, ref guid);
                }
            }
            catch { }
        }

        public static void AdjustMasterVolume(int deltaPercent)
        {
            int current = GetMasterVolumePercent();
            SetMasterVolumePercent(current + deltaPercent);
        }

        public static bool IsMasterMuted()
        {
            try
            {
                var vol = GetAudioEndpointVolume();
                if (vol != null && vol.GetMute(out bool muted) == 0)
                {
                    return muted;
                }
            }
            catch { }
            return false;
        }

        public static void ToggleMasterMute()
        {
            try
            {
                var vol = GetAudioEndpointVolume();
                if (vol != null && vol.GetMute(out bool muted) == 0)
                {
                    Guid guid = Guid.Empty;
                    vol.SetMute(!muted, ref guid);
                }
            }
            catch { }
        }
        #endregion
    }
}
