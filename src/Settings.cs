using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace FlatClip
{
    [DataContract]
    public class AppSettings
    {
        [DataMember] public string Hotkey { get; set; }
        [DataMember] public int RetentionDays { get; set; }
        [DataMember] public string Theme { get; set; }   // Dark | Light | System
        [DataMember] public string Language { get; set; }   // auto | en | nl

        public AppSettings() { SetDefaults(); }

        [OnDeserializing] void OnDeserializing(StreamingContext c) => SetDefaults();

        void SetDefaults()
        {
            Hotkey = "Alt+Shift+V";
            RetentionDays = 7;
            Theme = "Dark";
            Language = "auto";
        }

        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlatClip", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var s = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(fs);
                        if (s.RetentionDays < 1) s.RetentionDays = 7;
                        if (!Hotkeys.TryParse(s.Hotkey, out _, out _)) s.Hotkey = "Alt+Shift+V";
                        return s;
                    }
            }
            catch (Exception ex) { App.Log(ex); }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                // Write to a temp file and swap it in, so a crash halfway never leaves a broken settings file.
                var tmp = FilePath + ".tmp";
                using (var fs = File.Create(tmp))
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(w, this);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    static class Hotkeys
    {
        public static bool TryParse(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var raw in s.Split('+'))
            {
                var p = raw.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win": case "windows": mods |= Native.MOD_WIN; break;
                    default:
                        if (vk != 0) return false;
                        var key = ParseKey(p);
                        if (key == Key.None) return false;
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        break;
                }
            }
            return vk != 0;
        }

        static Key ParseKey(string p)
        {
            if (p.Length == 1 && char.IsDigit(p[0])) return Key.D0 + (p[0] - '0');
            return Enum.TryParse(p, true, out Key k) ? k : Key.None;
        }

        public static string Format(ModifierKeys m, Key k)
        {
            var sb = new StringBuilder();
            if (m.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
            if (m.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            if (m.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (m.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
            sb.Append(k >= Key.D0 && k <= Key.D9 ? ((int)(k - Key.D0)).ToString() : k.ToString());
            return sb.ToString();
        }

        public static bool IsModifierKey(Key k) =>
            k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt ||
            k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin;
    }

    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "FlatClip";
        static string Exe => System.Reflection.Assembly.GetExecutingAssembly().Location;

        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k?.GetValue(Name) is string v && v.IndexOf(Exe, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Name, "\"" + Exe + "\"");
                else if (k.GetValue(Name) != null) k.DeleteValue(Name);
            }
        }
    }

    static class ThemeManager
    {
        static readonly (string Key, string Dark, string Light)[] Palette =
        {
            ("BgBrush",          "#1E1F22", "#FFFFFF"),
            ("SurfaceBrush",     "#2A2B2F", "#F3F4F6"),
            ("StrokeBrush",      "#34363B", "#E2E4E8"),
            ("HoverBrush",       "#27282C", "#F4F5F7"),
            ("SelectedBrush",    "#30333B", "#E9EFFE"),
            ("TextBrush",        "#ECECEE", "#1B1C1F"),
            ("MutedBrush",       "#8B8E96", "#6B6F78"),
            ("AccentBrush",      "#5B8CFF", "#3A6FF7"),
            ("AccentHoverBrush", "#7AA2FF", "#2F5FE0"),
            ("AccentTextBrush",  "#FFFFFF", "#FFFFFF"),
            ("ThumbBrush",       "#44464C", "#C9CCD2"),
            ("ThumbHoverBrush",  "#5A5D64", "#A9ADB5"),
            ("DangerBrush",      "#F2555A", "#D93036"),
            ("KbdBrush",         "#2E3035", "#EEF0F3"),
        };

        public static bool IsDark(string theme)
        {
            if (theme == "Light") return false;
            if (theme == "System")
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return !(k?.GetValue("AppsUseLightTheme") is int v && v == 1);
            }
            return true;
        }

        public static void Apply(ResourceDictionary res, string theme)
        {
            bool dark = IsDark(theme);
            foreach (var p in Palette)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? p.Dark : p.Light));
                b.Freeze();
                res[p.Key] = b;
            }
        }
    }
}
