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

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(2)] public ushort wReserved1;
            [FieldOffset(4)] public ushort wReserved2;
            [FieldOffset(6)] public ushort wReserved3;
            [FieldOffset(8)] public IntPtr pwszVal;
            [FieldOffset(16)] public long dummy;
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        private const ushort VT_LPWSTR = 31;

        private static readonly PropertyKey PKEY_Device_DeviceDesc = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
        private static readonly PropertyKey PKEY_Device_FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        private static readonly PropertyKey PKEY_DeviceInterface_FriendlyName = new PropertyKey(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 6);
        private static readonly PropertyKey PKEY_Device_InstanceId = new PropertyKey(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

        private static string? _previousDefaultDeviceId = null;

        private static string GetStoreString(IPropertyStore store, PropertyKey key)
        {
            PropVariant pv = default;
            try
            {
                if (store.GetValue(ref key, out pv) == 0)
                {
                    if (pv.vt == VT_LPWSTR && pv.pwszVal != IntPtr.Zero)
                    {
                        return Marshal.PtrToStringUni(pv.pwszVal) ?? "";
                    }
                }
            }
            catch { }
            finally
            {
                try { PropVariantClear(ref pv); } catch { }
            }
            return "";
        }

        private static string GetDeviceDisplayName(IPropertyStore store)
        {
            string friendly = GetStoreString(store, PKEY_Device_FriendlyName);
            if (!string.IsNullOrWhiteSpace(friendly)) return friendly;

            string desc = GetStoreString(store, PKEY_Device_DeviceDesc);
            string iface = GetStoreString(store, PKEY_DeviceInterface_FriendlyName);

            if (!string.IsNullOrWhiteSpace(desc) && !string.IsNullOrWhiteSpace(iface))
                return $"{desc} ({iface})";
            if (!string.IsNullOrWhiteSpace(iface))
                return iface;
            if (!string.IsNullOrWhiteSpace(desc))
                return desc;

            return "Audio Output Device";
        }

        public static bool IsControllerDevice(string? deviceId, string? deviceName)
        {
            return IsControllerDevice(deviceId, deviceName, null);
        }

        private static bool IsControllerDevice(string? deviceId, string? deviceName, IPropertyStore? store)
        {
            if (!string.IsNullOrEmpty(deviceName))
            {
                if (deviceName.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
                    deviceName.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
                    deviceName.Contains("DualShock", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(deviceId))
            {
                if (deviceId.Contains("054C", StringComparison.OrdinalIgnoreCase) ||
                    deviceId.Contains("0CE6", StringComparison.OrdinalIgnoreCase) ||
                    deviceId.Contains("0DF2", StringComparison.OrdinalIgnoreCase) ||
                    deviceId.Contains("05C4", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (store != null)
            {
                string pnp = GetStoreString(store, PKEY_DeviceInterface_FriendlyName);
                if (pnp.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
                    pnp.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
                    pnp.Contains("DualShock", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string inst = GetStoreString(store, PKEY_Device_InstanceId);
                if (inst.Contains("054C", StringComparison.OrdinalIgnoreCase) ||
                    inst.Contains("0CE6", StringComparison.OrdinalIgnoreCase) ||
                    inst.Contains("0DF2", StringComparison.OrdinalIgnoreCase) ||
                    inst.Contains("05C4", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

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
                            device.GetId(out string id);
                            device.OpenPropertyStore(0 /* STGM_READ */, out var store);
                            if (store != null)
                            {
                                string displayName = GetDeviceDisplayName(store);
                                if (IsControllerDevice(id, displayName, store))
                                {
                                    return (id, displayName);
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
                        name = GetDeviceDisplayName(store);
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
