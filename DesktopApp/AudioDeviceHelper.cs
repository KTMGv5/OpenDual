using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenDualHub
{
    public static class AudioDeviceHelper
    {
        private enum ERole
        {
            eConsole = 0,
            eMultimedia = 1,
            eCommunications = 2
        }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat();
            [PreserveSig] int GetDeviceFormat();
            [PreserveSig] int ResetDeviceFormat();
            [PreserveSig] int SetDeviceFormat();
            [PreserveSig] int GetProcessingPeriod();
            [PreserveSig] int SetProcessingPeriod();
            [PreserveSig] int GetShareMode();
            [PreserveSig] int SetShareMode();
            [PreserveSig] int GetPropertyValue();
            [PreserveSig] int SetPropertyValue();

            [PreserveSig]
            int SetDefaultEndpoint(
                [In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId,
                [In, MarshalAs(UnmanagedType.U4)] ERole role);

            [PreserveSig] int SetEndpointVisibility();
        }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class CPolicyConfigClient { }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);

            [PreserveSig]
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

            [PreserveSig]
            int RegisterEndpointNotificationCallback(IntPtr pClient);

            [PreserveSig]
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint pcDevices);
            [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            [PreserveSig] int GetState(out int pdwState);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint cProps);
            [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant pv);
            [PreserveSig] int Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
            public PropertyKey(Guid g, uint id) { fmtid = g; pid = id; }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pwszVal;
        }

        private static readonly PropertyKey PKEY_Device_FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

        private static string? _previousDefaultDeviceId = null;

        public static (string? id, string? name) FindControllerPlaybackDevice()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* DEVICE_STATE_ACTIVE */, out var collection) == 0 && collection != null)
                {
                    collection.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out var device) == 0 && device != null)
                        {
                            device.OpenPropertyStore(0 /* STGM_READ */, out var store);
                            if (store != null)
                            {
                                var key = PKEY_Device_FriendlyName;
                                if (store.GetValue(ref key, out var pv) == 0 && pv.pwszVal != IntPtr.Zero)
                                {
                                    string friendlyName = Marshal.PtrToStringUni(pv.pwszVal) ?? "";
                                    if (friendlyName.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
                                        friendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
                                    {
                                        device.GetId(out string id);
                                        return (id, friendlyName);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return (null, null);
        }

        public static (string? id, string? name) GetDefaultPlaybackDevice()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out var device) == 0 && device != null)
                {
                    device.GetId(out string id);
                    device.OpenPropertyStore(0, out var store);
                    string name = "Default Playback Device";
                    if (store != null)
                    {
                        var key = PKEY_Device_FriendlyName;
                        if (store.GetValue(ref key, out var pv) == 0 && pv.pwszVal != IntPtr.Zero)
                        {
                            name = Marshal.PtrToStringUni(pv.pwszVal) ?? name;
                        }
                    }
                    return (id, name);
                }
            }
            catch { }
            return (null, null);
        }

        public static bool SetDefaultPlaybackDevice(string deviceId)
        {
            try
            {
                var policy = (IPolicyConfig)new CPolicyConfigClient();
                policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
                policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool RouteAudioToController(bool enable)
        {
            try
            {
                var (controllerId, controllerName) = FindControllerPlaybackDevice();
                var (currentDefaultId, currentDefaultName) = GetDefaultPlaybackDevice();

                if (enable)
                {
                    if (string.IsNullOrEmpty(controllerId)) return false;

                    // Remember previous default if it wasn't the controller
                    if (!string.IsNullOrEmpty(currentDefaultId) && currentDefaultId != controllerId)
                    {
                        _previousDefaultDeviceId = currentDefaultId;
                    }

                    return SetDefaultPlaybackDevice(controllerId);
                }
                else
                {
                    // If controller was default and we have a previous device, restore it
                    if (!string.IsNullOrEmpty(_previousDefaultDeviceId) && _previousDefaultDeviceId != controllerId)
                    {
                        SetDefaultPlaybackDevice(_previousDefaultDeviceId);
                        return true;
                    }
                    else
                    {
                        // Otherwise, find first non-controller active render device
                        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                        if (enumerator.EnumAudioEndpoints(0, 1, out var collection) == 0 && collection != null)
                        {
                            collection.GetCount(out uint count);
                            for (uint i = 0; i < count; i++)
                            {
                                if (collection.Item(i, out var device) == 0 && device != null)
                                {
                                    device.GetId(out string id);
                                    if (id != controllerId)
                                    {
                                        SetDefaultPlaybackDevice(id);
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void OpenSoundSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("mmsys.cpl") { UseShellExecute = true }); } catch { }
            }
        }
    }
}
