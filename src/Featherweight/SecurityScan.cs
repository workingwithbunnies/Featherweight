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
    enum Severity { OK = 0, Info = 1, Warning = 2, Alert = 3 }

    class Finding
    {
        public Severity Severity; public string Area, Title, Detail;
        public PlanItem Fix; // optional one-click fix (applied through the plan engine, so it is backed up)
        public string FixLabel;                 // button text for Fix, e.g. "Switch off this startup entry (backed up)"
        public string OpenLabel, OpenTarget;    // "Open in Windows" button: a Settings / Windows Security page, or an action Featherweight runs
    }

    // Read-only checks that mirror the manual sweep: Defender, signatures, persistence, certificates, network.
    static partial class SecurityScan
    {
        static readonly Regex UserWritable = new Regex(@"\\AppData\\|\\Temp\\|\\Downloads\\|\\Users\\Public\\", RegexOptions.IgnoreCase);

        public static List<Finding> Run(Action<string> progress)
        {
            var f = new List<Finding>();
            Step(f, progress, "Microsoft Defender", Defender);
            Step(f, progress, "Defender scans", DefenderScans);
            Step(f, progress, "Running programs", Programs);
            Step(f, progress, "Root certificates", RootCerts);
            Step(f, progress, "Autostart entries", Autostart);
            Step(f, progress, "Scheduled tasks", Tasks);
            Step(f, progress, "Hijack points", HijackPoints);
            Step(f, progress, "Network", Network);
            Step(f, progress, "Windows protection", WindowsProtection);
            Step(f, progress, "Windows Update", WindowsUpdate);
            Step(f, progress, "Sign-in and sharing", SignInAndSharing);
            Step(f, progress, "Services", ServicesFromUserFolders);
            Step(f, progress, "Browser extensions", BrowserExtensions);
            return f.OrderByDescending(x => x.Severity).ThenBy(x => x.Area).ToList();
        }

        static void Step(List<Finding> f, Action<string> progress, string area, Action<List<Finding>> check)
        {
            progress("Checking " + area.ToLower() + "…");
            try { check(f); }
            catch (Exception ex) { f.Add(new Finding { Severity = Severity.Info, Area = area, Title = "Check could not run", Detail = ex.Message }); }
        }

        static void Add(List<Finding> f, Severity s, string area, string title, string detail, PlanItem fix = null)
        { f.Add(new Finding { Severity = s, Area = area, Title = title, Detail = detail, Fix = fix }); }

        // ---------------------------------------------------------------- Defender
        static void Defender(List<Finding> f)
        {
            const string ns = @"root\Microsoft\Windows\Defender", A = "Defender";
            // Other antivirus products registered with Windows Security Center. When one is active,
            // Microsoft Defender switches to passive mode by design, so "Defender off" is not a problem.
            var otherAv = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string n = Convert.ToString(o["displayName"]);
                        uint state = Convert.ToUInt32(o["productState"]);
                        if (((state >> 12) & 0xF) == 1 && n.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) < 0) otherAv.Add(n);
                    }
            }
            catch { }
            bool sawDefender = false;
            try
            {
                using (var s = new ManagementObjectSearcher(ns, "SELECT * FROM MSFT_MpComputerStatus"))
                    foreach (ManagementObject o in s.Get())
                    {
                        sawDefender = true;
                        bool av = (bool)o["AntivirusEnabled"], rtp = (bool)o["RealTimeProtectionEnabled"];
                        bool tamper = o["IsTamperProtected"] != null && (bool)o["IsTamperProtected"];
                        string sig = o["AntivirusSignatureLastUpdated"] as string;
                        DateTime sigTime = sig != null ? ManagementDateTimeConverter.ToDateTime(sig) : DateTime.MinValue;
                        bool fresh = (DateTime.Now - sigTime).TotalDays < 7;
                        string detail = "Antivirus: " + av + "\r\nReal-time protection: " + rtp + "\r\nTamper protection: " + tamper + "\r\nDefinitions updated: " + sigTime;
                        if (av && rtp)
                        {
                            Add(f, fresh ? Severity.OK : Severity.Warning, A, fresh ? "Antivirus and real-time protection are on" : "Defender definitions are more than a week old", detail);
                            if (!tamper) Add(f, Severity.Warning, A, "Tamper protection is off", "Turn it on in Windows Security → Virus & threat protection settings.");
                        }
                        else if (otherAv.Count > 0)
                            Add(f, Severity.OK, A, "Protected by " + string.Join(", ", otherAv), "Microsoft Defender is in passive mode because another antivirus is active. That's normal.\r\n\r\n" + detail);
                        else
                            Add(f, Severity.Alert, A, "Antivirus or real-time protection is OFF", detail + "\r\n\r\nNo other antivirus is registered with Windows Security either.");
                    }
            }
            catch { }
            if (!sawDefender)
            {
                if (otherAv.Count > 0) Add(f, Severity.OK, A, "Protected by " + string.Join(", ", otherAv), "Microsoft Defender isn't available; another antivirus is active.");
                else Add(f, Severity.Alert, A, "No active antivirus found", "Neither Microsoft Defender nor another antivirus reported as active.");
                return; // threat history and exclusions below are Defender-only
            }
            using (var s = new ManagementObjectSearcher(ns, "SELECT * FROM MSFT_MpThreatDetection"))
            {
                var threats = s.Get().Cast<ManagementObject>().ToList();
                if (threats.Count == 0) Add(f, Severity.OK, A, "No threats in detection history", "");
                else Add(f, Severity.Warning, A, threats.Count + " threat detection(s) in history",
                    string.Join("\r\n", threats.Select(t => ManagementDateTimeConverter.ToDateTime((string)t["InitialDetectionTime"]) + "  " +
                        string.Join("; ", (string[])t["Resources"] ?? new string[0])).ToArray()));
            }
            using (var s = new ManagementObjectSearcher(ns, "SELECT * FROM MSFT_MpPreference"))
                foreach (ManagementObject o in s.Get())
                {
                    var ex = new List<string>();
                    foreach (var key in new[] { "ExclusionPath", "ExclusionProcess", "ExclusionExtension" })
                    {
                        var vals = o[key] as string[];
                        if (vals != null) ex.AddRange(vals.Where(v => !v.StartsWith("N/A")).Select(v => key.Replace("Exclusion", "") + ": " + v));
                        if (vals != null && vals.Any(v => v.StartsWith("N/A")))
                        { Add(f, Severity.Info, A, "Exclusions not readable", "Run Featherweight as administrator to check Defender exclusions."); return; }
                    }
                    if (ex.Count == 0) Add(f, Severity.OK, A, "No Defender exclusions", "");
                    else Add(f, Severity.Warning, A, ex.Count + " Defender exclusion(s)", "Malware often adds exclusions for itself. Make sure you recognise these:\r\n" + string.Join("\r\n", ex));
                }
        }

        // ---------------------------------------------------------------- running programs
        static void Programs(List<Finding> f)
        {
            const string A = "Programs";
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                string path = Native.ProcessPath(p.Id);
                if (path != null && !paths.ContainsKey(path)) paths[path] = p.ProcessName;
                p.Dispose();
            }
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            // Windows files are catalog-signed and Store apps (WindowsApps) are package-signed; neither carries an embedded signature.
            string storeApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + "\\";
            var thirdParty = paths.Where(kv => !kv.Key.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)
                                            && !kv.Key.StartsWith(storeApps, StringComparison.OrdinalIgnoreCase)).ToList();
            int unsigned = 0;
            foreach (var kv in thirdParty)
            {
                bool signedOk = Native.HasValidSignature(kv.Key);
                if (!signedOk && string.Equals(kv.Key, System.Windows.Forms.Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Honest, but not a warning: it's the program doing the check, and the user chose to run it.
                    Add(f, Severity.Info, A, "Featherweight itself isn't digitally signed yet",
                        kv.Key + "\r\n\r\nFeatherweight is free and open source, and code signing is planned. Until then Windows can't confirm who made it, " +
                        "so only use copies downloaded from the project's GitHub page.");
                }
                else if (!signedOk)
                {
                    unsigned++;
                    Add(f, Severity.Warning, A, "Unsigned program running: " + Path.GetFileName(kv.Key),
                        kv.Key + "\r\n\r\nNot necessarily bad (games and mods are often unsigned), but make sure you know what it is.");
                }
                else if (UserWritable.IsMatch(kv.Key))
                    Add(f, Severity.Info, A, "Runs from a user folder: " + Path.GetFileName(kv.Key), kv.Key + "\r\nSigned by: " + Signer(kv.Key));
            }
            var others = thirdParty.Where(kv => !string.Equals(kv.Key, System.Windows.Forms.Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (unsigned == 0) Add(f, Severity.OK, A, "All " + others.Count + " other non-Windows programs are validly signed",
                string.Join("\r\n", others.Select(kv => kv.Key).OrderBy(x => x)));
        }

        static string Signer(string path)
        {
            try
            {
                var c = X509Certificate.CreateFromSignedFile(path);
                var m = Regex.Match(c.Subject, "CN=\"?([^,\"]+)");
                return m.Success ? m.Groups[1].Value : c.Subject;
            }
            catch { return "(catalog-signed)"; }
        }

        // ---------------------------------------------------------------- root certificates
        static readonly Regex KnownRoots = new Regex(@"Microsoft|Thawte|VeriSign|Symantec|DigiCert|Baltimore|GlobalSign|Sectigo|COMODO|AddTrust|USERTrust|AAA Certificate|ISRG|Entrust|Go Daddy|Starfield|IdenTrust|QuoVadis|Certum|Amazon|SSL\.com|Google Trust|GTS Root", RegexOptions.IgnoreCase);

        static void RootCerts(List<Finding> f)
        {
            const string A = "Certificates";
            int custom = 0;
            // Roots that Microsoft's own trusted-root program has delivered to this PC are public CAs, not custom ones.
            HashSet<string> authRoot;
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SystemCertificates\AuthRoot\Certificates"))
                authRoot = new HashSet<string>(k == null ? new string[0] : k.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
            foreach (var loc in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
            {
                // Only certificates physically added to this store, not Microsoft's auto-updated AuthRoot list.
                string regPath = loc == StoreLocation.LocalMachine ? @"SOFTWARE\Microsoft\SystemCertificates\ROOT\Certificates" : @"Software\Microsoft\SystemCertificates\Root\Certificates";
                var hive = loc == StoreLocation.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                HashSet<string> physical;
                using (var k = hive.OpenSubKey(regPath))
                    physical = new HashSet<string>(k == null ? new string[0] : k.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
                if (physical.Count == 0) continue;
                var st = new X509Store(StoreName.Root, loc);
                st.Open(OpenFlags.ReadOnly);
                foreach (X509Certificate2 c in st.Certificates)
                {
                    if (!physical.Contains(c.Thumbprint) || authRoot.Contains(c.Thumbprint) || KnownRoots.IsMatch(c.Subject)) continue;
                    custom++;
                    string where = loc == StoreLocation.LocalMachine ? " (whole PC)" : " (your account)";
                    var ekus = c.Extensions.OfType<X509EnhancedKeyUsageExtension>().SelectMany(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()).ToList();
                    bool codeSigning = ekus.Count == 0 || ekus.Any(o => o.Value == "1.3.6.1.5.5.7.3.3");
                    string cn = c.GetNameInfo(X509NameType.SimpleName, false);
                    string detail = "Subject: " + c.Subject + "\r\nIssuer: " + c.Issuer + "\r\nValid: " + c.NotBefore.ToShortDateString() + " – " + c.NotAfter.ToShortDateString() +
                                    "\r\nPurposes: " + (ekus.Count == 0 ? "ALL purposes" : string.Join(", ", ekus.Select(o => o.FriendlyName ?? o.Value))) +
                                    "\r\nPrivate key on this PC: " + c.HasPrivateKey + "\r\nStore: " + loc + "\\Root\r\nThumbprint: " + c.Thumbprint;
                    var fix = new PlanItem { D = new Dictionary<string, object> {
                        { "type", "cert" }, { "location", loc.ToString() }, { "store", "Root" }, { "thumbprint", c.Thumbprint }, { "label", cn },
                        { "why", "Removed from the Security page" } } };
                    if (string.Equals(cn, Environment.MachineName, StringComparison.OrdinalIgnoreCase) && !codeSigning)
                        Add(f, Severity.Info, A, "Self-signed certificate for this PC" + where, detail + "\r\n\r\nCreated by Windows or a local app for HTTPS on this machine. Usually harmless.", fix);
                    else if (codeSigning)
                        Add(f, Severity.Warning, A, "Custom root trusted for code signing: " + cn + where,
                            detail + "\r\n\r\nAnything signed with this certificate's key looks like a trusted publisher on this PC. Remove it unless you know which app needs it.", fix);
                    else
                        Add(f, Severity.Info, A, "Custom root certificate: " + cn + where, detail, fix);
                }
                st.Close();
            }
            if (custom == 0) Add(f, Severity.OK, A, "No custom root certificates", "Only Windows' own trusted roots are installed.");
        }

        // ---------------------------------------------------------------- autostart
        static void Autostart(List<Finding> f)
        {
            const string A = "Autostart";
            var entries = new List<string>();
            // (hive, key, the Task Manager switch for it: StartupApproved\Run or \Run32, or null when there's none)
            var keys = new[] {
                Tuple.Create("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run"),
                Tuple.Create("HKCU", @"Software\Microsoft\Windows\CurrentVersion\RunOnce", (string)null),
                Tuple.Create("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run"),
                Tuple.Create("HKLM", @"Software\Microsoft\Windows\CurrentVersion\RunOnce", (string)null),
                Tuple.Create("HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32"),
                Tuple.Create("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", (string)null),
                Tuple.Create("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", (string)null) };
            foreach (var k in keys)
                using (var key = k.Item1 == "HKCU" ? Engine.OpenUserKey(k.Item2) : Registry.LocalMachine.OpenSubKey(k.Item2)) // the signed-in user's, even when elevated
                {
                    if (key == null) continue;
                    foreach (var n in key.GetValueNames())
                    {
                        string cmd = Convert.ToString(key.GetValue(n));
                        string line = k.Item1 + "\\…\\" + Path.GetFileName(k.Item2) + "  " + n + " = " + cmd;
                        entries.Add(line);
                        if (!UserWritable.IsMatch(cmd)) continue;
                        PlanItem fix = null; bool alreadyOff = false;
                        if (k.Item3 != null)
                        {
                            // Switch it off the way Task Manager does (backed up, undoable), unless it already is.
                            string approved = k.Item1 + @":\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + k.Item3;
                            var st = Engine.RegState(approved, n);
                            var bytes = (bool)st["Exists"] ? st["Value"] as List<int> : null;
                            alreadyOff = bytes != null && bytes.Count > 0 && bytes[0] % 2 == 1;
                            fix = new PlanItem { D = new Dictionary<string, object> { { "type", "registry" }, { "path", approved }, { "name", n }, { "kind", "Binary" },
                                { "value", new List<object> { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } }, { "label", "Startup entry '" + n + "' → off (same as Task Manager)" } } };
                        }
                        if (alreadyOff) { Add(f, Severity.Info, A, "Startup entry from a user folder (switched off): " + n, line); continue; }
                        f.Add(new Finding { Severity = Severity.Warning, Area = A, Title = "Startup entry runs from a user folder: " + n,
                            Detail = line + "\r\n\r\nNormal software starts from Program Files. Something starting from a user, temp or downloads folder is " +
                                     "worth a look. If you don't recognise it, switch it off (backed up) and run a Defender scan.",
                            Fix = fix, FixLabel = fix != null ? "Switch it off (backed up)" : null });
                    }
                }
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
                if (Directory.Exists(dir))
                    foreach (var file in Directory.GetFiles(dir).Where(x => !x.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                        entries.Add("Startup folder: " + file);
            Add(f, Severity.OK, A, entries.Count + " startup entries (Run keys + Startup folders)", string.Join("\r\n", entries));
        }

        // ---------------------------------------------------------------- scheduled tasks
        static void Tasks(List<Finding> f)
        {
            const string A = "Tasks";
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            var lines = new List<string>();
            Walk(svc.GetFolder("\\"), lines, f);
            Add(f, Severity.OK, A, lines.Count + " non-Microsoft scheduled tasks", string.Join("\r\n", lines));
        }

        static void Walk(dynamic folder, List<string> lines, List<Finding> f)
        {
            string fp = folder.Path;
            if (fp.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) return;
            foreach (dynamic t in folder.GetTasks(1))
            {
                var exe = new List<string>();
                try { foreach (dynamic a in t.Definition.Actions) if ((int)a.Type == 0) exe.Add((string)a.Path + " " + (string)a.Arguments); } catch { }
                string cmd = string.Join(" | ", exe);
                string line = (string)t.Path + "  [" + ((bool)t.Enabled ? "enabled" : "disabled") + "]  " + cmd;
                lines.Add(line);
                if ((bool)t.Enabled && UserWritable.IsMatch(cmd))
                {
                    string dir = fp == "\\" ? "\\" : fp + "\\";
                    f.Add(new Finding { Severity = Severity.Warning, Area = "Tasks", Title = "Scheduled task runs from a user folder: " + (string)t.Name,
                        Detail = line + "\r\n\r\nNormal software runs from Program Files. If you don't recognise this task, switch it off (backed up) and run a Defender scan.",
                        Fix = new PlanItem { D = new Dictionary<string, object> { { "type", "task" }, { "path", dir }, { "name", (string)t.Name } } },
                        FixLabel = "Switch it off (backed up)" });
                }
            }
            foreach (dynamic sub in folder.GetFolders(0)) Walk(sub, lines, f);
        }

        // ---------------------------------------------------------------- hijack points
        static void HijackPoints(List<Finding> f)
        {
            const string A = "Hijack points";
            int bad = 0;
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
            {
                string shell = Convert.ToString(k.GetValue("Shell", "")), userinit = Convert.ToString(k.GetValue("Userinit", ""));
                if (!string.Equals(shell.Trim(), "explorer.exe", StringComparison.OrdinalIgnoreCase)) { bad++; Add(f, Severity.Alert, A, "Winlogon Shell is not explorer.exe", shell); }
                if (!Regex.IsMatch(userinit.Trim(), @"^C:\\Windows\\system32\\userinit\.exe,?$", RegexOptions.IgnoreCase)) { bad++; Add(f, Severity.Alert, A, "Winlogon Userinit was changed", userinit); }
            }
            foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows" })
                using (var k = Registry.LocalMachine.OpenSubKey(path))
                    if (k != null && Convert.ToInt32(k.GetValue("LoadAppInit_DLLs", 0)) == 1 && Convert.ToString(k.GetValue("AppInit_DLLs", "")).Trim() != "")
                    { bad++; Add(f, Severity.Alert, A, "AppInit_DLLs injects DLLs into every program", Convert.ToString(k.GetValue("AppInit_DLLs"))); }
            try
            {
                using (var ifeo = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"))
                    foreach (var n in ifeo.GetSubKeyNames())
                        using (var k = ifeo.OpenSubKey(n))
                        {
                            var dbg = k == null ? null : k.GetValue("Debugger") as string;
                            if (!string.IsNullOrEmpty(dbg)) { bad++; Add(f, Severity.Alert, A, "Program launch redirected: " + n, "Debugger = " + dbg); }
                        }
            }
            catch (System.Security.SecurityException) { Add(f, Severity.Info, A, "Launch redirects not readable", "Run Featherweight as administrator to check Image File Execution Options."); }
            using (var s = new ManagementObjectSearcher(@"root\subscription", "SELECT * FROM __EventConsumer"))
                foreach (ManagementObject o in s.Get())
                {
                    string cls = o.ClassPath.ClassName;
                    if (cls == "CommandLineEventConsumer" || cls == "ActiveScriptEventConsumer")
                    { bad++; Add(f, Severity.Alert, A, "WMI persistence: " + o["Name"], cls + "\r\n" + (o["CommandLineTemplate"] ?? o["ScriptText"])); }
                }
            string hosts = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
            var hostLines = File.Exists(hosts) ? File.ReadAllLines(hosts).Where(l => l.Trim() != "" && !l.TrimStart().StartsWith("#")).ToList() : new List<string>();
            if (hostLines.Count > 0) { bad++; Add(f, Severity.Warning, A, hostLines.Count + " custom hosts-file entries", string.Join("\r\n", hostLines)); }
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
            {
                bool proxy = k != null && Convert.ToInt32(k.GetValue("ProxyEnable", 0)) == 1;
                string pac = k == null ? null : k.GetValue("AutoConfigURL") as string;
                if (proxy || !string.IsNullOrEmpty(pac)) { bad++; Add(f, Severity.Warning, A, "A proxy is configured", "Proxy: " + k.GetValue("ProxyServer") + "\r\nPAC script: " + pac); }
            }
            if (bad == 0) Add(f, Severity.OK, A, "No hijacks found", "Winlogon, AppInit_DLLs, launch redirects (IFEO), WMI persistence, hosts file and proxy are all clean.");
        }

        // ---------------------------------------------------------------- network
        static void Network(List<Finding> f)
        {
            const string A = "Network";
            var listeners = Native.TcpListeners(); // (address:port, pid); asks Windows directly, so no translated netstat text
            var names = new Dictionary<int, string>();
            Func<int, string> name = pid => { string n; if (!names.TryGetValue(pid, out n)) { try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName; } catch { n = "?"; } names[pid] = n; } return n; };
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var external = listeners.Where(l => !l.Item1.StartsWith("127.") && !l.Item1.StartsWith("[::1]")).ToList();
            foreach (var g in external.GroupBy(l => l.Item2))
            {
                string path = Native.ProcessPath(g.Key);
                if (path != null && !path.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase))
                    Add(f, Severity.Info, A, "Third-party program accepts network connections: " + name(g.Key),
                        path + "\r\nPorts: " + string.Join(", ", g.Select(l => l.Item1).Distinct()));
            }
            Add(f, Severity.OK, A, listeners.Count + " listening ports (" + external.Count + " reachable from the network)",
                string.Join("\r\n", listeners.OrderBy(l => l.Item1).Select(l => l.Item1.PadRight(28) + " " + name(l.Item2) + " (" + l.Item2 + ")")));
        }
    }
}
