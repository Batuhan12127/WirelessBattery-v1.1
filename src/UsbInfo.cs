using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WirelessBattery
{
    class UsbRate
    {
        public int Hz;
        public int Interval;
        public string Speed = "";   // Loc anahtarı: düşük hız / tam hız / yüksek hız / süper hız
    }

    // Aygıtın USB üzerinde BİLDİRDİĞİ tarama hızını okur: bağlı olduğu USB hub'ından, aygıtın açık
    // kesme (interrupt) uç noktalarının bInterval değerini alır. Düşük/tam hızda bInterval milisaniyedir,
    // yüksek/süper hızda 125 µs'nin 2^(bInterval-1) katıdır. Bu, aygıt ile bilgisayar arasındaki
    // en yüksek taşıma hızıdır (Bluetooth aygıtlarda yoktur).
    static class UsbInfo
    {
        static readonly Guid GuidHub = new Guid("F18A0E88-C30C-11D0-8815-00A0C906BED8");
        const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;
        const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        static readonly Dictionary<uint, UsbRate> cache = new Dictionary<uint, UsbRate>();

        static string DevId(uint inst)
        {
            StringBuilder sb = new StringBuilder(512);
            if (Native.CM_Get_Device_ID(inst, sb, 511, 0) != 0) return "";
            return sb.ToString();
        }

        public static UsbRate Get(uint hidDevInst, uint cacheKey)
        {
            lock (cache)
            {
                UsbRate c;
                if (cache.TryGetValue(cacheKey, out c)) return c;
            }
            UsbRate r = null;
            try { r = Query(hidDevInst); }
            catch (Exception) { r = null; }
            if (r != null)
            {
                lock (cache) { cache[cacheKey] = r; }
            }
            return r;
        }

        static UsbRate Query(uint hidDevInst)
        {
            if (hidDevInst == 0) return null;

            // HID düğümünden yukarı çık: USB aygıt düğümünü (arayüz değil) bul.
            uint cur = hidDevInst, usbDev = 0;
            for (int i = 0; i < 6; i++)
            {
                string id = DevId(cur);
                if (id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) && id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    usbDev = cur;
                    break;
                }
                uint parent;
                if (Native.CM_Get_Parent(out parent, cur, 0) != 0) return null;
                cur = parent;
            }
            if (usbDev == 0) return null; // USB değil (Bluetooth vb.)

            uint hub;
            if (Native.CM_Get_Parent(out hub, usbDev, 0) != 0) return null;
            string hubId = DevId(hub);
            if (hubId.Length == 0) return null;

            // Aygıtın hub üzerindeki port numarası.
            uint regType, len = 4;
            byte[] pb = new byte[4];
            if (Native.CM_Get_DevNode_Registry_Property(usbDev, 0x1D /* CM_DRP_ADDRESS */, out regType, pb, ref len, 0) != 0 || len < 4) return null;
            uint port = BitConverter.ToUInt32(pb, 0);

            string hubPath = FindHubPath(hubId);
            if (hubPath == null) return null;
            IntPtr hh = Native.CreateFile(hubPath, 0x40000000 /* GENERIC_WRITE */, 2 /* FILE_SHARE_WRITE */, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (hh == Native.INVALID_HANDLE || hh == IntPtr.Zero) return null;
            try { return ReadPipes(hh, port); }
            finally { Native.CloseHandle(hh); }
        }

        static string FindHubPath(string hubInstanceId)
        {
            Guid g = GuidHub;
            IntPtr di = Native.SetupDiGetClassDevsGuid(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (di == Native.INVALID_HANDLE || di == IntPtr.Zero) return null;
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
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        SP_DEVINFO_DATA dd = new SP_DEVINFO_DATA();
                        dd.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                        uint req;
                        if (!Native.SetupDiGetDeviceInterfaceDetailEx(di, ref ifd, buf, need, out req, ref dd)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));

                        StringBuilder sb = new StringBuilder(512);
                        uint req2;
                        if (Native.SetupDiGetDeviceInstanceId(di, ref dd, sb, 511, out req2) &&
                            string.Equals(sb.ToString(), hubInstanceId, StringComparison.OrdinalIgnoreCase))
                            return path;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(di); }
            return null;
        }

        // USB_NODE_CONNECTION_INFORMATION_EX (paketli): ConnectionIndex(4) DeviceDescriptor(18) CurrentConfig(1)
        // Speed(1) DeviceIsHub(1) DeviceAddress(2) NumberOfOpenPipes(4) ConnectionStatus(4) PipeList[] (her biri 11 bayt:
        // 7 bayt uç nokta tanımlayıcısı + 4 bayt ScheduleOffset)
        static UsbRate ReadPipes(IntPtr hub, uint port)
        {
            const int maxPipes = 32;
            int size = 35 + 11 * maxPipes;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
                Marshal.WriteInt32(buf, 0, (int)port);
                uint ret;
                if (!Native.DeviceIoControl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, buf, (uint)size, buf, (uint)size, out ret, IntPtr.Zero))
                    return null;

                int speed = Marshal.ReadByte(buf, 23);
                int open = Marshal.ReadInt32(buf, 27);
                if (open < 0) open = 0;
                if (open > maxPipes) open = maxPipes;

                double bestHz = 0;
                int bestInterval = 0;
                for (int p = 0; p < open; p++)
                {
                    int off = 35 + 11 * p;
                    int addr = Marshal.ReadByte(buf, off + 2);
                    int attr = Marshal.ReadByte(buf, off + 3);
                    int interval = Marshal.ReadByte(buf, off + 6);
                    bool interruptIn = (addr & 0x80) != 0 && (attr & 0x03) == 3;
                    if (!interruptIn || interval <= 0) continue;
                    double hz = speed >= 2 ? 8000.0 / Math.Pow(2.0, interval - 1) : 1000.0 / interval;
                    if (hz > bestHz)
                    {
                        bestHz = hz;
                        bestInterval = interval;
                    }
                }
                if (bestHz <= 0) return null;

                UsbRate r = new UsbRate();
                r.Hz = (int)Math.Round(bestHz);
                r.Interval = bestInterval;
                r.Speed = speed == 0 ? "düşük hız" : (speed == 1 ? "tam hız" : (speed == 2 ? "yüksek hız" : "süper hız"));
                return r;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }
}
