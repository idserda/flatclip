using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace FlatClip
{
    [DataContract]
    public class Snippet : INotifyPropertyChanged
    {
        static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        [DataMember] public string Name { get; set; }
        [DataMember] public string Content { get; set; }

        // Same display properties as ClipEntry, so both share one item template.
        public bool IsSnippet => true;
        public bool IsImage => false;
        public bool Pinned => false;
        public object Thumbnail => null;
        public string Glyph => "";
        public string Title => Name;
        public string Meta
        {
            get
            {
                var t = Content ?? "";
                if (t.Length > 300) t = t.Substring(0, 300);
                return "Snippet  ·  " + Whitespace.Replace(t, " ").Trim();
            }
        }

        public bool Matches(string[] terms) => terms.All(term =>
            (Name ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (Content ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

        public void RefreshMeta() { }

        public void Changed()
        {
            foreach (var p in new[] { nameof(Name), nameof(Content), nameof(Title), nameof(Meta) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Snippets in %APPDATA%\FlatClip\snippets.json (plain JSON, so it can be edited and backed up by hand).</summary>
    public class SnippetStore
    {
        public ObservableCollection<Snippet> Items { get; } = new ObservableCollection<Snippet>();

        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlatClip", "snippets.json");

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                using (var fs = File.OpenRead(FilePath))
                {
                    var list = (List<Snippet>)new DataContractJsonSerializer(typeof(List<Snippet>)).ReadObject(fs);
                    foreach (var s in list.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                        Items.Add(s);
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var tmp = FilePath + ".tmp";
                using (var fs = File.Create(tmp))
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(List<Snippet>)).WriteObject(w, Items.ToList());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>Adds a new snippet or saves changes to an existing one; keeps the list sorted by name.</summary>
        public void Upsert(Snippet s)
        {
            Items.Remove(s);
            int i = 0;
            while (i < Items.Count && string.Compare(Items[i].Name, s.Name, StringComparison.CurrentCultureIgnoreCase) <= 0) i++;
            Items.Insert(i, s);
            s.Changed();
            Save();
        }

        public void Remove(Snippet s)
        {
            Items.Remove(s);
            Save();
        }
    }
}
