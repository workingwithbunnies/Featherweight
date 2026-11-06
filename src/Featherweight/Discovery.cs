using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Featherweight
{
    /// <summary>
    /// Works out which company a program comes from, so any brand is recognised without a hand-written list:
    /// the organisation in its digital signature, else the company in its file details, else its Program Files folder.
    /// </summary>
    static class Publisher
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public const string Unknown = "Unknown publisher";

        // Long legal names people don't recognise, mapped to the brand they know. Matched as a prefix.
        static readonly string[][] Aliases =
        {
            new[] { "Advanced Micro Devices", "AMD" }, new[] { "Hewlett", "HP" }, new[] { "HP Inc", "HP" }, new[] { "HP Development", "HP" },
            new[] { "ASUSTeK", "ASUS" }, new[] { "Micro-Star", "MSI" }, new[] { "Lenovo", "Lenovo" }, new[] { "Dell", "Dell" },
            new[] { "Alienware", "Dell" }, new[] { "Acer", "Acer" }, new[] { "Samsung", "Samsung" }, new[] { "Razer", "Razer" },
            new[] { "Logitech", "Logitech" }, new[] { "Corsair", "Corsair" }, new[] { "Realtek", "Realtek" }, new[] { "Intel", "Intel" },
            new[] { "NVIDIA", "NVIDIA" }, new[] { "Google", "Google" }, new[] { "Microsoft", "Microsoft" }, new[] { "Windows (R)", "Microsoft" },
            new[] { "Valve", "Valve" }, new[] { "Epic Games", "Epic Games" }, new[] { "Electronic Arts", "EA" }, new[] { "Ubisoft", "Ubisoft" },
            new[] { "Adobe", "Adobe" }, new[] { "The Git Development Community", "Git" }, new[] { "ELAN", "ELAN" }, new[] { "Synaptics", "Synaptics" },
        };
        static readonly Regex Suffix = new Regex(@"[,\s]+(Inc\.?|Incorporated|Corporation|Corp\.?|Co\.?,?\s*Ltd\.?|Ltd\.?|Limited|LLC|L\.L\.C\.|GmbH|AG|SE|S\.A\.|B\.V\.|Pte\.?|PBC|Oy|e\.K\.|Company)$", RegexOptions.IgnoreCase);
        // Folder names that say nothing about who made a program ("C:\Program Files\Common Files\...").
        static readonly string[] GenericFolders = { "Common Files", "Shared", "Tools", "Programs" };

        /// <summary>Short brand name, e.g. "HP Inc." → "HP", "Bitsum Technologies (Bitsum LLC)" → "Bitsum Technologies".</summary>
        public static string Normalize(string company)
        {
            if (string.IsNullOrWhiteSpace(company)) return null;
            string c = company.Trim().Trim('"').Trim();
            foreach (var a in Aliases) if (c.StartsWith(a[0], StringComparison.OrdinalIgnoreCase)) return a[1];
            c = Regex.Replace(c, @"\s*\([^)]*\)\s*$", "").Trim(); // trailing "(...)"
            for (int i = 0; i < 3; i++) { string s = Suffix.Replace(c, "").Trim(); if (s == c) break; c = s; }
            return c.Length == 0 ? null : c;
        }

        public static bool IsMicrosoft(string brand) { return brand == "Microsoft"; }

        /// <summary>Brand of the program at <paramref name="path"/>. thorough = also read its digital signature (slower).</summary>
        public static string Of(string path, bool thorough = true)
        {
            if (string.IsNullOrEmpty(path)) return Unknown;
            string key = (thorough ? "s|" : "f|") + path;
            lock (cache) { string hit; if (cache.TryGetValue(key, out hit)) return hit; }
            string res = null;
            // Store apps: the package says who published it (their programs' own details are often blank or odd).
            string pkg = PackageRoot(path);
            if (pkg != null) { string t; Manifest(pkg, out t, out res); }
            if (res == null && thorough)
                try
                {
                    var c = X509Certificate.CreateFromSignedFile(path);
                    var m = Regex.Match(c.Subject, "(?:^|, )O=(\"[^\"]+\"|[^,]+)");
                    if (!m.Success) m = Regex.Match(c.Subject, "(?:^|, )CN=(\"[^\"]+\"|[^,]+)");
                    if (m.Success) res = Normalize(m.Groups[1].Value);
                }
                catch { }
            if (res == null) try { if (File.Exists(path)) res = Normalize(FileVersionInfo.GetVersionInfo(path).CompanyName); } catch { }
            if (res == null) res = FolderBrand(path);
            if (res == null) res = Unknown;
            lock (cache) cache[key] = res;
            return res;
        }

        /// <summary>"C:\Program Files\HP\..." → "HP" when the file itself names no company (generic folders are skipped).</summary>
        static string FolderBrand(string path)
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) })
            {
                if (string.IsNullOrEmpty(root) || !path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = path.Substring(root.Length + 1).Split('\\');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)) return null;
                    if (GenericFolders.Contains(parts[i], StringComparer.OrdinalIgnoreCase)) continue;
                    return Normalize(parts[i]);
                }
            }
            return null;
        }

        /// <summary>"C:\Program Files\WindowsApps\Pkg_1.0_x64__id\app\x.exe" → "C:\Program Files\WindowsApps\Pkg_1.0_x64__id\".</summary>
        public static string PackageRoot(string path)
        {
            int i = path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int start = i + "\\WindowsApps\\".Length, end = path.IndexOf('\\', start);
            return end < 0 ? null : path.Substring(0, end + 1);
        }

        /// <summary>App and publisher names from a package's AppxManifest.xml (skipped when they're resource references).</summary>
        public static void Manifest(string folder, out string title, out string brand)
        {
            title = brand = null;
            if (folder == null) return;
            string key = "m|" + folder;
            lock (cache)
            {
                string hit;
                if (cache.TryGetValue(key, out hit)) { var p = hit.Split('\n'); title = p[0].Length > 0 ? p[0] : null; brand = p[1].Length > 0 ? p[1] : null; return; }
            }
            try
            {
                string f = Path.Combine(folder, "AppxManifest.xml");
                if (File.Exists(f))
                {
                    string xml = File.ReadAllText(f);
                    var props = Regex.Match(xml, "<Properties>(.*?)</Properties>", RegexOptions.Singleline).Groups[1].Value;
                    var dn = Regex.Match(props, "<DisplayName>([^<]+)</DisplayName>");
                    var pn = Regex.Match(props, "<PublisherDisplayName>([^<]+)</PublisherDisplayName>");
                    if (dn.Success && !dn.Groups[1].Value.StartsWith("ms-resource:")) title = System.Net.WebUtility.HtmlDecode(dn.Groups[1].Value.Trim());
                    if (pn.Success && !pn.Groups[1].Value.StartsWith("ms-resource:")) brand = Normalize(System.Net.WebUtility.HtmlDecode(pn.Groups[1].Value));
                }
            }
            catch { }
            lock (cache) cache[key] = (title ?? "") + "\n" + (brand ?? "");
        }

        /// <summary>The program a command line runs: handles quotes, arguments, environment variables and script hosts.</summary>
        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            string c = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (c.StartsWith(@"\??\")) c = c.Substring(4);
            string exe, args;
            if (c.StartsWith("\""))
            {
                int q = c.IndexOf('"', 1);
                exe = q > 0 ? c.Substring(1, q - 1) : c.Substring(1);
                args = q > 0 ? c.Substring(q + 1) : "";
            }
            else
            {
                var m = Regex.Match(c, @"^(.+?\.(exe|com|bat|cmd))(\s|$)", RegexOptions.IgnoreCase);
                exe = m.Success ? m.Groups[1].Value : c.Split(' ')[0];
                args = m.Success ? c.Substring(m.Groups[1].Length) : c.Substring(exe.Length);
            }
            if (!exe.Contains("\\"))
            {
                string sys = Path.Combine(Environment.SystemDirectory, exe);
                if (File.Exists(sys)) exe = sys; else if (File.Exists(sys + ".exe")) exe = sys + ".exe";
            }
            // Script hosts run someone else's file: judge that file instead.
            string name = Path.GetFileName(exe).ToLowerInvariant();
            if (new[] { "rundll32.exe", "wscript.exe", "cscript.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "mshta.exe", "conhost.exe" }.Contains(name))
            {
                var m = Regex.Match(args, "\"([^\"]+\\.(dll|exe|vbs|js|ps1|bat|cmd|hta))\"|([^\\s\",]+\\.(dll|exe|vbs|js|ps1|bat|cmd|hta))", RegexOptions.IgnoreCase);
                if (m.Success) { string t = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value; if (t.Contains("\\")) return t; }
            }
            return exe;
        }
    }

    /// <summary>
    /// Finds background software on any PC, whatever the brand: services that start with Windows, startup apps,
    /// scheduled tasks that run at startup or sign-in, and Store apps allowed to run in the background or at sign-in.
    /// These show on the Optimize page as "not reviewed" items next to the reviewed catalog.
    /// </summary>
    static class Discovery
    {
        static readonly Regex Updater = new Regex(@"updat|upgrad", RegexOptions.IgnoreCase); // keep software patched: never offered
        static readonly string[] MicrosoftPackagePublishers = { "8wekyb3d8bbwe", "cw5n1h2txyewy" };
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        const string BackgroundApps = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\";
        const string PackageData = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\";
        const string PackageRepo = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages\";

        class Ctx
        {
            public HashSet<string> Covered;            // engine keys already handled by the reviewed catalog
            public HashSet<string> CoveredServices;    // services the catalog detects
            public HashSet<string> BackedUp;           // engine keys Featherweight changed before (so they can be undone)
            public List<string> SecurityDirs;          // folders of registered antivirus / firewall products
            public string WinDir, Self;
        }

        public static List<Optimization> Find(List<Optimization> catalog, Action<string> warn = null)
        {
            var ctx = new Ctx
            {
                Covered = new HashSet<string>(catalog.SelectMany(o => o.Actions).Select(a => Engine.Key(a.D)).Where(k => k != null)),
                CoveredServices = new HashSet<string>(catalog.Where(o => o.Detect != null && o.Detect.ContainsKey("service"))
                    .SelectMany(o => o.Detect["service"] is string ? new[] { (string)o.Detect["service"] } : ((System.Collections.IEnumerable)o.Detect["service"]).Cast<object>().Select(Convert.ToString)),
                    StringComparer.OrdinalIgnoreCase),
                BackedUp = new HashSet<string>(Engine.ListBackups().SelectMany(b => b.Items).Select(Engine.Key).Where(k => k != null)),
                SecurityDirs = SecurityProductDirs(),
                WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\",
                Self = Process.GetCurrentProcess().MainModule.FileName,
            };
            var res = new List<Optimization>();
            foreach (var step in new Action<Ctx, List<Optimization>>[] { Services, RunKeys, StartupFolders, Tasks, Packages })
                try { step(ctx, res); } catch (Exception ex) { if (warn != null) warn("Discovery step " + step.Method.Name + " failed: " + ex.Message); }
            foreach (var o in res)
            {
                // Identifiers that are the same on every PC, for matching knowledge-base entries.
                string id = o.Id.Substring("found:".Length), rest = id.Substring(id.IndexOf(':') + 1);
                if (id.StartsWith("service:")) o.Ids["service"] = rest;
                else if (id.StartsWith("app:")) o.Ids["package"] = rest;
                else if (id.StartsWith("startup:")) o.Ids["startup"] = rest.Substring(rest.LastIndexOf(':') + 1);
                else if (id.StartsWith("task:"))
                {
                    o.Ids["taskpath"] = rest;
                    o.Ids["task"] = Regex.Replace(rest.Substring(rest.LastIndexOf('\\') + 1), @"-sid-S-1-[\d-]+$", "", RegexOptions.IgnoreCase);
                }
                o.Ids["publisher"] = o.Category;
                string exe = o.ExePaths.FirstOrDefault(p => p != null && !p.EndsWith("\\"));
                if (exe != null) o.Ids["exe"] = Path.GetFileName(exe);
            }
            return res;
        }

        /// <summary>True for programs Featherweight must never offer to change.</summary>
        static bool Protected(Ctx c, string exe)
        {
            if (string.IsNullOrEmpty(exe)) return true;
            if (string.Equals(exe, c.Self, StringComparison.OrdinalIgnoreCase)) return true;
            if (Updater.IsMatch(Path.GetFileName(exe))) return true;
            string dir = (Path.GetDirectoryName(exe) ?? "") + "\\";
            return c.SecurityDirs.Any(s => dir.StartsWith(s, StringComparison.OrdinalIgnoreCase));    // antivirus / firewall
        }

        static List<string> SecurityProductDirs()
        {
            var res = new List<string>();
            foreach (var cls in new[] { "AntiVirusProduct", "FirewallProduct", "AntiSpywareProduct" })
                try
                {
                    foreach (ManagementObject o in new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT pathToSignedProductExe, pathToSignedReportingExe FROM " + cls).Get())
                        foreach (var p in new[] { "pathToSignedProductExe", "pathToSignedReportingExe" })
                        {
                            string exe = Publisher.ExeFromCommand(Convert.ToString(o[p]));
                            if (string.IsNullOrEmpty(exe) || exe.StartsWith("windowsdefender:", StringComparison.OrdinalIgnoreCase)) continue;
                            string dir = Path.GetDirectoryName(exe);
                            if (!string.IsNullOrEmpty(dir) && dir.Length > 3) res.Add(dir.TrimEnd('\\') + "\\");
                        }
                }
                catch { } // not available on every edition: the Windows-folder and LaunchProtected rules still apply
            return res;
        }

        static Optimization Item(string id, string kind, string title, string brand, string description, params Dictionary<string, object>[] actions)
        {
            return new Optimization
            {
                Id = "found:" + id, Discovered = true, Kind = kind, Title = title, Category = brand, Description = description,
                Risk = "Not reviewed", Level = "optional", Present = true,
                Actions = actions.Select(a => new PlanItem { D = a }).ToList()
            };
        }

        /// <summary>
        /// Programs inside the Windows folder: Microsoft's (or ones naming no company) are never offered. Other companies'
        /// are helpers installed with a hardware driver (AMD, Realtek, HP…): offered, but only under "Show advanced".
        /// </summary>
        static bool SkipWindowsPart(Ctx c, string exe, string brand, out bool driver)
        {
            driver = exe != null && exe.StartsWith(c.WinDir, StringComparison.OrdinalIgnoreCase);
            return driver && (Publisher.IsMicrosoft(brand) || brand == Publisher.Unknown);
        }

        static void MarkDriver(Optimization o, string brand)
        {
            // Driver helpers can run thermals, audio, touchpad, display or security chips: shown so their memory use is
            // visible, but only a reviewed catalog entry may change one.
            o.Level = "advanced"; o.ViewOnly = true;
            o.Kind = "Driver " + o.Kind.ToLowerInvariant();
            string what = o.Kind == "Driver service" ? "a background service" : o.Kind == "Driver startup app" ? "a program that starts when you sign in" : "a task that runs at startup or sign-in";
            o.Description = "Installed together with a hardware driver from " + brand + ": " + what + " (" + Path.GetFileName(o.ExePaths.FirstOrDefault() ?? "") + "). " +
                "Helpers like this can control things such as cooling, audio, touchpad, display or security chips, so Featherweight only shows it here " +
                "for information and won't change it until it has been reviewed.";
        }

        static bool Wanted(Ctx c, bool isOn, params Dictionary<string, object>[] actions)
        {
            var keys = actions.Select(a => Engine.Key(a)).ToList();
            if (keys.Any(k => c.Covered.Contains(k))) return false; // the reviewed catalog already has it
            return isOn || keys.Any(k => c.BackedUp.Contains(k));  // off and never touched by us: nothing to show
        }

        // ---------------------------------------------------------------- services
        static void Services(Ctx c, List<Optimization> res)
        {
            var dependedOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var all = new List<Tuple<string, RegistryKey>>();
            using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
            {
                foreach (var n in root.GetSubKeyNames())
                {
                    var k = root.OpenSubKey(n); if (k == null) continue;
                    var dep = k.GetValue("DependOnService") as string[];
                    if (dep != null) foreach (var d in dep) dependedOn.Add(d);
                    all.Add(Tuple.Create(n, k));
                }
            }
            foreach (var t in all)
                using (var k = t.Item2)
                {
                    string name = t.Item1;
                    int type = Convert.ToInt32(k.GetValue("Type", 0)), start = Convert.ToInt32(k.GetValue("Start", 4));
                    if ((type & 0x30) == 0) continue;                                         // drivers and other kernel parts
                    if (start != 2 && start != 3) continue;                                    // automatic or manual only
                    if (Convert.ToInt32(k.GetValue("LaunchProtected", 0)) != 0) continue;     // antimalware / protected services
                    if (dependedOn.Contains(name) || c.CoveredServices.Contains(name)) continue;
                    if (Updater.IsMatch(name)) continue;
                    string exe = Publisher.ExeFromCommand(k.GetValue("ImagePath", "", RegistryValueOptions.None) as string);
                    if (Protected(c, exe) || !File.Exists(exe)) continue;
                    string brand = Publisher.Of(exe);
                    bool driver;
                    if (Publisher.IsMicrosoft(brand) || SkipWindowsPart(c, exe, brand, out driver)) continue;
                    var act = new Dictionary<string, object> { { "type", "service" }, { "name", name }, { "start", "Manual" } };
                    if (!Wanted(c, start == 2, act)) continue;
                    string display = ServiceDisplayName(name, k);
                    var o = Item("service:" + name, "Service", display, brand,
                        "A background service from " + brand + " that starts with Windows (" + Path.GetFileName(exe) + "). Featherweight hasn't reviewed it yet. " +
                        "Optimizing sets it to Manual: Windows then starts it only when an app asks for it, and it is fully running again after Restore default. " +
                        "Some features of " + brand + "'s software (for example hotkeys, fan, lighting or audio effects) may need it, so if something stops working, restore it.", act);
                    o.ExePaths.Add(exe);
                    if (driver) MarkDriver(o, brand);
                    res.Add(o);
                }
        }

        static string ServiceDisplayName(string name, RegistryKey k)
        {
            try { using (var sc = new System.ServiceProcess.ServiceController(name)) if (!string.IsNullOrEmpty(sc.DisplayName)) return sc.DisplayName; } catch { }
            string d = k.GetValue("DisplayName") as string;
            return string.IsNullOrEmpty(d) || d.StartsWith("@") ? name : d;
        }

        // ---------------------------------------------------------------- startup apps (Run keys, Startup folders)
        static void RunKeys(Ctx c, List<Optimization> res)
        {
            var sources = new[]
            {
                Tuple.Create("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run"),
                Tuple.Create("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run"),
                Tuple.Create("HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32"),
            };
            foreach (var s in sources)
                using (var k = s.Item1 == "HKCU" ? Engine.OpenUserKey(s.Item2) : Registry.LocalMachine.OpenSubKey(s.Item2))
                {
                    if (k == null) continue;
                    foreach (var v in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(v)) continue;
                        string exe = Publisher.ExeFromCommand(Convert.ToString(k.GetValue(v)));
                        if (Protected(c, exe) || Updater.IsMatch(v)) continue;
                        Startup(c, res, s.Item1, s.Item3, v, v, exe);
                    }
                }
        }

        static void StartupFolders(Ctx c, List<Optimization> res)
        {
            var folders = new[]
            {
                Tuple.Create("HKCU", Environment.GetFolderPath(Environment.SpecialFolder.Startup)),
                Tuple.Create("HKLM", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)),
            };
            foreach (var f in folders)
            {
                if (string.IsNullOrEmpty(f.Item2) || !Directory.Exists(f.Item2)) continue;
                foreach (var file in Directory.GetFiles(f.Item2))
                {
                    string fn = Path.GetFileName(file);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string exe = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : file;
                    if (Protected(c, exe)) continue;
                    Startup(c, res, f.Item1, "StartupFolder", fn, Path.GetFileNameWithoutExtension(fn), exe);
                }
            }
        }

        static string ShortcutTarget(string lnk)
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                string t = shell.CreateShortcut(lnk).TargetPath;
                return string.IsNullOrEmpty(t) ? null : t;
            }
            catch { return null; }
        }

        /// <summary>One startup app, switched off the same way as Task Manager's Startup apps tab (StartupApproved).</summary>
        static void Startup(Ctx c, List<Optimization> res, string hive, string approvedKey, string valueName, string title, string exe)
        {
            string brand = Publisher.Of(exe);
            bool driver;
            if (SkipWindowsPart(c, exe, brand, out driver)) return;
            string path = hive + ":\\" + Approved + approvedKey;
            var cur = Engine.RegState(path, valueName);
            var bytes = (bool)cur["Exists"] ? cur["Value"] as List<int> : null;
            bool on = bytes == null || bytes.Count == 0 || bytes[0] % 2 == 0; // odd first byte = disabled
            // Already off (e.g. via Task Manager): keep its exact value so it reads as done instead of rewriting it.
            object off = !on && bytes != null ? bytes.Cast<object>().ToList() : new List<object> { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            var act = new Dictionary<string, object> { { "type", "registry" }, { "path", path }, { "name", valueName }, { "kind", "Binary" }, { "value", off },
                                                        { "label", "Startup app '" + title + "' → off (same as Task Manager)" } };
            if (!Wanted(c, on, act)) return;
            string desc = FileDescription(exe);
            var o = Item("startup:" + hive + ":" + approvedKey + ":" + valueName, "Startup app", desc != null && desc.Length < 60 ? desc : title, brand,
                (desc ?? title) + " from " + brand + " starts every time you sign in (" + Path.GetFileName(exe) + "). Featherweight hasn't reviewed it yet. " +
                "Optimizing switches it off in the same place as Task Manager's Startup apps tab. The app still works when you open it yourself; " +
                "you only lose what it does on its own in the background (for example tray icons, sync or notifications).", act);
            o.ExePaths.Add(exe);
            if (driver) MarkDriver(o, brand);
            res.Add(o);
        }

        static string FileDescription(string exe)
        {
            try { if (File.Exists(exe)) { var d = FileVersionInfo.GetVersionInfo(exe).FileDescription; if (!string.IsNullOrWhiteSpace(d)) return d.Trim(); } } catch { }
            return null;
        }

        // ---------------------------------------------------------------- scheduled tasks that run at startup or sign-in
        static void Tasks(Ctx c, List<Optimization> res)
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            WalkTasks(c, res, svc.GetFolder("\\"));
        }

        static void WalkTasks(Ctx c, List<Optimization> res, dynamic folder)
        {
            string fpath = folder.Path;
            if (fpath.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) return;
            foreach (dynamic t in folder.GetTasks(1)) // 1 = include hidden
                try
                {
                    string name = t.Name;
                    if (Updater.IsMatch(name)) continue;
                    bool atStart = false;
                    foreach (dynamic tr in t.Definition.Triggers) { int ty = tr.Type; if (ty == 8 || ty == 9) atStart = true; } // boot / logon
                    if (!atStart) continue;
                    string exe = null;
                    foreach (dynamic a in t.Definition.Actions) if ((int)a.Type == 0) { exe = Publisher.ExeFromCommand("\"" + (string)a.Path + "\" " + (string)a.Arguments); break; }
                    if (Protected(c, exe)) continue;
                    string brand = Publisher.Of(exe);
                    bool driver;
                    if (Publisher.IsMicrosoft(brand) || SkipWindowsPart(c, exe, brand, out driver)) continue;
                    string dir = fpath == "\\" ? "\\" : fpath + "\\";
                    var act = new Dictionary<string, object> { { "type", "task" }, { "path", dir }, { "name", name } };
                    if (!Wanted(c, (bool)t.Enabled, act)) continue;
                    // Per-user tasks carry the account's SID in their name ("OmenOverlay-sid-S-1-5-21-…"): show a clean name.
                    string shown = Regex.Replace(name, @"-sid-S-1-[\d-]+$", "", RegexOptions.IgnoreCase);
                    string fdesc = FileDescription(exe);
                    if (fdesc != null && fdesc.Length < 60) shown = fdesc;
                    var o = Item("task:" + dir + name, "Scheduled task", shown, brand,
                        "A scheduled task from " + brand + " that runs when Windows starts or when you sign in (" + Path.GetFileName(exe) + "). " +
                        "Featherweight hasn't reviewed it yet. Optimizing disables the task; Restore default turns it back on. " +
                        "If a feature of " + brand + "'s software stops working, restore it.", act);
                    o.ExePaths.Add(exe);
                    if (driver) MarkDriver(o, brand);
                    res.Add(o);
                }
                catch { }
            foreach (dynamic sub in folder.GetFolders(0)) WalkTasks(c, res, sub);
        }

        // ---------------------------------------------------------------- Store apps (background permission, sign-in tasks)
        static void Packages(Ctx c, List<Optimization> res)
        {
            // family name → install folder, from the signed-in user's package list
            var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var repo = Engine.OpenUserKey(PackageRepo))
                if (repo != null)
                    foreach (var full in repo.GetSubKeyNames())
                        using (var k = repo.OpenSubKey(full))
                        {
                            string fam = FamilyOf(full), root = k == null ? null : k.GetValue("PackageRootFolder") as string;
                            if (fam != null && root != null && !folders.ContainsKey(fam)) folders[fam] = root.TrimEnd('\\') + "\\";
                        }

            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 1. packaged programs running in the background right now (no window open: not something the user is using)
            string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + "\\";
            var windowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inBackground = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
                try
                {
                    string path = Native.ProcessPath(p.Id);
                    if (path == null || !path.StartsWith(apps, StringComparison.OrdinalIgnoreCase)) continue;
                    string full = path.Substring(apps.Length).Split('\\')[0], fam = FamilyOf(full);
                    if (fam == null) continue;
                    if (!folders.ContainsKey(fam)) folders[fam] = apps + full + "\\";
                    if (p.MainWindowHandle != IntPtr.Zero) windowed.Add(fam); else inBackground.Add(fam);
                }
                catch { }
                finally { p.Dispose(); }
            inBackground.ExceptWith(windowed);
            families.UnionWith(inBackground);
            // 2. packages with a sign-in task, and 3. ones Featherweight changed before
            using (var data = Engine.OpenUserKey(PackageData))
                if (data != null) foreach (var fam in data.GetSubKeyNames()) if (StartupTasks(fam).Count > 0) families.Add(fam);
            foreach (var key in c.BackedUp)
            {
                var m = Regex.Match(key, @"^registry\|hkcu:\\" + Regex.Escape(BackgroundApps.ToLowerInvariant()) + @"([^|\\]+)\|");
                if (m.Success) families.Add(m.Groups[1].Value);
            }

            foreach (var fam in families)
            {
                // Microsoft's own Store apps and parts of Windows are left alone (Phone Link etc. are reviewed in the catalog).
                string pub = fam.Substring(fam.LastIndexOf('_') + 1).ToLowerInvariant();
                if (MicrosoftPackagePublishers.Contains(pub)) continue;
                string folder; folders.TryGetValue(fam, out folder);
                if (folder != null && Protected(c, folder + "x.exe")) continue;

                var acts = new List<Dictionary<string, object>>();
                bool on = false;
                foreach (var task in StartupTasks(fam))
                {
                    var st = Engine.RegState("HKCU:\\" + PackageData + fam + "\\" + task, "State");
                    int state = (bool)st["Exists"] ? Convert.ToInt32(st["Value"]) : 0;
                    // 2 = on, 1 = switched off by the user. 0 is off by default and 3/4 are set by policy: leave those alone.
                    var act = new Dictionary<string, object> { { "type", "registry" }, { "path", "HKCU:\\" + PackageData + fam + "\\" + task }, { "name", "State" } };
                    if (state != 2 && state != 1 && !c.BackedUp.Contains(Engine.Key(act))) continue;
                    if (state == 2) on = true;
                    act["kind"] = "DWord"; act["value"] = 1; act["label"] = "Starts at sign-in (" + task + ") → off (same as Task Manager)";
                    acts.Add(act);
                }
                string bg = "HKCU:\\" + BackgroundApps + fam;
                var bgState = Engine.RegState(bg, "Disabled");
                if (!((bool)bgState["Exists"] && Convert.ToInt32(bgState["Value"]) == 1) && inBackground.Contains(fam)) on = true;
                acts.Add(new Dictionary<string, object> { { "type", "registry" }, { "path", bg }, { "name", "Disabled" }, { "kind", "DWord" }, { "value", 1 }, { "label", "Run in the background → off" } });
                acts.Add(new Dictionary<string, object> { { "type", "registry" }, { "path", bg }, { "name", "DisabledByUser" }, { "kind", "DWord" }, { "value", 1 }, { "label", "Run in the background → off (chosen by you)" } });
                if (!Wanted(c, on, acts.ToArray())) continue;

                string title, brand;
                Publisher.Manifest(folder, out title, out brand);
                if (brand == null)
                {
                    string exe = folder != null && Directory.Exists(folder) ? Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
                    brand = exe != null ? Publisher.Of(exe) : Publisher.Unknown;
                }
                if (title == null) title = PackageTitle(fam);
                var o = Item("app:" + fam, "Store app", title, brand,
                    title + " is a Store app from " + brand + " that can run in the background or start when you sign in. " +
                    "Featherweight hasn't reviewed it yet. Optimizing turns that off, the same switches as Settings → Apps → Installed apps → Advanced options and " +
                    "Task Manager's Startup apps tab. The app still works when you open it; it just doesn't run on its own until you do. " +
                    "Some apps use the background for things like fan, lighting or performance modes, so if something stops working, restore it.", acts.ToArray());
                if (folder != null) o.ExePaths.Add(folder); // a folder: every program inside the package counts
                res.Add(o);
            }
        }

        /// <summary>"AD2F1837.OMENCommandCenter_1101.2405.7.0_x64__v10z8vjag6ke6" → "AD2F1837.OMENCommandCenter_v10z8vjag6ke6".</summary>
        static string FamilyOf(string fullName)
        {
            int u = fullName.IndexOf('_'), dd = fullName.LastIndexOf("__", StringComparison.Ordinal);
            if (u <= 0 || dd < u) return null;
            return fullName.Substring(0, u) + "_" + fullName.Substring(dd + 2);
        }

        static List<string> StartupTasks(string fam)
        {
            var res = new List<string>();
            using (var k = Engine.OpenUserKey(PackageData + fam))
                if (k != null)
                    foreach (var sub in k.GetSubKeyNames())
                        using (var s = k.OpenSubKey(sub))
                            if (s != null && s.GetValue("State") is int) res.Add(sub);
            return res;
        }

        static string PackageTitle(string fam)
        {
            string n = fam.Substring(0, fam.IndexOf('_'));
            int dot = n.IndexOf('.');
            if (dot > 0 && dot < n.Length - 1) n = n.Substring(dot + 1); // drop the publisher prefix ("AD2F1837.")
            return Regex.Replace(n, "([a-z])([A-Z])", "$1 $2");
        }
    }
}
