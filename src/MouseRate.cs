using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace WirelessBattery
{
    // Fare girişlerinin saniyedeki rapor sayısını (Hz) CANLI ölçer. Ham girişi (Raw Input) arayüzden
    // bağımsız, kendi iş parçacığında ve yüksek öncelikle işler: panel çizimi / animasyon ölçümü
    // geciktirmez ve olay kaybı olmaz. Yalnızca panel açıkken çalışır.
    // Klavyede yalnızca olayların ZAMANI ölçülür; hangi tuşa basıldığı okunmaz / saklanmaz.
    class MouseRateMonitor : NativeWindow
    {
        public class Stat
        {
            public IntPtr Handle;
            public string Path = "";
            public int Vid = -1, Pid = -1;
            public double Hz;            // son ölçüm penceresi
            public double Peak;          // en yüksek ölçüm
            public DateTime Updated = DateTime.MinValue;
            public double WinStart, LastEvt;
            public int Count;

            public Stat Copy() { return (Stat)MemberwiseClone(); }
        }

        public class KStat
        {
            public IntPtr Handle;
            public int Vid = -1, Pid = -1;
            public double MinMs = double.MaxValue;
            public int Samples;
            public DateTime Updated = DateTime.MinValue;
            public double LastEvt = -1;

            public KStat Copy() { return (KStat)MemberwiseClone(); }
        }

        // Cihaz takılıp çıkarıldığında veya bilgisayar uykudan uyandığında tetiklenir (ayrı iş parçacığında!).
        public event EventHandler SystemChanged;

        const int WM_INPUT = 0x00FF;
        const uint RID_INPUT = 0x10000003;
        const uint RIDI_DEVICENAME = 0x20000007;
        const double WindowMs = 200.0;   // ölçüm penceresi
        const double GapMs = 30.0;       // bundan uzun sessizlik: fare durdu

        readonly Dictionary<IntPtr, Stat> stats = new Dictionary<IntPtr, Stat>();
        readonly Dictionary<IntPtr, KStat> kstats = new Dictionary<IntPtr, KStat>();
        readonly object sync = new object();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        volatile bool enabled;

        static readonly Regex RxA = new Regex(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.Compiled);
        static readonly Regex RxB = new Regex(@"VID&[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})_PID&([0-9A-Fa-f]{4})", RegexOptions.Compiled);

        public MouseRateMonitor()
        {
            Thread t = new Thread(new ThreadStart(Run));
            t.IsBackground = true;
            t.Name = "WirelessBattery.RawInput";
            t.Priority = ThreadPriority.AboveNormal;
            t.Start();
            ready.WaitOne(3000);
        }

        void Run()
        {
            try
            {
                CreateParams cp = new CreateParams();
                cp.Style = unchecked((int)0x80000000); // WS_POPUP (görünmez)
                cp.ExStyle = 0x80;                      // WS_EX_TOOLWINDOW
                cp.X = 0; cp.Y = 0; cp.Width = 0; cp.Height = 0;
                CreateHandle(cp);
            }
            catch (Exception) { }
            ready.Set();
            if (Handle != IntPtr.Zero) Application.Run(); // bu iş parçacığının kendi ileti döngüsü
        }

        public void Enable(bool on)
        {
            if (Handle == IntPtr.Zero) return;
            if (!on && !enabled) return;

            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[2];
            rid[0].usUsagePage = 1;
            rid[0].usUsage = 2; // fare
            rid[1].usUsagePage = 1;
            rid[1].usUsage = 6; // klavye (yalnızca zamanlama)
            for (int i = 0; i < 2; i++)
            {
                if (on)
                {
                    rid[i].dwFlags = 0x00000100; // RIDEV_INPUTSINK: pencere arka plandayken de al
                    rid[i].hwndTarget = Handle;
                }
                else
                {
                    rid[i].dwFlags = 0x00000001; // RIDEV_REMOVE
                    rid[i].hwndTarget = IntPtr.Zero;
                }
            }
            if (on)
            {
                lock (sync) { kstats.Clear(); }
            }
            if (Native.RegisterRawInputDevices(rid, 2, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                enabled = on;
        }

        public List<Stat> Snapshot()
        {
            List<Stat> r = new List<Stat>();
            lock (sync)
            {
                foreach (Stat s in stats.Values) r.Add(s.Copy());
            }
            return r;
        }

        public List<KStat> KeyboardSnapshot()
        {
            List<KStat> r = new List<KStat>();
            lock (sync)
            {
                foreach (KStat k in kstats.Values) r.Add(k.Copy());
            }
            return r;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                try { OnRawInput(m.LParam); } catch (Exception) { }
            }
            else if (m.Msg == 0x0219) // WM_DEVICECHANGE
            {
                if (m.WParam.ToInt64() == 0x0007) Raise(); // DBT_DEVNODES_CHANGED
            }
            else if (m.Msg == 0x0218) // WM_POWERBROADCAST
            {
                long w = m.WParam.ToInt64();
                if (w == 0x12 || w == 0x07) Raise(); // uykudan dönüş
            }
            base.WndProc(ref m);
        }

        void Raise()
        {
            EventHandler h = SystemChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        void OnRawInput(IntPtr hRaw)
        {
            int hs = Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint size = 0;
            if (Native.GetRawInputData(hRaw, RID_INPUT, IntPtr.Zero, ref size, (uint)hs) != 0 || size == 0 || size > 1024) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputData(hRaw, RID_INPUT, buf, ref size, (uint)hs) != size) return;
                double now = clock.Elapsed.TotalMilliseconds;
                int type = Marshal.ReadInt32(buf, 0);
                IntPtr dev = Marshal.ReadIntPtr(buf, 8);
                if (type == 1) { CountKey(dev, now); return; } // klavye: yalnızca zaman
                if (type != 0) return;                         // geri kalan: fare

                int flags = Marshal.ReadInt16(buf, hs) & 0xFFFF;
                if ((flags & 1) != 0) return; // mutlak konum (dokunmatik vb.)
                int dx = Marshal.ReadInt32(buf, hs + 12);
                int dy = Marshal.ReadInt32(buf, hs + 16);
                if (dx == 0 && dy == 0) return; // tuş / tekerlek olayı
                CountMouse(dev, now);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        void CountMouse(IntPtr dev, double now)
        {
            lock (sync)
            {
                Stat s;
                if (!stats.TryGetValue(dev, out s))
                {
                    s = new Stat();
                    s.Handle = dev;
                    s.Path = DevicePath(dev);
                    ParseIds(s.Path, out s.Vid, out s.Pid);
                    s.WinStart = now;
                    s.LastEvt = now;
                    stats[dev] = s;
                    return;
                }
                if (now - s.LastEvt > GapMs)
                {
                    // Fare durmuştu: yeni pencere başlat (boşluğu ortalamaya katma).
                    s.WinStart = now;
                    s.LastEvt = now;
                    s.Count = 0;
                    return;
                }
                s.LastEvt = now;
                s.Count++;
                double span = now - s.WinStart;
                if (span >= WindowMs)
                {
                    s.Hz = s.Count * 1000.0 / span;
                    if (s.Hz > s.Peak) s.Peak = s.Hz;
                    s.Updated = DateTime.Now;
                    s.WinStart = now;
                    s.Count = 0;
                }
            }
        }

        void CountKey(IntPtr dev, double now)
        {
            lock (sync)
            {
                KStat k;
                if (!kstats.TryGetValue(dev, out k))
                {
                    k = new KStat();
                    k.Handle = dev;
                    ParseIds(DevicePath(dev), out k.Vid, out k.Pid);
                    kstats[dev] = k;
                }
                if (k.LastEvt >= 0)
                {
                    double dt = now - k.LastEvt;
                    // 0.12 ms altı: aynı raporun ikiz olayları; 40 ms üstü: normal yazma aralığı.
                    if (dt >= 0.12 && dt <= 40.0)
                    {
                        k.Samples++;
                        if (dt < k.MinMs) k.MinMs = dt;
                        k.Updated = DateTime.Now;
                    }
                }
                k.LastEvt = now;
            }
        }

        static string DevicePath(IntPtr dev)
        {
            try
            {
                uint n = 0;
                Native.GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, IntPtr.Zero, ref n);
                if (n == 0 || n > 1024) return "";
                IntPtr p = Marshal.AllocHGlobal((int)n * 2 + 2);
                try
                {
                    if (Native.GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, p, ref n) == uint.MaxValue) return "";
                    return Marshal.PtrToStringUni(p) ?? "";
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch (Exception) { return ""; }
        }

        static void ParseIds(string path, out int vid, out int pid)
        {
            vid = -1;
            pid = -1;
            Match m = RxA.Match(path);
            if (!m.Success) m = RxB.Match(path);
            if (!m.Success) return;
            vid = Convert.ToInt32(m.Groups[1].Value, 16);
            pid = Convert.ToInt32(m.Groups[2].Value, 16);
        }

        // Ölçülen en yüksek değerden standart sınıfı bulur: bir aygıt kendi sınıfının üstüne çıkamaz,
        // bu yüzden en yüksek ölçümü karşılayan en küçük standart değer seçilir.
        public static int Classify(double peak)
        {
            int[] std = { 125, 250, 500, 1000, 2000, 4000, 8000 };
            foreach (int n in std)
                if (peak <= n * 1.04) return n;
            return 0;
        }

        // 1000 Hz sınıfı gibi yakın standart değeri bulur (klavye tahmini için gürültüyü yuvarlar).
        public static int Nominal(double hz)
        {
            int[] std = { 125, 250, 500, 1000, 2000, 4000, 8000 };
            foreach (int s in std)
                if (Math.Abs(hz - s) <= s * 0.12) return s;
            return 0;
        }
    }
}
