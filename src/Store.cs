using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;

namespace FlatClip
{
    /// <summary>
    /// Clipboard history, persisted to %LOCALAPPDATA%\FlatClip. Everything on disk is encrypted with
    /// DPAPI (current Windows user only), because clipboards routinely contain sensitive data.
    /// </summary>
    public class Store
    {
        public static Store Current { get; private set; }

        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FlatClip.history.v1");

        public ObservableCollection<ClipEntry> Items { get; } = new ObservableCollection<ClipEntry>();

        readonly string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlatClip");
        string IndexPath => Path.Combine(dir, "history.dat");
        string ImageDir => Path.Combine(dir, "images");
        readonly DispatcherTimer saveTimer;

        public Store()
        {
            Current = this;
            Directory.CreateDirectory(ImageDir);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(); };
        }

        public void Load()
        {
            if (!File.Exists(IndexPath)) return;
            try
            {
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(IndexPath), Entropy, DataProtectionScope.CurrentUser);
                var ser = new DataContractJsonSerializer(typeof(List<ClipEntry>));
                var list = (List<ClipEntry>)ser.ReadObject(new MemoryStream(bytes));
                foreach (var e in list.Where(x => x != null && x.Hash != null).OrderByDescending(x => x.LastCopiedTicks))
                    Items.Add(e);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                try { File.Move(IndexPath, IndexPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { }
            }

            // Remove image files that no entry refers to anymore.
            try
            {
                var used = new HashSet<string>(Items.Where(i => i.ImageFile != null).Select(i => i.ImageFile), StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(ImageDir))
                    if (!used.Contains(Path.GetFileName(f))) File.Delete(f);
            }
            catch { }
        }

        public void Add(ClipEntry e)
        {
            var existing = Items.FirstOrDefault(x => x.Hash == e.Hash);
            if (existing != null)
            {
                // Same content copied again: bump it to the top, keep pin state, refresh formats.
                if (e.Rtf != null) existing.Rtf = e.Rtf;
                if (e.Html != null) existing.Html = e.Html;
                if (!string.IsNullOrEmpty(e.Source)) existing.Source = e.Source;
                existing.LastCopied = e.LastCopied;
                existing.InvalidateTitle();
                MoveToTop(existing);
            }
            else
            {
                if (e.Kind == ClipKind.Image && e.PendingImage != null)
                {
                    e.ImageFile = e.Id + ".bin";
                    File.WriteAllBytes(Path.Combine(ImageDir, e.ImageFile),
                        ProtectedData.Protect(e.PendingImage, Entropy, DataProtectionScope.CurrentUser));
                }
                Items.Insert(0, e);
            }
            ScheduleSave();
        }

        public void Touch(ClipEntry e)
        {
            e.LastCopied = DateTime.UtcNow;
            MoveToTop(e);
            ScheduleSave();
        }

        void MoveToTop(ClipEntry e)
        {
            var i = Items.IndexOf(e);
            if (i > 0) Items.Move(i, 0);
        }

        public void TogglePin(ClipEntry e)
        {
            e.Pinned = !e.Pinned;
            ScheduleSave();
        }

        public void Remove(ClipEntry e)
        {
            Items.Remove(e);
            DeleteImage(e);
            ScheduleSave();
        }

        public int Purge(int days)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days).Ticks;
            var old = Items.Where(x => !x.Pinned && x.LastCopiedTicks < cutoff).ToList();
            foreach (var e in old) { Items.Remove(e); DeleteImage(e); }
            if (old.Count > 0) ScheduleSave();
            return old.Count;
        }

        public void Clear(bool keepPinned)
        {
            foreach (var e in Items.Where(x => !keepPinned || !x.Pinned).ToList()) { Items.Remove(e); DeleteImage(e); }
            SaveNow();
        }

        public byte[] LoadImage(ClipEntry e)
        {
            if (e.ImageFile == null) return null;
            var path = Path.Combine(ImageDir, e.ImageFile);
            if (!File.Exists(path)) return null;
            return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        }

        void DeleteImage(ClipEntry e)
        {
            if (e.ImageFile == null) return;
            try { File.Delete(Path.Combine(ImageDir, e.ImageFile)); } catch { }
        }

        void ScheduleSave() { saveTimer.Stop(); saveTimer.Start(); }

        public void SaveNow()
        {
            saveTimer.Stop();
            try
            {
                var ms = new MemoryStream();
                new DataContractJsonSerializer(typeof(List<ClipEntry>)).WriteObject(ms, Items.ToList());
                var data = ProtectedData.Protect(ms.ToArray(), Entropy, DataProtectionScope.CurrentUser);
                var tmp = IndexPath + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(IndexPath)) File.Replace(tmp, IndexPath, null);
                else File.Move(tmp, IndexPath);
                foreach (var e in Items) e.PendingImage = null;
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }
}
