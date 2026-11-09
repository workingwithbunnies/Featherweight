using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Featherweight
{
    enum OptState { Optimized, NotOptimized, Partial, ChangedBack }

    /// <summary>What Featherweight suggests for this item on this particular PC.</summary>
    enum Verdict { Recommended, Optional, Avoid }

    /// <summary>One user-facing optimization (e.g. "Dell SupportAssist"), made of one or more low-level plan actions.</summary>
    class Optimization
    {
        public string Id, Title, Category, Description, Risk, Manual;
        public string Level = "optional"; // recommended | optional | advanced (catalog default before per-PC checks)
        public double EstimateMB;
        public List<string> Processes = new List<string>();
        public Dictionary<string, object> Detect;
        public List<Dictionary<string, object>> Checks = new List<Dictionary<string, object>>();
        public List<PlanItem> Actions = new List<PlanItem>();

        // Filled in by Catalog.Evaluate
        public bool Present;
        public OptState State;
        public Verdict Verdict;
        public string VerdictNote; // "On this PC: …" explanation from the matching check
        public double LiveMB;
        public int RunningCount;
        public List<string> Details = new List<string>();

        // Found on this PC by Discovery rather than written in the catalog (not reviewed by anyone yet).
        public bool Discovered;
        public string Kind;                                     // "Service", "Startup app", "Scheduled task", "Store app"
        public bool ViewOnly;                                   // listed for information only (driver helpers): never changed
        // What identifies it on any PC, for matching knowledge-base entries: "service", "task", "taskpath", "package",
        // "startup", "exe" (file name), "publisher".
        public Dictionary<string, string> Ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public KnowledgeEntry Knowledge;                        // our reviewed entry for it, if there is one
        public List<string> Facts = new List<string>();         // read from this PC (Facts.Describe)
        public string Summary;                                  // short text for the top of the details panel (found items)
        public List<string> Changes = new List<string>();       // each change in plain words, with its current state (shown on screen)
        public List<string> ExePaths = new List<string>();      // programs whose memory counts for it (a path ending in \ = a whole folder)

        public bool IsManual { get { return Manual != null; } }
        public bool IsAdvanced { get { return Level == "advanced"; } }
    }

    static class Catalog
    {
        /// <summary>Label recorded in backups as the source of a change.</summary>
        public const string FilePath = "Featherweight catalog";
        static string StatePath { get { return Path.Combine(Paths.Root, "optimizer-state.json"); } }

        /// <summary>The catalog built into the exe, plus the user's own custom-optimizations.json (same id = override).</summary>
        public static List<Optimization> Load()
        {
            string builtIn;
            using (var s = typeof(Catalog).Assembly.GetManifestResourceStream("Featherweight.catalog.json"))
            using (var r = new StreamReader(s)) builtIn = r.ReadToEnd();
            var res = Parse(builtIn);
            if (File.Exists(Paths.CustomCatalog))
                foreach (var o in Parse(File.ReadAllText(Paths.CustomCatalog)))
                {
                    int i = res.FindIndex(x => x.Id == o.Id); // an override keeps the built-in item's place in the list
                    if (i >= 0) res[i] = o; else res.Add(o);
                }
            return res;
        }

        static List<Optimization> Parse(string json)
        {
            var d = Json.Parse(json) as Dictionary<string, object>;
            return d.List("optimizations").Select(o => new Optimization
            {
                Id = o.Str("id"), Title = o.Str("title") ?? o.Str("id"), Category = o.Str("category") ?? "Other",
                Description = o.Str("description") ?? "", Risk = o.Str("risk") ?? "Safe", Manual = o.Str("manual"),
                Level = (o.Str("level") ?? "optional").ToLowerInvariant(),
                EstimateMB = o.Num("estimateMB"), Processes = Strings(o, "processes"), Detect = o.Obj("detect"),
                Checks = o.List("checks"),
                Actions = o.List("actions").Select(a => new PlanItem { D = Resolve(a) }).ToList()
            }).ToList();
        }

        // ---------------------------------------------------------------- Dell TechHub agents
        const string DellAgents = @"SOFTWARE\Dell\Dell TechHub\AgentRegistration";
        static readonly Dictionary<string, int> DellStartup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { { "Delayed", 1 }, { "Manual", 2 }, { "Auto", 3 } }; // Dell.UnifiedAgent.SubAgentControl.Manager.StartupType

        /// <summary>Finds a TechHub agent's registration key by its program file name (IDs can differ between versions).</summary>
        static string FindDellAgent(string exe)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(DellAgents))
            {
                if (k == null) return null;
                foreach (var id in k.GetSubKeyNames())
                    using (var sub = k.OpenSubKey(id))
                    {
                        string path = sub == null ? null : sub.GetValue("ExecutablePath") as string;
                        if (path != null && string.Equals(Path.GetFileName(path), exe, StringComparison.OrdinalIgnoreCase)) return id;
                    }
            }
            return null;
        }

        /// <summary>Turns catalog-only action types into plain engine actions for this PC.</summary>
        static Dictionary<string, object> Resolve(Dictionary<string, object> a)
        {
            if (a.Str("type") != "dell-agent") return a;
            string exe = a.Str("exe"), id = FindDellAgent(exe);
            int value; if (!DellStartup.TryGetValue(a.Str("start") ?? "Manual", out value)) value = 2;
            var r = new Dictionary<string, object> {
                { "type", "registry" }, { "path", "HKLM:\\" + DellAgents + "\\" + (id ?? "?") }, { "name", "StartupType" },
                { "kind", "DWord" }, { "value", value }, { "label", "Dell agent " + exe + " → starts only when needed" } };
            if (id == null) r["missing"] = true; // not on this PC: never create keys for it
            return r;
        }

        static List<string> Strings(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return new List<string>();
            if (v is string) return new List<string> { (string)v };
            return ((IEnumerable)v).Cast<object>().Select(x => Convert.ToString(x)).ToList();
        }

        // ---------------------------------------------------------------- status
        /// <summary>
        /// A full scan as the Optimize page shows it: catalog + discovered items, their state on this PC, our knowledge-base
        /// entries, and the facts read from this PC. Runs on a background thread.
        /// </summary>
        public static List<Optimization> ScanAll(Action<string> warn = null)
        {
            var all = LoadWithDiscovered(warn);
            Evaluate(all);
            try { global::Featherweight.Knowledge.Apply(all); } catch (Exception ex) { if (warn != null) warn("Knowledge base: " + ex.Message); }
            try { global::Featherweight.Facts.Describe(all); } catch (Exception ex) { if (warn != null) warn("Reading facts: " + ex.Message); }
            return all;
        }

        /// <summary>The reviewed catalog plus everything else Discovery finds on this PC.</summary>
        public static List<Optimization> LoadWithDiscovered(Action<string> warn = null)
        {
            var res = Load();
            res.AddRange(Discovery.Find(res, warn));
            return res;
        }

        public static void Evaluate(List<Optimization> opts)
        {
            Probes.Reset(); // re-ask the per-PC questions on every scan
            var procs = ProcScan.Processes();
            var mem = procs.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Tuple.Create(g.Sum(p => p.PrivMB), g.Count()), StringComparer.OrdinalIgnoreCase);
            var byPath = opts.Any(o => o.Discovered) ? procs.Select(p => new { p, path = ProcScan.PathOf(p) }).ToList() : null;
            var state = LoadState(); bool dirty = false;

            foreach (var o in opts)
            {
                o.LiveMB = 0; o.RunningCount = 0; o.Details.Clear(); o.Changes.Clear();
                foreach (var pn in o.Processes)
                {
                    Tuple<double, int> m;
                    if (mem.TryGetValue(pn, out m)) { o.LiveMB += m.Item1; o.RunningCount += m.Item2; }
                }
                // Found items: match by program path. Without admin rights Windows hides the path of services running as
                // SYSTEM, so fall back to the program's name for those.
                if (o.Discovered)
                    foreach (var x in byPath.Where(x => o.ExePaths.Any(e => e != null && (x.path == null
                             ? !e.EndsWith("\\") && string.Equals(x.p.Name, Path.GetFileNameWithoutExtension(e), StringComparison.OrdinalIgnoreCase)
                             : e.EndsWith("\\") ? x.path.StartsWith(e, StringComparison.OrdinalIgnoreCase) : string.Equals(x.path, e, StringComparison.OrdinalIgnoreCase)))))
                    { o.LiveMB += x.p.PrivMB; o.RunningCount++; }

                if (o.IsManual)
                {
                    // Featherweight can't switch these and doesn't know who did: "not running" isn't counted as optimized by us.
                    o.Present = Detected(o.Detect, mem);
                    o.State = o.RunningCount > 0 ? OptState.NotOptimized : OptState.Optimized;
                    o.Details.Add("Manual setting — " + (o.RunningCount > 0 ? "currently running: " : "not running: ") + string.Join(", ", o.Processes));
                    if (o.Present) Judge(o);
                    continue;
                }
                else
                {
                    int applied = 0, notApplied = 0, missing = 0;
                    foreach (var a in o.Actions)
                    {
                        string det; var s = Engine.Check(a, out det);
                        if (s == ItemState.Applied) applied++; else if (s == ItemState.NotFound) missing++; else notApplied++;
                        o.Details.Add((s == ItemState.Applied ? "[✓] " : s == ItemState.NotFound ? "[–] " : "[ ] ") + a.Describe() + "   (" + det + ")");
                        var plain = global::Featherweight.Facts.PlainChange(a, s, det); if (plain != null) o.Changes.Add(plain);
                    }
                    if (o.ViewOnly)
                    {
                        // Nothing will be changed: show what it is and its current state instead of "would change" lines.
                        o.Details.Clear();
                        foreach (var p in o.ExePaths) o.Details.Add("Program: " + p);
                        foreach (var a in o.Actions) { string det; Engine.Check(a, out det); o.Details.Add("Current state: " + det.Replace("now: ", "")); }
                        o.Details.Add("Featherweight won't change this until it has been reviewed.");
                    }
                    o.Present = o.Discovered ? missing < o.Actions.Count : o.Detect != null ? Detected(o.Detect, mem) : missing < o.Actions.Count;
                    o.State = notApplied == 0 ? OptState.Optimized : applied == 0 ? OptState.NotOptimized : OptState.Partial;
                }

                // Remember what the user chose, so we can tell "never optimized" from "an update switched it back".
                string chosen; state.TryGetValue(o.Id, out chosen);
                if (o.State == OptState.Optimized && chosen != "optimized" && o.Present) { state[o.Id] = "optimized"; dirty = true; }
                if (o.State != OptState.Optimized && chosen == "optimized") o.State = OptState.ChangedBack;

                if (o.Present && !o.Discovered) Judge(o);
                if (o.Discovered) { o.Verdict = Verdict.Optional; o.VerdictNote = null; }
            }
            if (dirty) SaveState(state);
        }

        /// <summary>
        /// Starts from the catalog level, then the first matching check ("probe" is/isn't true) decides the verdict
        /// for this PC and supplies the "On this PC" note.
        /// </summary>
        static void Judge(Optimization o)
        {
            o.Verdict = o.Level == "recommended" ? Verdict.Recommended : Verdict.Optional;
            o.VerdictNote = null;
            foreach (var c in o.Checks)
            {
                var r = Probes.Get(c.Str("probe") ?? "");
                if (r == null) continue;
                bool want = !c.ContainsKey("is") || c.Bool("is");
                if (r.Value != want) continue;
                switch ((c.Str("verdict") ?? "").ToLowerInvariant())
                {
                    case "recommended": o.Verdict = Verdict.Recommended; break;
                    case "avoid": o.Verdict = Verdict.Avoid; break;
                    default: o.Verdict = Verdict.Optional; break;
                }
                o.VerdictNote = (c.Str("note") ?? "").Replace("{text}", r.Text);
                break;
            }
        }

        static bool Detected(Dictionary<string, object> detect, Dictionary<string, Tuple<double, int>> running)
        {
            if (detect == null) return true;
            foreach (var kv in detect)
            {
                // A detect value may be one string or a list of alternatives; any match counts.
                var values = kv.Value is string ? new List<string> { (string)kv.Value }
                           : kv.Value is IEnumerable ? ((IEnumerable)kv.Value).Cast<object>().Select(x => Convert.ToString(x)).ToList()
                           : new List<string> { Convert.ToString(kv.Value) };
                foreach (var v in values)
                    try
                    {
                        switch (kv.Key)
                        {
                            case "service": if (Engine.ServiceStart(v) != null) return true; break;
                            case "file": if (File.Exists(Environment.ExpandEnvironmentVariables(v))) return true; break;
                            case "regkey": if (Engine.RegKeyExists(v)) return true; break;
                            case "process": if (running.ContainsKey(v)) return true; break;
                            case "package": if (PackageInstalled(v)) return true; break;
                            case "dell-agent": if (FindDellAgent(v) != null) return true; break;
                        }
                    }
                    catch { }
            }
            return false;
        }

        static bool PackageInstalled(string name)
        {
            using (var k = Engine.OpenUserKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                return k != null && k.GetSubKeyNames().Any(n => n.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------- user choice memory
        static Dictionary<string, string> LoadState()
        {
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(StatePath)) foreach (var kv in Json.ReadFile(StatePath)) res[kv.Key] = Convert.ToString(kv.Value); } catch { }
            return res;
        }

        static void SaveState(Dictionary<string, string> s)
        {
            Json.WriteFile(StatePath, s.ToDictionary(kv => kv.Key, kv => (object)kv.Value));
        }

        static void Remember(IEnumerable<Optimization> opts, string choice)
        {
            var s = LoadState();
            foreach (var o in opts) s[o.Id] = choice;
            SaveState(s);
        }

        // ---------------------------------------------------------------- actions
        /// <summary>Applies the automatic optimizations (backed up first). Returns the backup path or null.</summary>
        public static string Apply(IList<Optimization> opts, Action<string> log)
        {
            var auto = opts.Where(o => !o.IsManual).ToList();
            if (auto.Count == 0) return null;
            var plan = new Plan
            {
                File = FilePath,
                Name = "Optimized: " + string.Join(", ", auto.Select(o => o.Title)),
                Items = auto.SelectMany(o => o.Actions).ToList()
            };
            string backup = Engine.Apply(plan, plan.Items, false, log);
            Remember(auto, "optimized");
            return backup;
        }

        /// <summary>Puts each action back to its original value, taken from the newest backup that recorded it.</summary>
        public static int RestoreDefaults(IList<Optimization> opts, Action<string> log)
        {
            var backups = Engine.ListBackups(); // newest first
            int restored = 0;
            var fullyRestored = new List<Optimization>();
            foreach (var o in opts.Where(x => !x.IsManual))
            {
                log("Restoring default: " + o.Title);
                bool failed = false;
                foreach (var a in o.Actions)
                {
                    string det;
                    if (Engine.Check(a, out det) != ItemState.Applied) { log("SKIP   " + a.Describe() + "  (already at default)"); continue; }
                    string key = Engine.Key(a.D);
                    Dictionary<string, object> entry = null; string dir = null;
                    foreach (var b in backups)
                    {
                        entry = b.Items.FirstOrDefault(i => Engine.Key(i) == key);
                        if (entry != null) { dir = Path.GetDirectoryName(b.File); break; }
                    }
                    if (entry == null) { log("SKIP   " + a.Describe() + "  (no saved original: it was already like this before Featherweight)"); continue; }
                    if (Engine.RestoreItem(entry, dir, log)) restored++; else failed = true;
                }
                if (!failed) fullyRestored.Add(o);
            }
            Remember(fullyRestored, "default"); // a failed restore must not hide a later "changed back" warning
            log("Restore finished: " + restored + " setting(s) put back. Restart your PC so startup behaviour is fully back.");
            return restored;
        }
    }
}
