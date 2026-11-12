using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace FlatClip
{
    class PopupController
    {
        enum Mode { History, Snippets }

        readonly Store store;
        readonly SnippetStore snippets;
        readonly Window win;
        readonly TextBox search;
        readonly ListBox list;
        readonly TextBlock placeholder, countText, emptyText;
        readonly FrameworkElement emptyPanel, historyHints, snippetHints;
        readonly RadioButton tabHistory, tabSnippets;
        readonly ListCollectionView historyView, snippetView;
        readonly IntPtr hwnd;
        IntPtr previousWindow;
        string[] terms = new string[0];
        Mode mode = Mode.History;
        bool pasting, editorOpen;
        Snippet pendingDelete;

        public PopupController(App app, Store store, SnippetStore snippets)
        {
            this.store = store;
            this.snippets = snippets;
            win = App.LoadXaml<Window>("Popup.xaml");
            T Find<T>(string name) => (T)win.FindName(name);
            search = Find<TextBox>("Search");
            list = Find<ListBox>("List");
            placeholder = Find<TextBlock>("Placeholder");
            countText = Find<TextBlock>("CountText");
            emptyText = Find<TextBlock>("EmptyText");
            emptyPanel = Find<FrameworkElement>("EmptyPanel");
            historyHints = Find<FrameworkElement>("HistoryHints");
            snippetHints = Find<FrameworkElement>("SnippetHints");
            tabHistory = Find<RadioButton>("TabHistory");
            tabSnippets = Find<RadioButton>("TabSnippets");

            // In the history tab, snippets only show up (on top) when they match the search.
            snippetView = new ListCollectionView(snippets.Items)
            {
                Filter = o => mode == Mode.Snippets ? (terms.Length == 0 || ((Snippet)o).Matches(terms))
                                                    : (terms.Length > 0 && ((Snippet)o).Matches(terms))
            };
            historyView = new ListCollectionView(store.Items)
            {
                Filter = o => mode == Mode.History && (terms.Length == 0 || ((ClipEntry)o).Matches(terms))
            };
            list.ItemsSource = new CompositeCollection
            {
                new CollectionContainer { Collection = snippetView },
                new CollectionContainer { Collection = historyView },
            };
            store.Items.CollectionChanged += (s, e) => UpdateStatus();
            snippets.Items.CollectionChanged += (s, e) => UpdateStatus();

            hwnd = new WindowInteropHelper(win).EnsureHandle();

            search.TextChanged += (s, e) =>
            {
                placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                terms = search.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                Refresh();
            };

            tabHistory.Checked += (s, e) => SetMode(Mode.History);
            tabSnippets.Checked += (s, e) => SetMode(Mode.Snippets);

            win.PreviewKeyDown += OnKeyDown;
            win.Deactivated += (s, e) => { if (!pasting && !editorOpen) Hide(); };
            list.MouseDoubleClick += (s, e) =>
            {
                if (ItemUnderMouse(e) != null) Paste(list.SelectedItem, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            };
            // Keep typing going to the search box after clicking an item.
            list.PreviewMouseLeftButtonUp += (s, e) => win.Dispatcher.BeginInvoke(new Action(() => search.Focus()));
            list.SelectionChanged += (s, e) => ClearPendingDelete();
            list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (e.OriginalSource is Button b && (b.Tag as string) == "edit" && b.DataContext is Snippet sn)
                    EditSnippet(sn, null);
            }));
            Find<Button>("SettingsButton").Click += (s, e) => { Hide(); app.OpenSettings(); };
            Find<Button>("NewSnippetButton").Click += (s, e) => EditSnippet(null, null);
        }

        ListBoxItem ItemUnderMouse(MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is ListBoxItem) && !(d is Button)) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return d as ListBoxItem;
        }

        public void Toggle()
        {
            if (win.IsVisible) Hide(); else Show();
        }

        public void Show() => Show(keepTarget: false, select: null);

        void Show(bool keepTarget, object select)
        {
            if (!keepTarget)
            {
                var fg = Native.GetForegroundWindow();
                if (fg != hwnd) previousWindow = fg;
                tabHistory.IsChecked = true;
            }

            foreach (var item in store.Items) item.RefreshMeta();
            if (!keepTarget) search.Text = "";
            Refresh();
            if (select != null)
            {
                var i = list.Items.IndexOf(select);
                if (i >= 0) Select(i);
            }

            PositionOnActiveScreen();
            win.Show();
            win.Activate();
            Native.SetForegroundWindow(hwnd);
            search.Focus();
            Keyboard.Focus(search);
        }

        public void Hide()
        {
            if (win.IsVisible) win.Hide();
        }

        void SetMode(Mode m)
        {
            if (mode == m) return;
            mode = m;
            tabHistory.IsChecked = m == Mode.History;
            tabSnippets.IsChecked = m == Mode.Snippets;
            historyHints.Visibility = m == Mode.History ? Visibility.Visible : Visibility.Collapsed;
            snippetHints.Visibility = m == Mode.Snippets ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
            search.Focus();
        }

        void Refresh()
        {
            snippetView.Refresh();
            historyView.Refresh();
            Select(0);
            UpdateStatus();
        }

        void PositionOnActiveScreen()
        {
            var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
            double scale;
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
            var left = screen.Left / scale + (screen.Width / scale - win.Width) / 2;
            var top = screen.Top / scale + (screen.Height / scale - win.Height) / 2.6;
            win.Left = Math.Round(left);
            win.Top = Math.Round(Math.Max(screen.Top / scale, top));
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            var selected = list.SelectedItem;
            bool ctrl = mods.HasFlag(ModifierKeys.Control);

            if (!(key == Key.Delete && mods.HasFlag(ModifierKeys.Shift))) ClearPendingDelete();

            switch (key)
            {
                case Key.Escape:
                    Hide(); e.Handled = true; break;
                case Key.Enter:
                    Paste(selected, plain: mods.HasFlag(ModifierKeys.Alt)); e.Handled = true; break;
                case Key.Tab:
                    SetMode(mode == Mode.History ? Mode.Snippets : Mode.History); e.Handled = true; break;
                case Key.Down: Select(list.SelectedIndex + 1); e.Handled = true; break;
                case Key.Up: Select(list.SelectedIndex - 1); e.Handled = true; break;
                case Key.PageDown: Select(list.SelectedIndex + 8); e.Handled = true; break;
                case Key.PageUp: Select(list.SelectedIndex - 8); e.Handled = true; break;
                case Key.Home when ctrl: Select(0); e.Handled = true; break;
                case Key.End when ctrl: Select(list.Items.Count - 1); e.Handled = true; break;

                case Key.Delete when mods.HasFlag(ModifierKeys.Shift):
                    if (selected is ClipEntry entry)
                    {
                        var i = list.SelectedIndex;
                        store.Remove(entry);
                        Select(Math.Min(i, list.Items.Count - 1));
                    }
                    else if (selected is Snippet sn)
                    {
                        if (pendingDelete == sn)
                        {
                            var i = list.SelectedIndex;
                            pendingDelete = null;
                            snippets.Remove(sn);
                            Select(Math.Min(i, list.Items.Count - 1));
                        }
                        else
                        {
                            pendingDelete = sn;   // ask for confirmation by pressing it twice
                            countText.Text = L.T("ConfirmDelete");
                            countText.Foreground = (System.Windows.Media.Brush)win.FindResource("DangerBrush");
                        }
                    }
                    e.Handled = true; break;

                case Key.P when ctrl:
                    if (selected is ClipEntry pin) store.TogglePin(pin);
                    e.Handled = true; break;
                case Key.N when ctrl:
                    EditSnippet(null, null); e.Handled = true; break;
                case Key.E when ctrl:
                case Key.F2:
                    if (selected is Snippet edit) EditSnippet(edit, null);
                    e.Handled = true; break;
                case Key.S when ctrl:
                    if (selected is ClipEntry c && c.Kind != ClipKind.Image) EditSnippet(null, c.Text);
                    e.Handled = true; break;

                default:
                    if (!search.IsKeyboardFocused && !ctrl && !mods.HasFlag(ModifierKeys.Alt))
                        search.Focus();   // typed text lands in the search box
                    break;
            }
        }

        void ClearPendingDelete()
        {
            if (pendingDelete == null) return;
            pendingDelete = null;
            UpdateStatus();
        }

        void EditSnippet(Snippet existing, string prefill)
        {
            editorOpen = true;
            Hide();
            SnippetEditor.Open(snippets, existing, prefill, saved =>
            {
                editorOpen = false;
                // Back to the popup, on the snippets tab, with the paste target unchanged.
                SetMode(Mode.Snippets);
                Show(keepTarget: true, select: saved);
            });
        }

        void Select(int index)
        {
            var count = list.Items.Count;
            if (count == 0) { list.SelectedIndex = -1; return; }
            list.SelectedIndex = Math.Max(0, Math.Min(count - 1, index));
            list.ScrollIntoView(list.SelectedItem);
        }

        void UpdateStatus()
        {
            countText.ClearValue(TextBlock.ForegroundProperty);
            countText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            int shown = list.Items.Count;
            if (mode == Mode.Snippets)
            {
                var total = snippets.Items.Count;
                countText.Text = terms.Length == 0 ? (total == 1 ? L.T("OneSnippet") : L.F("NSnippets", total)) : L.F("NofM", shown, total);
                emptyText.Text = L.T(total == 0 ? "NoSnippets" : "NoSnippetsFound");
            }
            else
            {
                var total = store.Items.Count;
                countText.Text = terms.Length == 0 ? (total == 1 ? L.T("OneItem") : L.F("NItems", total)) : (shown == 1 ? L.T("OneResult") : L.F("NResults", shown));
                emptyText.Text = L.T(total == 0 ? "NothingCopied" : "NoResults");
            }
            emptyPanel.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async void Paste(object item, bool plain)
        {
            if (item == null) return;
            pasting = true;
            try
            {
                win.Hide();
                bool ok = item is Snippet sn ? ClipboardIO.SetText(sn.Content) : ClipboardIO.Set((ClipEntry)item, plain);
                if (!ok) return;
                if (item is ClipEntry entry) store.Touch(entry);
                if (previousWindow != IntPtr.Zero && Native.IsWindow(previousWindow))
                    Native.SetForegroundWindow(previousWindow);
                await Task.Delay(60);
                Native.SendCtrlV();
            }
            catch (Exception ex) { App.Log(ex); }
            finally { pasting = false; }
        }
    }
}
