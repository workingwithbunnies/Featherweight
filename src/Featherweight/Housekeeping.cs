using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Featherweight
{
    /// <summary>
    /// Keeps the data folder tidy, quietly, once per start. It never removes anything you still need:
    /// - Snapshots: keeps the 30 newest unnamed ones; snapshots you named are kept until you delete them.
    /// - Backups: a backup is your undo, so only ones that were already undone more than 90 days ago are removed
    ///   (plus certificate copies no remaining backup needs).
    /// - Logs: activity.log and crash.log keep their most recent part (about 512 KB) once they pass 1 MB.
    /// </summary>
    static class Housekeeping
    {
        public const int KeepUnnamedSnapshots = 30;
        public const int UndoneBackupDays = 90;
        const long LogLimit = 1024 * 1024, LogKeep = 512 * 1024;
        static readonly Regex AutoName = new Regex(@"^\d{8}-\d{6}( \(\d+\))?$");

        public static void Run()
        {
            Try(TidySnapshots); Try(TidyBackups);
            foreach (var log in new[] { "activity.log", "crash.log" }) Try(() => TrimLog(Path.Combine(Paths.Logs, log)));
        }

        static void Try(Action a) { try { a(); } catch { } } // housekeeping must never get in the way

        public static bool IsUnnamed(string snapshotFile) { return AutoName.IsMatch(Path.GetFileNameWithoutExtension(snapshotFile)); }

        static void TidySnapshots()
        {
            foreach (var f in Snapshots.List().Where(IsUnnamed).Skip(KeepUnnamedSnapshots)) File.Delete(f); // List() is newest first
        }

        static void TidyBackups()
        {
            var all = Engine.ListBackups();
            var keep = new List<BackupInfo>();
            foreach (var b in all)
            {
                DateTime undone;
                string at = null; try { at = Json.ReadFile(b.File).Str("RestoredAt"); } catch { }
                if (at != null && DateTime.TryParse(at, out undone) && DateTime.Now - undone > TimeSpan.FromDays(UndoneBackupDays)) File.Delete(b.File);
                else keep.Add(b);
            }
            // certificate copies are only needed while a kept backup refers to them
            var needed = new HashSet<string>(keep.SelectMany(b => b.Items).Where(i => i.Str("type") == "cert").Select(i => (i.Str("thumbprint") ?? "").ToUpperInvariant()));
            if (all.Count == keep.Count) return; // nothing removed: leave certificate copies exactly as they are
            foreach (var cer in Directory.GetFiles(Paths.Backups, "cert-*.cer"))
            {
                string thumb = Path.GetFileNameWithoutExtension(cer).Substring(5).ToUpperInvariant();
                if (!needed.Contains(thumb)) File.Delete(cer);
            }
        }

        static void TrimLog(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length <= LogLimit) return;
            byte[] tail;
            using (var s = File.OpenRead(path)) { s.Seek(-LogKeep, SeekOrigin.End); tail = new byte[LogKeep]; int n = 0, r; while (n < tail.Length && (r = s.Read(tail, n, tail.Length - n)) > 0) n += r; }
            string text = Encoding.UTF8.GetString(tail);
            int nl = text.IndexOf('\n'); if (nl >= 0) text = text.Substring(nl + 1); // start at a whole line
            File.WriteAllText(path, "(older lines removed to keep this log small)" + Environment.NewLine + text, new UTF8Encoding(false));
        }
    }
}
