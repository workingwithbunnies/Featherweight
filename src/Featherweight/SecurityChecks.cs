using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Featherweight
{
    // More of the security check: Windows' own protections, updates, sign-in and sharing, services, browser extensions
    // and Defender scans. Everything here only reads. Windows security settings are never changed by Featherweight:
    // a finding offers "Open in Windows" to the exact Settings / Windows Security page instead.
    // Values are read from the registry, WMI and COM (fixed names and numbers), never from translated text.
    static partial class SecurityScan
    {
        public const string QuickScanTarget = "featherweight:defender-quick-scan";

        static object Reg(RegistryKey hive, string path, string name)
        {
            using (var k = hive.OpenSubKey(path)) return k == null ? null : k.GetValue(name);
        }
        static int? RegInt(RegistryKey hive, string path, string name)
        {
            var v = Reg(hive, path, name);
            try { return v == null ? (int?)null : Convert.ToInt32(v); } catch { return null; }
        }

        static void AddOpen(List<Finding> f, Severity s, string area, string title, string detail, string openLabel, string target)
        { f.Add(new Finding { Severity = s, Area = area, Title = title, Detail = detail, OpenLabel = openLabel, OpenTarget = target }); }

        // ---------------------------------------------------------------- Windows' own protections
        static void WindowsProtection(List<Finding> f)
        {
            const string A = "Protection";

            // Firewall, per network type. Another firewall product registered with Windows Security also counts.
            var otherFw = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM FirewallProduct"))
                    foreach (ManagementObject o in s.Get())
                        if (((Convert.ToUInt32(o["productState"]) >> 12) & 0xF) == 1) otherFw.Add(Convert.ToString(o["displayName"]));
            }
            catch { }
            const string fwBase = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
            const string fwPolicy = @"SOFTWARE\Policies\Microsoft\WindowsFirewall\";
            var profiles = new[] { Tuple.Create("DomainProfile", "work network (domain)"), Tuple.Create("StandardProfile", "private network"), Tuple.Create("PublicProfile", "public network") };
            var off = new List<string>();
            foreach (var p in profiles)
            {
                int? pol = RegInt(Registry.LocalMachine, fwPolicy + p.Item1, "EnableFirewall");
                int? on = pol ?? RegInt(Registry.LocalMachine, fwBase + p.Item1, "EnableFirewall");
                if (on == 0) off.Add(p.Item2);
            }
            if (off.Count == 0) Add(f, Severity.OK, A, "Windows Firewall is on for every network type", "");
            else if (otherFw.Count > 0) Add(f, Severity.OK, A, "Firewall: " + string.Join(", ", otherFw), "Windows Firewall is off for: " + string.Join(", ", off) + ". That's normal while another firewall is active.");
            else AddOpen(f, off.Count == 3 ? Severity.Alert : Severity.Warning, A, "Windows Firewall is off for: " + string.Join(", ", off),
                "With the firewall off, programs on this PC can be reached from the network without being asked first. " +
                "Public networks (cafés, hotels, airports) matter most.", "Open firewall settings", "windowsdefender://network");

            // SmartScreen for apps and files
            string ss = Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled") as string;
            int? ssPolicy = RegInt(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen");
            if (ssPolicy == 0 || string.Equals(ss, "Off", StringComparison.OrdinalIgnoreCase))
                AddOpen(f, Severity.Warning, A, "SmartScreen is off for apps and files",
                    "SmartScreen warns before you run apps and files that are unknown or reported as harmful.", "Open app & browser control", "windowsdefender://appbrowser");
            else Add(f, Severity.OK, A, "SmartScreen is on for apps and files", "");

            // User Account Control
            const string sys = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            int? lua = RegInt(Registry.LocalMachine, sys, "EnableLUA"), consent = RegInt(Registry.LocalMachine, sys, "ConsentPromptBehaviorAdmin");
            if (lua == 0) AddOpen(f, Severity.Alert, A, "User Account Control (UAC) is turned off",
                "Every program runs with full administrator rights without asking, so anything harmful can change the whole PC.", "Open UAC settings", "UserAccountControlSettings.exe");
            else if (consent == 0) AddOpen(f, Severity.Warning, A, "UAC never asks before giving administrator rights",
                "Programs can get administrator rights silently. The default is to ask.", "Open UAC settings", "UserAccountControlSettings.exe");
            else Add(f, Severity.OK, A, "User Account Control (UAC) is on", "");

            // Secure Boot
            int? sb = RegInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
            if (sb == 1) Add(f, Severity.OK, A, "Secure Boot is on", "");
            else if (sb == 0) Add(f, Severity.Info, A, "Secure Boot is off",
                "Secure Boot stops harmful software from loading before Windows starts. It's switched on in the PC's firmware (BIOS/UEFI) settings, " +
                "not in Windows. Some setups (for example Linux dual-boot) turn it off on purpose.");
            else Add(f, Severity.Info, A, "Secure Boot isn't available", "This PC starts in legacy (BIOS) mode or doesn't report Secure Boot.");

            // Memory integrity (Core isolation)
            int? hvci = RegInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            if (hvci == 1) Add(f, Severity.OK, A, "Memory integrity (Core isolation) is on", "");
            else AddOpen(f, Severity.Info, A, "Memory integrity (Core isolation) is off",
                "Memory integrity makes it harder for harmful drivers to take over Windows. Some older drivers and a few games' anti-cheat " +
                "don't work with it, which is why it's sometimes off.", "Open Core isolation", "windowsdefender://coreisolation");

            // TPM and drive encryption need administrator rights to read
            if (!Admin.IsAdmin) { Add(f, Severity.Info, A, "TPM and drive encryption not checked", "Run Featherweight as administrator to check them."); return; }
            try
            {
                bool tpm = false;
                using (var s = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm"))
                    foreach (ManagementObject o in s.Get()) tpm = o["IsEnabled_InitialValue"] is bool && (bool)o["IsEnabled_InitialValue"];
                if (tpm) Add(f, Severity.OK, A, "TPM security chip is on", "");
                else Add(f, Severity.Info, A, "No TPM security chip found", "The TPM keeps encryption keys and Windows Hello safe. It's switched on in the PC's firmware settings.");
            }
            catch (Exception ex) { Add(f, Severity.Info, A, "TPM couldn't be checked", ex.Message); }
            try
            {
                string drive = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
                uint? prot = null;
                using (var s = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption", "SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='" + drive + "'"))
                    foreach (ManagementObject o in s.Get()) prot = Convert.ToUInt32(o["ProtectionStatus"]);
                if (prot == 1) Add(f, Severity.OK, A, "Drive " + drive + " is encrypted", "");
                else AddOpen(f, Severity.Info, A, "Drive " + drive + " isn't encrypted",
                    "If this PC is lost or stolen, its files can be read by putting the drive in another computer. Device encryption or BitLocker " +
                    "prevents that (BitLocker needs Windows Pro). Keep the recovery key somewhere safe before turning it on.", "Open device encryption", "ms-settings:deviceencryption");
            }
            catch (Exception ex) { Add(f, Severity.Info, A, "Drive encryption couldn't be checked", ex.Message); }
        }

        // ---------------------------------------------------------------- Windows Update
        static void WindowsUpdate(List<Finding> f)
        {
            const string A = "Updates";
            if (Engine.ServiceStart("wuauserv") == "disabled")
                AddOpen(f, Severity.Alert, A, "Windows Update is switched off",
                    "The Windows Update service is disabled, so security fixes aren't installed.", "Open Windows Update", "ms-settings:windowsupdate");
            DateTime last = DateTime.MinValue;
            try
            {
                dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
                dynamic searcher = session.CreateUpdateSearcher();
                int count = searcher.GetTotalHistoryCount();
                if (count > 0)
                    foreach (dynamic e in searcher.QueryHistory(0, Math.Min(count, 100)))
                    {
                        int op = e.Operation, rc = e.ResultCode; DateTime d = e.Date;
                        if (op == 1 && (rc == 2 || rc == 3) && d > last) last = d; // install, succeeded (with or without errors)
                    }
            }
            catch { }
            if (last == DateTime.MinValue) Add(f, Severity.Info, A, "Last Windows update couldn't be read", "");
            else
            {
                int days = (int)(DateTime.Now - last.ToLocalTime()).TotalDays;
                if (days > 45) AddOpen(f, Severity.Warning, A, "No Windows update installed for " + days + " days",
                    "The last update was installed on " + last.ToLocalTime().ToString("d MMM yyyy") + ". Windows normally installs security fixes every month.",
                    "Open Windows Update", "ms-settings:windowsupdate");
                else Add(f, Severity.OK, A, "Windows updates are recent", "Last installed on " + last.ToLocalTime().ToString("d MMM yyyy") + ".");
            }
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                if (k != null) AddOpen(f, Severity.Info, A, "A restart is needed to finish installing updates", "Until you restart, the updates aren't fully in place.", "Open Windows Update", "ms-settings:windowsupdate");
        }

        // ---------------------------------------------------------------- sign-in and sharing
        static void SignInAndSharing(List<Finding> f)
        {
            const string A = "Sign-in";
            // Automatic sign-in with the password stored in plain text in the registry
            const string wl = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            string auto = Convert.ToString(Reg(Registry.LocalMachine, wl, "AutoAdminLogon"));
            bool plain = !string.IsNullOrEmpty(Reg(Registry.LocalMachine, wl, "DefaultPassword") as string);
            if (auto == "1" && plain)
                AddOpen(f, Severity.Alert, A, "Automatic sign-in stores your password in plain text",
                    "Windows signs in by itself using a password saved unencrypted in the registry, where any program can read it. " +
                    "Turn automatic sign-in off (or set it up again with Microsoft's Autologon tool, which encrypts it).", "Open user accounts", "netplwiz.exe");
            else if (auto == "1")
                Add(f, Severity.Info, A, "Windows signs in automatically", "Anyone who switches the PC on gets into your account without a password.");

            // Remote Desktop
            int? deny = RegInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections");
            if (deny == 0)
            {
                int? nla = RegInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication");
                AddOpen(f, nla == 0 ? Severity.Warning : Severity.Info, "Sharing", "Remote Desktop is on" + (nla == 0 ? " without network sign-in check" : ""),
                    "Other computers can connect to this PC's desktop. Turn it off if you don't use it." +
                    (nla == 0 ? " Network Level Authentication is off, so the sign-in screen is exposed before anyone proves who they are." : ""),
                    "Open Remote Desktop settings", "ms-settings:remotedesktop");
            }

            // SMB version 1 (old, unsafe file sharing)
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT InstallState FROM Win32_OptionalFeature WHERE Name='SMB1Protocol'"))
                    foreach (ManagementObject o in s.Get())
                        if (Convert.ToUInt32(o["InstallState"]) == 1)
                            AddOpen(f, Severity.Warning, "Sharing", "Old SMB1 file sharing is switched on",
                                "SMB1 is the outdated file-sharing protocol that the WannaCry ransomware spread through. Very old network drives and printers " +
                                "may need it; otherwise turn it off under “SMB 1.0/CIFS File Sharing Support”.", "Open Windows features", "optionalfeatures.exe");
            }
            catch { }
        }

        // ---------------------------------------------------------------- services running from user folders
        static void ServicesFromUserFolders(List<Finding> f)
        {
            using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                foreach (var n in root.GetSubKeyNames())
                    using (var k = root.OpenSubKey(n))
                    {
                        if (k == null || (Convert.ToInt32(k.GetValue("Type", 0)) & 0x30) == 0) continue;
                        string cmd = k.GetValue("ImagePath") as string;
                        if (string.IsNullOrEmpty(cmd) || !UserWritable.IsMatch(cmd)) continue;
                        Add(f, Severity.Warning, "Services", "Service runs from a user folder: " + n,
                            cmd + "\r\n\r\nServices run with high rights. Normal software installs them under Program Files; one in a user, temp or " +
                            "downloads folder is unusual. If you don't recognise it, run a Defender scan.");
                    }
        }

        // ---------------------------------------------------------------- browser extensions
        static void BrowserExtensions(List<Finding> f)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var chromium = new[]
            {
                Tuple.Create("Chrome", Path.Combine(local, @"Google\Chrome\User Data")),
                Tuple.Create("Edge", Path.Combine(local, @"Microsoft\Edge\User Data")),
                Tuple.Create("Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data")),
                Tuple.Create("Vivaldi", Path.Combine(local, @"Vivaldi\User Data")),
            };
            foreach (var b in chromium)
            {
                if (!Directory.Exists(b.Item2)) continue;
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var profile in Directory.GetDirectories(b.Item2).Where(d => { string n = Path.GetFileName(d); return n == "Default" || n.StartsWith("Profile "); }))
                {
                    string ext = Path.Combine(profile, "Extensions");
                    if (!Directory.Exists(ext)) continue;
                    foreach (var id in Directory.GetDirectories(ext))
                    {
                        var ver = Directory.GetDirectories(id).OrderByDescending(d => d).FirstOrDefault();
                        string name = ver == null ? null : ChromiumName(ver);
                        if (name != null) names.Add(name);
                    }
                }
                ExtensionsFinding(f, b.Item1, names.ToList());
            }
            string ff = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ff))
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var profile in Directory.GetDirectories(ff))
                {
                    string json = Path.Combine(profile, "extensions.json");
                    if (!File.Exists(json)) continue;
                    try
                    {
                        var d = Json.Parse(File.ReadAllText(json)) as Dictionary<string, object>;
                        foreach (var a in d.List("addons"))
                        {
                            string loc = a.Str("location") ?? "";
                            if (a.Str("type") != "extension" || loc.StartsWith("app-")) continue; // built into Firefox
                            var dl = a.Obj("defaultLocale");
                            string n = dl != null ? dl.Str("name") : a.Str("id");
                            if (n != null) names.Add(n + (a.Bool("active") ? "" : " (disabled)"));
                        }
                    }
                    catch { }
                }
                ExtensionsFinding(f, "Firefox", names.ToList());
            }
        }

        static void ExtensionsFinding(List<Finding> f, string browser, List<string> names)
        {
            if (names.Count == 0) { Add(f, Severity.OK, "Browsers", browser + ": no extensions installed", ""); return; }
            Add(f, Severity.Info, "Browsers", browser + ": " + names.Count + " extension(s)",
                string.Join("\r\n", names) + "\r\n\r\nExtensions can read the pages you visit. Featherweight can't judge each one: remove any you don't " +
                "recognise or no longer use, from the browser's Extensions page.");
        }

        /// <summary>An extension's name from its manifest, resolving "__MSG_name__" from its default language.</summary>
        static string ChromiumName(string dir)
        {
            try
            {
                var m = Json.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))) as Dictionary<string, object>;
                string name = m.Str("name");
                if (name != null && name.StartsWith("__MSG_") && name.EndsWith("__"))
                {
                    string key = name.Substring(6, name.Length - 8), loc = m.Str("default_locale") ?? "en";
                    string file = Path.Combine(dir, "_locales", loc, "messages.json");
                    if (!File.Exists(file)) return null;
                    var msgs = Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
                    var entry = msgs.Where(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value as Dictionary<string, object>).FirstOrDefault();
                    name = entry != null ? entry.Str("message") : null;
                }
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- Defender scans
        static void DefenderScans(List<Finding> f)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, QuickScanAge, FullScanAge FROM MSFT_MpComputerStatus"))
                    foreach (ManagementObject o in s.Get())
                    {
                        if (!(o["AntivirusEnabled"] is bool) || !(bool)o["AntivirusEnabled"]) return; // another antivirus is in charge
                        uint q = Convert.ToUInt32(o["QuickScanAge"]), full = Convert.ToUInt32(o["FullScanAge"]);
                        string when = q > 10000 ? "never" : q == 0 ? "today" : q + " day(s) ago";
                        f.Add(new Finding
                        {
                            Severity = q > 14 && q <= 10000 ? Severity.Info : Severity.OK, Area = "Defender",
                            Title = "Last Defender scan: " + when,
                            Detail = "Quick scan: " + when + ". Full scan: " + (full > 10000 ? "never" : full + " day(s) ago") + ".\r\n\r\n" +
                                     "Defender checks files as they're used all the time; a scan also looks through files that haven't been opened.",
                            OpenLabel = "Run a quick scan now", OpenTarget = QuickScanTarget
                        });
                    }
            }
            catch { }
        }

        /// <summary>Runs a Microsoft Defender quick scan with Defender's own command-line tool. Returns a plain sentence.</summary>
        public static string RunQuickScan()
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            if (!File.Exists(exe)) return "Microsoft Defender's scanner wasn't found. Open Windows Security to scan instead.";
            string output;
            int code = Engine.Run(exe, "-Scan -ScanType 1", out output);
            if (code == 0) return "Quick scan finished: no threats found.";
            if (code == 2) return "Quick scan finished and found something. Open Windows Security → Virus & threat protection to see what Defender did about it.";
            return "The scan didn't finish (code " + code + "). You can run it from Windows Security → Virus & threat protection instead.";
        }
    }
}
