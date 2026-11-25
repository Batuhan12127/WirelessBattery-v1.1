using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("WirelessBattery")]
[assembly: System.Reflection.AssemblyProduct("WirelessBattery")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace WirelessBattery
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            if (args.Length > 0 && (args[0] == "--rapor" || args[0] == "--report"))
            {
                Report.Run();
                return;
            }

            bool created;
            using (Mutex m = new Mutex(true, "WirelessBattery_SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
                GC.KeepAlive(m);
            }
        }
    }

    class App : ApplicationContext
    {
        readonly Control ui = new Control();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly TaskbarWidget widget = new TaskbarWidget();
        readonly MouseRateMonitor rate;
        readonly PopupForm popup;
        readonly Poller poller;
        readonly System.Windows.Forms.Timer layoutTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer topTimer = new System.Windows.Forms.Timer();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolStripMenuItem miShow = new ToolStripMenuItem();
        readonly ToolStripMenuItem miRefresh = new ToolStripMenuItem();
        readonly ToolStripMenuItem miAutostart = new ToolStripMenuItem();
        readonly ToolStripMenuItem miTray = new ToolStripMenuItem();
        readonly ToolStripMenuItem miFont = new ToolStripMenuItem();
        readonly ToolStripMenuItem miTheme = new ToolStripMenuItem();
        readonly ToolStripMenuItem miLang = new ToolStripMenuItem();
        readonly ToolStripMenuItem miReport = new ToolStripMenuItem();
        readonly ToolStripMenuItem miUninstall = new ToolStripMenuItem();
        readonly ToolStripMenuItem miExit = new ToolStripMenuItem();
        WinEventDelegate winProc;
        IntPtr winHook = IntPtr.Zero;
        List<DeviceInfo> devices = new List<DeviceInfo>();
        Icon curIcon;

        public App()
        {
            IntPtr force = ui.Handle; // BeginInvoke için UI iş parçacığında handle oluştur
            GC.KeepAlive(force);

            rate = new MouseRateMonitor();
            popup = new PopupForm(rate);
            popup.PopupOpened += delegate { poller.SetFast(true); rate.Enable(true); };
            popup.PopupClosed += delegate { poller.SetFast(false); rate.Enable(false); };
            popup.LanguageChanged += delegate { ApplyLanguage(); };

            // ---- sağ tık menüsü (metinler ApplyLanguage ile atanır)
            miShow.Click += delegate { TogglePopup(); };
            miRefresh.Click += delegate { poller.RefreshNow(); };
            miAutostart.Click += delegate
            {
                Util.SetAutostart(!Util.IsAutostart(), Application.ExecutablePath);
                miAutostart.Checked = Util.IsAutostart();
            };
            miTray.Checked = Util.GetBool("TrayIcon", true);
            miTray.Click += delegate
            {
                bool on = !Util.GetBool("TrayIcon", true);
                Util.SetBool("TrayIcon", on);
                miTray.Checked = on;
                tray.Visible = on;
            };

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                ToolStripMenuItem it = new ToolStripMenuItem();
                it.Click += delegate { Util.SetInt("WidgetFont", idx); widget.Relayout(); };
                miFont.DropDownItems.Add(it);
            }
            miFont.DropDownOpening += delegate
            {
                int cur = Util.GetInt("WidgetFont", 1);
                for (int j = 0; j < miFont.DropDownItems.Count; j++)
                    ((ToolStripMenuItem)miFont.DropDownItems[j]).Checked = (j == cur);
            };

            for (int i = 0; i < 3; i++)
            {
                int tidx = i;
                ToolStripMenuItem it2 = new ToolStripMenuItem();
                it2.Click += delegate { Util.SetInt("PanelTheme", tidx); };
                miTheme.DropDownItems.Add(it2);
            }
            miTheme.DropDownOpening += delegate
            {
                int cur = Util.GetInt("PanelTheme", 0);
                for (int j = 0; j < miTheme.DropDownItems.Count; j++)
                    ((ToolStripMenuItem)miTheme.DropDownItems[j]).Checked = (j == cur);
            };

            for (int i = 0; i < Loc.Names.Length; i++)
            {
                int lidx = i;
                ToolStripMenuItem it3 = new ToolStripMenuItem(Loc.Names[i]);
                it3.Click += delegate
                {
                    Loc.Set(lidx);
                    ApplyLanguage();
                };
                miLang.DropDownItems.Add(it3);
            }
            miLang.DropDownOpening += delegate
            {
                for (int j = 0; j < miLang.DropDownItems.Count; j++)
                    ((ToolStripMenuItem)miLang.DropDownItems[j]).Checked = (j == Loc.Index);
            };

            miReport.Click += delegate { ThreadPool.QueueUserWorkItem(delegate { Report.Run(); }); };
            miUninstall.Click += delegate { Uninstall(); };
            miExit.Click += delegate { Quit(); };

            menu.Items.Add(miShow);
            menu.Items.Add(miRefresh);
            menu.Items.Add(miAutostart);
            menu.Items.Add(miTray);
            menu.Items.Add(miFont);
            menu.Items.Add(miTheme);
            menu.Items.Add(miLang);
            menu.Items.Add(miReport);
            menu.Items.Add(miUninstall);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);
            menu.Opening += delegate { miAutostart.Checked = Util.IsAutostart(); };

            tray.ContextMenuStrip = menu;
            tray.Icon = MakeIcon("--", Color.White);
            tray.Visible = Util.GetBool("TrayIcon", true);
            tray.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) TogglePopup();
            };

            widget.Clicked += delegate { TogglePopup(); };
            widget.RightClicked += delegate { menu.Show(Cursor.Position); };

            // Gösterge görev çubuğunun arkasında kalırsa hemen öne al: hem hızlı bir zamanlayıcı hem de
            // "öndeki pencere değişti" olaylarıyla.
            topTimer.Interval = 150;
            topTimer.Tick += delegate { widget.KeepOnTop(); };
            topTimer.Start();
            winProc = new WinEventDelegate(delegate(IntPtr hHook, uint evType, IntPtr hWnd, int idObj, int idChild, uint evThread, uint evTime)
            {
                widget.KeepOnTop();
            });
            winHook = Native.SetWinEventHook(0x0003, 0x0017, IntPtr.Zero, winProc, 0, 0, 0);

            layoutTimer.Interval = 1000;
            layoutTimer.Tick += delegate { widget.Relayout(); };
            layoutTimer.Start();

            poller = new Poller(ui, OnData);
            poller.Start();

            // Cihaz takılıp çıkınca / uykudan dönünce hemen yenile (sürekli tarama gerekmesin).
            debounce.Interval = 1500;
            debounce.Tick += delegate { debounce.Stop(); poller.RefreshNow(); };
            rate.SystemChanged += delegate
            {
                // Bu olay ham giriş iş parçacığında gelir: arayüz iş parçacığına taşı.
                try
                {
                    ui.BeginInvoke(new MethodInvoker(delegate { debounce.Stop(); debounce.Start(); }));
                }
                catch (Exception) { }
            };

            ApplyLanguage();
            ui.BeginInvoke(new MethodInvoker(AskAutostart));
        }

        // Menü ve panel metinlerini geçerli dile çevirir.
        void ApplyLanguage()
        {
            miShow.Text = Loc.T("Ayrıntıları göster");
            miRefresh.Text = Loc.T("Şimdi yenile");
            miAutostart.Text = Loc.T("Windows ile başlat");
            miTray.Text = Loc.T("Tepsi ikonunu göster");
            miFont.Text = Loc.T("Gösterge yazı boyutu");
            string[] fn = { "Küçük", "Orta", "Büyük" };
            for (int i = 0; i < miFont.DropDownItems.Count && i < fn.Length; i++) miFont.DropDownItems[i].Text = Loc.T(fn[i]);
            miTheme.Text = Loc.T("Panel teması");
            string[] tn = { "Otomatik (Windows'u izle)", "Açık", "Koyu" };
            for (int i = 0; i < miTheme.DropDownItems.Count && i < tn.Length; i++) miTheme.DropDownItems[i].Text = Loc.T(tn[i]);
            miLang.Text = Loc.T("Dil");
            miReport.Text = Loc.T("Rapor oluştur (sorun giderme)");
            miUninstall.Text = Loc.T("Programı kaldır…");
            miExit.Text = Loc.T("Çıkış");
            tray.Text = Loc.T("Kablosuz pil");
            popup.RefreshUi();
            UpdateTray();
        }

        void OnData(List<DeviceInfo> d)
        {
            devices = d;
            widget.Update(d);
            popup.SetData(d);
            UpdateTray();
        }

        // İlk çalıştırmada bir kez sorar: bilgisayar açılınca otomatik başlasın mı?
        void AskAutostart()
        {
            if (Util.GetBool("AskedAutostart", false)) return;
            Util.SetBool("AskedAutostart", true);
            if (Util.IsAutostart()) return;
            DialogResult r = MessageBox.Show(
                Loc.T("Bilgisayar her açıldığında WirelessBattery kendiliğinden başlasın mı?\n\n(İstediğin zaman gösterge üzerinde sağ tık > \"Windows ile başlat\" ile değiştirebilirsin.)"),
                "WirelessBattery", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) Util.SetAutostart(true, Application.ExecutablePath);
        }

        void TogglePopup()
        {
            if (popup.IsOpen) { popup.HidePopup(); return; }
            if (popup.Visible) return; // kapanış animasyonu sürüyor
            if (Math.Abs(Environment.TickCount - popup.LastHideTick) < 300) return;
            popup.SetData(devices);
            popup.ShowNear(Cursor.Position);
        }

        void UpdateTray()
        {
            DeviceInfo primary = null;
            StringBuilder tip = new StringBuilder();
            foreach (DeviceInfo d in devices)
            {
                if (!d.Connected || d.Percent < 0) continue;
                if (primary == null) primary = d;
                string line = d.Name + " %" + d.Percent;
                if (tip.Length + line.Length + 1 < 63)
                {
                    if (tip.Length > 0) tip.Append('\n');
                    tip.Append(line);
                }
            }

            bool light = Util.SystemLight("SystemUsesLightTheme");
            string txt = primary != null ? primary.Percent.ToString() : "--";
            Color col = light ? Color.FromArgb(24, 24, 24) : Color.White;
            if (primary != null && primary.Percent <= 15) col = Color.FromArgb(255, 90, 90);

            Icon old = curIcon;
            curIcon = MakeIcon(txt, col);
            tray.Icon = curIcon;
            if (old != null) old.Dispose();
            string t = tip.Length > 0 ? tip.ToString() : Loc.T("Kablosuz pil: cihaz yok");
            if (t.Length > 63) t = t.Substring(0, 63);
            tray.Text = t;
        }

        static Icon MakeIcon(string txt, Color col)
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    float px = txt.Length >= 3 ? 16f : 24f;
                    using (Font f = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(col))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(txt, f, b, new RectangleF(0, 0, 32, 32), sf);
                    }
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
                }
                finally { Native.DestroyIcon(h); }
            }
        }

        // Başlangıç kaydını, ayarları ve program dosyalarını siler. Klasörü yalnızca boşsa kaldırır
        // (başka dosyalarına dokunmaz).
        void Uninstall()
        {
            DialogResult r = MessageBox.Show(
                Loc.T("WirelessBattery kaldırılsın mı?\n\n• Windows ile başlama kaydı silinir\n• Program dosyaları (exe, kaynak kodlar, build.bat) silinir\n\nKlasör: ") + Application.StartupPath,
                Loc.T("WirelessBattery'yi kaldır"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            try
            {
                Util.SetAutostart(false, "");
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\WirelessBattery", false); } catch (Exception) { }

                string dir = Application.StartupPath.TrimEnd('\\');
                string exe = Application.ExecutablePath;
                string bat = Path.Combine(Path.GetTempPath(), "wb_uninstall.bat");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("ping 127.0.0.1 -n 4 >nul");
                sb.AppendLine("del /f /q \"" + exe + "\"");
                sb.AppendLine("del /f /q \"" + dir + "\\build.bat\" \"" + dir + "\\README.txt\" \"" + dir + "\\kaldir.bat\"");
                sb.AppendLine("rd /s /q \"" + dir + "\\src\"");
                sb.AppendLine("rd \"" + dir + "\"");
                sb.AppendLine("del /f /q \"" + Path.Combine(Path.GetTempPath(), "WirelessBattery-rapor.txt") + "\"");
                sb.AppendLine("del /f /q \"%~f0\"");
                File.WriteAllText(bat, sb.ToString(), Encoding.Default);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("Kaldırma sırasında hata: ") + ex.Message, "WirelessBattery");
                return;
            }
            Quit();
        }

        void Quit()
        {
            layoutTimer.Stop();
            topTimer.Stop();
            debounce.Stop();
            if (winHook != IntPtr.Zero) Native.UnhookWinEvent(winHook);
            tray.Visible = false;
            tray.Dispose();
            widget.Detach();
            poller.Stop();
            rate.Enable(false);
            ExitThread();
        }
    }

    // Sorun giderme raporu: bulunan her şeyi bir metin dosyasına yazar (Not Defteri'nde açılır).
    static class Report
    {
        public static void Run()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WirelessBattery raporu  " + DateTime.Now);
            sb.AppendLine("Windows: " + Environment.OSVersion + "   64-bit: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine();

            sb.AppendLine("=== Gösterge (görev çubuğu) teşhisi ===");
            sb.AppendLine(TaskbarWidget.LastDiag);
            sb.AppendLine();

            sb.AppendLine("=== Bluetooth cihazlar (pil bilgisi olanlar) ===");
            try
            {
                List<DeviceInfo> bt = BluetoothProvider.Query();
                if (bt.Count == 0) sb.AppendLine("(yok)");
                foreach (DeviceInfo d in bt)
                    sb.AppendLine(d.Name + " | %" + d.Percent + " | bağlı=" + d.Connected + " | " + d.Kind + " | " + d.Mac + " | " + d.Container);
            }
            catch (Exception ex) { sb.AppendLine("HATA: " + ex.Message); }
            sb.AppendLine();

            sb.AppendLine("=== Ses aygıtları (aktif) ===");
            try
            {
                foreach (AudioEndpoint a in AudioProvider.Query())
                    sb.AppendLine((a.Render ? "çıkış   " : "mikrofon") + " | " + a.Name + " | " +
                                  (a.Rate > 0 ? Util.KHz(a.Rate) + ", " + a.Bits + " bit, " + Util.Channels(a.Channels) : "format yok") +
                                  " | bus=" + a.Bus + " | varsayılan=" + a.IsDefault + " | " + a.Container);
            }
            catch (Exception ex) { sb.AppendLine("HATA: " + ex.Message); }
            sb.AppendLine();

            sb.AppendLine("=== Üretici sürücüleri (HID) ===");
            try
            {
                List<DeviceInfo> hid = Poller.QueryHid();
                if (hid.Count == 0) sb.AppendLine("(yok)");
                foreach (DeviceInfo d in hid)
                    sb.AppendLine(d.Name + " | %" + d.Percent + " | şarj=" + d.Charging + " | uyuyor=" + d.Asleep + " | " + d.Via + " | " +
                                  Util.Hex4(d.Vid) + ":" + Util.Hex4(d.Pid) + " | " + d.Source);
            }
            catch (Exception ex) { sb.AppendLine("HATA: " + ex.Message); }
            sb.AppendLine();

            sb.AppendLine("=== Tüm HID arayüzleri ===");
            try
            {
                foreach (HidColl c in HidLayer.Enumerate())
                    sb.AppendLine(Util.Hex4(c.Vid) + ":" + Util.Hex4(c.Pid) + "  sayfa=" + Util.Hex4(c.UsagePage) + " kullanım=" + Util.Hex4(c.Usage) +
                                  "  arayüz=" + c.Iface + "  in=" + c.InLen + " out=" + c.OutLen + " feat=" + c.FeatLen + "  " + c.Manufacturer + " / " + c.Product);
            }
            catch (Exception ex) { sb.AppendLine("HATA: " + ex.Message); }

            try
            {
                string path = Path.Combine(Path.GetTempPath(), "WirelessBattery-rapor.txt");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception) { }
        }
    }
}
