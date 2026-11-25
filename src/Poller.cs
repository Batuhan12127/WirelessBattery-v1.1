using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace WirelessBattery
{
    // Arka planda cihazları tarar. Bluetooth + ses bilgisi hızlıdır (detay penceresi açıkken 2 sn,
    // kapalıyken 15 sn). Üretici (HID) sorguları cihazı rahatsız etmesin diye daha seyrek yapılır
    // (pencere açıkken 10 sn, kapalıyken 30 sn).
    class Poller
    {
        readonly Control ui;
        readonly Action<List<DeviceInfo>> onData;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread thread;
        volatile bool stop;
        volatile bool fast;
        volatile bool forceHid;
        DateTime nextHid = DateTime.MinValue;
        List<DeviceInfo> hidCache = new List<DeviceInfo>();
        readonly Dictionary<string, int> lastPct = new Dictionary<string, int>();
        readonly Dictionary<string, DateTime> lastPctTime = new Dictionary<string, DateTime>();

        public Poller(Control ui, Action<List<DeviceInfo>> onData)
        {
            this.ui = ui;
            this.onData = onData;
        }

        public void Start()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "WirelessBattery.Poller";
            thread.Start();
        }

        public void Stop()
        {
            stop = true;
            wake.Set();
        }

        public void SetFast(bool f)
        {
            fast = f;
            wake.Set();
        }

        public void RefreshNow()
        {
            forceHid = true;
            wake.Set();
        }

        void Loop()
        {
            while (!stop)
            {
                List<DeviceInfo> res = null;
                try { res = Collect(); }
                catch (Exception) { }
                if (res != null && !stop)
                {
                    try
                    {
                        if (ui.IsHandleCreated && !ui.IsDisposed) ui.BeginInvoke(onData, new object[] { res });
                    }
                    catch (Exception) { }
                }
                wake.WaitOne(fast ? 2000 : 30000);
            }
        }

        public static List<DeviceInfo> QueryHid()
        {
            PollContext ctx = new PollContext(HidLayer.Enumerate());
            try { WlMouseDriver.Poll(ctx); } catch (Exception) { }
            try { DriverCoreDriver.Poll(ctx); } catch (Exception) { }
            try { RongyuanDriver.Poll(ctx); } catch (Exception) { }
            try { RazerDriver.Poll(ctx); } catch (Exception) { }
            try { AddGenericMice(ctx); } catch (Exception) { }
            try { AddKeyboards(ctx); } catch (Exception) { }
            try { AttachUsbRates(ctx); } catch (Exception) { }
            return ctx.Out;
        }

        // Aygıtın USB'de bildirdiği tarama hızını (bInterval) ekler: klavye ve mouse'lar için, anında ve doğru.
        static void AttachUsbRates(PollContext ctx)
        {
            foreach (DeviceInfo d in ctx.Out)
            {
                if (d.Vid == 0 || (d.Kind != DeviceKind.Mouse && d.Kind != DeviceKind.Keyboard)) continue;
                HidColl pick = null;
                foreach (HidColl c in ctx.Colls)
                {
                    if (c.Vid == d.Vid && c.Pid == d.Pid && c.DevInst != 0) { pick = c; break; }
                }
                if (pick == null) continue;
                UsbRate r = UsbInfo.Get(pick.DevInst, PollContext.Key(d.Vid, d.Pid));
                if (r == null) continue;
                d.UsbHz = r.Hz;
                d.UsbInterval = r.Interval;
                d.UsbSpeed = r.Speed;
            }
        }

        // HID tanımlayıcılarından klavyenin özelliklerini çıkarır (tahminî; uzunluklar rapor boyutlarından gelir).
        static List<string[]> KeyboardFacts(PollContext ctx, int vid, int pid)
        {
            List<string[]> f = new List<string[]>();
            List<int> ifs = new List<int>();
            int kbLen = 0;
            bool consumer = false, mouse = false, vendor = false;
            string man = "";
            foreach (HidColl c in ctx.Colls)
            {
                if (c.Vid != vid || c.Pid != pid) continue;
                if (!ifs.Contains(c.Iface)) ifs.Add(c.Iface);
                if (c.UsagePage == 0x01 && c.Usage == 0x06) kbLen = Math.Max(kbLen, c.InLen);
                if (c.UsagePage == 0x0C) consumer = true;
                if (c.UsagePage == 0x01 && c.Usage == 0x02) mouse = true;
                if (c.UsagePage >= 0xFF00) vendor = true;
                if (!string.IsNullOrEmpty(c.Manufacturer)) man = c.Manufacturer;
            }
            if (man.Length > 0) f.Add(new string[] { "Üretici", man });
            f.Add(new string[] { "HID arayüzü sayısı", ifs.Count.ToString() });
            if (kbLen > 0)
            {
                if (kbLen <= 9) f.Add(new string[] { "Tuş rollover (tahmin)", "6KRO: aynı anda en çok 6 tuş (standart rapor)" });
                else f.Add(new string[] { "Tuş rollover (tahmin)", "NKRO olasılığı yüksek: genişletilmiş rapor ({0} bayt)", kbLen.ToString() });
            }
            f.Add(new string[] { "Medya tuşları", consumer ? "Var (ses, oynat/durdur vb.)" : "Ayrı medya arayüzü yok" });
            if (mouse) f.Add(new string[] { "Fare işlevi", "Var (imleç/tıklama gönderebilir)" });
            if (vendor) f.Add(new string[] { "Üretici arayüzü", "Var (RGB, makro, ayar yazılımı bu yoldan çalışır)" });
            return f;
        }

        // Bluetooth dışı (USB / alıcı) klavyeleri listele; Bluetooth klavyeler Windows'un pil bilgisi yoluyla gelir.
        static void AddKeyboards(PollContext ctx)
        {
            List<uint> seen = new List<uint>();
            foreach (HidColl c in ctx.Colls)
            {
                if (c.UsagePage != 0x01 || c.Usage != 0x06) continue;
                string pl = c.Path.ToLowerInvariant();
                if (pl.IndexOf("vid_", StringComparison.Ordinal) < 0) continue; // dahili / Bluetooth klavyeleri atla
                string prod = (c.Product ?? "").Trim();
                string low = prod.ToLowerInvariant();
                if (low.Contains("mouse") || low.Contains("fare")) continue;
                uint key = PollContext.Key(c.Vid, c.Pid);
                if (seen.Contains(key)) continue;
                seen.Add(key);

                bool isMouse = false;
                DeviceInfo existing = null;
                foreach (DeviceInfo o in ctx.Out)
                {
                    if (o.Vid != c.Vid || o.Pid != c.Pid) continue;
                    if (o.Kind == DeviceKind.Mouse) isMouse = true;
                    if (o.Kind == DeviceKind.Keyboard) existing = o;
                }
                if (isMouse) continue;

                List<string[]> facts = KeyboardFacts(ctx, c.Vid, c.Pid);
                if (existing != null) { existing.Facts = facts; continue; } // pil sürücüsü zaten listeledi: özellikleri ekle

                DeviceInfo d = new DeviceInfo();
                d.Key = "hidkb:" + Util.Hex4(c.Vid) + ":" + Util.Hex4(c.Pid);
                d.Kind = DeviceKind.Keyboard;
                d.Name = prod.Length > 0 ? prod : "Keyboard";
                d.Product = prod;
                d.Via = "USB / 2.4 GHz alıcı";
                d.Source = "Windows HID";
                d.Vid = c.Vid;
                d.Pid = c.Pid;
                d.Facts = facts;
                ctx.Out.Add(d);
            }
        }

        // Hiçbir sürücünün sahiplenmediği USB / alıcı fareleri de listele: model adı (HID ürün dizesi) ve canlı Hz için.
        static void AddGenericMice(PollContext ctx)
        {
            List<uint> seen = new List<uint>();
            // Klavye koleksiyonu da olan aygıtlar (klavyeler, makrolu cihazlar) fare olarak listelenmez.
            List<uint> hasKeyboard = new List<uint>();
            foreach (HidColl k in ctx.Colls)
                if (k.UsagePage == 0x01 && k.Usage == 0x06) hasKeyboard.Add(PollContext.Key(k.Vid, k.Pid));
            foreach (HidColl c in ctx.Colls)
            {
                if (c.UsagePage != 0x01 || c.Usage != 0x02) continue;
                string pl = c.Path.ToLowerInvariant();
                if (pl.IndexOf("vid_", StringComparison.Ordinal) < 0) continue; // dahili dokunmatik yüzey vb. ve Bluetooth'u atla
                string prod = (c.Product ?? "").Trim();
                string low = prod.ToLowerInvariant();
                if (low.Contains("touchpad") || low.Contains("trackpad") || low.Contains("keyboard") || low.Contains("klavye")) continue;
                // Hem klavye hem fare arayüzü olan aygıt, adında "mouse" geçmiyorsa klavye sayılır.
                if (hasKeyboard.Contains(PollContext.Key(c.Vid, c.Pid)) && !(low.Contains("mouse") || low.Contains("fare"))) continue;
                uint key = PollContext.Key(c.Vid, c.Pid);
                if (ctx.Claimed.Contains(key) || seen.Contains(key)) continue;
                seen.Add(key);

                DeviceInfo d = new DeviceInfo();
                d.Key = "hidmouse:" + Util.Hex4(c.Vid) + ":" + Util.Hex4(c.Pid);
                d.Kind = DeviceKind.Mouse;
                d.Name = prod.Length > 0 ? prod : ((c.Manufacturer ?? "").Trim().Length > 0 ? c.Manufacturer.Trim() + " mouse" : "Mouse");
                d.Product = prod;
                d.Via = "USB / 2.4 GHz alıcı";
                d.Source = "Windows HID";
                d.Vid = c.Vid;
                d.Pid = c.Pid;
                ctx.Out.Add(d);
            }
        }

        static bool Contains(string hay, string needle)
        {
            return needle.Length >= 3 && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool LooksLikeHeadset(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("headset") || n.Contains("headphone") || n.Contains("kulakl") || n.Contains("earphone") ||
                   n.Contains("earbud") || n.Contains("buds") || n.Contains("airpods");
        }

        // Ses uç noktalarını cihazlara bağlar: önce container kimliği, olmazsa ad eşleşmesi.
        public static void AttachAudio(List<DeviceInfo> devices, List<AudioEndpoint> endpoints)
        {
            foreach (DeviceInfo d in devices)
            {
                foreach (AudioEndpoint a in endpoints)
                {
                    bool match = d.Container.Length > 0 && a.Container.Length > 0 &&
                                 string.Equals(d.Container, a.Container, StringComparison.OrdinalIgnoreCase);
                    if (!match) match = Contains(a.Name, d.Name) || (d.Product.Length > 0 && Contains(a.Name, d.Product));
                    if (match)
                    {
                        d.Audio.Add(a);
                        a.Used = true;
                    }
                }
                if (d.Audio.Count > 0 && d.Kind == DeviceKind.Other) d.Kind = DeviceKind.Headset;
            }

            // Pil bilgisi olmayan ama Bluetooth/kulaklık gibi görünen ses aygıtlarını da (model + kHz için) listele.
            Dictionary<string, DeviceInfo> extra = new Dictionary<string, DeviceInfo>();
            foreach (AudioEndpoint a in endpoints)
            {
                if (a.Used) continue;
                bool bt = a.Bus.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
                if (!bt && !LooksLikeHeadset(a.Name)) continue;
                string key = a.Container.Length > 0 ? a.Container : a.Name;
                DeviceInfo d;
                if (!extra.TryGetValue(key, out d))
                {
                    d = new DeviceInfo();
                    d.Key = "audio:" + key;
                    d.Container = a.Container;
                    d.Kind = DeviceKind.Headset;
                    d.Via = bt ? "Bluetooth" : "USB / kablolu";
                    d.Source = "Windows Audio";
                    d.Name = CleanEndpointName(a.Name);
                    extra[key] = d;
                    devices.Add(d);
                }
                d.Audio.Add(a);
                a.Used = true;
            }

            // Hiçbir cihaza eşlenemeyen tüm aktif ses aygıtları: kulaklığın hangisi olduğundan bağımsız kHz görünsün.
            List<AudioEndpoint> rest = new List<AudioEndpoint>();
            foreach (AudioEndpoint a in endpoints)
                if (!a.Used) rest.Add(a);
            if (rest.Count > 0)
            {
                DeviceInfo card = new DeviceInfo();
                card.Key = "audio-others";
                card.Name = "Ses aygıtları";
                card.AudioOnlyCard = true;
                card.Connected = true;
                card.Audio.AddRange(rest);
                devices.Add(card);
            }
        }

        // "Kulaklık (WH-1000XM4 Hands-Free)" -> "WH-1000XM4"
        static string CleanEndpointName(string n)
        {
            int a = n.IndexOf('(');
            int b = n.LastIndexOf(')');
            if (a >= 0 && b > a) n = n.Substring(a + 1, b - a - 1);
            string[] suf = { " Hands-Free", " Stereo", " Hands Free" };
            foreach (string s in suf)
                if (n.EndsWith(s, StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - s.Length);
            n = n.Trim();
            return n.Length > 0 ? n : "Audio device";
        }

        List<DeviceInfo> Collect()
        {
            if (forceHid || DateTime.UtcNow >= nextHid)
            {
                forceHid = false;
                nextHid = DateTime.UtcNow.AddSeconds(fast ? 10 : 60);
                try { hidCache = QueryHid(); }
                catch (Exception) { }
            }

            List<DeviceInfo> all = new List<DeviceInfo>();
            foreach (DeviceInfo h in hidCache) all.Add(h.Clone());

            List<DeviceInfo> bt = new List<DeviceInfo>();
            try { bt = BluetoothProvider.Query(); }
            catch (Exception) { }
            foreach (DeviceInfo b in bt)
            {
                bool dup = false;
                foreach (DeviceInfo h in all)
                    if (string.Equals(h.Name, b.Name, StringComparison.OrdinalIgnoreCase)) dup = true;
                if (!dup) all.Add(b);
            }

            List<AudioEndpoint> au = new List<AudioEndpoint>();
            try { au = AudioProvider.Query(); }
            catch (Exception) { }
            AttachAudio(all, au);

            // Uyuyan cihazlar için son bilinen değeri 30 dakika göster.
            DateTime now = DateTime.UtcNow;
            foreach (DeviceInfo d in all)
            {
                if (d.Percent >= 0 && d.Connected && !d.Asleep)
                {
                    lastPct[d.Key] = d.Percent;
                    lastPctTime[d.Key] = now;
                }
                else if (d.Percent < 0 && lastPct.ContainsKey(d.Key) && (now - lastPctTime[d.Key]).TotalMinutes < 30)
                {
                    d.Percent = lastPct[d.Key];
                    d.PercentIsStale = true;
                }
            }

            // Pil vermeyen, türü belli olmayan ve sesi olmayan Bluetooth cihazlarını (telefon vb.) gizle.
            all.RemoveAll(delegate(DeviceInfo d)
            {
                return !d.AudioOnlyCard && d.Percent < 0 && d.Via == "Bluetooth" && d.Kind == DeviceKind.Other && d.Audio.Count == 0;
            });

            all.Sort(delegate(DeviceInfo x, DeviceInfo y)
            {
                if (x.Connected != y.Connected) return x.Connected ? -1 : 1;
                int kx = x.AudioOnlyCard ? 9 : KindOrder(x.Kind), ky = y.AudioOnlyCard ? 9 : KindOrder(y.Kind);
                if (kx != ky) return kx - ky;
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });
            return all;
        }

        static int KindOrder(DeviceKind k)
        {
            switch (k)
            {
                case DeviceKind.Headset: return 0;
                case DeviceKind.Mouse: return 1;
                case DeviceKind.Keyboard: return 2;
                case DeviceKind.Gamepad: return 3;
                default: return 4;
            }
        }
    }
}
