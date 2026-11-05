using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Featherweight
{
    enum ItemState { Applied, NotApplied, NotFound, Error }

    class PlanItem
    {
        public Dictionary<string, object> D;
        public string Type { get { return D.Str("type"); } }
        public string Why { get { return D.Str("why") ?? ""; } }

        public string Describe()
        {
            if (D.Str("label") != null) return D.Str("label");
            switch (Type)
            {
                case "service": return "Service '" + D.Str("name") + "' → " + D.Str("start") + (D.Bool("stop") ? ", stop now" : "");
                case "registry": return "Registry " + ShortPath(D.Str("path")) + "\\" + D.Str("name") + " = " + Engine.Normalize(D["value"]);
                case "task": return "Task " + D.Str("path") + D.Str("name") + " → Disabled";
                case "cert": return "Certificate '" + (D.Str("label") ?? D.Str("thumbprint")) + "' → remove from " + D.Str("location") + "\\" + D.Str("store");
                default: return "Unknown item type '" + Type + "'";
            }
        }

        static string ShortPath(string p)
        {
            if (p == null) return "";
            var parts = p.Split('\\');
            return parts.Length <= 4 ? p : parts[0] + "\\…\\" + parts[parts.Length - 2] + "\\" + parts[parts.Length - 1];
        }
    }

    class Plan
    {
        public string File, Name;
        public List<PlanItem> Items = new List<PlanItem>();

        public static Plan Load(string file)
        {
            var d = Json.ReadFile(file);
            return new Plan
            {
                File = file,
                Name = d.Str("name") ?? Path.GetFileNameWithoutExtension(file),
                Items = d.List("items").Select(x => new PlanItem { D = x }).ToList()
            };
        }
    }

    class BackupInfo
    {
        public string File, PlanName, Time; public int Count;
        public List<Dictionary<string, object>> Items;
    }

    static class Engine
    {
        static readonly Dictionary<string, string> StartMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { { "Manual", "demand" }, { "Disabled", "disabled" }, { "Automatic", "auto" }, { "AutomaticDelayed", "delayed-auto" } };

        public static string BackupsDir { get { return Paths.Backups; } }

        // ---------------------------------------------------------------- services
        public static string ServiceStart(string name)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
            {
                if (k == null || k.GetValue("Start") == null) return null;
                int start = Convert.ToInt32(k.GetValue("Start"));
                int delayed = Convert.ToInt32(k.GetValue("DelayedAutostart", 0));
                switch (start)
                {
                    case 0: return "boot";
                    case 1: return "system";
                    case 2: return delayed == 1 ? "delayed-auto" : "auto";
                    case 3: return "demand";
                    case 4: return "disabled";
                    default: return start.ToString();
                }
            }
        }

        public static bool ServiceRunning(string name)
        {
            try { using (var sc = new ServiceController(name)) return sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending; }
            catch { return false; }
        }

        static void SetServiceStart(string name, string start)
        {
            string output;
            int code = Run("sc.exe", "config \"" + name + "\" start= " + start, out output);
            if (code != 0) throw new Exception("sc config failed: " + output.Trim());
        }

        static void StopService(string name)
        {
            using (var sc = new ServiceController(name))
            {
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }

        public static int Run(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // ---------------------------------------------------------------- registry
        static RegistryKey Hive(string path, out string sub)
        {
            int i = path.IndexOf('\\');
            string hive = (i < 0 ? path : path.Substring(0, i)).TrimEnd(':').ToUpperInvariant();
            sub = i < 0 ? "" : path.Substring(i + 1);
            switch (hive)
            {
                case "HKLM": case "HKEY_LOCAL_MACHINE": return Registry.LocalMachine;
                case "HKCU":
                case "HKEY_CURRENT_USER":
                    // Elevated with a different admin account: HKCU is the admin's hive, so target the signed-in user's.
                    if (Admin.DifferentUser) { sub = Admin.DesktopUserSid + (sub.Length > 0 ? "\\" + sub : ""); return Registry.Users; }
                    return Registry.CurrentUser;
                case "HKCR": case "HKEY_CLASSES_ROOT": return Registry.ClassesRoot;
                case "HKU": case "HKEY_USERS": return Registry.Users;
                default: throw new Exception("unknown registry hive in " + path);
            }
        }

        /// <summary>Opens a key under the signed-in user's hive (see Hive for the different-admin case).</summary>
        public static RegistryKey OpenUserKey(string sub)
        {
            string s; var hive = Hive("HKCU\\" + sub, out s);
            return hive.OpenSubKey(s);
        }

        public static bool RegKeyExists(string path)
        {
            string sub; var hive = Hive(path, out sub);
            using (var k = hive.OpenSubKey(sub)) return k != null;
        }

        public static Dictionary<string, object> RegState(string path, string name)
        {
            string sub; var hive = Hive(path, out sub);
            var st = new Dictionary<string, object> { { "KeyExisted", false }, { "Exists", false } };
            using (var k = hive.OpenSubKey(sub))
            {
                if (k == null) return st;
                st["KeyExisted"] = true;
                if (!k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return st;
                st["Exists"] = true;
                st["Kind"] = k.GetValueKind(name).ToString();
                object v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var bytes = v as byte[];
                st["Value"] = bytes != null ? (object)bytes.Select(b => (int)b).ToList() : v;
                return st;
            }
        }

        static object ToRegValue(string kind, object value)
        {
            switch (kind)
            {
                case "Binary": return ((System.Collections.IEnumerable)value).Cast<object>().Select(x => Convert.ToByte(x)).ToArray();
                case "DWord": return Convert.ToInt32(value);
                case "QWord": return Convert.ToInt64(value);
                case "MultiString":
                    var en = value as System.Collections.IEnumerable;
                    return value is string ? new[] { (string)value } : en.Cast<object>().Select(x => Convert.ToString(x)).ToArray();
                default: return Convert.ToString(value);
            }
        }

        static void SetReg(string path, string name, string kind, object value)
        {
            string sub; var hive = Hive(path, out sub);
            using (var k = hive.CreateSubKey(sub)) // opens the key if it exists; never wipes it
                k.SetValue(name, ToRegValue(kind, value), (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), kind));
        }

        public static string Normalize(object v)
        {
            if (v == null) return "";
            if (v is string) return (string)v;
            var en = v as System.Collections.IEnumerable;
            if (en != null) return string.Join(",", en.Cast<object>().Select(x => Convert.ToString(x)));
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- scheduled tasks (COM)
        static dynamic GetTask(string path, string name)
        {
            try
            {
                dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                svc.Connect();
                string folder = path == "\\" ? "\\" : path.TrimEnd('\\');
                return svc.GetFolder(folder).GetTask(name);
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- certificates
        static X509Certificate2 FindCert(string location, string store, string thumb)
        {
            var st = new X509Store(store, (StoreLocation)Enum.Parse(typeof(StoreLocation), location));
            st.Open(OpenFlags.ReadOnly);
            try { return st.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => string.Equals(c.Thumbprint, thumb, StringComparison.OrdinalIgnoreCase)); }
            finally { st.Close(); }
        }

        static void CertStoreEdit(string location, string store, Action<X509Store> edit)
        {
            var st = new X509Store(store, (StoreLocation)Enum.Parse(typeof(StoreLocation), location));
            st.Open(OpenFlags.ReadWrite);
            try { edit(st); } finally { st.Close(); }
        }

        // ---------------------------------------------------------------- status
        public static ItemState Check(PlanItem it, out string detail)
        {
            var d = it.D;
            if (d.Bool("missing")) { detail = "not on this PC"; return ItemState.NotFound; }
            try
            {
                switch (it.Type)
                {
                    case "service":
                        {
                            string cur = ServiceStart(d.Str("name"));
                            if (cur == null) { detail = "service not installed"; return ItemState.NotFound; }
                            string target; StartMap.TryGetValue(d.Str("start") ?? "", out target);
                            detail = "now: " + cur + (ServiceRunning(d.Str("name")) ? ", running" : ", stopped");
                            return cur == target ? ItemState.Applied : ItemState.NotApplied;
                        }
                    case "registry":
                        {
                            var st = RegState(d.Str("path"), d.Str("name"));
                            bool exists = (bool)st["Exists"];
                            detail = "now: " + (exists ? Normalize(st["Value"]) : "not set");
                            return exists && (string)st["Kind"] == d.Str("kind") && Normalize(st["Value"]) == Normalize(d["value"]) ? ItemState.Applied : ItemState.NotApplied;
                        }
                    case "task":
                        {
                            var t = GetTask(d.Str("path"), d.Str("name"));
                            if (t == null) { detail = "task not found"; return ItemState.NotFound; }
                            bool enabled = t.Enabled;
                            detail = enabled ? "now: enabled" : "now: disabled";
                            return enabled ? ItemState.NotApplied : ItemState.Applied;
                        }
                    case "cert":
                        {
                            var c = FindCert(d.Str("location"), d.Str("store"), d.Str("thumbprint"));
                            detail = c == null ? "not in store" : "present: " + c.Subject;
                            return c == null ? ItemState.Applied : ItemState.NotApplied;
                        }
                }
                detail = "unknown type"; return ItemState.Error;
            }
            catch (Exception ex) { detail = ex.Message; return ItemState.Error; }
        }

        // ---------------------------------------------------------------- apply
        /// <returns>Path of the backup file, or null when nothing changed.</returns>
        public static string Apply(Plan plan, IList<PlanItem> items, bool dryRun, Action<string> log)
        {
            Directory.CreateDirectory(BackupsDir);
            // Unique name: two runs in the same second must never share (or delete) a backup file.
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss"), backupPath = Path.Combine(BackupsDir, "backup-" + stamp + ".json");
            for (int n = 2; File.Exists(backupPath); n++) backupPath = Path.Combine(BackupsDir, "backup-" + stamp + "-" + n + ".json");
            var done = new List<object>(); // the file is only written once the first change is recorded
            Action save = () => Json.WriteFile(backupPath, new Dictionary<string, object> {
                { "Plan", plan.File }, { "PlanName", plan.Name }, { "Time", DateTime.Now.ToString("s") }, { "Items", done } });

            log((dryRun ? "DRY RUN — nothing will be changed. " : "") + "Plan: " + plan.Name);
            int ok = 0, skipped = 0, failed = 0;
            foreach (var it in items)
            {
                var d = it.D; string label = it.Describe();
                try
                {
                    string skip = d.Bool("missing") ? "not on this PC" : null, would = null;
                    if (skip == null)
                    switch (it.Type)
                    {
                        case "service":
                            {
                                string name = d.Str("name"), cur = ServiceStart(name);
                                if (cur == null) { skip = "service not found"; break; }
                                string target;
                                if (!StartMap.TryGetValue(d.Str("start") ?? "", out target)) throw new Exception("unknown start type '" + d.Str("start") + "'");
                                bool running = ServiceRunning(name);
                                if (cur == target && !(d.Bool("stop") && running)) { skip = "already " + target; break; }
                                if (dryRun) { would = "now: " + cur + ", running=" + running; break; }
                                done.Add(new Dictionary<string, object> { { "type", "service" }, { "name", name }, { "start", cur }, { "running", running } }); save();
                                SetServiceStart(name, target);
                                if (d.Bool("stop") && running) StopService(name);
                                break;
                            }
                        case "registry":
                            {
                                var st = RegState(d.Str("path"), d.Str("name"));
                                if ((bool)st["Exists"] && (string)st["Kind"] == d.Str("kind") && Normalize(st["Value"]) == Normalize(d["value"])) { skip = "already set"; break; }
                                if (dryRun) { would = "now: " + ((bool)st["Exists"] ? Normalize(st["Value"]) : "not set"); break; }
                                done.Add(new Dictionary<string, object> { { "type", "registry" }, { "path", d.Str("path") }, { "name", d.Str("name") }, { "orig", st } }); save();
                                SetReg(d.Str("path"), d.Str("name"), d.Str("kind"), d["value"]);
                                break;
                            }
                        case "task":
                            {
                                var t = GetTask(d.Str("path"), d.Str("name"));
                                if (t == null) { skip = "task not found"; break; }
                                if (!(bool)t.Enabled) { skip = "already disabled"; break; }
                                if (dryRun) { would = "now: enabled"; break; }
                                done.Add(new Dictionary<string, object> { { "type", "task" }, { "path", d.Str("path") }, { "name", d.Str("name") }, { "state", "Ready" } }); save();
                                t.Enabled = false;
                                break;
                            }
                        case "cert":
                            {
                                var c = FindCert(d.Str("location"), d.Str("store"), d.Str("thumbprint"));
                                if (c == null) { skip = "certificate not in store"; break; }
                                if (dryRun) { would = "now present: " + c.Subject; break; }
                                string cer = Path.Combine(BackupsDir, "cert-" + c.Thumbprint + ".cer");
                                File.WriteAllBytes(cer, c.Export(X509ContentType.Cert));
                                done.Add(new Dictionary<string, object> { { "type", "cert" }, { "location", d.Str("location") }, { "store", d.Str("store") },
                                    { "thumbprint", c.Thumbprint }, { "file", cer }, { "subject", c.Subject } }); save();
                                CertStoreEdit(d.Str("location"), d.Str("store"), s => s.Remove(c));
                                break;
                            }
                        default: throw new Exception("unknown item type '" + it.Type + "'");
                    }
                    if (skip != null) { log("SKIP   " + label + "  (" + skip + ")"); skipped++; }
                    else if (would != null) log("WOULD  " + label + "  (" + would + ")");
                    else { log("OK     " + label); ok++; }
                }
                catch (Exception ex) { log("FAIL   " + label + "  → " + ex.Message); failed++; }
            }
            if (dryRun) { log("Dry run finished."); return null; }
            log(string.Format("Done: {0} changed, {1} skipped, {2} failed.", ok, skipped, failed));
            if (done.Count == 0) return null;
            log("Backup saved: " + Path.GetFileName(backupPath) + "  (undo it on the Backups page)");
            return backupPath;
        }

        // ---------------------------------------------------------------- backups / restore
        public static List<BackupInfo> ListBackups()
        {
            var res = new List<BackupInfo>();
            if (!Directory.Exists(BackupsDir)) return res;
            foreach (var f in Directory.GetFiles(BackupsDir, "backup-*.json").OrderByDescending(x => x))
            {
                try
                {
                    var d = Json.ReadFile(f);
                    var items = d.List("Items");
                    string planName = d.Str("PlanName");
                    if (planName == null && d.Str("Plan") != null) planName = Path.GetFileName(d.Str("Plan"));
                    res.Add(new BackupInfo { File = f, Time = d.Str("Time"), PlanName = planName ?? "?", Count = items.Count, Items = items });
                }
                catch { }
            }
            return res;
        }

        public static string DescribeBackupItem(Dictionary<string, object> i)
        {
            switch (i.Str("type"))
            {
                case "service": return "Service '" + i.Str("name") + "' was " + i.Str("start") + (i.Bool("running") ? ", running" : "");
                case "registry":
                    var o = i.Obj("orig");
                    return "Registry " + i.Str("path") + "\\" + i.Str("name") + " was " + (o.Bool("Exists") ? Normalize(o["Value"]) : "not set");
                case "task": return "Task " + i.Str("path") + i.Str("name") + " was enabled";
                case "cert": return "Certificate " + i.Str("subject") + " was in " + i.Str("location") + "\\" + i.Str("store");
                default: return i.Str("type");
            }
        }

        public static void Restore(string backupPath, Action<string> log)
        {
            var b = Json.ReadFile(backupPath);
            log("Restoring " + Path.GetFileName(backupPath) + " (applied " + b.Str("Time") + ")");
            var items = b.List("Items"); items.Reverse();
            foreach (var i in items) RestoreItem(i, Path.GetDirectoryName(backupPath), log);
            log("Restore finished. Reboot to make sure startup behaviour is back to how it was.");
        }

        /// <summary>Identity of a change, shared by plan items and backup entries (used to find an item's saved original).</summary>
        public static string Key(Dictionary<string, object> d)
        {
            switch (d.Str("type"))
            {
                case "service": return "service|" + d.Str("name");
                case "registry": return ("registry|" + d.Str("path") + "|" + d.Str("name")).ToLowerInvariant();
                case "task": return ("task|" + d.Str("path") + d.Str("name")).ToLowerInvariant();
                case "cert": return "cert|" + (d.Str("thumbprint") ?? "").ToUpperInvariant();
                default: return null;
            }
        }

        /// <summary>Puts one backed-up setting back. Failures are logged, not thrown.</summary>
        public static bool RestoreItem(Dictionary<string, object> i, string backupDir, Action<string> log)
        {
                try
                {
                    switch (i.Str("type"))
                    {
                        case "service":
                            SetServiceStart(i.Str("name"), i.Str("start"));
                            if (i.Bool("running")) { try { using (var sc = new ServiceController(i.Str("name"))) sc.Start(); } catch { } }
                            log("OK     service '" + i.Str("name") + "' → " + i.Str("start") + (i.Bool("running") ? ", started" : ""));
                            break;
                        case "registry":
                            {
                                var o = i.Obj("orig");
                                if (o.Bool("Exists"))
                                {
                                    SetReg(i.Str("path"), i.Str("name"), o.Str("Kind"), o["Value"]);
                                    log("OK     registry " + i.Str("path") + "\\" + i.Str("name") + " restored");
                                }
                                else
                                {
                                    string sub; var hive = Hive(i.Str("path"), out sub);
                                    using (var k = hive.OpenSubKey(sub, true)) if (k != null) k.DeleteValue(i.Str("name"), false);
                                    if (!o.Bool("KeyExisted"))
                                        using (var k = hive.OpenSubKey(sub))
                                            if (k != null && k.ValueCount == 0 && k.SubKeyCount == 0) { k.Close(); hive.DeleteSubKey(sub, false); }
                                    log("OK     registry " + i.Str("path") + "\\" + i.Str("name") + " removed (was not set before)");
                                }
                                break;
                            }
                        case "task":
                            if (i.Str("state") != "Disabled")
                            {
                                var t = GetTask(i.Str("path"), i.Str("name"));
                                if (t == null) throw new Exception("task not found");
                                t.Enabled = true;
                            }
                            log("OK     task " + i.Str("path") + i.Str("name") + " re-enabled");
                            break;
                        case "cert":
                            {
                                // the folder may have moved since the backup was made; fall back to the copy next to the backup
                                string f = i.Str("file");
                                if (f == null || !File.Exists(f)) f = Path.Combine(backupDir, "cert-" + i.Str("thumbprint") + ".cer");
                                var c = new X509Certificate2(File.ReadAllBytes(f));
                                CertStoreEdit(i.Str("location"), i.Str("store"), s => s.Add(c));
                                log("OK     certificate " + i.Str("subject") + " put back");
                                break;
                            }
                    }
                    return true;
                }
                catch (Exception ex) { log("FAIL   " + i.Str("type") + " " + (i.Str("name") ?? i.Str("subject")) + " → " + ex.Message); return false; }
        }
    }
}
