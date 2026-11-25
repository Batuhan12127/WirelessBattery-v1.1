using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace WirelessBattery
{
    // Göstergeye tıklayınca açılan detay paneli. Açıkken canlı güncellenir. Kartların başlığına
    // tıklayınca kart açılıp kapanır, içerik uzunsa fare tekerleğiyle kaydırılır, en altta dil seçilir.
    class PopupForm : Form
    {
        class Theme
        {
            public Color Bg, Card, Border, Text, Dim, Track, Accent;
        }

        class Row
        {
            public string Key, Value;
            public bool Live;
            public Row(string k, string v) { Key = k; Value = v; }
        }

        readonly MouseRateMonitor rate;
        List<DeviceInfo> devices = new List<DeviceInfo>();
        readonly Timer tick = new Timer();
        readonly Timer anim = new Timer();
        readonly Timer scrollTimer = new Timer();
        readonly Timer cardTimer = new Timer();
        Theme th;
        float s = 1f;
        bool anchorBottom = true;
        Font fTitle, fBody, fBig, fIcon, fSmall, fHead, fIconS;
        DateTime lastLayout = DateTime.MinValue;

        // açılış / kapanış animasyonu
        bool animating, closing;
        int animFrom, animTo, closeFrom, closeTo;
        DateTime animStart, closeStart;

        // kaydırma
        float scrollY, scrollTarget, contentH;

        // tıklanabilir alanlar (içerik koordinatlarında)
        RectangleF themeBtn = RectangleF.Empty, langBtn = RectangleF.Empty;
        readonly List<KeyValuePair<RectangleF, string>> headerHits = new List<KeyValuePair<RectangleF, string>>();
        readonly List<KeyValuePair<RectangleF, int>> chipHits = new List<KeyValuePair<RectangleF, int>>();
        bool langOpen;

        // kart açma / kapama durumu
        readonly Dictionary<string, float> cardAmt = new Dictionary<string, float>();
        readonly Dictionary<string, bool> cardCollapsed = new Dictionary<string, bool>();

        public int LastHideTick;
        public event EventHandler PopupOpened;
        public event EventHandler PopupClosed;
        public event EventHandler LanguageChanged;

        public PopupForm(MouseRateMonitor rate)
        {
            this.rate = rate;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Width = 420;
            Height = 200;
            tick.Interval = 100;
            tick.Tick += delegate { OnTick(); };
            anim.Interval = 12;
            anim.Tick += delegate { OnAnim(); };
            scrollTimer.Interval = 15;
            scrollTimer.Tick += delegate { OnScrollTick(); };
            cardTimer.Interval = 15;
            cardTimer.Tick += delegate { OnCardTick(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW (Alt+Tab'da görünmez)
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = 2; // DWMWCP_ROUND (Windows 11 yuvarlak köşe)
                Native.DwmSetWindowAttribute(Handle, 33, ref pref, 4);
            }
            catch (Exception) { }
        }

        void MakeTheme()
        {
            int mode = Util.GetInt("PanelTheme", 0); // 0 otomatik, 1 açık, 2 koyu
            bool light = mode == 1 || (mode == 0 && Util.SystemLight("AppsUseLightTheme"));
            th = new Theme();
            if (light)
            {
                th.Bg = Color.FromArgb(243, 243, 246);
                th.Card = Color.FromArgb(255, 255, 255);
                th.Border = Color.FromArgb(220, 220, 226);
                th.Text = Color.FromArgb(28, 28, 32);
                th.Dim = Color.FromArgb(112, 112, 122);
                th.Track = Color.FromArgb(226, 226, 232);
                th.Accent = Color.FromArgb(0, 103, 192);
            }
            else
            {
                th.Bg = Color.FromArgb(30, 30, 34);
                th.Card = Color.FromArgb(43, 43, 49);
                th.Border = Color.FromArgb(62, 62, 70);
                th.Text = Color.FromArgb(242, 242, 245);
                th.Dim = Color.FromArgb(158, 158, 170);
                th.Track = Color.FromArgb(70, 70, 80);
                th.Accent = Color.FromArgb(96, 165, 250);
            }
            BackColor = th.Bg;
        }

        void MakeFonts()
        {
            DisposeFonts();
            fHead = new Font("Segoe UI Semibold", 9f, FontStyle.Regular, GraphicsUnit.Point);
            fTitle = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
            fBody = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            fSmall = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
            fBig = new Font("Segoe UI Semibold", 16f, FontStyle.Regular, GraphicsUnit.Point);
            fIcon = new Font("Segoe MDL2 Assets", 18f, FontStyle.Regular, GraphicsUnit.Point);
            fIconS = new Font("Segoe MDL2 Assets", 9f, FontStyle.Regular, GraphicsUnit.Point);
        }

        void DisposeFonts()
        {
            Font[] all = { fHead, fTitle, fBody, fSmall, fBig, fIcon, fIconS };
            foreach (Font f in all) if (f != null) f.Dispose();
        }

        public void SetData(List<DeviceInfo> d)
        {
            devices = d ?? new List<DeviceInfo>();
            if (Visible) { Relayout(); Invalidate(); }
        }

        // Dil değişince metinleri yeniden düzenler.
        public void RefreshUi()
        {
            if (Visible) { Relayout(); Invalidate(); }
        }

        // Açık ve kapanış animasyonunda değil mi?
        public bool IsOpen { get { return Visible && !closing; } }

        // Fare imlecinin yanına, görev çubuğunun üstüne açar.
        public void ShowNear(Point cursor)
        {
            closing = false;
            anim.Stop();
            scrollTimer.Stop();
            scrollY = scrollTarget = 0;
            MakeTheme();
            MakeFonts();
            s = Util.Scale();
            Width = (int)(420 * s);
            Screen scr = Screen.FromPoint(cursor);
            Rectangle wa = scr.WorkingArea;
            anchorBottom = cursor.Y >= wa.Top + wa.Height / 2;
            int margin = (int)(8 * s);
            int x = cursor.X - Width / 2;
            if (x + Width > wa.Right - margin) x = wa.Right - Width - margin;
            if (x < wa.Left + margin) x = wa.Left + margin;
            Left = x;
            Relayout();

            // Açılış animasyonu: görev çubuğuna doğru yukarıdan/aşağıdan kayarak ve belirginleşerek gelir.
            int target = Top;
            int off = (int)(26 * s);
            animTo = target;
            animFrom = anchorBottom ? target + off : target - off;
            Top = animFrom;
            Opacity = 0;
            animating = true;
            animStart = DateTime.Now;
            Show();
            Activate();
            anim.Start();
            tick.Start();
            if (PopupOpened != null) PopupOpened(this, EventArgs.Empty);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HidePopup();
        }

        public void HidePopup()
        {
            if (!Visible || closing) return;
            tick.Stop();
            anim.Stop();
            scrollTimer.Stop();
            cardTimer.Stop();
            animating = false;
            LastHideTick = Environment.TickCount;
            if (PopupClosed != null) PopupClosed(this, EventArgs.Empty);

            // Kapanış animasyonu: görev çubuğuna doğru kayarak solar, sonra gizlenir.
            int off = (int)(18 * s);
            closeFrom = Top;
            closeTo = anchorBottom ? Top + off : Top - off;
            closeStart = DateTime.Now;
            closing = true;
            anim.Start();
        }

        void OnTick()
        {
            if (!Visible || closing) return;
            if ((DateTime.Now - lastLayout).TotalMilliseconds > 900) Relayout();
            Invalidate();
        }

        void Relayout()
        {
            if (fBody == null || closing) return;
            lastLayout = DateTime.Now;
            float h;
            using (Graphics g = CreateGraphics())
            {
                h = Render(g, false);
            }
            contentH = h;
            Screen scr = Screen.FromControl(this);
            Rectangle wa = scr.WorkingArea;
            int margin = (int)(8 * s);
            int newH = (int)Math.Ceiling(h);
            int maxH = wa.Height - 2 * margin;
            if (newH > maxH) newH = maxH;
            float maxScrollNow = Math.Max(0f, contentH - newH);
            if (scrollTarget > maxScrollNow) scrollTarget = maxScrollNow;
            if (scrollY > maxScrollNow) scrollY = maxScrollNow;
            if (newH != Height) Height = newH;
            int top = anchorBottom ? wa.Bottom - Height - margin : wa.Top + margin;
            if (animating) animTo = top;
            else Top = top;
        }

        void OnAnim()
        {
            if (closing)
            {
                double ct = (DateTime.Now - closeStart).TotalMilliseconds / 160.0;
                if (ct >= 1.0)
                {
                    anim.Stop();
                    closing = false;
                    Hide();
                    Opacity = 1;
                    return;
                }
                double ce = ct * ct; // ease-in
                Top = closeFrom + (int)Math.Round((closeTo - closeFrom) * ce);
                Opacity = 1.0 - ce;
                return;
            }
            double t = (DateTime.Now - animStart).TotalMilliseconds / 220.0;
            if (t >= 1.0)
            {
                t = 1.0;
                anim.Stop();
                animating = false;
            }
            double e = 1.0 - Math.Pow(1.0 - t, 3.0); // ease-out
            Top = animFrom + (int)Math.Round((animTo - animFrom) * e);
            Opacity = e;
        }

        // ------------------------------------------------------------------ kaydırma

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta / 120f * 70f * s);
            base.OnMouseWheel(e);
        }

        void ScrollBy(float dy)
        {
            float maxScroll = Math.Max(0f, contentH - Height);
            if (maxScroll <= 0f) return;
            scrollTarget = Math.Max(0f, Math.Min(maxScroll, scrollTarget + dy));
            if (!scrollTimer.Enabled) scrollTimer.Start();
        }

        void OnScrollTick()
        {
            float d = scrollTarget - scrollY;
            if (Math.Abs(d) < 0.5f)
            {
                scrollY = scrollTarget;
                scrollTimer.Stop();
            }
            else scrollY += d * 0.28f;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) HidePopup();
            else if (e.KeyCode == Keys.Down) ScrollBy(60f * s);
            else if (e.KeyCode == Keys.Up) ScrollBy(-60f * s);
            else if (e.KeyCode == Keys.PageDown) ScrollBy(Height * 0.8f);
            else if (e.KeyCode == Keys.PageUp) ScrollBy(-Height * 0.8f);
            else if (e.KeyCode == Keys.Home) ScrollBy(-100000f);
            else if (e.KeyCode == Keys.End) ScrollBy(100000f);
            base.OnKeyDown(e);
        }

        // ------------------------------------------------------------------ tıklamalar

        bool HitAny(PointF p)
        {
            if (themeBtn.Contains(p) || langBtn.Contains(p)) return true;
            foreach (KeyValuePair<RectangleF, int> kv in chipHits) if (kv.Key.Contains(p)) return true;
            foreach (KeyValuePair<RectangleF, string> kv2 in headerHits) if (kv2.Key.Contains(p)) return true;
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = HitAny(new PointF(e.X, e.Y + scrollY)) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !closing)
            {
                PointF p = new PointF(e.X, e.Y + scrollY);
                if (themeBtn.Contains(p)) CycleTheme();
                else if (langBtn.Contains(p))
                {
                    langOpen = !langOpen;
                    Relayout();
                    Invalidate();
                }
                else
                {
                    int lang = -1;
                    string card = null;
                    foreach (KeyValuePair<RectangleF, int> kv in chipHits)
                        if (kv.Key.Contains(p)) { lang = kv.Value; break; }
                    if (lang < 0)
                        foreach (KeyValuePair<RectangleF, string> kv2 in headerHits)
                            if (kv2.Key.Contains(p)) { card = kv2.Value; break; }
                    if (lang >= 0) SetLanguage(lang);
                    else if (card != null) ToggleCard(card);
                }
            }
            base.OnMouseUp(e);
        }

        void CycleTheme()
        {
            Util.SetInt("PanelTheme", (Util.GetInt("PanelTheme", 0) + 1) % 3);
            MakeTheme();
            Relayout();
            Invalidate();
        }

        void SetLanguage(int i)
        {
            Loc.Set(i);
            langOpen = false;
            Relayout();
            Invalidate();
            if (LanguageChanged != null) LanguageChanged(this, EventArgs.Empty);
        }

        float GetAmt(string key)
        {
            float a;
            if (!cardAmt.TryGetValue(key, out a))
            {
                bool c = Util.GetBool("Collapsed:" + key, false);
                cardCollapsed[key] = c;
                a = c ? 0f : 1f;
                cardAmt[key] = a;
            }
            return a;
        }

        void ToggleCard(string key)
        {
            GetAmt(key);
            bool c = !cardCollapsed[key];
            cardCollapsed[key] = c;
            Util.SetBool("Collapsed:" + key, c);
            if (!cardTimer.Enabled) cardTimer.Start();
        }

        void OnCardTick()
        {
            bool moving = false;
            List<string> keys = new List<string>(cardAmt.Keys);
            foreach (string k in keys)
            {
                float target = cardCollapsed[k] ? 0f : 1f;
                float a = cardAmt[k];
                if (Math.Abs(target - a) > 0.01f)
                {
                    a += (target - a) * 0.3f;
                    moving = true;
                }
                else a = target;
                cardAmt[k] = a;
            }
            Relayout();
            Invalidate();
            if (!moving) cardTimer.Stop();
        }

        // ------------------------------------------------------------------ çizim

        protected override void OnPaint(PaintEventArgs e)
        {
            if (th == null || fBody == null) return;
            Graphics g = e.Graphics;
            g.Clear(th.Bg);
            g.TranslateTransform(0, -scrollY);
            Render(g, true);
            g.ResetTransform();

            // İçerik ekrana sığmıyorsa ince bir kaydırma çubuğu göster.
            float maxScroll = Math.Max(0f, contentH - Height);
            if (maxScroll > 0.5f)
            {
                float m = 6 * s;
                float trackH = Height - 2 * m;
                float thumbH = Math.Max(24 * s, trackH * Height / contentH);
                float thumbY = m + (trackH - thumbH) * (scrollY / maxScroll);
                RectangleF thumb = new RectangleF(Width - 7 * s, thumbY, 3 * s, thumbH);
                using (GraphicsPath tp = RoundRect(thumb, 1.5f * s))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(130, th.Dim)))
                    g.FillPath(tb, tp);
            }
            using (Pen p = new Pen(th.Border))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static string Glyph(DeviceKind k)
        {
            switch (k)
            {
                case DeviceKind.Headset: return "\uE7F6";
                case DeviceKind.Mouse: return "\uE962";
                case DeviceKind.Keyboard: return "\uE765";
                case DeviceKind.Gamepad: return "\uE7FC";
                default: return "\uE702";
            }
        }

        static string KindName(DeviceKind k)
        {
            switch (k)
            {
                case DeviceKind.Headset: return "Kulaklık";
                case DeviceKind.Mouse: return "Mouse";
                case DeviceKind.Keyboard: return "Klavye";
                case DeviceKind.Gamepad: return "Oyun kolu";
                default: return "Cihaz";
            }
        }

        static Color PctColor(int pct)
        {
            if (pct <= 15) return Color.FromArgb(240, 82, 82);
            if (pct <= 30) return Color.FromArgb(245, 170, 40);
            return Color.FromArgb(52, 199, 110);
        }

        // Metni çizer / ölçer; yüksekliği döndürür.
        float Txt(Graphics g, bool draw, string text, Font f, Color c, float x, float y, float w, StringAlignment align)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = align;
                sf.FormatFlags &= ~StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisWord;
                SizeF sz = g.MeasureString(text, f, new SizeF(w, 2000f), sf);
                if (draw)
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(text, f, b, new RectangleF(x, y, w, sz.Height + 2), sf);
                return sz.Height;
            }
        }

        // ------------------------------------------------------------------ satır içerikleri

        static string AudioFmt(AudioEndpoint a)
        {
            string fmt = a.Rate > 0
                ? Util.KHz(a.Rate) + "   ·   " + a.Bits + " bit   ·   " + Util.Channels(a.Channels)
                : Loc.T("okunamadı");
            return fmt + (a.IsDefault ? "   " + Loc.T("(varsayılan)") : "");
        }

        string MouseLive(MouseRateMonitor.Stat st)
        {
            if (st == null || st.Updated == DateTime.MinValue) return Loc.T("Fareyi hızlıca daireler çizerek hareket ettir…");
            bool fresh = (DateTime.Now - st.Updated).TotalSeconds < 1.0;
            string cur = Math.Round(st.Hz) + " Hz";
            return fresh ? cur : Loc.T("(durdu)") + " " + cur;
        }

        string MousePeak(MouseRateMonitor.Stat st)
        {
            if (st == null || st.Peak <= 0) return null;
            int cls = MouseRateMonitor.Classify(st.Peak);
            if (cls > 0) return Loc.F("{0} Hz  →  ~{1} Hz sınıfı", Math.Round(st.Peak), cls);
            return Math.Round(st.Peak) + " Hz";
        }

        string KeyLive(MouseRateMonitor.KStat k)
        {
            if (k == null || k.Samples < 4) return Loc.T("Birkaç tuşa art arda hızlıca bas…");
            double hz = 1000.0 / k.MinMs;
            int nom = MouseRateMonitor.Nominal(hz);
            string txt = nom > 0 ? "~" + nom + " Hz" : "≥ " + Math.Round(hz) + " Hz";
            return Loc.F("{0}   ·   en kısa tuş aralığı {1} ms", txt, k.MinMs.ToString("0.0"));
        }

        static string UsbText(DeviceInfo d)
        {
            return string.Format("{0} Hz  ({1}, bInterval {2})", d.UsbHz, Loc.T(d.UsbSpeed), d.UsbInterval);
        }

        MouseRateMonitor.KStat FindKStat(List<MouseRateMonitor.KStat> ks, DeviceInfo d)
        {
            MouseRateMonitor.KStat best = null;
            foreach (MouseRateMonitor.KStat k in ks)
            {
                if (d.Vid != 0 && k.Vid == d.Vid && k.Pid == d.Pid)
                {
                    if (best == null || k.Samples > best.Samples) best = k;
                }
            }
            return best;
        }

        MouseRateMonitor.Stat FindStat(List<MouseRateMonitor.Stat> stats, DeviceInfo d)
        {
            MouseRateMonitor.Stat best = null;
            foreach (MouseRateMonitor.Stat st in stats)
            {
                if (d.Vid != 0 && st.Vid == d.Vid && st.Pid == d.Pid)
                {
                    if (best == null || st.Peak > best.Peak) best = st;
                }
            }
            return best;
        }

        List<Row> BuildRows(DeviceInfo d, List<MouseRateMonitor.Stat> stats, List<MouseRateMonitor.KStat> kstats, ref bool anyLive)
        {
            List<Row> rows = new List<Row>();
            if (d.AudioOnlyCard)
            {
                foreach (AudioEndpoint a in d.Audio)
                {
                    rows.Add(new Row(Loc.T(a.Render ? "Ses çıkışı" : "Mikrofon"), AudioFmt(a)));
                    rows.Add(new Row("   " + Loc.T("aygıt"), a.Name));
                }
                return rows;
            }

            string state;
            if (!d.Connected) state = "Bağlı değil (son bilinen değer)";
            else if (d.Asleep) state = "Uyuyor (son bilinen değer)";
            else if (d.Charging) state = "Şarj oluyor";
            else state = "Bağlı";
            rows.Add(new Row(Loc.T("Durum"), Loc.T(state)));
            rows.Add(new Row(Loc.T("Bağlantı"), Loc.T(d.Via)));
            if (d.Percent < 0)
                rows.Add(new Row(Loc.T("Pil"), Loc.T(d.Asleep ? "Uyuyor, henüz değer okunamadı" : "Okunamadı (cihaz pil bilgisi vermiyor ya da bu model için sürücü yok)")));
            if (d.Product.Length > 0 && !string.Equals(d.Product, d.Name, StringComparison.OrdinalIgnoreCase))
                rows.Add(new Row(Loc.T("Ürün adı"), d.Product));
            if (d.Vid != 0 || d.Pid != 0) rows.Add(new Row(Loc.T("Donanım kimliği"), Util.Hex4(d.Vid) + ":" + Util.Hex4(d.Pid)));
            if (d.Mac.Length > 0) rows.Add(new Row(Loc.T("Bluetooth adresi"), d.Mac));

            bool lowRate = false;
            foreach (AudioEndpoint a in d.Audio)
            {
                rows.Add(new Row(Loc.T(a.Render ? "Ses çıkışı" : "Mikrofon"), AudioFmt(a)));
                rows.Add(new Row("   " + Loc.T("aygıt"), a.Name));
                if (a.Render && a.Rate > 0 && a.Rate <= 16000) lowRate = true;
            }
            if (lowRate)
                rows.Add(new Row(Loc.T("Mod"), Loc.T("Konuşma (Hands-Free) — mikrofon kullanıldığı için ses kalitesi düşük")));
            else if (d.Audio.Count > 0 && d.Via == "Bluetooth")
                rows.Add(new Row(Loc.T("Mod"), Loc.T("Yüksek kalite ses")));

            if (d.Kind == DeviceKind.Mouse)
            {
                if (d.UsbHz > 0) rows.Add(new Row(Loc.T("USB bildirilen hız"), UsbText(d)));
                MouseRateMonitor.Stat st = FindStat(stats, d);
                Row r = new Row(Loc.T("Rapor hızı (canlı)"), MouseLive(st));
                r.Live = true;
                rows.Add(r);
                string pk = MousePeak(st);
                if (pk != null)
                {
                    Row pr = new Row(Loc.T("En yüksek ölçüm"), pk);
                    pr.Live = true;
                    rows.Add(pr);
                }
                anyLive = true;
            }
            if (d.Kind == DeviceKind.Keyboard)
            {
                if (d.UsbHz > 0) rows.Add(new Row(Loc.T("Tarama hızı (USB)"), UsbText(d)));
                Row kr = new Row(Loc.T("Tuş testi (canlı)"), KeyLive(FindKStat(kstats, d)));
                kr.Live = true;
                rows.Add(kr);
                anyLive = true;
            }
            foreach (string[] fact in d.Facts)
            {
                string val = fact.Length > 2 ? Loc.F(fact[1], fact[2]) : Loc.T(fact[1]);
                rows.Add(new Row(Loc.T(fact[0]), val));
            }
            if (d.Kind == DeviceKind.Keyboard && d.Vid != 0)
                rows.Add(new Row(Loc.T("Gizlilik"), Loc.T("Hangi tuşa bastığın okunmaz; yalnızca olayların zamanı ölçülür.")));
            if (d.Source.Length > 0) rows.Add(new Row(Loc.T("Veri kaynağı"), d.Source));
            return rows;
        }

        // ------------------------------------------------------------------ düzen / çizim parçaları

        // draw=false: yalnızca toplam yüksekliği ölç. Tıklama alanları her iki kipte de güncellenir.
        float Render(Graphics g, bool draw)
        {
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            headerHits.Clear();
            chipHits.Clear();

            float W = ClientSize.Width > 0 ? ClientSize.Width : Width;
            float pad = 14 * s;
            float inner = W - 2 * pad;
            float y = pad;

            y = RenderHeader(g, draw, W, pad, y);

            List<MouseRateMonitor.Stat> stats = rate != null ? rate.Snapshot() : new List<MouseRateMonitor.Stat>();
            List<MouseRateMonitor.KStat> kstats = rate != null ? rate.KeyboardSnapshot() : new List<MouseRateMonitor.KStat>();
            List<MouseRateMonitor.Stat> used = new List<MouseRateMonitor.Stat>();
            bool anyLive = false;

            if (devices.Count == 0) y = RenderEmpty(g, draw, pad, inner, y);
            foreach (DeviceInfo d in devices)
                y = RenderDevice(g, draw, d, stats, kstats, used, pad, inner, y, ref anyLive);
            y = RenderOtherMice(g, draw, stats, used, pad, inner, y, ref anyLive);
            y = RenderFooter(g, draw, anyLive, pad, inner, y);
            y = RenderLanguage(g, draw, pad, inner, y);
            return y + pad;
        }

        float RenderHeader(Graphics g, bool draw, float W, float pad, float y)
        {
            float inner = W - 2 * pad;
            Txt(g, draw, Loc.T("Kablosuz cihazlar"), fHead, th.Dim, pad, y, inner, StringAlignment.Near);
            string timeText = DateTime.Now.ToString("HH:mm:ss");
            using (StringFormat tsf = new StringFormat(StringFormat.GenericTypographic))
            {
                // Tema düğmesi: Otomatik -> Açık -> Koyu
                int tmode = Util.GetInt("PanelTheme", 0);
                string lab = Loc.T(tmode == 1 ? "Açık" : (tmode == 2 ? "Koyu" : "Otomatik"));
                string gl = tmode == 1 ? "\uE706" : (tmode == 2 ? "\uE708" : "\uE7F4");
                float timeW = g.MeasureString(timeText, fSmall, PointF.Empty, tsf).Width;
                SizeF labSz = g.MeasureString(lab, fSmall, PointF.Empty, tsf);
                SizeF glSz = g.MeasureString(gl, fIconS, PointF.Empty, tsf);
                float btnW = 10 * s + glSz.Width + 5 * s + labSz.Width + 10 * s, btnH = 20 * s;
                float btnX = W - pad - timeW - 10 * s - btnW;
                themeBtn = new RectangleF(btnX, y - 3 * s, btnW, btnH);
                if (draw)
                {
                    using (GraphicsPath bp = RoundRect(themeBtn, btnH / 2))
                    {
                        using (SolidBrush bb = new SolidBrush(th.Card)) g.FillPath(bb, bp);
                        using (Pen bpen = new Pen(th.Border)) g.DrawPath(bpen, bp);
                    }
                    using (SolidBrush gb = new SolidBrush(th.Accent))
                        g.DrawString(gl, fIconS, gb, btnX + 10 * s, themeBtn.Y + (btnH - glSz.Height) / 2f, tsf);
                    using (SolidBrush lb = new SolidBrush(th.Text))
                        g.DrawString(lab, fSmall, lb, btnX + 10 * s + glSz.Width + 5 * s, themeBtn.Y + (btnH - labSz.Height) / 2f, tsf);
                }
            }
            if (draw) Txt(g, true, timeText, fSmall, th.Dim, pad, y + 1, inner, StringAlignment.Far);
            return y + 22 * s;
        }

        float RenderEmpty(Graphics g, bool draw, float pad, float inner, float y)
        {
            string msg = Loc.T("Pil bilgisi veren cihaz bulunamadı.\nCihazın Bluetooth ile bağlı olduğundan emin ol. 2.4 GHz alıcılı cihazlar yalnızca desteklenen markalarda görünür.");
            float ch = Txt(g, false, msg, fBody, th.Text, pad + 14 * s, y + 14 * s, inner - 28 * s, StringAlignment.Near);
            float cardH = ch + 28 * s;
            if (draw)
            {
                DrawCard(g, new RectangleF(pad, y, inner, cardH));
                Txt(g, true, msg, fBody, th.Text, pad + 14 * s, y + 14 * s, inner - 28 * s, StringAlignment.Near);
            }
            return y + cardH + 10 * s;
        }

        float RenderDevice(Graphics g, bool draw, DeviceInfo d, List<MouseRateMonitor.Stat> stats, List<MouseRateMonitor.KStat> kstats,
                           List<MouseRateMonitor.Stat> used, float pad, float inner, float y, ref bool anyLive)
        {
            List<Row> rows = BuildRows(d, stats, kstats, ref anyLive);
            if (d.Kind == DeviceKind.Mouse)
            {
                MouseRateMonitor.Stat st = FindStat(stats, d);
                if (st != null) used.Add(st);
            }
            float cx = pad + 14 * s, cw = inner - 28 * s;
            float keyW = 118 * s;
            float amt = GetAmt(d.Key);

            // satır yükseklikleri
            float[] rh = new float[rows.Count];
            float rowsH = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                float vh = Txt(g, false, rows[i].Value, fBody, th.Text, cx + keyW, 0, cw - keyW, StringAlignment.Near);
                float kh = Txt(g, false, rows[i].Key, fBody, th.Dim, cx, 0, keyW - 6 * s, StringAlignment.Near);
                rh[i] = Math.Max(18 * s, Math.Max(vh, kh) + 4 * s);
                rowsH += rh[i];
            }
            float head = 14 * s + 30 * s + (d.AudioOnlyCard ? 4 * s : 24 * s);
            float h = head + rowsH * amt + 12 * s;
            headerHits.Add(new KeyValuePair<RectangleF, string>(new RectangleF(pad, y, inner, head), d.Key));

            if (draw)
            {
                DrawCard(g, new RectangleF(pad, y, inner, h));
                float ty = y + 14 * s;
                bool fresh = d.Connected && !d.Asleep && !d.PercentIsStale;
                float chevW = 20 * s;

                Txt(g, true, d.AudioOnlyCard ? "\uE767" : Glyph(d.Kind), fIcon, d.Connected ? th.Accent : th.Dim, cx, ty - 2 * s, 34 * s, StringAlignment.Near);
                float nameW = cw - 38 * s - 90 * s - chevW;
                string title = d.AudioOnlyCard ? Loc.T("Ses aygıtları") : d.Name;
                Txt(g, true, title, fTitle, th.Text, cx + 38 * s, ty - 2 * s, nameW, StringAlignment.Near);
                string sub = d.AudioOnlyCard ? Loc.T("Windows'un kullandığı ses formatı") : Loc.T(KindName(d.Kind)) + "  ·  " + Loc.T(d.Via);
                Txt(g, true, sub, fSmall, th.Dim, cx + 38 * s, ty + 17 * s, nameW, StringAlignment.Near);

                if (!d.AudioOnlyCard)
                {
                    if (d.Percent >= 0)
                        Txt(g, true, "%" + d.Percent, fBig, fresh ? PctColor(d.Percent) : th.Dim, cx + cw - 90 * s - chevW, ty - 2 * s, 90 * s, StringAlignment.Far);
                    else
                        Txt(g, true, "—", fBig, th.Dim, cx + cw - 90 * s - chevW, ty - 2 * s, 90 * s, StringAlignment.Far);
                }
                // aç / kapat oku
                Txt(g, true, amt > 0.5f ? "\uE70E" : "\uE70D", fIconS, th.Dim, cx + cw - chevW, ty + 4 * s, chevW, StringAlignment.Far);

                ty += d.AudioOnlyCard ? 34 * s : 36 * s;
                if (!d.AudioOnlyCard)
                {
                    RectangleF track = new RectangleF(cx, ty, cw, 6 * s);
                    using (GraphicsPath tp = RoundRect(track, 3 * s))
                    using (SolidBrush tb = new SolidBrush(th.Track))
                        g.FillPath(tb, tp);
                    if (d.Percent > 0)
                    {
                        float fw = Math.Max(6 * s, cw * d.Percent / 100f);
                        RectangleF fill = new RectangleF(cx, ty, fw, 6 * s);
                        using (GraphicsPath fp = RoundRect(fill, 3 * s))
                        using (SolidBrush fb = new SolidBrush(fresh ? PctColor(d.Percent) : th.Dim))
                            g.FillPath(fb, fp);
                    }
                    ty += 18 * s;
                }

                // satırlar (açılıp kapanırken kırpılarak görünür)
                float vis = rowsH * amt;
                if (vis > 0.5f)
                {
                    GraphicsState gs = g.Save();
                    g.SetClip(new RectangleF(pad, ty, inner, vis), CombineMode.Intersect);
                    float ry = ty;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        Txt(g, true, rows[i].Key, fBody, th.Dim, cx, ry, keyW - 6 * s, StringAlignment.Near);
                        Txt(g, true, rows[i].Value, fBody, rows[i].Live ? th.Accent : th.Text, cx + keyW, ry, cw - keyW, StringAlignment.Near);
                        ry += rh[i];
                    }
                    g.Restore(gs);
                }
            }
            return y + h + 10 * s;
        }

        // Hiçbir cihaza eşlenemeyen fare girişleri (örn. pil bilgisi vermeyen mouse).
        float RenderOtherMice(Graphics g, bool draw, List<MouseRateMonitor.Stat> stats, List<MouseRateMonitor.Stat> used,
                              float pad, float inner, float y, ref bool anyLive)
        {
            List<MouseRateMonitor.Stat> others = new List<MouseRateMonitor.Stat>();
            foreach (MouseRateMonitor.Stat st in stats)
            {
                bool u = false;
                foreach (MouseRateMonitor.Stat x in used) if (x.Handle == st.Handle) u = true;
                if (!u) others.Add(st);
            }
            if (others.Count == 0) return y;

            float cx = pad + 14 * s, cw = inner - 28 * s;
            float h = 14 * s + 22 * s + others.Count * 20 * s + 10 * s;
            if (draw)
            {
                DrawCard(g, new RectangleF(pad, y, inner, h));
                float ty = y + 14 * s;
                Txt(g, true, Loc.T("Fare girişleri (canlı rapor hızı)"), fHead, th.Text, cx, ty, cw, StringAlignment.Near);
                ty += 22 * s;
                foreach (MouseRateMonitor.Stat st in others)
                {
                    string nm = st.Vid >= 0 ? Loc.T("Fare") + " " + Util.Hex4(st.Vid) + ":" + Util.Hex4(st.Pid) : Loc.T("Fare");
                    string val = MouseLive(st);
                    if (st.Peak > 0) val += "  ·  " + Math.Round(st.Peak) + " Hz";
                    Txt(g, true, nm, fBody, th.Dim, cx, ty, 130 * s, StringAlignment.Near);
                    Txt(g, true, val, fBody, th.Accent, cx + 130 * s, ty, cw - 130 * s, StringAlignment.Near);
                    ty += 20 * s;
                }
            }
            anyLive = true;
            return y + h + 10 * s;
        }

        float RenderFooter(Graphics g, bool draw, bool anyLive, float pad, float inner, float y)
        {
            string foot = Loc.T("Bluetooth ve ses: 2 sn'de bir  ·  alıcı pilleri: 10 sn'de bir");
            if (anyLive) foot = Loc.T("Hız testleri: fareyi hızlıca hareket ettir / klavyede hızlıca tuşlara bas  ·  ") + foot;
            float fh = Txt(g, draw, foot, fSmall, th.Dim, pad, y, inner, StringAlignment.Near);
            return y + fh + 10 * s;
        }

        // Dil seçici: en altta bir hap düğme; tıklayınca dil çipleri açılır.
        float RenderLanguage(Graphics g, bool draw, float pad, float inner, float y)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                string gl = "\uE774"; // Globe
                string lab = Loc.T("Dil") + ": " + Loc.Names[Loc.Index];
                SizeF glSz = g.MeasureString(gl, fIconS, PointF.Empty, sf);
                SizeF labSz = g.MeasureString(lab, fSmall, PointF.Empty, sf);
                float ph = 24 * s;
                float pw = 10 * s + glSz.Width + 6 * s + labSz.Width + 8 * s + 14 * s;
                langBtn = new RectangleF(pad, y, pw, ph);
                if (draw)
                {
                    using (GraphicsPath bp = RoundRect(langBtn, ph / 2))
                    {
                        using (SolidBrush bb = new SolidBrush(th.Card)) g.FillPath(bb, bp);
                        using (Pen bpen = new Pen(th.Border)) g.DrawPath(bpen, bp);
                    }
                    using (SolidBrush gb = new SolidBrush(th.Accent))
                        g.DrawString(gl, fIconS, gb, pad + 10 * s, y + (ph - glSz.Height) / 2f, sf);
                    using (SolidBrush lb = new SolidBrush(th.Text))
                        g.DrawString(lab, fSmall, lb, pad + 10 * s + glSz.Width + 6 * s, y + (ph - labSz.Height) / 2f, sf);
                    using (SolidBrush cb = new SolidBrush(th.Dim))
                        g.DrawString(langOpen ? "\uE70E" : "\uE70D", fIconS, cb, pad + pw - 20 * s, y + (ph - glSz.Height) / 2f, sf);
                }
                y += ph;

                if (langOpen)
                {
                    float x = pad, rowY = y + 8 * s, chipH = 26 * s, gap = 6 * s;
                    for (int i = 0; i < Loc.Names.Length; i++)
                    {
                        SizeF ns = g.MeasureString(Loc.Names[i], fSmall, PointF.Empty, sf);
                        float cwid = ns.Width + 22 * s;
                        if (x + cwid > pad + inner)
                        {
                            x = pad;
                            rowY += chipH + gap;
                        }
                        RectangleF r = new RectangleF(x, rowY, cwid, chipH);
                        chipHits.Add(new KeyValuePair<RectangleF, int>(r, i));
                        if (draw)
                        {
                            bool sel = i == Loc.Index;
                            using (GraphicsPath cp = RoundRect(r, chipH / 2))
                            {
                                using (SolidBrush fb = new SolidBrush(sel ? Color.FromArgb(55, th.Accent) : th.Card)) g.FillPath(fb, cp);
                                using (Pen pen = new Pen(sel ? th.Accent : th.Border)) g.DrawPath(pen, cp);
                            }
                            using (StringFormat cf = new StringFormat(sf))
                            {
                                cf.Alignment = StringAlignment.Center;
                                cf.LineAlignment = StringAlignment.Center;
                                using (SolidBrush tb = new SolidBrush(sel ? th.Accent : th.Text))
                                    g.DrawString(Loc.Names[i], fSmall, tb, r, cf);
                            }
                        }
                        x += cwid + gap;
                    }
                    y = rowY + chipH;
                }
            }
            return y;
        }

        void DrawCard(Graphics g, RectangleF r)
        {
            using (GraphicsPath p = RoundRect(r, 8 * s))
            {
                using (SolidBrush b = new SolidBrush(th.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(th.Border)) g.DrawPath(pen, p);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tick.Dispose();
                anim.Dispose();
                scrollTimer.Dispose();
                cardTimer.Dispose();
                DisposeFonts();
            }
            base.Dispose(disposing);
        }
    }
}
