using System;
using System.Collections.Generic;
using System.Text;

namespace WirelessBattery
{
    // Windows, Bluetooth cihazların pil değerini aygıt özelliği olarak tutar (Ayarlar > Bluetooth'ta
    // görünen değer). Bir cihaz birden çok aygıt düğümü olarak görünür: ana düğümde gerçek ad ve
    // "bağlı mı" bilgisi, servis düğümünde (Hands-Free vb.) pil değeri bulunur. Hepsi aynı
    // "container" kimliğini paylaşır; bu yüzden container'a göre birleştiriyoruz.
    static class BluetoothProvider
    {
        static readonly Guid GCommon = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"); // friendly=14, desc=2
        static readonly Guid GBattery = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"); // pid 2
        static readonly Guid GConnected = new Guid("83DA6326-97A6-4088-9453-A1923F573B29"); // pid 15
        static readonly Guid GContainer = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"); // pid 2
        static readonly Guid GClassOfDevice = new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"); // pid 10

        const uint TYPE_BYTE = 0x03, TYPE_UINT32 = 0x07, TYPE_GUID = 0x0D, TYPE_BOOLEAN = 0x11, TYPE_STRING = 0x12;
        const uint DIGCF_PRESENT = 0x02, DIGCF_ALLCLASSES = 0x04;

        class MainNode
        {
            public string Name = "", Id = "", Mac = "";
            public bool HasConn, Conn = true;
            public uint Cod;
            public bool HasCod;
        }

        class BattNode
        {
            public int Pct;
            public string Name = "", Id = "";
        }

        static byte[] GetProp(IntPtr h, ref SP_DEVINFO_DATA d, Guid g, uint pid, out uint type)
        {
            DEVPROPKEY k = new DEVPROPKEY();
            k.fmtid = g;
            k.pid = pid;
            byte[] buf = new byte[1024];
            uint req;
            type = 0;
            if (!Native.SetupDiGetDevicePropertyW(h, ref d, ref k, out type, buf, (uint)buf.Length, out req, 0)) return null;
            int n = (int)Math.Min(req, (uint)buf.Length);
            byte[] r = new byte[n];
            Array.Copy(buf, r, n);
            return r;
        }

        static string GetString(IntPtr h, ref SP_DEVINFO_DATA d, Guid g, uint pid)
        {
            uint t;
            byte[] b = GetProp(h, ref d, g, pid, out t);
            if (b == null || t != TYPE_STRING) return "";
            return Encoding.Unicode.GetString(b).TrimEnd('\0');
        }

        static string MacFromId(string id)
        {
            int p = id.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
            if (p < 0 || id.Length < p + 4 + 12) return "";
            string m = id.Substring(p + 4, 12);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) sb.Append(':');
                sb.Append(m.Substring(i * 2, 2));
            }
            return sb.ToString().ToUpperInvariant();
        }

        static string StripServiceSuffix(string n)
        {
            string[] suf = { " Hands-Free AG", " Hands-Free", " Avrcp Transport", " Stereo", " Audio Gateway" };
            foreach (string s in suf)
                if (n.Length > s.Length && n.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    return n.Substring(0, n.Length - s.Length);
            return n;
        }

        static DeviceKind KindFromCod(uint cod)
        {
            uint major = (cod >> 8) & 0x1F, minor = (cod >> 2) & 0x3F;
            if (major == 0x04) return DeviceKind.Headset;
            if (major == 0x05)
            {
                if ((minor & 0x0F) == 0x01 || (minor & 0x0F) == 0x02) return DeviceKind.Gamepad;
                if ((minor >> 4) == 1) return DeviceKind.Keyboard;
                if ((minor >> 4) == 2) return DeviceKind.Mouse;
                if ((minor >> 4) == 3) return DeviceKind.Keyboard; // klavye + işaretçi birleşik
            }
            return DeviceKind.Other;
        }

        public static DeviceKind GuessKind(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("mouse") || n.Contains("fare")) return DeviceKind.Mouse;
            if (n.Contains("keyboard") || n.Contains("klavye")) return DeviceKind.Keyboard;
            if (n.Contains("controller") || n.Contains("gamepad")) return DeviceKind.Gamepad;
            if (n.Contains("head") || n.Contains("kulakl") || n.Contains("buds") || n.Contains("airpods") || n.Contains("earbud")) return DeviceKind.Headset;
            return DeviceKind.Other;
        }

        public static List<DeviceInfo> Query()
        {
            List<DeviceInfo> result = new List<DeviceInfo>();
            Dictionary<string, MainNode> mains = new Dictionary<string, MainNode>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, BattNode> batts = new Dictionary<string, BattNode>(StringComparer.OrdinalIgnoreCase);

            string[] enumerators = { "BTHENUM", "BTHLE", "BTHLEDEVICE" };
            foreach (string en in enumerators)
            {
                IntPtr h = Native.SetupDiGetClassDevsEnum(IntPtr.Zero, en, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
                if (h == Native.INVALID_HANDLE || h == IntPtr.Zero) continue;
                try
                {
                    for (uint i = 0; ; i++)
                    {
                        SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
                        d.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                        if (!Native.SetupDiEnumDeviceInfo(h, i, ref d)) break;

                        StringBuilder sb = new StringBuilder(512);
                        uint need;
                        if (!Native.SetupDiGetDeviceInstanceId(h, ref d, sb, 511, out need)) continue;
                        string id = sb.ToString();
                        string cid = id;

                        uint t;
                        byte[] gb = GetProp(h, ref d, GContainer, 2, out t);
                        if (gb != null && t == TYPE_GUID && gb.Length >= 16) cid = new Guid(gb).ToString("B");

                        string name = GetString(h, ref d, GCommon, 14);
                        if (name.Length == 0) name = GetString(h, ref d, GCommon, 2);

                        bool isMain = id.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) ||
                                      id.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase);
                        if (isMain)
                        {
                            MainNode m;
                            if (!mains.TryGetValue(cid, out m)) { m = new MainNode(); mains[cid] = m; }
                            m.Name = name;
                            m.Id = id;
                            m.Mac = MacFromId(id);

                            byte[] cb = GetProp(h, ref d, GConnected, 15, out t);
                            if (cb != null && t == TYPE_BOOLEAN && cb.Length >= 1)
                            {
                                bool c = cb[0] != 0;
                                // Klasik ve LE düğümleri aynı container'ı paylaşabilir: biri bağlıysa bağlı say.
                                m.Conn = m.HasConn ? (m.Conn || c) : c;
                                m.HasConn = true;
                            }
                            byte[] cod = GetProp(h, ref d, GClassOfDevice, 10, out t);
                            if (cod != null && t == TYPE_UINT32 && cod.Length >= 4)
                            {
                                m.Cod = BitConverter.ToUInt32(cod, 0);
                                m.HasCod = true;
                            }
                        }

                        byte[] bb = GetProp(h, ref d, GBattery, 2, out t);
                        if (bb != null && t == TYPE_BYTE && bb.Length >= 1 && bb[0] <= 100)
                        {
                            BattNode b = new BattNode();
                            b.Pct = bb[0];
                            b.Name = name;
                            b.Id = id;
                            batts[cid] = b;
                        }
                    }
                }
                finally { Native.SetupDiDestroyDeviceInfoList(h); }
            }

            foreach (KeyValuePair<string, BattNode> kv in batts)
            {
                MainNode m;
                bool has = mains.TryGetValue(kv.Key, out m);
                DeviceInfo dev = new DeviceInfo();
                dev.Key = "bt:" + kv.Key;
                dev.Container = kv.Key;
                dev.Percent = kv.Value.Pct;
                dev.Via = "Bluetooth";
                dev.Source = "Windows Bluetooth";
                if (has && m.Name.Length > 0) dev.Name = m.Name;
                else dev.Name = kv.Value.Name.Length > 0 ? StripServiceSuffix(kv.Value.Name) : "Bluetooth device";
                dev.Mac = (has && m.Mac.Length > 0) ? m.Mac : MacFromId(kv.Value.Id);
                dev.Connected = (has && m.HasConn) ? m.Conn : true;
                dev.Kind = (has && m.HasCod) ? KindFromCod(m.Cod) : DeviceKind.Other;
                if (dev.Kind == DeviceKind.Other) dev.Kind = GuessKind(dev.Name);
                result.Add(dev);
            }

            // Bağlı ama Windows'a pil bilgisi vermeyen cihazlar (model + ses bilgisi için listelenir).
            foreach (KeyValuePair<string, MainNode> kv in mains)
            {
                if (batts.ContainsKey(kv.Key)) continue;
                MainNode m = kv.Value;
                if (!(m.HasConn && m.Conn) || m.Name.Length == 0) continue;
                DeviceInfo dev = new DeviceInfo();
                dev.Key = "bt:" + kv.Key;
                dev.Container = kv.Key;
                dev.Name = m.Name;
                dev.Via = "Bluetooth";
                dev.Source = "Windows Bluetooth";
                dev.Mac = m.Mac;
                dev.Connected = true;
                dev.Kind = m.HasCod ? KindFromCod(m.Cod) : DeviceKind.Other;
                if (dev.Kind == DeviceKind.Other) dev.Kind = GuessKind(dev.Name);
                result.Add(dev);
            }
            return result;
        }
    }
}
