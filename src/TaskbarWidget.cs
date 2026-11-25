using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WirelessBattery
{
    // Görev çubuğunun üstünde duran, sistem tepsisinin (saat / ^ alanı) hemen soluna yapışan gösterge:
    //     [kulaklık] 85%   [mouse] 92%
    // Çubuğa "gömülmez": şeffaf, tıklanabilir, odak çalmayan ayrı bir en-üst pencere olarak çizilir.
    // Üstüne gelince hafif aydınlanır, basılı tutunca belirgin şekilde çöker (basıldığı belli olur).
    // Her saniye konumu ve en üstte olması kontrol edilir; tam ekran uygulama / oyun varken ve görev
    // çubuğu gizliyken kendini gizler.
    class TaskbarWidget : NativeWindow
    {
        public event EventHandler Clicked;
        public event EventHandler RightClicked;

        public static string LastDiag = "(henüz çalışmadı)";

        const int WS_EX_LAYERED = 0x00080000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x00000008;
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        const uint GW_HWNDPREV = 3;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // Piksel başına alfa için "premultiplied" DIB: GDI+ doğrudan bu belleğe çizer (renkler solmaz).
        class Frame : IDisposable
        {
            public IntPtr HBmp = IntPtr.Zero;
            public Bitmap Bmp;
            public void Dispose()
            {
                if (Bmp != null) Bmp.Dispose();
                Bmp = null;
                if (HBmp != IntPtr.Zero) Native.DeleteObject(HBmp);
                HBmp = IntPtr.Zero;
            }
        }

        List<DeviceInfo> devices = new List<DeviceInfo>();
        string lastSig = "";
        int curW, curH;
        bool fresh;
        bool hidden = true;
        bool hover, pressed;
        IntPtr lastTray = IntPtr.Zero;
        int lastPx = -99999, lastPy = -99999, lastW = -1, lastH = -1;
        int fsHits;
        float hl, pr;                       // vurgu ve basma animasyon seviyeleri (0..1)
        readonly Timer fx = new Timer();
        DateTime lastPaint = DateTime.MinValue;
        List<DeviceInfo> lastShown;
        float lastS = 1f;
        bool lastLight;
        int lastTrayH;

        public TaskbarWidget()
        {
            fx.Interval = 15;
            fx.Tick += delegate { FxTick(); };
        }

        public void Update(List<DeviceInfo> devs)
        {
            devices = devs ?? new List<DeviceInfo>();
            Relayout();
        }

        public void Detach()
        {
            if (Handle != IntPtr.Zero)
            {
                try { DestroyHandle(); } catch (Exception) { }
            }
            lastSig = "";
            hidden = true;
            hover = pressed = false;
            fx.Stop();
            hl = pr = 0f;
        }

        List<DeviceInfo> Shown()
        {
            List<DeviceInfo> r = new List<DeviceInfo>();
            foreach (DeviceInfo d in devices)
            {
                if (d.AudioOnlyCard || !d.Connected || d.Percent < 0 || r.Count >= 4) continue;
                if (d.Kind != DeviceKind.Mouse && d.Kind != DeviceKind.Headset) continue; // klavye vb. yalnızca panelde
                r.Add(d);
            }
            return r;
        }

        void HideWin()
        {
            if (Handle != IntPtr.Zero && !hidden) Native.ShowWindow(Handle, 0);
            hidden = true;
            lastSig = "";
        }

        bool EnsureCreated()
        {
            if (Handle != IntPtr.Zero && Native.IsWindow(Handle)) return true;
            Detach();
            try
            {
                CreateParams cp = new CreateParams();
                cp.Style = unchecked((int)0x80000000); // WS_POPUP
                cp.ExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                cp.X = 0; cp.Y = 0; cp.Width = 10; cp.Height = 10;
                CreateHandle(cp);
                fresh = true;
                return Handle != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                LastDiag = "Pencere oluşturulamadı: " + ex.Message;
                return false;
            }
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

        public static Color LevelColor(int pct, bool light)
        {
            if (pct <= 15) return light ? Color.FromArgb(200, 30, 30) : Color.FromArgb(255, 105, 105);
            if (pct <= 30) return light ? Color.FromArgb(185, 100, 0) : Color.FromArgb(255, 196, 80);
            return light ? Color.FromArgb(10, 10, 10) : Color.FromArgb(255, 255, 255);
        }

        static float GetScale(IntPtr tray)
        {
            try
            {
                uint d = Native.GetDpiForWindow(tray);
                if (d >= 96) return d / 96f;
            }
            catch (Exception) { }
            return Util.Scale();
        }

        static float TextPoints()
        {
            int f = Util.GetInt("WidgetFont", 1); // 0 küçük, 1 orta, 2 büyük
            if (f == 0) return 9.5f;
            if (f == 2) return 12.5f;
            return 11f;
        }

        string Signature(List<DeviceInfo> shown, float s, bool light, int trayH)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(s).Append('|').Append(light).Append('|').Append(trayH).Append('|').Append(TextPoints());
            foreach (DeviceInfo d in shown)
                sb.Append('|').Append((int)d.Kind).Append(':').Append(d.Percent).Append(d.Charging).Append(d.Asleep).Append(d.PercentIsStale);
            return sb.ToString();
        }

        static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(128);
            Native.GetClassName(h, sb, 128);
            return sb.ToString();
        }

        // Gerçekten tam ekran mı: öndeki pencere ekranın tamamını (görev çubuğu dahil) kaplamalı.
        // Sadece büyük açılan / ekranı kaplayan normal pencereler "tam ekran" sayılmaz.
        static bool FullscreenAppRunning()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                string cls = ClassOf(fg);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
                RECT r;
                if (!Native.GetWindowRect(fg, out r)) return false;
                Rectangle b = Screen.FromHandle(fg).Bounds;
                bool covers = r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
                if (!covers) return false;
                int st;
                if (Native.SHQueryUserNotificationState(out st) != 0) return false;
                return st == 2 || st == 3 || st == 4; // meşgul (tam ekran) / D3D tam ekran / sunum modu
            }
            catch (Exception) { return false; }
        }

        // Kısa süreli yanlış alarmlarda (uygulama açılırken) gizlenip geri gelmesin: art arda 2 kontrol gerekir.
        bool FullscreenConfirmed()
        {
            if (!FullscreenAppRunning()) { fsHits = 0; return false; }
            fsHits++;
            return fsHits >= 2;
        }

        // Windows, yeni bir uygulama açılınca görev çubuğunu öne alabilir. Göstergenin arkada kalıp
        // kaybolmaması için çubuğun üstüne çıktığı an hemen tekrar en üste alınır.
        public void KeepOnTop()
        {
            if (hidden || Handle == IntPtr.Zero || lastTray == IntPtr.Zero) return;
            IntPtr p = Native.GetWindow(Handle, GW_HWNDPREV);
            int n = 0;
            bool covered = false;
            while (p != IntPtr.Zero && n < 12)
            {
                if (p == lastTray) { covered = true; break; }
                p = Native.GetWindow(p, GW_HWNDPREV);
                n++;
            }
            if (covered) Native.SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // Her saniye ve fare etkileşiminde çağrılır: görev çubuğunu bulur, gerekirse yeniden çizer,
        // konumlar, en üste alır.
        public void Relayout()
        {
            List<DeviceInfo> shown = Shown();
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            RECT tr;
            tr.Left = tr.Top = tr.Right = tr.Bottom = 0;
            bool haveTray = tray != IntPtr.Zero && Native.GetWindowRect(tray, out tr);

            string why = null;
            if (shown.Count == 0) why = "gösterilecek cihaz yok";
            else if (!haveTray) why = "Shell_TrayWnd bulunamadı";
            else if (tr.Right - tr.Left < tr.Bottom - tr.Top) why = "dikey görev çubuğu";
            else if (FullscreenConfirmed()) why = "tam ekran uygulama çalışıyor";
            else
            {
                Rectangle b = Screen.FromRectangle(new Rectangle(tr.Left, tr.Top, Math.Max(1, tr.Right - tr.Left), Math.Max(1, tr.Bottom - tr.Top))).Bounds;
                if (tr.Top >= b.Bottom - 2 || tr.Bottom <= b.Top + 2) why = "görev çubuğu gizli (otomatik gizle)";
            }
            if (why != null)
            {
                HideWin();
                LastDiag = "Gösterge gizli: " + why;
                return;
            }
            if (!EnsureCreated()) return;
            lastTray = tray;

            float s = GetScale(tray);
            bool light = Util.SystemLight("SystemUsesLightTheme");
            int trayH = tr.Bottom - tr.Top;

            Frame frame = null;
            string sig = Signature(shown, s, light, trayH);
            lastShown = shown;
            lastS = s;
            lastLight = light;
            lastTrayH = trayH;
            // Ekran modu değişince (oyun çıkışı vb.) Windows katmanlı pencerenin içeriğini silebilir:
            // bu yüzden değişiklik olmasa da birkaç saniyede bir yeniden çiziyoruz.
            if (fresh || hidden || sig != lastSig || (DateTime.Now - lastPaint).TotalSeconds >= 3)
            {
                frame = Compose(shown, s, light, trayH);
                lastSig = sig;
                lastPaint = DateTime.Now;
                fresh = false;
            }

            // Konum: sistem tepsisinin (TrayNotifyWnd) hemen solu.
            string notifyInfo = "TrayNotifyWnd yok";
            int edge = tr.Right - (int)(300 * s);
            IntPtr notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notify != IntPtr.Zero)
            {
                RECT nr;
                Native.GetWindowRect(notify, out nr);
                notifyInfo = "TrayNotifyWnd " + nr.Left + "," + nr.Top + "," + nr.Right + "," + nr.Bottom;
                if (nr.Right - nr.Left > 20 * s && nr.Left > tr.Left + 100 * s && nr.Left < tr.Right) edge = nr.Left;
            }

            // Windows 10: "Haberler ve ilgi alanları" tepsinin hemen solunda durur; onun soluna geç.
            string[] feeds = { "ShellFeedsTaskbarViewWindow", "DynamicContent2" };
            foreach (string cls in feeds)
            {
                IntPtr f = Native.FindWindowEx(tray, IntPtr.Zero, cls, null);
                if (f == IntPtr.Zero || !Native.IsWindowVisible(f)) continue;
                RECT fr;
                Native.GetWindowRect(f, out fr);
                if (fr.Left < edge && fr.Right > fr.Left) edge = fr.Left;
            }

            int px = edge - curW - (int)(2 * s);
            int py = tr.Top;
            bool moved = hidden || px != lastPx || py != lastPy || curW != lastW || curH != lastH;
            if (moved)
            {
                Native.SetWindowPos(Handle, HWND_TOPMOST, px, py, curW, curH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                lastPx = px; lastPy = py; lastW = curW; lastH = curH;
            }
            hidden = false;
            if (frame != null)
            {
                Present(frame, curW, curH);
                frame.Dispose();
            }
            KeepOnTop();
            LastDiag = "Gösterge açık. Görev çubuğu " + tr.Left + "," + tr.Top + "," + tr.Right + "," + tr.Bottom +
                       " | " + notifyInfo + " | gösterge konumu " + px + "," + py + " boyut " + curW + "x" + curH +
                       " | ölçek " + s + " | cihaz sayısı " + shown.Count;
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

        Frame Compose(List<DeviceInfo> shown, float s, bool light, int trayH)
        {
            float pt = TextPoints();
            using (Font fIcon = new Font("Segoe MDL2 Assets", pt + 1.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (Font fText = new Font("Segoe UI", pt, FontStyle.Bold, GraphicsUnit.Point))
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics pg = Graphics.FromImage(probe))
            {
                StringFormat sf = StringFormat.GenericTypographic;
                float gap = 5 * s, itemGap = 14 * s, padX = 10 * s;
                float total = padX;
                List<float> iconW = new List<float>(), textW = new List<float>();
                List<string> texts = new List<string>();
                foreach (DeviceInfo d in shown)
                {
                    string txt = (d.Percent >= 0 ? d.Percent.ToString() + "%" : "--") + (d.Charging ? "\u26A1" : "");
                    float iw = pg.MeasureString(Glyph(d.Kind), fIcon, PointF.Empty, sf).Width;
                    float tw = pg.MeasureString(txt, fText, PointF.Empty, sf).Width;
                    iconW.Add(iw);
                    textW.Add(tw);
                    texts.Add(txt);
                    total += iw + gap + tw + itemGap;
                }
                total = total - itemGap + padX;
                int w = (int)Math.Ceiling(total), h = trayH;
                curW = w;
                curH = h;

                BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
                bi.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bi.biWidth = w;
                bi.biHeight = -h; // yukarıdan aşağıya
                bi.biPlanes = 1;
                bi.biBitCount = 32;
                bi.biCompression = 0;
                IntPtr bits;
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                IntPtr hbmp = Native.CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
                Native.ReleaseDC(IntPtr.Zero, screen);

                Frame frame = new Frame();
                frame.HBmp = hbmp;
                if (hbmp == IntPtr.Zero || bits == IntPtr.Zero) return frame;
                frame.Bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);

                using (Graphics g = Graphics.FromImage(frame.Bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    // Neredeyse görünmez zemin: tıklamaların bu pencereye gelmesi için.
                    using (SolidBrush hit = new SolidBrush(Color.FromArgb(3, 0, 0, 0)))
                        g.FillRectangle(hit, 0, 0, w, h);

                    // Üstüne gelince / basılınca arka plan vurgusu.
                    if (hl > 0.01f)
                    {
                        int a = (int)Math.Round(hl * 80f);
                        int c = light ? 0 : 255;
                        RectangleF bg = new RectangleF(2 * s, 4 * s, w - 4 * s, h - 8 * s);
                        using (GraphicsPath gp = RoundRect(bg, 6 * s))
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(a, c, c, c)))
                            g.FillPath(hb, gp);
                    }

                    Color iconColor = light ? Color.FromArgb(30, 30, 30) : Color.FromArgb(235, 235, 235);
                    Color dim = light ? Color.FromArgb(90, 90, 90) : Color.FromArgb(185, 185, 185);
                    float dy = pr * 1.5f * s; // basılıyken içerik hafifçe aşağı kayar
                    float x = padX;
                    for (int i = 0; i < shown.Count; i++)
                    {
                        DeviceInfo d = shown[i];
                        Color tc = d.Percent >= 0 ? LevelColor(d.Percent, light) : dim;
                        if (d.PercentIsStale || d.Asleep) tc = dim;
                        float ih = g.MeasureString(Glyph(d.Kind), fIcon, PointF.Empty, sf).Height;
                        float th = g.MeasureString(texts[i], fText, PointF.Empty, sf).Height;
                        using (SolidBrush bi2 = new SolidBrush(iconColor))
                            g.DrawString(Glyph(d.Kind), fIcon, bi2, x, (h - ih) / 2f + dy, sf);
                        x += iconW[i] + gap;
                        using (SolidBrush bt = new SolidBrush(tc))
                            g.DrawString(texts[i], fText, bt, x, (h - th) / 2f + dy, sf);
                        x += textW[i] + itemGap;
                    }
                }
                return frame;
            }
        }

        void Present(Frame f, int w, int h)
        {
            if (f.HBmp == IntPtr.Zero) return;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr old = IntPtr.Zero;
            try
            {
                old = Native.SelectObject(memDc, f.HBmp);
                SIZE size = new SIZE();
                size.cx = w;
                size.cy = h;
                POINT src = new POINT();
                BLENDFUNCTION bf = new BLENDFUNCTION();
                bf.BlendOp = 0;            // AC_SRC_OVER
                bf.BlendFlags = 0;
                bf.SourceConstantAlpha = 255;
                bf.AlphaFormat = 1;        // AC_SRC_ALPHA
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref size, memDc, ref src, 0, ref bf, 2 /*ULW_ALPHA*/);
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        void SetState(bool newHover, bool newPressed)
        {
            if (newHover == hover && newPressed == pressed) return;
            hover = newHover;
            pressed = newPressed;
            if (!fx.Enabled) fx.Start();
        }

        // Üstüne gelince yumuşakça aydınlanır, basınca koyulaşır, çekilince yavaşça söner.
        void FxTick()
        {
            float hlT = pressed ? 1f : (hover ? 0.45f : 0f);
            float prT = pressed ? 1f : 0f;
            hl += (hlT - hl) * 0.22f;
            pr += (prT - pr) * 0.35f;
            if (Math.Abs(hlT - hl) < 0.01f && Math.Abs(prT - pr) < 0.01f)
            {
                hl = hlT;
                pr = prT;
                fx.Stop();
            }
            Repaint();
        }

        // Konumu değiştirmeden yalnızca içeriği yeniden çizer (animasyon karelerinde ucuzdur).
        void Repaint()
        {
            if (hidden || Handle == IntPtr.Zero || lastShown == null) return;
            Frame f = Compose(lastShown, lastS, lastLight, lastTrayH);
            Present(f, curW, curH);
            f.Dispose();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0084: // WM_NCHITTEST
                    m.Result = (IntPtr)1; // HTCLIENT
                    return;
                case 0x0021: // WM_MOUSEACTIVATE
                    m.Result = (IntPtr)3; // MA_NOACTIVATE
                    return;
                case 0x007E: // WM_DISPLAYCHANGE (oyundan çıkış / çözünürlük değişimi)
                case 0x031E: // WM_DWMCOMPOSITIONCHANGED
                case 0x001A: // WM_SETTINGCHANGE (tema vb.)
                    fresh = true;
                    break;
                case 0x0200: // WM_MOUSEMOVE
                    if (!hover)
                    {
                        TRACKMOUSEEVENT t = new TRACKMOUSEEVENT();
                        t.cbSize = (uint)Marshal.SizeOf(typeof(TRACKMOUSEEVENT));
                        t.dwFlags = 0x2; // TME_LEAVE
                        t.hwndTrack = Handle;
                        Native.TrackMouseEvent(ref t);
                        SetState(true, pressed);
                    }
                    break;
                case 0x02A3: // WM_MOUSELEAVE
                    SetState(false, false);
                    return;
                case 0x0201: // WM_LBUTTONDOWN
                    SetState(true, true);
                    return;
                case 0x0202: // WM_LBUTTONUP
                    {
                        bool was = pressed;
                        SetState(hover, false);
                        if (was && Clicked != null) Clicked(this, EventArgs.Empty);
                    }
                    return;
                case 0x0205: // WM_RBUTTONUP
                    if (RightClicked != null) RightClicked(this, EventArgs.Empty);
                    return;
            }
            base.WndProc(ref m);
        }
    }
}
