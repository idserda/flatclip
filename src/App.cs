using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace FlatClip
{
    public class App : Application
    {
        const string MutexName = @"Local\FlatClip_SingleInstance";
        const string ShowEventName = @"Local\FlatClip_Show";
        const int HotkeyId = 0xC11B;

        public AppSettings Settings { get; private set; }
        Store store;
        PopupController popup;
        SettingsController settingsWindow;
        HwndSource msgWindow;
        WinForms.NotifyIcon tray;
        WinForms.ToolStripMenuItem openItem, pauseItem;
        DispatcherTimer captureTimer, purgeTimer;
        EventWaitHandle showEvent, settingsEvent;
        string registeredHotkey;
        IntPtr pendingSourceWindow;
        readonly uint ourPid = (uint)Process.GetCurrentProcess().Id;

        const string SettingsEventName = @"Local\FlatClip_Settings";
        static bool openSettingsAtStart;

        [STAThread]
        static void Main(string[] args)
        {
            bool settings = Array.Exists(args, a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
            using (var mutex = new Mutex(true, MutexName, out bool created))
            {
                if (!created)
                {
                    // Already running: ask the running instance to open the popup (or settings).
                    try
                    {
                        Native.AllowSetForegroundWindow(-1);
                        using (var ev = EventWaitHandle.OpenExisting(settings ? SettingsEventName : ShowEventName)) ev.Set();
                    }
                    catch { }
                    return;
                }
                openSettingsAtStart = settings;
                new App().Run();
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, a) => { Log(a.Exception); a.Handled = true; };

            Settings = AppSettings.Load();
            L.Init(Settings.Language);
            Resources.MergedDictionaries.Add(LoadXaml<ResourceDictionary>("Theme.xaml"));
            ThemeManager.Apply(Resources, Settings.Theme);

            store = new Store();
            store.Load();
            store.Purge(Settings.RetentionDays);

            var snippets = new SnippetStore();
            snippets.Load();
            popup = new PopupController(this, store, snippets);

            captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            captureTimer.Tick += (s, a) => { captureTimer.Stop(); Capture(); };

            purgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            purgeTimer.Tick += (s, a) => store.Purge(Settings.RetentionDays);
            purgeTimer.Start();

            var p = new HwndSourceParameters("FlatClipMessageWindow") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
            msgWindow = new HwndSource(p);
            msgWindow.AddHook(WndProc);
            Native.AddClipboardFormatListener(msgWindow.Handle);

            CreateTray();

            if (!TryRegisterHotkey(Settings.Hotkey))
                tray.ShowBalloonTip(5000, "FlatClip", L.F("HotkeyInUse", Settings.Hotkey), WinForms.ToolTipIcon.Warning);

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(showEvent, (s, t) => Dispatcher.BeginInvoke(new Action(popup.Show)), null, -1, false);
            settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsEventName);
            ThreadPool.RegisterWaitForSingleObject(settingsEvent, (s, t) => Dispatcher.BeginInvoke(new Action(OpenSettings)), null, -1, false);
            if (openSettingsAtStart) OpenSettings();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Settings.Theme == "System")
                Dispatcher.BeginInvoke(new Action(() => ThemeManager.Apply(Resources, Settings.Theme)));
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE)
            {
                pendingSourceWindow = Native.GetForegroundWindow();
                captureTimer.Stop();
                captureTimer.Start();   // small delay: some apps put formats on the clipboard one by one
            }
            else if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                popup.Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        }

        void Capture()
        {
            if (pauseItem.Checked) return;
            try
            {
                // Ignore what we put on the clipboard ourselves (pasting from history).
                var owner = Native.GetClipboardOwner();
                if (owner != IntPtr.Zero)
                {
                    Native.GetWindowThreadProcessId(owner, out uint pid);
                    if (pid == ourPid) return;
                }
                var entry = ClipboardIO.Capture(SourceName(pendingSourceWindow));
                if (entry != null) store.Add(entry);
            }
            catch (Exception ex) { Log(ex); }
        }

        static string SourceName(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return null;
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                using (var p = Process.GetProcessById((int)pid))
                {
                    try
                    {
                        var desc = p.MainModule?.FileVersionInfo?.FileDescription;
                        if (!string.IsNullOrWhiteSpace(desc)) return desc.Trim();
                    }
                    catch { }
                    return p.ProcessName;
                }
            }
            catch { return null; }
        }

        // ---------- hotkey ----------

        public bool TryRegisterHotkey(string hotkey)
        {
            if (!Hotkeys.TryParse(hotkey, out uint mods, out uint vk)) return false;
            Native.UnregisterHotKey(msgWindow.Handle, HotkeyId);
            if (Native.RegisterHotKey(msgWindow.Handle, HotkeyId, mods | Native.MOD_NOREPEAT, vk))
            {
                registeredHotkey = hotkey;
                UpdateTrayText();
                return true;
            }
            ResumeHotkey();   // put the previous one back
            return false;
        }

        public void SuspendHotkey() => Native.UnregisterHotKey(msgWindow.Handle, HotkeyId);

        public void ResumeHotkey()
        {
            if (registeredHotkey != null && Hotkeys.TryParse(registeredHotkey, out uint mods, out uint vk))
            {
                Native.UnregisterHotKey(msgWindow.Handle, HotkeyId);
                Native.RegisterHotKey(msgWindow.Handle, HotkeyId, mods | Native.MOD_NOREPEAT, vk);
            }
        }

        // ---------- settings ----------

        public void OpenSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsController(this);
                settingsWindow.Closed += () => settingsWindow = null;
            }
            settingsWindow.Show();
        }

        public void ApplySettings(int days, string theme, bool autoStart)
        {
            Settings.Hotkey = registeredHotkey;
            Settings.RetentionDays = days;
            Settings.Theme = theme;
            Settings.Save();
            ThemeManager.Apply(Resources, theme);
            try { AutoStart.Set(autoStart); } catch (Exception ex) { Log(ex); }
            store.Purge(days);
            UpdateTrayText();
        }

        public void ClearHistory() => store.Clear(keepPinned: true);

        // ---------- tray ----------

        void CreateTray()
        {
            var menu = new WinForms.ContextMenuStrip();
            openItem = new WinForms.ToolStripMenuItem(L.T("TrayOpen"), null, (s, a) => popup.Show());
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
            pauseItem = new WinForms.ToolStripMenuItem(L.T("TrayPause")) { CheckOnClick = true };
            pauseItem.CheckedChanged += (s, a) => UpdateTrayText();
            menu.Items.Add(openItem);
            menu.Items.Add(pauseItem);
            menu.Items.Add(new WinForms.ToolStripMenuItem(L.T("TraySettings"), null, (s, a) => OpenSettings()));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(new WinForms.ToolStripMenuItem(L.T("TrayExit"), null, (s, a) => Quit()));

            tray = new WinForms.NotifyIcon { Icon = LoadTrayIcon(), ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, a) => { if (a.Button == WinForms.MouseButtons.Left) popup.Show(); };
            UpdateTrayText();
        }

        void UpdateTrayText()
        {
            if (tray == null) return;
            var hk = registeredHotkey ?? Settings.Hotkey;
            openItem.Text = $"{L.T("TrayOpen")}\t{hk}";
            var text = pauseItem.Checked ? L.T("TrayPaused") : $"FlatClip – {hk}";
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        static System.Drawing.Icon LoadTrayIcon()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("FlatClip.icon.ico"))
                return new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
        }

        void Quit()
        {
            store.SaveNow();
            Native.RemoveClipboardFormatListener(msgWindow.Handle);
            Native.UnregisterHotKey(msgWindow.Handle, HotkeyId);
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            tray.Visible = false;
            tray.Dispose();
            Shutdown();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            store.SaveNow();
            base.OnSessionEnding(e);
        }

        // ---------- helpers ----------

        public static T LoadXaml<T>(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("FlatClip." + name))
            using (var r = new StreamReader(s))
                return (T)XamlReader.Parse(L.Localize(r.ReadToEnd()));
        }

        public static void Log(Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlatClip");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            }
            catch { }
        }
    }
}
