using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WirelessBattery
{
    // Bir HID arayüzü / koleksiyonu.
    class HidColl
    {
        public string Path = "";
        public int Vid, Pid, Iface = -1;
        public int Usage, UsagePage;
        public int InLen, OutLen, FeatLen;
        public string Product = "";
        public string Manufacturer = "";
        public uint DevInst;   // aygıt ağacındaki düğüm (USB hızı için)
    }

    static class HidLayer
    {
        const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;

        public static List<HidColl> Enumerate()
        {
            List<HidColl> list = new List<HidColl>();
            Guid g;
            Native.HidD_GetHidGuid(out g);
            IntPtr di = Native.SetupDiGetClassDevsGuid(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (di == Native.INVALID_HANDLE || di == IntPtr.Zero) return list;
            try
            {
                for (uint i = 0; ; i++)
                {
                    SP_DEVICE_INTERFACE_DATA ifd = new SP_DEVICE_INTERFACE_DATA();
                    ifd.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!Native.SetupDiEnumDeviceInterfaces(di, IntPtr.Zero, ref g, i, ref ifd)) break;

                    uint need;
                    Native.SetupDiGetDeviceInterfaceDetail(di, ref ifd, IntPtr.Zero, 0, out need, IntPtr.Zero);
                    if (need == 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal((int)need);
                    string path = null;
                    uint devInst = 0;
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize
                        uint req;
                        SP_DEVINFO_DATA dd = new SP_DEVINFO_DATA();
                        dd.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                        if (Native.SetupDiGetDeviceInterfaceDetailEx(di, ref ifd, buf, need, out req, ref dd))
                        {
                            path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                            devInst = dd.DevInst;
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                    if (string.IsNullOrEmpty(path)) continue;

                    IntPtr h = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (h == Native.INVALID_HANDLE || h == IntPtr.Zero) continue;
                    try
                    {
                        HidColl c = new HidColl();
                        c.Path = path;
                        c.DevInst = devInst;
                        HIDD_ATTRIBUTES a = new HIDD_ATTRIBUTES();
                        a.Size = (uint)Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                        if (!Native.HidD_GetAttributes(h, ref a)) continue;
                        c.Vid = a.VendorID;
                        c.Pid = a.ProductID;

                        IntPtr pp;
                        if (Native.HidD_GetPreparsedData(h, out pp))
                        {
                            HIDP_CAPS caps;
                            if (Native.HidP_GetCaps(pp, out caps) == 0x110000)
                            {
                                c.Usage = caps.Usage;
                                c.UsagePage = caps.UsagePage;
                                c.InLen = caps.InputReportByteLength;
                                c.OutLen = caps.OutputReportByteLength;
                                c.FeatLen = caps.FeatureReportByteLength;
                            }
                            Native.HidD_FreePreparsedData(pp);
                        }

                        StringBuilder sb = new StringBuilder(256);
                        if (Native.HidD_GetProductString(h, sb, 512)) c.Product = sb.ToString().Trim();
                        sb = new StringBuilder(256);
                        if (Native.HidD_GetManufacturerString(h, sb, 512)) c.Manufacturer = sb.ToString().Trim();

                        string lower = path.ToLowerInvariant();
                        int mi = lower.IndexOf("&mi_", StringComparison.Ordinal);
                        if (mi >= 0 && lower.Length >= mi + 6)
                        {
                            try { c.Iface = Convert.ToInt32(lower.Substring(mi + 4, 2), 16); } catch (Exception) { }
                        }
                        list.Add(c);
                    }
                    finally { Native.CloseHandle(h); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(di); }
            return list;
        }
    }

    // Yalnızca feature report göndermek/okumak için açılan HID aygıtı (erişim hakkı 0: okuma-yazma gerekmez).
    class HidDev : IDisposable
    {
        IntPtr h = Native.INVALID_HANDLE;
        public HidColl Coll;

        public bool OpenFeatureOnly(HidColl c)
        {
            Close();
            Coll = c;
            h = Native.CreateFile(c.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            return h != Native.INVALID_HANDLE && h != IntPtr.Zero;
        }

        public void Close()
        {
            if (h != Native.INVALID_HANDLE && h != IntPtr.Zero) Native.CloseHandle(h);
            h = Native.INVALID_HANDLE;
        }

        public void Dispose() { Close(); }

        public bool SetFeature(byte[] data)
        {
            if (Coll == null || Coll.FeatLen <= 0 || h == Native.INVALID_HANDLE) return false;
            byte[] b = new byte[Coll.FeatLen];
            Array.Copy(data, b, Math.Min(data.Length, b.Length));
            return Native.HidD_SetFeature(h, b, (uint)b.Length);
        }

        // Dönen dizinin [0] elemanı report id'dir.
        public byte[] GetFeature(byte reportId)
        {
            if (Coll == null || Coll.FeatLen <= 0 || h == Native.INVALID_HANDLE) return null;
            byte[] b = new byte[Coll.FeatLen];
            b[0] = reportId;
            if (!Native.HidD_GetFeature(h, b, (uint)b.Length)) return null;
            return b;
        }
    }

    // Bir sürücünün çalışırken gördüğü ortam.
    class PollContext
    {
        public List<HidColl> Colls;
        public HashSet<uint> Claimed = new HashSet<uint>();
        public List<DeviceInfo> Out = new List<DeviceInfo>();

        public PollContext(List<HidColl> colls) { Colls = colls; }

        public static uint Key(int vid, int pid) { return ((uint)vid << 16) | (uint)pid; }

        public bool IsClaimed(int vid, int pid) { return Claimed.Contains(Key(vid, pid)); }
        public void Claim(int vid, int pid) { Claimed.Add(Key(vid, pid)); }

        public List<HidColl> Find(int vid, int pid, Func<HidColl, bool> pred)
        {
            List<HidColl> r = new List<HidColl>();
            foreach (HidColl c in Colls)
                if (c.Vid == vid && c.Pid == pid && pred(c)) r.Add(c);
            return r;
        }

        public List<int> Pids(int vid)
        {
            List<int> r = new List<int>();
            foreach (HidColl c in Colls)
                if (c.Vid == vid && !r.Contains(c.Pid)) r.Add(c.Pid);
            return r;
        }
    }
}
