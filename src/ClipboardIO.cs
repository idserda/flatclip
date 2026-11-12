using System;
using System.Collections.Specialized;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace FlatClip
{
    static class ClipboardIO
    {
        const int MaxText = 10 * 1024 * 1024;   // chars
        const int MaxRich = 10 * 1024 * 1024;
        const int MaxImage = 60 * 1024 * 1024;  // PNG bytes

        // Formats that password managers & co. set to say "don't record this".
        static readonly uint FmtExclude = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        static readonly uint FmtViewerIgnore = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
        const string FmtCanInclude = "CanIncludeInClipboardHistory";

        public static ClipEntry Capture(string source)
        {
            if (Native.IsClipboardFormatAvailable(FmtExclude) || Native.IsClipboardFormatAvailable(FmtViewerIgnore))
                return null;

            IDataObject data = null;
            for (int i = 0; i < 8 && data == null; i++)
            {
                try { data = Clipboard.GetDataObject(); }
                catch { Thread.Sleep(40); }
            }
            if (data == null) { App.Log("Capture skipped: clipboard stayed locked by another app."); return null; }

            try
            {
                if (data.GetDataPresent(FmtCanInclude) && data.GetData(FmtCanInclude) is MemoryStream flag && flag.Length >= 4)
                {
                    var b = new byte[4];
                    flag.Read(b, 0, 4);
                    if (BitConverter.ToInt32(b, 0) == 0) return null;
                }
            }
            catch (Exception ex) { App.Log(ex); }

            var now = DateTime.UtcNow;
            var e = new ClipEntry { Id = Guid.NewGuid().ToString("N"), CreatedTicks = now.Ticks, LastCopiedTicks = now.Ticks, Source = source };

            // 1. Files (Explorer copy)
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    e.Kind = ClipKind.Files;
                    e.Files = files;
                    e.Text = string.Join(Environment.NewLine, files);
                    e.Hash = Hash("F:" + e.Text);
                    return e;
                }
            }
            catch (Exception ex) { App.Log(ex); }

            // 2. Text (+ RTF / HTML if available)
            string text = null;
            try
            {
                if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string;
                else if (data.GetDataPresent(DataFormats.Text)) text = data.GetData(DataFormats.Text) as string;
            }
            catch (Exception ex) { App.Log(ex); }

            if (!string.IsNullOrWhiteSpace(text) && text.Length <= MaxText)
            {
                e.Kind = ClipKind.Text;
                e.Text = text;
                try { if (data.GetDataPresent(DataFormats.Rtf) && data.GetData(DataFormats.Rtf) is string rtf && rtf.Length <= MaxRich) e.Rtf = rtf; } catch (Exception ex) { App.Log(ex); }
                try { if (data.GetDataPresent(DataFormats.Html) && data.GetData(DataFormats.Html) is string html && html.Length <= MaxRich) e.Html = html; } catch (Exception ex) { App.Log(ex); }
                e.Hash = Hash("T:" + text);
                return e;
            }

            // 3. Image. Prefer the PNG format (keeps transparency), otherwise fall back to the bitmap.
            byte[] png = null;
            int w = 0, h = 0;
            try
            {
                if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream s)
                {
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    var frame = BitmapDecoder.Create(new MemoryStream(ms.ToArray()), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    png = ms.ToArray(); w = frame.PixelWidth; h = frame.PixelHeight;
                }
            }
            catch (Exception ex) { App.Log(ex); png = null; }

            if (png == null)
            {
                try
                {
                    if (WinForms.Clipboard.ContainsImage())
                    {
                        using (var img = WinForms.Clipboard.GetImage())
                        {
                            if (img != null)
                            {
                                var ms = new MemoryStream();
                                img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                png = ms.ToArray(); w = img.Width; h = img.Height;
                            }
                        }
                    }
                }
                catch (Exception ex) { App.Log(ex); }
            }

            if (png != null && png.Length <= MaxImage)
            {
                e.Kind = ClipKind.Image;
                e.PendingImage = png;
                e.ImageWidth = w;
                e.ImageHeight = h;
                using (var sha = SHA1.Create()) e.Hash = "I:" + Convert.ToBase64String(sha.ComputeHash(png));
                return e;
            }

            return null;
        }

        public static bool Set(ClipEntry e, bool plain)
        {
            var d = new DataObject();
            switch (e.Kind)
            {
                case ClipKind.Files:
                    if (!plain)
                    {
                        var sc = new StringCollection();
                        sc.AddRange(e.Files);
                        d.SetFileDropList(sc);
                    }
                    d.SetText(e.Text ?? "", TextDataFormat.UnicodeText);
                    break;

                case ClipKind.Image:
                    var bytes = e.PendingImage ?? Store.Current.LoadImage(e);
                    if (bytes == null) return false;
                    var bmp = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    d.SetImage(bmp);
                    d.SetData("PNG", new MemoryStream(bytes));
                    break;

                default:
                    d.SetText(e.Text ?? "", TextDataFormat.UnicodeText);
                    if (!plain)
                    {
                        if (e.Rtf != null) d.SetData(DataFormats.Rtf, e.Rtf);
                        if (e.Html != null) d.SetData(DataFormats.Html, e.Html);
                    }
                    break;
            }

            return SetDataObject(d);
        }

        public static bool SetText(string text)
        {
            var d = new DataObject();
            d.SetText(text ?? "", TextDataFormat.UnicodeText);
            return SetDataObject(d);
        }

        static bool SetDataObject(DataObject d)
        {
            for (int i = 0; i < 10; i++)
            {
                try { Clipboard.SetDataObject(d, true); return true; }
                catch { Thread.Sleep(40); }
            }
            return false;
        }

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }
    }
}
