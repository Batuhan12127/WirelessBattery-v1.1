using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace WirelessBattery
{
    enum DeviceKind { Other, Headset, Mouse, Keyboard, Gamepad }

    // Bir ses uç noktası (hoparlör / mikrofon) ve Windows'un onu çalıştırdığı format.
    class AudioEndpoint
    {
        public string Id = "";
        public string Name = "";
        public string Bus = "";        // "USB", "BTHENUM" ...
        public string Container = "";  // aynı fiziksel cihazı bulmak için
        public bool Render = true;     // true: çıkış, false: mikrofon
        public bool IsDefault;
        public int Rate;               // Hz
        public int Bits;
        public int Channels;
        public bool Used;              // bir cihaza eşlendi mi
    }

    class DeviceInfo
    {
        public string Key = "";
        public string Name = "";
        public string Via = "";            // "Bluetooth", "2.4 GHz alıcı", "USB kablo"
        public string Source = "";         // pilin nereden okunduğu
        public string Product = "";        // HID ürün dizesi
        public string Mac = "";
        public string Container = "";
        public DeviceKind Kind = DeviceKind.Other;
        public int Percent = -1;
        public bool Charging;
        public bool Asleep;
        public bool Connected = true;
        public bool PercentIsStale;        // eski değer gösteriliyor
        public bool AudioOnlyCard;         // pil kartı değil, "Ses aygıtları" listesi
        public int Vid, Pid;
        public List<AudioEndpoint> Audio = new List<AudioEndpoint>();
        public List<string[]> Facts = new List<string[]>();   // {başlık, değer} veya {başlık, şablon, argüman}
        public int UsbHz, UsbInterval;                        // USB'nin bildirdiği tarama hızı (varsa)
        public string UsbSpeed = "";   // {başlık, değer} - cihaza özgü ek bilgiler (klavye özellikleri vb.)

        public DeviceInfo Clone()
        {
            DeviceInfo d = (DeviceInfo)MemberwiseClone();
            d.Audio = new List<AudioEndpoint>();
            return d;
        }

        public bool HasLevel { get { return Percent >= 0; } }
    }

    static class Util
    {
        public static string Hex4(int v) { return v.ToString("X4", CultureInfo.InvariantCulture); }

        public static string KHz(int rate)
        {
            double k = rate / 1000.0;
            return k.ToString("0.##", CultureInfo.InvariantCulture) + " kHz";
        }

        public static string Channels(int c)
        {
            if (c == 1) return "Mono";
            if (c == 2) return "Stereo";
            if (c == 6) return "5.1";
            if (c == 8) return "7.1";
            return Loc.F("{0} kanal", c);
        }

        public static bool SystemLight(string valueName)
        {
            try
            {
                object o = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", valueName, 1);
                if (o is int) return (int)o != 0;
            }
            catch (Exception) { }
            return true;
        }

        public static float Scale()
        {
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            try
            {
                int dpi = Native.GetDeviceCaps(dc, 88); // LOGPIXELSX
                if (dpi < 96) dpi = 96;
                return dpi / 96f;
            }
            finally { Native.ReleaseDC(IntPtr.Zero, dc); }
        }

        const string SettingsKey = @"Software\WirelessBattery";

        public static bool GetBool(string name, bool def)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (k == null) return def;
                    object o = k.GetValue(name);
                    if (o is int) return (int)o != 0;
                }
            }
            catch (Exception) { }
            return def;
        }

        public static void SetBool(string name, bool v)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (k != null) k.SetValue(name, v ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }

        public static int GetInt(string name, int def)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (k == null) return def;
                    object o = k.GetValue(name);
                    if (o is int) return (int)o;
                }
            }
            catch (Exception) { }
            return def;
        }

        public static void SetInt(string name, int v)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (k != null) k.SetValue(name, v, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return k != null && k.GetValue("WirelessBattery") != null;
                }
            }
            catch (Exception) { return false; }
        }

        public static void SetAutostart(bool on, string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("WirelessBattery", "\"" + exePath + "\"");
                    else k.DeleteValue("WirelessBattery", false);
                }
            }
            catch (Exception) { }
        }
    }
}
