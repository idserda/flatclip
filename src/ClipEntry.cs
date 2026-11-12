using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace FlatClip
{
    public enum ClipKind { Text = 0, Image = 1, Files = 2 }

    [DataContract]
    public class ClipEntry : INotifyPropertyChanged
    {
        static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        [DataMember] public string Id { get; set; }
        [DataMember] public ClipKind Kind { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public string Rtf { get; set; }
        [DataMember] public string Html { get; set; }
        [DataMember] public string[] Files { get; set; }
        [DataMember] public string ImageFile { get; set; }
        [DataMember] public int ImageWidth { get; set; }
        [DataMember] public int ImageHeight { get; set; }
        [DataMember] public string Source { get; set; }
        [DataMember] public string Hash { get; set; }
        [DataMember] public long CreatedTicks { get; set; }
        [DataMember] public long LastCopiedTicks { get; set; }

        bool pinned;
        [DataMember]
        public bool Pinned
        {
            get { return pinned; }
            set { pinned = value; OnChanged(nameof(Pinned)); }
        }

        /// <summary>PNG bytes of a freshly captured image, until the store has written them to disk.</summary>
        public byte[] PendingImage;

        public DateTime LastCopied
        {
            get { return new DateTime(LastCopiedTicks, DateTimeKind.Utc); }
            set { LastCopiedTicks = value.ToUniversalTime().Ticks; OnChanged(nameof(Meta)); }
        }

        public bool IsImage => Kind == ClipKind.Image;
        public bool IsSnippet => false;
        public bool HasFormatting => Kind == ClipKind.Text && (Rtf != null || Html != null);

        public string Glyph
        {
            get
            {
                switch (Kind)
                {
                    case ClipKind.Image: return "";
                    case ClipKind.Files: return "";
                    default:
                        var t = Text?.Trim() ?? "";
                        if ((t.StartsWith("http://") || t.StartsWith("https://")) && !t.Any(char.IsWhiteSpace)) return "";
                        return "";
                }
            }
        }

        string title;
        public string Title
        {
            get
            {
                if (title != null) return title;
                switch (Kind)
                {
                    case ClipKind.Image: title = L.T("Image"); break;
                    case ClipKind.Files:
                        title = string.Join(", ", (Files ?? new string[0]).Select(f => Path.GetFileName(f.TrimEnd('\\')) is var n && n.Length > 0 ? n : f));
                        break;
                    default:
                        var t = Text ?? "";
                        if (t.Length > 600) t = t.Substring(0, 600);
                        title = Whitespace.Replace(t, " ").Trim();
                        break;
                }
                return title;
            }
        }

        public string Meta
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(Source)) parts.Add(Source);
                switch (Kind)
                {
                    case ClipKind.Image: parts.Add($"{ImageWidth} × {ImageHeight}"); break;
                    case ClipKind.Files:
                        var n = Files?.Length ?? 0;
                        parts.Add(n == 1 ? L.T("OneFile") : L.F("NFiles", n));
                        break;
                    default:
                        if (HasFormatting) parts.Add(L.T("Formatted"));
                        if (Text != null && Text.Length > 200) parts.Add(L.F("NChars", Text.Length.ToString("N0", L.Culture)));
                        break;
                }
                parts.Add(RelativeTime(LastCopied.ToLocalTime()));
                return string.Join("  ·  ", parts);
            }
        }

        static string RelativeTime(DateTime t)
        {
            var now = DateTime.Now;
            var diff = now - t;
            if (diff.TotalMinutes < 1) return L.T("JustNow");
            if (diff.TotalMinutes < 60) return L.F("MinAgo", (int)diff.TotalMinutes);
            if (t.Date == now.Date) return L.F("Today", t.ToString("HH:mm", L.Culture));
            if (t.Date == now.Date.AddDays(-1)) return L.F("Yesterday", t.ToString("HH:mm", L.Culture));
            if (diff.TotalDays < 6) return t.ToString("ddd HH:mm", L.Culture);
            return t.ToString("d MMM HH:mm", L.Culture);
        }

        BitmapImage thumbnail;
        public BitmapImage Thumbnail
        {
            get
            {
                if (thumbnail != null || Kind != ClipKind.Image) return thumbnail;
                try
                {
                    var bytes = PendingImage ?? Store.Current?.LoadImage(this);
                    if (bytes == null) return null;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = new MemoryStream(bytes);
                    if (ImageHeight > 180) bi.DecodePixelHeight = 180;
                    bi.EndInit();
                    bi.Freeze();
                    thumbnail = bi;
                }
                catch { }
                return thumbnail;
            }
        }

        public bool Matches(string[] terms)
        {
            foreach (var term in terms)
            {
                bool hit = (Text != null && Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (Source != null && Source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (Kind == ClipKind.Image && ("image afbeelding".IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
                if (!hit) return false;
            }
            return true;
        }

        public void RefreshMeta() => OnChanged(nameof(Meta));

        public void InvalidateTitle() { title = null; OnChanged(nameof(Title)); OnChanged(nameof(Glyph)); OnChanged(nameof(Meta)); }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
