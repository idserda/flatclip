using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FlatClip
{
    /// <summary>Dialog for creating / editing a snippet.</summary>
    class SnippetEditor
    {
        public static void Open(SnippetStore store, Snippet existing, string prefill, Action<Snippet> closed)
        {
            var win = App.LoadXaml<Window>("SnippetEditor.xaml");
            T Find<T>(string name) => (T)win.FindName(name);
            var nameBox = Find<TextBox>("NameBox");
            var contentBox = Find<TextBox>("ContentBox");
            var error = Find<TextBlock>("ErrorText");
            var deleteButton = Find<Button>("DeleteButton");
            Snippet result = null;

            Find<TextBlock>("HeaderText").Text = L.T(existing == null ? "NewSnippet" : "EditSnippet");
            deleteButton.Visibility = existing == null ? Visibility.Collapsed : Visibility.Visible;
            nameBox.Text = existing?.Name ?? "";
            contentBox.Text = existing?.Content ?? prefill ?? "";

            void Save()
            {
                var name = nameBox.Text.Trim();
                if (name.Length == 0) { error.Text = L.T("NeedName"); error.Visibility = Visibility.Visible; nameBox.Focus(); return; }
                if (contentBox.Text.Length == 0) { error.Text = L.T("EmptyContent"); error.Visibility = Visibility.Visible; contentBox.Focus(); return; }
                var s = existing ?? new Snippet();
                s.Name = name;
                s.Content = contentBox.Text;
                store.Upsert(s);
                result = s;
                win.Close();
            }

            Find<FrameworkElement>("Header").MouseLeftButtonDown += (o, e) => { try { win.DragMove(); } catch { } };
            Find<Button>("CloseButton").Click += (o, e) => win.Close();
            Find<Button>("CancelButton").Click += (o, e) => win.Close();
            Find<Button>("SaveButton").Click += (o, e) => Save();
            deleteButton.Click += (o, e) =>
            {
                if (MessageBox.Show(win, L.F("DeleteSnippetConfirm", existing.Name), "FlatClip",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                store.Remove(existing);
                win.Close();
            };
            win.PreviewKeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape) { win.Close(); e.Handled = true; }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || nameBox.IsKeyboardFocused)) { Save(); e.Handled = true; }
            };
            win.Closed += (o, e) => closed?.Invoke(result);

            win.Show();
            win.Activate();
            Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(win).Handle);
            var focus = existing == null ? nameBox : contentBox;
            focus.Focus();
            focus.SelectAll();
        }
    }
}
