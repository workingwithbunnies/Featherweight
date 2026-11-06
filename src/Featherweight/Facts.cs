using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Featherweight
{
    /// <summary>
    /// Builds each found item's description from facts read on this PC, with no guessing: the maker's own description,
    /// which installed product it belongs to, what starts it, which driver installed it, what a Store app is allowed to
    /// do, and what it's doing right now. Works offline and in any display language (Windows supplies the maker's text
    /// in the user's language; Featherweight only reads fixed, non-translated values).
    /// </summary>
    static class Facts
    {
        class Live { public int Count; public double MB, CpuSeconds; public TimeSpan Up; public bool Window; public int Connections; public bool CpuKnown; }

        public static void Describe(List<Optimization> items)
        {
            var found = items.Where(o => o.Discovered && o.Present).ToList();
            if (found.Count == 0) return;
            var installed = InstalledProducts();
            var conns = SafeConns();
            var procs = ProcScan.Processes().Select(p => new { p, path = ProcScan.PathOf(p) }).ToList();

            foreach (var o in found)
            {
                o.Facts.Clear();
                try
                {
                    string exe = o.ExePaths.FirstOrDefault(p => p != null && !p.EndsWith("\\"));
                    string folder = o.ExePaths.FirstOrDefault(p => p != null && p.EndsWith("\\"));
                    string makerWords = null;

                    // ---- the program itself
                    if (exe != null && File.Exists(exe))
                    {
                        var vi = FileVersionInfo.GetVersionInfo(exe);
                        string name = Clean(vi.FileDescription) ?? Path.GetFileNameWithoutExtension(exe);
                        string signed = Native.HasValidSignature(exe) ? "digitally signed by " + Publisher.Of(exe) : "not digitally signed";
                        o.Facts.Add("Program: " + name + " (" + Path.GetFileName(exe) + (Clean(vi.FileVersion) != null ? ", version " + Clean(vi.FileVersion) : "") + "), " + signed + ".");
                    }

                    // ---- what kind of item, what its maker says, what starts it
                    string svc; o.Ids.TryGetValue("service", out svc);
                    string taskPath; o.Ids.TryGetValue("taskpath", out taskPath);
                    string pkg; o.Ids.TryGetValue("package", out pkg);
                    if (svc != null) makerWords = ServiceFacts(o, svc);
                    else if (taskPath != null) makerWords = TaskFacts(o, taskPath);
                    else if (pkg != null) makerWords = PackageFacts(o, folder);
                    else if (o.Kind.EndsWith("startup app", StringComparison.OrdinalIgnoreCase))
                        o.Facts.Add("Starts: every time you sign in (it's in Task Manager's Startup apps list).");

                    if (makerWords != null) o.Facts.Insert(Math.Min(1, o.Facts.Count), "In " + o.Category + "'s words: \u201C" + Shorten(makerWords, 320) + "\u201D");
                    // Short text for the top of the panel: our reviewed text, else the maker's own words, else what it is.
                    o.Summary = o.Knowledge != null ? o.Knowledge.About
                              : makerWords != null ? o.Category + " describes it as: \u201C" + Shorten(makerWords, 200) + "\u201D"
                              : "A " + o.Kind.ToLowerInvariant() + " from " + o.Category + ". It doesn't describe itself; see the facts below.";

                    // ---- which installed product it belongs to
                    string where = exe ?? folder;
                    var product = where == null ? null : installed.Where(p => where.StartsWith(p.Item1, StringComparison.OrdinalIgnoreCase))
                                                                  .OrderByDescending(p => p.Item1.Length).FirstOrDefault();
                    if (product != null) o.Facts.Add("Part of: " + product.Item2 + (product.Item3 != null ? " (installed " + product.Item3 + ")" : "") + ".");

                    // ---- installed with a driver: which kind of hardware
                    string drv = exe == null ? null : DriverFact(exe);
                    if (drv != null) o.Facts.Add(drv);

                    // ---- right now
                    var live = new Live();
                    foreach (var x in procs)
                    {
                        bool match = o.ExePaths.Any(e => e != null && (x.path == null
                            ? !e.EndsWith("\\") && string.Equals(x.p.Name, Path.GetFileNameWithoutExtension(e), StringComparison.OrdinalIgnoreCase)
                            : e.EndsWith("\\") ? x.path.StartsWith(e, StringComparison.OrdinalIgnoreCase) : string.Equals(x.path, e, StringComparison.OrdinalIgnoreCase)));
                        if (!match) continue;
                        live.Count++; live.MB += x.p.PrivMB;
                        int c; if (conns.TryGetValue(x.p.Id, out c)) live.Connections += c;
                        try
                        {
                            using (var p = Process.GetProcessById(x.p.Id))
                            {
                                if (p.MainWindowHandle != IntPtr.Zero) live.Window = true;
                                var up = DateTime.Now - p.StartTime; if (up > live.Up) live.Up = up;
                                live.CpuSeconds += p.TotalProcessorTime.TotalSeconds; live.CpuKnown = true;
                            }
                        }
                        catch { } // protected or elevated processes: some details need admin rights
                    }
                    o.Facts.Add(LiveText(live));
                }
                catch (Exception ex) { o.Facts.Add("(Some details couldn't be read: " + ex.Message + ")"); }

                o.Description = Compose(o);
            }
        }

        // ---------------------------------------------------------------- the text shown in the details panel
        static string Compose(Optimization o)
        {
            var lines = new List<string>();
            var k = o.Knowledge;
            if (k != null)
            {
                lines.Add(k.About);
                if (!string.IsNullOrEmpty(k.Lose)) lines.Add("What you lose: " + k.Lose);
                if (!string.IsNullOrEmpty(k.KeepIf)) lines.Add("Keep it if " + k.KeepIf);
                lines.Add("");
            }
            lines.Add("On this PC:");
            lines.AddRange(o.Facts.Select(f => "•  " + f));
            lines.Add("");
            string action;
            if (o.ViewOnly) action = "Featherweight lists it for information only and won't change it.";
            else if (o.Kind == "Service") action = "Optimize sets it to start only when needed (Manual). Restore default puts it back.";
            else if (o.Kind == "Startup app") action = "Optimize switches it off in Task Manager's Startup apps list. Restore default turns it back on.";
            else if (o.Kind == "Scheduled task") action = "Optimize disables this task. Restore default turns it back on.";
            else action = "Optimize turns off its background permission and sign-in start. The app still works when you open it; Restore default turns them back on.";
            lines.Add(action);
            lines.Add(k != null
                ? "Reviewed by Featherweight" + (string.IsNullOrEmpty(k.Reviewed) ? "" : " (" + k.Reviewed + ")") + (string.IsNullOrEmpty(k.Confidence) ? "" : "; " + k.Confidence) + ". \"On this PC\" was read from this PC."
                : "Read from this PC. Featherweight hasn't reviewed this item yet, so it can't say what you'd lose.");
            return string.Join("\r\n", lines);
        }

        static string LiveText(Live l)
        {
            if (l.Count == 0) return "Right now: not running.";
            var parts = new List<string> { l.Count + (l.Count == 1 ? " process" : " processes"), Theme.MB(l.MB) };
            if (l.Up.TotalMinutes >= 1) parts.Add("running for " + Duration(l.Up));
            if (l.CpuKnown && l.Up.TotalSeconds > 60)
            {
                double pct = 100.0 * l.CpuSeconds / l.Up.TotalSeconds / Environment.ProcessorCount;
                parts.Add(pct < 0.1 ? "almost no processor time" : "about " + pct.ToString(pct < 10 ? "0.0" : "0") + "% of the processor on average");
            }
            parts.Add(l.Window ? "a window is open" : "no window open");
            parts.Add(l.Connections == 0 ? "no internet connections" : l.Connections + (l.Connections == 1 ? " internet connection" : " internet connections"));
            return "Right now: " + string.Join(", ", parts) + ".";
        }

        static string Duration(TimeSpan t)
        {
            if (t.TotalDays >= 1) return (int)t.TotalDays + " d " + t.Hours + " h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + " h " + t.Minutes + " min";
            return (int)t.TotalMinutes + " min";
        }

        // ---------------------------------------------------------------- services
        static string ServiceFacts(Optimization o, string name)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
            {
                if (k == null) return null;
                int start = Convert.ToInt32(k.GetValue("Start", 3)), delayed = Convert.ToInt32(k.GetValue("DelayedAutostart", 0));
                bool trigger = false; using (var t = k.OpenSubKey("TriggerInfo")) trigger = t != null && t.SubKeyCount > 0;
                string starts = start == 2 ? (delayed == 1 ? "with Windows, shortly after startup" : "with Windows")
                              : start == 3 ? (trigger ? "when Windows sees certain events (for example a device or network change)" : "only when an app asks for it")
                              : start == 4 ? "never (it's disabled)" : "early in Windows startup";
                o.Facts.Add("Starts: " + starts + ".");
                string account = Convert.ToString(k.GetValue("ObjectName") ?? "");
                if (account.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase)) o.Facts.Add("Runs with: full system rights (the Local System account).");
                else if (account.Length > 0) o.Facts.Add("Runs with: limited rights (" + account.Replace("NT AUTHORITY\\", "").Replace("NT Authority\\", "") + ").");
                var dependents = Dependents(name);
                if (dependents.Count > 0) o.Facts.Add("Other services that need it: " + string.Join(", ", dependents.Take(4)) + ".");
                return Clean(Native.IndirectString(k.GetValue("Description", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string));
            }
        }

        static List<string> Dependents(string name)
        {
            var res = new List<string>();
            using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                foreach (var n in root.GetSubKeyNames())
                    using (var k = root.OpenSubKey(n))
                    {
                        var dep = k == null ? null : k.GetValue("DependOnService") as string[];
                        if (dep != null && dep.Contains(name, StringComparer.OrdinalIgnoreCase)) res.Add(n);
                    }
            return res;
        }

        // ---------------------------------------------------------------- scheduled tasks
        static readonly Dictionary<int, string> TriggerNames = new Dictionary<int, string>
        {
            { 0, "when a certain event is logged" }, { 1, "at a set time" }, { 2, "every day" }, { 3, "every week" }, { 4, "every month" },
            { 5, "on certain days of the month" }, { 6, "when the PC is idle" }, { 7, "when the task is created or changed" },
            { 8, "when Windows starts" }, { 9, "when you sign in" }, { 11, "when you lock, unlock or switch user" },
        };

        static string TaskFacts(Optimization o, string full)
        {
            int cut = full.LastIndexOf('\\');
            string dir = cut <= 0 ? "\\" : full.Substring(0, cut), name = full.Substring(cut + 1);
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            dynamic t = svc.GetFolder(dir).GetTask(name);
            var when = new List<string>();
            foreach (dynamic tr in t.Definition.Triggers) { string w; if (TriggerNames.TryGetValue((int)tr.Type, out w) && !when.Contains(w)) when.Add(w); }
            if (when.Count > 0) o.Facts.Add("Starts: " + string.Join(", and ", when) + ".");
            try { if ((int)t.Definition.Principal.RunLevel == 1) o.Facts.Add("Runs with: administrator rights."); } catch { }
            try { if ((bool)t.Definition.Settings.Hidden) o.Facts.Add("Hidden in Task Scheduler's default view."); } catch { }
            string desc = null;
            try { desc = Clean(Native.IndirectString((string)t.Definition.RegistrationInfo.Description)); } catch { }
            return desc;
        }

        // ---------------------------------------------------------------- Store apps
        static readonly Dictionary<string, string> Capabilities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "internetClient", "use the internet" }, { "internetClientServer", "accept connections from the internet" },
            { "privateNetworkClientServer", "talk to devices on your home network" }, { "runFullTrust", "run as a full desktop program" },
            { "location", "use your location" }, { "microphone", "use the microphone" }, { "webcam", "use the camera" },
            { "bluetooth", "use Bluetooth" }, { "broadFileSystemAccess", "read and write all your files" }, { "picturesLibrary", "use your Pictures folder" },
            { "musicLibrary", "use your Music folder" }, { "videosLibrary", "use your Videos folder" }, { "documentsLibrary", "use your Documents folder" },
            { "allowElevation", "ask for administrator rights" }, { "packageManagement", "install or remove other apps" }, { "appDiagnostics", "see other running apps" },
        };

        static string PackageFacts(Optimization o, string folder)
        {
            var starts = new List<string>();
            if (o.Actions.Any(a => (a.D.Str("name") ?? "") == "State" && Check(a) == ItemState.NotApplied)) starts.Add("when you sign in");
            if (o.Actions.Any(a => (a.D.Str("name") ?? "") == "Disabled" && Check(a) == ItemState.NotApplied)) starts.Add("it may keep running in the background");
            if (starts.Count > 0) o.Facts.Add("Starts: " + string.Join("; ", starts) + ".");
            string f = folder == null ? null : Path.Combine(folder, "AppxManifest.xml");
            if (f == null || !File.Exists(f)) return null;
            string xml = File.ReadAllText(f);
            var allowed = Regex.Matches(xml, "<(?:[\\w]+:)?(?:Capability|DeviceCapability)\\s+Name=\"([^\"]+)\"").Cast<Match>()
                               .Select(m => m.Groups[1].Value).Where(Capabilities.ContainsKey).Select(n => Capabilities[n]).Distinct().ToList();
            if (allowed.Count > 0) o.Facts.Add("Allowed to: " + string.Join(", ", allowed) + ".");
            string fullName = folder.TrimEnd('\\'); fullName = fullName.Substring(fullName.LastIndexOf('\\') + 1);
            string pkgName = fullName.Split('_')[0];
            var props = Regex.Match(xml, "<Properties>(.*?)</Properties>", RegexOptions.Singleline).Groups[1].Value;
            var d = Regex.Match(props, "<Description>([^<]+)</Description>");
            if (!d.Success) return null;
            string text = System.Net.WebUtility.HtmlDecode(d.Groups[1].Value.Trim());
            if (text.StartsWith("ms-resource:")) text = Native.IndirectString("@{" + fullName + "?" + ResourceUri(pkgName, text) + "}");
            return Clean(text);
        }

        static ItemState Check(PlanItem a) { string det; return Engine.Check(a, out det); }

        /// <summary>"ms-resource:AppDescription" → "ms-resource://Pkg/Resources/AppDescription" (the form Windows resolves).</summary>
        static string ResourceUri(string pkgName, string res)
        {
            string key = res.Substring("ms-resource:".Length);
            if (key.StartsWith("//")) return "ms-resource:" + key;
            if (key.StartsWith("/")) return "ms-resource://" + pkgName + key;
            return "ms-resource://" + pkgName + "/Resources/" + key;
        }

        // ---------------------------------------------------------------- drivers
        // Device classes from the driver's INF file (fixed English keywords, the same in every Windows language).
        static readonly Dictionary<string, string> DriverClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MEDIA", "sound" }, { "AudioProcessingObject", "sound effects" }, { "Display", "graphics" }, { "HIDClass", "keyboard, touchpad or other input device" },
            { "Keyboard", "keyboard" }, { "Mouse", "mouse or touchpad" }, { "Net", "network adapter" }, { "Bluetooth", "Bluetooth" },
            { "Camera", "camera" }, { "Image", "camera or scanner" }, { "Biometric", "fingerprint or face sign-in" }, { "System", "system device" },
            { "SoftwareComponent", "a device (as a software part of its driver)" }, { "Extension", "a device (as an add-on to its driver)" },
            { "Firmware", "firmware" }, { "SecurityDevices", "security chip" }, { "Battery", "battery" }, { "Sensor", "sensors" },
            { "USB", "USB" }, { "Printer", "printer" }, { "SmartCardReader", "smart card reader" },
        };

        static string DriverFact(string exe)
        {
            const string repo = "\\DriverStore\\FileRepository\\";
            int i = exe.IndexOf(repo, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int start = i + repo.Length, end = exe.IndexOf('\\', start);
            if (end < 0) return null;
            string dir = exe.Substring(0, end), inf = Directory.GetFiles(dir, "*.inf").FirstOrDefault();
            if (inf == null) return "Came with: a hardware driver.";
            string section = null, cls = null, provider = null;
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadLines(inf))
            {
                string line = raw.Split(';')[0].Trim();
                if (line.StartsWith("[")) { section = line.Trim('[', ']').ToLowerInvariant(); continue; }
                int eq = line.IndexOf('='); if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim().Trim('"');
                if (section == "version" && key.Equals("Class", StringComparison.OrdinalIgnoreCase)) cls = val;
                else if (section == "version" && key.Equals("Provider", StringComparison.OrdinalIgnoreCase)) provider = val;
                else if (section == "strings" && !strings.ContainsKey(key)) strings[key] = val;
            }
            if (provider != null && provider.StartsWith("%")) { string s; provider = strings.TryGetValue(provider.Trim('%'), out s) ? s : null; }
            string what; if (cls == null || !DriverClasses.TryGetValue(cls, out what)) what = "a device";
            return "Came with: the driver for " + what + (provider != null ? " (driver by " + Publisher.Normalize(provider) + ")" : "") + ".";
        }

        // ---------------------------------------------------------------- installed programs (Settings → Apps)
        static List<Tuple<string, string, string>> InstalledProducts()
        {
            var res = new List<Tuple<string, string, string>>();
            var roots = new[]
            {
                Tuple.Create(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            };
            foreach (var r in roots) Read(res, r.Item1.OpenSubKey(r.Item2));
            Read(res, Engine.OpenUserKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"));
            return res;
        }

        static void Read(List<Tuple<string, string, string>> res, RegistryKey root)
        {
            if (root == null) return;
            using (root)
                foreach (var n in root.GetSubKeyNames())
                    using (var k = root.OpenSubKey(n))
                    {
                        if (k == null) continue;
                        string name = Clean(k.GetValue("DisplayName") as string), loc = k.GetValue("InstallLocation") as string;
                        if (name == null || string.IsNullOrWhiteSpace(loc)) continue;
                        loc = Environment.ExpandEnvironmentVariables(loc.Trim().Trim('"')).TrimEnd('\\') + "\\";
                        if (loc.Length < 12 || loc.Count(ch => ch == '\\') < 3) continue; // "C:\Program Files\" alone says nothing
                        string date = k.GetValue("InstallDate") as string; DateTime d;
                        string shown = date != null && DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out d) ? d.ToString("d MMM yyyy") : null;
                        res.Add(Tuple.Create(loc, name, shown));
                    }
        }

        // ---------------------------------------------------------------- helpers
        static Dictionary<int, int> SafeConns() { try { return Native.TcpConnectionsByPid(); } catch { return new Dictionary<int, int>(); } }

        static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.StartsWith("@") || s.StartsWith("ms-resource:")) return null;
            s = Regex.Replace(s.Trim(), @"\s+", " ");
            return s.Length == 0 ? null : s;
        }

        static string Shorten(string s, int max) { return s.Length <= max ? s : s.Substring(0, max).TrimEnd() + "…"; }
    }
}
