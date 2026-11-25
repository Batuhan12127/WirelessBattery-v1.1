using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace WirelessBattery
{
    // Üretici sürücüleri: yalnızca SALT-OKUNUR pil sorguları gönderir.
    // Protokol bilgisi: OpenRazer, qu1ncky/BatteryTray (Rongyuan), djnawara/mouse-gauge ve
    // Waturus/HaloBattery (WLMouse) projelerinin herkese açık dökümantasyonuna dayanır.

    // -------------------------------------------------------------------- Rongyuan (Attack Shark, Ajazz ...)
    // 2.4 GHz alıcılar. İstek: [00 F7 00 ...] (arayüz 2, FFFF:0002), cevap: [00][ok][klavye %][mouse %][klavye yok][mouse yok]
    static class RongyuanDriver
    {
        static readonly uint[] Receivers = new uint[] {
            0x0DB0197E, 0x0DB08E9A, 0x145F0376, 0x145F038D, 0x14A5C045, 0x25AA2008, 0x25AA4008, 0x25AA4018,
            0x31514011, 0x31514014, 0x31514017, 0x31514021, 0x31514027, 0x3151402D, 0x31514038, 0x31515006,
            0x31515007, 0x31515008, 0x31515020, 0x31515026, 0x3151502B, 0x31515031, 0x31515037, 0x31515038,
            0x3151503D, 0x3151503F, 0x31515040, 0x31515042, 0x31515045, 0x3151504B, 0x3151504E, 0x31515050,
            0x31515052, 0x31515055, 0x3151505A, 0x342DE3E0, 0x342DE3E1, 0x342DE3E2, 0x342DE3E6, 0x342DE3E7,
            0x342DE3E8, 0x342DE3F5, 0x342DE3F6, 0x342DE3F7, 0x342DE3FB, 0x342DE3FC, 0x342DE3FD, 0x347AA302,
            0x347AA305, 0x374AA205, 0x374AA217, 0x374AA220, 0x374AA223, 0x374AA226, 0x374AA229, 0x374AA234,
            0x374AA237, 0x374AA251, 0x374AA263, 0x374AA268, 0x374AA311, 0x374AA314, 0x374AA319, 0x374AA322,
            0x374AA329, 0x374AA355, 0x379A1804, 0x379A1807, 0x38A90002, 0x38EE0000, 0x38EE0002, 0x38EE0039,
            0x391D1A04, 0x391D1A08, 0x391D1A0E, 0x391D1A13, 0x391D1A25, 0x391D1A2B, 0x391D1A34, 0x391D2017,
            0x391D2A05
        };

        static string Brand(string product)
        {
            string[] skip = { "2.4G", "2.4GHZ", "8K", "4K", "2K", "1K", "RECEIVER", "WIRELESS", "DONGLE", "USB" };
            StringBuilder sb = new StringBuilder();
            foreach (string w in product.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Array.IndexOf(skip, w.ToUpperInvariant()) >= 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w);
            }
            return sb.ToString();
        }

        static byte[] QueryStatus(HidDev d)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) Thread.Sleep(100);
                byte[] cmd = new byte[d.Coll.FeatLen];
                cmd[1] = 0xF7;
                if (!d.SetFeature(cmd)) return null;
                Thread.Sleep(20);
                byte[] r = d.GetFeature(0);
                if (r != null && r.Length >= 7 && (r[2] | r[3] | r[4] | r[5] | r[6]) != 0) return r;
            }
            return null;
        }

        public static void Poll(PollContext ctx)
        {
            List<uint> products = new List<uint>();
            foreach (HidColl c in ctx.Colls)
            {
                uint key = PollContext.Key(c.Vid, c.Pid);
                if (Array.IndexOf(Receivers, key) >= 0 && !ctx.Claimed.Contains(key) && !products.Contains(key))
                    products.Add(key);
            }

            foreach (uint key in products)
            {
                int vid = (int)(key >> 16), pid = (int)(key & 0xFFFF);
                List<HidColl> cands = ctx.Find(vid, pid, delegate(HidColl c)
                {
                    return c.Iface == 2 && c.UsagePage == 0xFFFF && c.Usage == 0x0002 && c.FeatLen >= 7;
                });
                foreach (HidColl c in cands)
                {
                    using (HidDev d = new HidDev())
                    {
                        if (!d.OpenFeatureOnly(c)) continue;
                        byte[] r = QueryStatus(d);
                        if (r == null) continue;

                        string product = c.Product ?? "";
                        bool generic = product.Length == 0 || product.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) >= 0 || product.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0;
                        for (int slot = 0; slot < 2; slot++)
                        {
                            bool keyboard = slot == 0;
                            if (r[keyboard ? 4 : 5] != 0) continue; // bu yuvada cihaz yok
                            int level = r[keyboard ? 2 : 3];

                            string brand = Brand(product);
                            bool otherBrand = brand.Length > 0 && brand.ToUpperInvariant().IndexOf("ATTACK SHARK", StringComparison.Ordinal) < 0;
                            string name;
                            if (vid == 0x3151 && pid == 0x402D && !keyboard && !otherBrand) name = "Attack Shark R3";
                            else if (vid == 0x3151 && pid == 0x5038 && keyboard) name = "MonsGeek keyboard";
                            else if (generic && brand.Length > 0) name = brand + (keyboard ? " wireless keyboard" : " wireless mouse");
                            else if (generic) name = keyboard ? "Wireless keyboard" : "Wireless mouse";
                            else name = product;

                            DeviceInfo s = new DeviceInfo();
                            s.Key = "rongyuan:" + Util.Hex4(vid) + ":" + Util.Hex4(pid) + (keyboard ? ":kb" : ":ms");
                            s.Name = name;
                            s.Kind = keyboard ? DeviceKind.Keyboard : DeviceKind.Mouse;
                            s.Via = "2.4 GHz alıcı";
                            s.Source = "Rongyuan HID";
                            s.Product = product;
                            s.Vid = vid;
                            s.Pid = pid;
                            if (level >= 1 && level <= 100) s.Percent = level;
                            ctx.Out.Add(s);
                        }
                        ctx.Claim(vid, pid);
                    }
                    break;
                }
            }
        }
    }

    // -------------------------------------------------------------------- WLMouse (Beast X / Beast G ...)
    // Vendor koleksiyonu FFFF, 65 baytlık feature report 0:
    //   SET [00][00][00][02][02][00][83]...   GET -> [00][durum][00][02][02][00][83][şarj][pil%]   durum A1/A2 = cevap, A0 = uyuyor
    static class WlMouseDriver
    {
        const int Vid = 0x36A7;

        class Model
        {
            public int Pid;
            public bool Receiver;
            public string Name;
            public int ReceiverPid;
            public Model(int pid, bool receiver, string name, int receiverPid)
            {
                Pid = pid; Receiver = receiver; Name = name; ReceiverPid = receiverPid;
            }
        }

        // Kablolu modeller önce gelir: kablo cevap verirse alıcısını sahiplenir, mouse tek kez listelenir.
        static readonly Model[] Models = new Model[] {
            new Model(0xA879, false, "WLMouse Sword X 8K", 0xA878),
            new Model(0xA884, false, "WLMouse Beast X 8K", 0xA883),
            new Model(0xA861, false, "WLMouse Beast G", 0xA860),
            new Model(0xA866, false, "WLMouse mouse", 0),
            new Model(0xA867, false, "WLMouse mouse", 0),
            new Model(0xA871, false, "WLMouse Beast X Pro", 0xA870),
            new Model(0xA856, true, "WLMouse Beast X V2", 0),
            new Model(0xA860, true, "WLMouse Beast G", 0),
            new Model(0xA868, true, "WLMouse Beast X Mini Pro", 0),
            new Model(0xA870, true, "WLMouse Beast X Pro", 0),
            new Model(0xA878, true, "WLMouse Sword X 8K", 0),
            new Model(0xA880, true, "WLMouse Beast MAX", 0),
            new Model(0xA883, true, "WLMouse Beast X 8K", 0),
            new Model(0xA885, true, "WLMouse Beast X Mini", 0)
        };

        // 0: pil, 1: uyuyor, 2: cevap yok
        static int Query(HidDev d, out int pct, out bool charging)
        {
            pct = -1;
            charging = false;
            byte[] req = new byte[65];
            req[3] = 0x02;
            req[4] = 0x02;
            req[6] = 0x83;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!d.SetFeature(req)) return 2;
                for (int read = 0; read < 6; read++)
                {
                    Thread.Sleep(60);
                    byte[] r = d.GetFeature(0);
                    if (r == null || r.Length < 9) return 2;
                    if (r[3] != 0x02 || r[4] != 0x02 || r[6] != 0x83) continue;
                    if (r[1] == 0xA0) return 1;
                    if (r[1] != 0xA1 && r[1] != 0xA2) continue;
                    if (r[8] > 100) return 2;
                    pct = r[8];
                    charging = r[7] == 0x01;
                    return 0;
                }
            }
            return 2;
        }

        public static void Poll(PollContext ctx)
        {
            foreach (Model m in Models)
            {
                if (ctx.IsClaimed(Vid, m.Pid)) continue;
                List<HidColl> colls = ctx.Find(Vid, m.Pid, delegate(HidColl c) { return c.UsagePage == 0xFFFF && c.FeatLen >= 65; });
                colls.Sort(delegate(HidColl a, HidColl b) { return (b.Usage == 0 ? 1 : 0) - (a.Usage == 0 ? 1 : 0); });
                foreach (HidColl c in colls)
                {
                    using (HidDev d = new HidDev())
                    {
                        if (!d.OpenFeatureOnly(c)) continue;
                        int pct;
                        bool charging;
                        int rep = Query(d, out pct, out charging);
                        if (rep == 2) continue;

                        DeviceInfo s = new DeviceInfo();
                        s.Key = "wlmouse:" + Util.Hex4(m.Pid);
                        s.Name = m.Name;
                        s.Kind = DeviceKind.Mouse;
                        s.Via = m.Receiver ? "2.4 GHz alıcı" : "USB kablo";
                        s.Source = "WLMouse HID";
                        s.Product = c.Product ?? "";
                        s.Vid = Vid;
                        s.Pid = m.Pid;
                        if (rep == 1) s.Asleep = true;
                        else { s.Percent = pct; s.Charging = charging; }
                        ctx.Out.Add(s);
                        ctx.Claim(Vid, m.Pid);
                        if (m.ReceiverPid != 0) ctx.Claim(Vid, m.ReceiverPid);
                    }
                    break;
                }
            }
        }
    }

    // -------------------------------------------------------------------- Razer (yalnızca mouse'lar)
    // 90 baytlık feature report. Pil: sınıf 07 komut 80 -> args[1] (0..255). Şarj: sınıf 07 komut 84 -> args[1] == 1.
    static class RazerDriver
    {
        const int Vid = 0x1532;

        static readonly int[] MousePids = new int[] {
            0x0013, 0x0015, 0x0016, 0x001F, 0x0020, 0x0024, 0x0025, 0x0029,
            0x002E, 0x002F, 0x0032, 0x0034, 0x0036, 0x0037, 0x0038, 0x0039,
            0x003E, 0x003F, 0x0040, 0x0041, 0x0042, 0x0043, 0x0044, 0x0045,
            0x0046, 0x0048, 0x004C, 0x004F, 0x0050, 0x0053, 0x0054, 0x0059,
            0x005A, 0x005B, 0x005C, 0x005E, 0x005F, 0x0060, 0x0062, 0x0064,
            0x0065, 0x0067, 0x006A, 0x006B, 0x006C, 0x006E, 0x006F, 0x0070,
            0x0071, 0x0072, 0x0073, 0x0077, 0x0078, 0x007A, 0x007B, 0x007C,
            0x007D, 0x0080, 0x0083, 0x0084, 0x0085, 0x0086, 0x0088, 0x008A,
            0x008C, 0x008D, 0x008F, 0x0090, 0x0091, 0x0094, 0x0095, 0x0096,
            0x0098, 0x0099, 0x009A, 0x009C, 0x009E, 0x009F, 0x00A1, 0x00A3,
            0x00A5, 0x00A6, 0x00A7, 0x00A8, 0x00AA, 0x00AB, 0x00AF, 0x00B0,
            0x00B2, 0x00B3, 0x00B4, 0x00B6, 0x00B7, 0x00B8, 0x00B9, 0x00BE,
            0x00BF, 0x00C0, 0x00C1, 0x00C2, 0x00C3, 0x00C4, 0x00C5, 0x00C7,
            0x00C8, 0x00CB, 0x00CC, 0x00CD, 0x00D0, 0x00D1, 0x00D3, 0x00D4,
            0x00D6, 0x00D7, 0x00DA, 0x00DB, 0x00DE, 0x00DF, 0x00E0, 0x00E5,
            0x00E6
        };

        // 0: tamam, 1: çevrimdışı, 2: desteklenmiyor, 3: cevap yok
        static int Command(HidDev d, byte tid, byte cls, byte id, byte size, out byte[] args)
        {
            args = null;
            byte[] rep = new byte[91]; // [0] = report id 0, sonra 90 baytlık rapor
            rep[2] = tid;
            rep[6] = size;
            rep[7] = cls;
            rep[8] = id;
            byte crc = 0;
            for (int i = 3; i < 89; i++) crc ^= rep[i];
            rep[89] = crc;

            bool send = true;
            int res = 3;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (send && !d.SetFeature(rep)) return 3;
                Thread.Sleep(attempt == 0 ? 15 : 40);
                send = true;
                res = 3;
                byte[] resp = d.GetFeature(0);
                if (resp == null || resp.Length < 91) continue;
                byte replyCrc = 0;
                for (int i = 3; i < 89; i++) replyCrc ^= resp[i];
                // Synapse aynı posta kutusunu paylaşır: farklı bir cevap gelirse yeniden istek gerekir.
                if (resp[2] != tid || resp[3] != 0 || resp[4] != 0 || resp[5] != 0 || resp[6] != size ||
                    resp[7] != cls || resp[8] != id || resp[89] != replyCrc) continue;
                if (resp[1] == 0x01) { send = false; continue; } // meşgul
                if (resp[1] == 0x02)
                {
                    args = new byte[80];
                    Array.Copy(resp, 9, args, 0, 80);
                    return 0;
                }
                if (resp[1] == 0x04) { res = 1; continue; }
                if (resp[1] == 0x05 || resp[1] == 0x03) return 2;
            }
            return res;
        }

        static string CleanName(string n)
        {
            string[] suffixes = { " Dongle", " (Wired)", " (Wireless)", " Wireless Receiver", " Receiver", " HyperSpeed Dongle" };
            foreach (string s in suffixes)
                if (n.Length > s.Length && n.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    n = n.Substring(0, n.Length - s.Length);
            if (n.Length == 0) n = "Razer mouse";
            return n;
        }

        public static void Poll(PollContext ctx)
        {
            List<string> seen = new List<string>();
            foreach (int pid in ctx.Pids(Vid))
            {
                if (Array.IndexOf(MousePids, pid) < 0 || ctx.IsClaimed(Vid, pid)) continue;
                List<HidColl> cands = ctx.Find(Vid, pid, delegate(HidColl c) { return c.FeatLen == 91; });
                foreach (HidColl c in cands)
                {
                    using (HidDev d = new HidDev())
                    {
                        if (!d.OpenFeatureOnly(c)) continue;
                        int res = 3;
                        byte tid = 0;
                        byte[] args = null;
                        bool sawOffline = false;
                        byte[] tids = { 0x1F, 0x3F, 0xFF, 0x08, 0x9F };
                        foreach (byte t in tids)
                        {
                            int r = Command(d, t, 0x07, 0x80, 0x02, out args);
                            if (r == 0) { res = 0; tid = t; break; }
                            if (r == 1) sawOffline = true;
                        }
                        if (res != 0 && sawOffline) res = 1;
                        if (res != 0 && res != 1) continue;

                        DeviceInfo s = new DeviceInfo();
                        s.Key = "razer:" + Util.Hex4(pid);
                        s.Name = CleanName(c.Product ?? "");
                        s.Kind = DeviceKind.Mouse;
                        s.Via = "USB / 2.4 GHz alıcı";
                        s.Source = "Razer HID";
                        s.Product = c.Product ?? "";
                        s.Vid = Vid;
                        s.Pid = pid;
                        if (res == 1) s.Connected = false;
                        else
                        {
                            s.Percent = Math.Min(100, (args[1] * 100 + 127) / 255);
                            byte[] ch;
                            if (Command(d, tid, 0x07, 0x84, 0x02, out ch) == 0 && ch != null) s.Charging = ch[1] == 1;
                        }

                        // Şarj için takılı mouse hem kablo hem alıcı olarak görünür: bir kez listele.
                        if (seen.Contains(s.Name))
                        {
                            ctx.Claim(Vid, pid);
                            foreach (DeviceInfo o in ctx.Out)
                                if (o.Name == s.Name && s.Charging) o.Charging = true;
                        }
                        else
                        {
                            seen.Add(s.Name);
                            ctx.Out.Add(s);
                            ctx.Claim(Vid, pid);
                        }
                    }
                    break;
                }
            }
        }
    }

    // -------------------------------------------------------------------- DriverCore ailesi (LAMZU, Arbiter Studio ...)
    // Üretici kimlikleri 373E ve 37B0. Vendor koleksiyonu, 65 baytlık feature report 0:
    //   SET [00][00][00][02][02][00][83]...   ~100 ms sonra GET -> [00][durum][..][..][02][..][83][şarj][pil%]
    //   (report id baytı olmadan da gelebilir). durum A1/A2 = cevap, A0 = mouse uyuyor; alıcıdan %0 = mouse kapalı/bağlı değil.
    static class DriverCoreDriver
    {
        static readonly Dictionary<uint, string> Names = BuildNames();

        static void Add(Dictionary<uint, string> d, int vid, string name, params int[] pids)
        {
            foreach (int p in pids) d[PollContext.Key(vid, p)] = name;
        }

        static Dictionary<uint, string> BuildNames()
        {
            Dictionary<uint, string> d = new Dictionary<uint, string>();
            Add(d, 0x373E, "Arbiter Studio Akitsu", 0x0009, 0x000A);
            Add(d, 0x373E, "Arbiter Studio Akitsu 3950", 0x0038, 0x0039);
            Add(d, 0x373E, "Arbiter Studio Akitsu Medium", 0x0066, 0x0067);
            Add(d, 0x373E, "LAMZU Maya X", 0x001C, 0x001D, 0x001E);
            Add(d, 0x373E, "LAMZU Maya X V2", 0x0016);
            Add(d, 0x37B0, "LAMZU Maya X V2", 0x001F);
            Add(d, 0x37B0, "LAMZU Tachi", 0x0005, 0x000B, 0x000C);
            Add(d, 0x37B0, "LAMZU Tachi Lite", 0x001C, 0x001E);
            Add(d, 0x37B0, "LAMZU Inca", 0x0009, 0x000F, 0x0010);
            Add(d, 0x37B0, "LAMZU Maya", 0x0011, 0x0013, 0x0015);
            Add(d, 0x37B0, "LAMZU Paro", 0x0007, 0x000D, 0x000E);
            Add(d, 0x37B0, "LAMZU Thorn", 0x0017, 0x0019, 0x001B);
            Add(d, 0x37B0, "LAMZU Thorn V2", 0x0021, 0x0003, 0x0023, 0x0030, 0x0032, 0x0040);
            Add(d, 0x37B0, "LAMZU Atlantis OG Champion", 0x0025, 0x0027);
            Add(d, 0x37B0, "LAMZU Atlantis Mini", 0x0028, 0x002B, 0x002A, 0x003E, 0x003F);
            Add(d, 0x37B0, "LAMZU Maya M", 0x002C, 0x0052);
            Add(d, 0x37B0, "LAMZU Maya X", 0x003A, 0x003B, 0x0044);
            Add(d, 0x37B0, "LAMZU Maya", 0x003C, 0x0050);
            Add(d, 0x37B0, "LAMZU Orcus", 0x0042, 0x0056);
            Add(d, 0x37B0, "LAMZU Mini", 0x0046, 0x0054);
            return d;
        }

        static bool IsCable(int vid, int pid)
        {
            if (vid == 0x373E) return pid == 0x001C || pid == 0x0009 || pid == 0x0038 || pid == 0x0066;
            int[] cables = { 0x0005, 0x0007, 0x0009, 0x0011, 0x0017, 0x001C, 0x001F, 0x0021, 0x0025, 0x0028, 0x002C, 0x0030, 0x0034, 0x0038,
                             0x003A, 0x003C, 0x003E, 0x0040, 0x0042, 0x0044, 0x0046, 0x0050, 0x0052, 0x0056 };
            return Array.IndexOf(cables, pid) >= 0;
        }

        public static void Poll(PollContext ctx)
        {
            int[] vendors = { 0x373E, 0x37B0 };
            foreach (int vendor in vendors)
            {
                foreach (int pid in ctx.Pids(vendor))
                {
                    if (ctx.IsClaimed(vendor, pid)) continue;
                    List<HidColl> cands = ctx.Find(vendor, pid, delegate(HidColl c) { return c.FeatLen == 65; });
                    foreach (HidColl c in cands)
                    {
                        using (HidDev d = new HidDev())
                        {
                            if (!d.OpenFeatureOnly(c)) continue;
                            byte[] req = new byte[65];
                            req[3] = 0x02;
                            req[4] = 0x02;
                            req[6] = 0x83;
                            if (!d.SetFeature(req)) continue;
                            Thread.Sleep(100);
                            byte[] r = d.GetFeature(0);
                            if (r == null || r.Length < 10) continue;

                            int status, chg, pct;
                            if (r[4] == 0x02 && r[6] == 0x83) { status = r[1]; chg = r[7]; pct = r[8]; }
                            else if (r[3] == 0x02 && r[5] == 0x83) { status = r[0]; chg = r[6]; pct = r[7]; }
                            else continue;
                            if (status != 0xA0 && status != 0xA1 && status != 0xA2) continue;
                            if (pct > 100) continue;

                            string name;
                            if (!Names.TryGetValue(PollContext.Key(vendor, pid), out name))
                                name = (c.Product != null && c.Product.Length > 0) ? c.Product : "Wireless mouse";
                            bool cable = IsCable(vendor, pid);

                            DeviceInfo s = new DeviceInfo();
                            s.Key = "drivercore:" + Util.Hex4(vendor) + ":" + Util.Hex4(pid);
                            s.Name = name;
                            s.Kind = DeviceKind.Mouse;
                            s.Via = cable ? "USB kablo" : "2.4 GHz alıcı";
                            s.Source = "DriverCore HID (LAMZU / Arbiter)";
                            s.Product = c.Product ?? "";
                            s.Vid = vendor;
                            s.Pid = pid;
                            if (status == 0xA0) s.Asleep = true;
                            else
                            {
                                s.Connected = pct > 0 || cable;
                                if (pct > 0) s.Percent = pct;
                                s.Charging = chg == 1;
                            }
                            ctx.Out.Add(s);
                            ctx.Claim(vendor, pid);
                        }
                        break;
                    }
                }
            }
        }
    }
}
