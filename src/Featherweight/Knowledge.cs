using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Featherweight
{
    /// <summary>One reviewed description, matched to found items by identifiers that are the same on every PC.</summary>
    class KnowledgeEntry
    {
        public string Id, Title, About, Lose, KeepIf, Suggest, Reviewed, Confidence;
        public bool AllowChange;
        public Dictionary<string, string> Match = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Sources = new List<string>();
    }

    /// <summary>
    /// Featherweight's own knowledge base (knowledge.json): what common background software does and what you lose without
    /// it, written and checked by the project, not generated. Built into the exe; a newer copy can be downloaded from the
    /// project's GitHub repository during the optional update check (nothing about the PC is sent).
    /// </summary>
    static class Knowledge
    {
        public const string DownloadUrl = "https://raw.githubusercontent.com/" + Updates.Repo + "/main/src/Featherweight/knowledge.json";
        const int MaxDownloadBytes = 4 * 1024 * 1024;
        static string LocalPath { get { return Path.Combine(Paths.Root, "knowledge.json"); } }

        static List<KnowledgeEntry> entries;
        public static int Version { get; private set; }
        public static string Origin { get; private set; } // "built in" or "downloaded"

        public static List<KnowledgeEntry> Entries
        {
            get
            {
                if (entries != null) return entries;
                string builtIn;
                using (var s = typeof(Knowledge).Assembly.GetManifestResourceStream("Featherweight.knowledge.json"))
                using (var r = new StreamReader(s)) builtIn = r.ReadToEnd();
                int v; var list = Parse(builtIn, false, out v);
                Version = v; Origin = "built in";
                try
                {
                    if (File.Exists(LocalPath))
                    {
                        int dv; var dl = Parse(File.ReadAllText(LocalPath), true, out dv);
                        if (dv > v && dl.Count > 0) { list = dl; Version = dv; Origin = "downloaded"; }
                    }
                }
                catch { } // a damaged download never stops the app: the built-in copy is used
                return entries = list;
            }
        }

        /// <param name="downloaded">
        /// Safety: a downloaded file can describe items, but can't make one "Recommended" (which "Apply recommended" would
        /// change in one click) or unlock a driver helper. Those need the copy built into a released version.
        /// </param>
        static List<KnowledgeEntry> Parse(string json, bool downloaded, out int version)
        {
            var d = Json.Parse(json) as Dictionary<string, object>;
            version = (int)d.Num("version");
            var res = new List<KnowledgeEntry>();
            foreach (var e in d.List("entries"))
            {
                var k = new KnowledgeEntry
                {
                    Id = e.Str("id"), Title = e.Str("title"), About = e.Str("about"), Lose = e.Str("lose"), KeepIf = e.Str("keepIf"),
                    Suggest = (e.Str("suggest") ?? "optional").ToLowerInvariant(), Reviewed = e.Str("reviewed"), Confidence = e.Str("confidence"),
                    AllowChange = e.Bool("allowChange"),
                };
                var m = e.Obj("match");
                if (k.Id == null || k.About == null || m == null || m.Count == 0) continue; // incomplete entries are ignored
                foreach (var kv in m) k.Match[kv.Key] = Convert.ToString(kv.Value);
                object src;
                if (e.TryGetValue("sources", out src) && src is System.Collections.IEnumerable && !(src is string))
                    foreach (var s in (System.Collections.IEnumerable)src) k.Sources.Add(Convert.ToString(s));
                if (downloaded) { if (k.Suggest == "recommended") k.Suggest = "optional"; k.AllowChange = false; }
                res.Add(k);
            }
            return res;
        }

        public static KnowledgeEntry Find(Optimization o)
        {
            foreach (var k in Entries)
            {
                bool all = true;
                foreach (var kv in k.Match)
                {
                    string have;
                    if (!o.Ids.TryGetValue(kv.Key, out have) || !Glob(kv.Value).IsMatch(have)) { all = false; break; }
                }
                if (all) return k;
            }
            return null;
        }

        static readonly Dictionary<string, Regex> globs = new Dictionary<string, Regex>();
        static Regex Glob(string pattern)
        {
            lock (globs)
            {
                Regex r;
                if (!globs.TryGetValue(pattern, out r))
                    globs[pattern] = r = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
                return r;
            }
        }

        /// <summary>Attaches our reviewed entries to found items and sets their suggestion.</summary>
        public static void Apply(List<Optimization> items)
        {
            foreach (var o in items.Where(x => x.Discovered))
            {
                var k = Find(o);
                o.Knowledge = k;
                if (k == null) continue;
                if (!string.IsNullOrEmpty(k.Title)) o.Title = k.Title;
                if (k.AllowChange) o.ViewOnly = false;
                if (k.Suggest == "advanced") o.Level = "advanced";
                o.Verdict = k.Suggest == "recommended" ? Verdict.Recommended : k.Suggest == "keep" ? Verdict.Avoid : Verdict.Optional;
                o.VerdictNote = k.Suggest == "keep" && !string.IsNullOrEmpty(k.KeepIf) ? "Keep it if " + k.KeepIf : null;
            }
        }

        // ---------------------------------------------------------------- optional download (part of the update check)
        /// <summary>Downloads the newest knowledge file from GitHub if it's newer than what we have. Sends nothing about the PC.</summary>
        public static bool TryUpdate(out string message)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(DownloadUrl);
                req.UserAgent = "Featherweight/" + MainForm.AppVersion; req.Timeout = 15000; req.ReadWriteTimeout = 15000;
                string text;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[65536]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxDownloadBytes) { message = "knowledge file too large; ignored"; return false; } }
                    text = Encoding.UTF8.GetString(ms.ToArray());
                }
                int v; var list = Parse(text, true, out v);
                var current = Entries;
                if (v <= Version || list.Count == 0) { message = "descriptions are up to date (version " + Version + ")"; return false; }
                File.WriteAllText(LocalPath, text, new UTF8Encoding(false));
                entries = null; var _ = Entries; // reload
                message = "downloaded newer descriptions (version " + v + ", " + list.Count + " entries)";
                return true;
            }
            catch (Exception ex) { message = "couldn't download descriptions: " + ex.Message; return false; }
        }
    }
}
