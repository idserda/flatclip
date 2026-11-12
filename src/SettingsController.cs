using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FlatClip
{
    class SettingsController
    {
        readonly App app;
        readonly Window win;
        readonly TextBox hotkeyBox, daysBox;
        readonly RadioButton themeDark, themeLight, themeSystem;
        readonly CheckBox autoStart;
        readonly TextBlock errorText;
        string pendingHotkey;

        public event Action Closed;

        public SettingsController(App app)
        {
            this.app = app;
            win = App.LoadXaml<Window>("Settings.xaml");
            T Find<T>(string name) => (T)win.FindName(name);

            hotkeyBox = Find<TextBox>("HotkeyBox");
            daysBox = Find<TextBox>("DaysBox");
            themeDark = Find<RadioButton>("ThemeDark");
            themeLight = Find<RadioButton>("ThemeLight");
            themeSystem = Find<RadioButton>("ThemeSystem");
            autoStart = Find<CheckBox>("AutoStart");
            errorText = Find<TextBlock>("ErrorText");

            var s = app.Settings;
            pendingHotkey = s.Hotkey;
            hotkeyBox.Text = s.Hotkey;
            daysBox.Text = s.RetentionDays.ToString();
            themeDark.IsChecked = s.Theme == "Dark";
            themeLight.IsChecked = s.Theme == "Light";
            themeSystem.IsChecked = s.Theme == "System";
            try { autoStart.IsChecked = AutoStart.IsEnabled(); } catch { }

            Find<FrameworkElement>("Header").MouseLeftButtonDown += (o, e) => { try { win.DragMove(); } catch { } };
            Find<Button>("CloseButton").Click += (o, e) => win.Close();
            Find<Button>("CancelButton").Click += (o, e) => win.Close();
            Find<Button>("SaveButton").Click += (o, e) => Save();
            Find<Button>("ClearButton").Click += (o, e) => Clear();

            // While the hotkey field has focus the global hotkey is suspended, so it can be pressed here.
            hotkeyBox.GotKeyboardFocus += (o, e) => { app.SuspendHotkey(); hotkeyBox.Text = "Druk op een toetscombinatie…"; };
            hotkeyBox.LostKeyboardFocus += (o, e) => { hotkeyBox.Text = pendingHotkey; app.ResumeHotkey(); };
            hotkeyBox.PreviewKeyDown += CaptureHotkey;

            daysBox.PreviewTextInput += (o, e) => e.Handled = !e.Text.All(char.IsDigit);
            win.PreviewKeyDown += (o, e) => { if (e.Key == Key.Escape && !hotkeyBox.IsKeyboardFocused) win.Close(); };
            win.Closed += (o, e) => { app.ResumeHotkey(); Closed?.Invoke(); };
        }

        public void Show()
        {
            win.Show();
            win.Activate();
        }

        void CaptureHotkey(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            if (key == Key.Tab && mods == ModifierKeys.None) return;   // let focus move on
            e.Handled = true;
            if (Hotkeys.IsModifierKey(key)) return;
            if (key == Key.Escape && mods == ModifierKeys.None) { Keyboard.ClearFocus(); win.Focus(); return; }

            bool isFunctionKey = key >= Key.F1 && key <= Key.F24;
            if (mods == ModifierKeys.None && !isFunctionKey)
            {
                ShowError("Gebruik minimaal één van Ctrl, Alt, Shift of Win (of een F-toets).");
                return;
            }
            errorText.Visibility = Visibility.Collapsed;
            pendingHotkey = Hotkeys.Format(mods, key);
            hotkeyBox.Text = pendingHotkey;
        }

        void Save()
        {
            errorText.Visibility = Visibility.Collapsed;
            if (!int.TryParse(daysBox.Text, out int days) || days < 1 || days > 3650)
            {
                ShowError("De bewaartermijn moet tussen 1 en 3650 dagen liggen.");
                return;
            }
            if (!app.TryRegisterHotkey(pendingHotkey))
            {
                ShowError($"De sneltoets {pendingHotkey} kan niet worden gebruikt; waarschijnlijk is hij al in gebruik door een ander programma.");
                return;
            }
            var theme = themeLight.IsChecked == true ? "Light" : themeSystem.IsChecked == true ? "System" : "Dark";
            app.ApplySettings(days, theme, autoStart.IsChecked == true);
            win.Close();
        }

        void Clear()
        {
            var r = MessageBox.Show(win, "Weet je zeker dat je de hele geschiedenis wilt wissen?\n\nVastgezette items blijven bewaard.",
                "FlatClip", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (r == MessageBoxResult.Yes) app.ClearHistory();
        }

        void ShowError(string msg)
        {
            errorText.Text = msg;
            errorText.Visibility = Visibility.Visible;
        }
    }
}
