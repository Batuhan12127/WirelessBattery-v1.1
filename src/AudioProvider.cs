using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WirelessBattery
{
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid g, uint p)
        {
            fmtid = g;
            pid = p;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, IntPtr propVariant);
        [PreserveSig] int SetValue(ref PropertyKey key, IntPtr propVariant);
        [PreserveSig] int Commit();
    }

    // Windows'un her ses aygıtı için kullandığı formatı (kHz / bit / kanal) okur.
    static class AudioProvider
    {
        static readonly Guid GDevice = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");
        static readonly Guid GFormat = new Guid("F19F064D-082C-4E27-BC73-6882A1BB8E4C");
        static readonly Guid GContainer = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C");

        const int VT_LPWSTR = 31, VT_BLOB = 65, VT_CLSID = 72;

        static IntPtr NewVariant()
        {
            IntPtr p = Marshal.AllocCoTaskMem(24);
            for (int i = 0; i < 24; i++) Marshal.WriteByte(p, i, 0);
            return p;
        }

        static string ReadString(IPropertyStore ps, PropertyKey key)
        {
            IntPtr pv = NewVariant();
            try
            {
                if (ps.GetValue(ref key, pv) != 0) return "";
                int vt = Marshal.ReadInt16(pv) & 0xFFFF;
                if (vt != VT_LPWSTR) return "";
                IntPtr s = Marshal.ReadIntPtr(pv, 8);
                return s == IntPtr.Zero ? "" : (Marshal.PtrToStringUni(s) ?? "");
            }
            finally
            {
                Native.PropVariantClear(pv);
                Marshal.FreeCoTaskMem(pv);
            }
        }

        static string ReadContainer(IPropertyStore ps)
        {
            PropertyKey key = new PropertyKey(GContainer, 2);
            IntPtr pv = NewVariant();
            try
            {
                if (ps.GetValue(ref key, pv) != 0) return "";
                int vt = Marshal.ReadInt16(pv) & 0xFFFF;
                if (vt != VT_CLSID) return "";
                IntPtr g = Marshal.ReadIntPtr(pv, 8);
                if (g == IntPtr.Zero) return "";
                byte[] b = new byte[16];
                Marshal.Copy(g, b, 0, 16);
                return new Guid(b).ToString("B");
            }
            finally
            {
                Native.PropVariantClear(pv);
                Marshal.FreeCoTaskMem(pv);
            }
        }

        static void ReadFormat(IPropertyStore ps, AudioEndpoint a)
        {
            PropertyKey key = new PropertyKey(GFormat, 0);
            IntPtr pv = NewVariant();
            try
            {
                if (ps.GetValue(ref key, pv) != 0) return;
                int vt = Marshal.ReadInt16(pv) & 0xFFFF;
                if (vt != VT_BLOB) return;
                int cb = Marshal.ReadInt32(pv, 8);
                IntPtr data = Marshal.ReadIntPtr(pv, IntPtr.Size == 8 ? 16 : 12);
                if (cb < 16 || data == IntPtr.Zero) return;
                // WAVEFORMATEX: wFormatTag(2) nChannels(2) nSamplesPerSec(4) nAvgBytesPerSec(4) nBlockAlign(2) wBitsPerSample(2)
                a.Channels = Marshal.ReadInt16(data, 2) & 0xFFFF;
                a.Rate = Marshal.ReadInt32(data, 4);
                a.Bits = Marshal.ReadInt16(data, 14) & 0xFFFF;
            }
            finally
            {
                Native.PropVariantClear(pv);
                Marshal.FreeCoTaskMem(pv);
            }
        }

        static string DefaultId(IMMDeviceEnumerator en, int flow)
        {
            IMMDevice dev = null;
            try
            {
                if (en.GetDefaultAudioEndpoint(flow, 0, out dev) != 0 || dev == null) return "";
                string id;
                dev.GetId(out id);
                return id ?? "";
            }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
            }
        }

        public static List<AudioEndpoint> Query()
        {
            List<AudioEndpoint> list = new List<AudioEndpoint>();
            IMMDeviceEnumerator en = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                for (int flow = 0; flow < 2; flow++) // 0: çıkış, 1: mikrofon
                {
                    string defId = DefaultId(en, flow);
                    IMMDeviceCollection col = null;
                    try
                    {
                        if (en.EnumAudioEndpoints(flow, 1 /*DEVICE_STATE_ACTIVE*/, out col) != 0 || col == null) continue;
                        uint n;
                        col.GetCount(out n);
                        for (uint i = 0; i < n; i++)
                        {
                            IMMDevice dev = null;
                            IPropertyStore ps = null;
                            try
                            {
                                if (col.Item(i, out dev) != 0 || dev == null) continue;
                                AudioEndpoint a = new AudioEndpoint();
                                a.Render = (flow == 0);
                                string id;
                                dev.GetId(out id);
                                a.Id = id ?? "";
                                a.IsDefault = a.Id.Length > 0 && a.Id == defId;
                                if (dev.OpenPropertyStore(0, out ps) == 0 && ps != null)
                                {
                                    a.Name = ReadString(ps, new PropertyKey(GDevice, 14));
                                    a.Bus = ReadString(ps, new PropertyKey(GDevice, 24));
                                    a.Container = ReadContainer(ps);
                                    ReadFormat(ps, a);
                                }
                                list.Add(a);
                            }
                            catch (Exception) { }
                            finally
                            {
                                if (ps != null) Marshal.ReleaseComObject(ps);
                                if (dev != null) Marshal.ReleaseComObject(dev);
                            }
                        }
                    }
                    finally
                    {
                        if (col != null) Marshal.ReleaseComObject(col);
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                if (en != null) Marshal.ReleaseComObject(en);
            }
            return list;
        }
    }
}
