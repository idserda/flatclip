using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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
        readonly object writeLock = new object();

        /// <summary>Max number of unpinned entries; older ones are dropped.</summary>
        const int MaxUnpinned = 1000;

        public Store()
        {
            Current = this;
            Directory.CreateDirectory(ImageDir);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            saveTimer.Tick += (s, e) => SaveInBackground();
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
                // Keep the image files: they belong to the entries in the .corrupt backup.
                return;
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
                // Same content copied again: bump it to the top, keep pin state, take over the new formats
                // (also when they are gone now, so pasting never brings back formatting that wasn't copied).
                existing.Rtf = e.Rtf;
                existing.Html = e.Html;
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
                TrimToMax();
            }
            ScheduleSave();
        }

        /// <summary>Caps the history so saving (which rewrites the whole file) stays fast. Pinned items don't count.</summary>
        void TrimToMax()
        {
            var unpinned = Items.Where(x => !x.Pinned).ToList();
            for (int i = MaxUnpinned; i < unpinned.Count; i++) { Items.Remove(unpinned[i]); DeleteImage(unpinned[i]); }
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

        /// <summary>Saves synchronously (on exit / logoff); waits for a background save that is still running.</summary>
        public void SaveNow()
        {
            var json = Serialize();
            if (json != null) Write(json);
        }

        /// <summary>
        /// Serializes on the UI thread (the entries are only touched there), then encrypts and writes
        /// on a worker thread so a large history doesn't make the popup stutter.
        /// </summary>
        void SaveInBackground()
        {
            var json = Serialize();
            if (json != null) Task.Run(() => Write(json));
        }

        byte[] Serialize()
        {
            saveTimer.Stop();
            try
            {
                var ms = new MemoryStream();
                new DataContractJsonSerializer(typeof(List<ClipEntry>)).WriteObject(ms, Items.ToList());
                // Images were written to disk in Add(); the in-memory copy is no longer needed.
                foreach (var e in Items) e.PendingImage = null;
                return ms.ToArray();
            }
            catch (Exception ex) { App.Log(ex); return null; }
        }

        void Write(byte[] json)
        {
            lock (writeLock)   // one writer at a time, in order; the newest snapshot is always written last
            {
                try
                {
                    var data = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
                    var tmp = IndexPath + ".tmp";
                    File.WriteAllBytes(tmp, data);
                    if (File.Exists(IndexPath)) File.Replace(tmp, IndexPath, null);
                    else File.Move(tmp, IndexPath);
                }
                catch (Exception ex) { App.Log(ex); }
            }
        }
    }
}
