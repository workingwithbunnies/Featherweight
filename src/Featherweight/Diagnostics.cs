using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Featherweight
{
    /// <summary>Removes personal details (user name, PC name, profile path, SID, emails) from text meant for bug reports.</summary>
    static class Privacy
    {
        public static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
                new KeyValuePair<string, string>(WindowsIdentity.GetCurrent().User.Value, "<user-sid>"),
                new KeyValuePair<string, string>(Admin.DesktopUserSid ?? "", "<user-sid>"),
                new KeyValuePair<string, string>(Environment.UserName, "<user>"),
                new KeyValuePair<string, string>(Environment.MachineName, "<pc>"),
                new KeyValuePair<string, string>(Environment.UserDomainName, "<domain>"),
            };
            foreach (var p in pairs)
                if (!string.IsNullOrEmpty(p.Key) && p.Key.Length >= 3)
                    s = Regex.Replace(s, Regex.Escape(p.Key), p.Value, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[\w.+-]+@[\w-]+(\.[\w-]+)+", "<email>");
            return s;
        }
    }

    /// <summary>"Export diagnostics": a plain-text report for bug reports, with personal details removed.</summary>
    static class Diagnostics
    {
        public static string Build()
        {
            var sb = new StringBuilder();
            Action<string> h = t => sb.AppendLine().AppendLine("== " + t + " ==");
            sb.AppendLine("Featherweight diagnostics, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Please read it before sharing. Your user name and PC name were replaced with <user> and <pc>.");

            h("App");
            sb.AppendLine("Version: " + MainForm.AppVersion + (Paths.Portable ? " (portable)" : ""));
            sb.AppendLine("Running as administrator: " + Admin.IsAdmin + (Admin.DifferentUser ? " (elevated with a different account than the signed-in user)" : ""));
            sb.AppendLine("Data folder: " + Paths.Root);
            sb.AppendLine("Settings: update check " + (Settings.CheckUpdates ? "on" : "off") + ", restore points " + (Settings.RestorePoints ? "on" : "off") +
                          ", Windows System Protection " + (RestorePoint.ProtectionOff ? "OFF" : "on"));

            h("Windows");
            Safe(sb, () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build; int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out build);
                    string name = Convert.ToString(k.GetValue("ProductName"));
                    if (build >= 22000) name = name.Replace("Windows 10", "Windows 11"); // the registry name still says 10 on Windows 11
                    sb.AppendLine(name + " " + k.GetValue("DisplayVersion") + ", build " + build + "." + k.GetValue("UBR") + ", edition " + k.GetValue("EditionID"));
                }
                sb.AppendLine("64-bit: " + Environment.Is64BitOperatingSystem + ", display language: " + System.Globalization.CultureInfo.CurrentUICulture.Name);
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                    sb.AppendLine(".NET Framework release: " + (k == null ? "?" : k.GetValue("Release")));
            });

            h("PC");
            Safe(sb, () =>
            {
                foreach (ManagementObject o in new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem").Get())
                    sb.AppendLine("Model: " + o["Manufacturer"] + " " + o["Model"]);
                foreach (ManagementObject o in new ManagementObjectSearcher("SELECT Name FROM Win32_Processor").Get())
                    sb.AppendLine("CPU: " + Convert.ToString(o["Name"]).Trim());
                var m = SysMem.Read();
                sb.AppendLine(string.Format("Memory: {0:0} MB total, {1:0} MB in use ({2:0}%), commit {3:0}/{4:0} MB, kernel non-paged {5:0} MB, uptime {6:0} min",
                    m.TotalMB, m.UsedMB, m.UsedPct, m.CommitMB, m.CommitLimitMB, m.NonpagedMB, SysMem.UptimeMin));
            });

            h("Antivirus registered with Windows Security");
            Safe(sb, () =>
            {
                foreach (ManagementObject o in new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct").Get())
                    sb.AppendLine(o["displayName"] + " (state 0x" + Convert.ToUInt32(o["productState"]).ToString("X") + ")");
            });

            h("Optimizations");
            Safe(sb, () =>
            {
                var opts = Catalog.ScanAll(w => sb.AppendLine("note: " + w));
                sb.AppendLine("knowledge base: version " + Knowledge.Version + " (" + Knowledge.Origin + "), " + Knowledge.Entries.Count + " entries");
                foreach (var p in new[] { "hypervVms", "firmwareViaWindowsUpdate", "phonePaired" })
                {
                    var r = Probes.Get(p);
                    sb.AppendLine("check " + p + ": " + (r == null ? "?" : r.Value + " (" + r.Text + ")"));
                }
                foreach (var o in opts.Where(x => !x.Discovered))
                {
                    if (!o.Present) { sb.AppendLine("- " + o.Id + ": not on this PC"); continue; }
                    sb.AppendLine("- " + o.Id + ": " + o.State + ", suggested " + o.Verdict + (o.IsAdvanced ? " (advanced)" : "") + ", running " + o.RunningCount + " process(es) " + o.LiveMB.ToString("0") + " MB");
                    foreach (var d in o.Details) sb.AppendLine("    " + d);
                }
                h("Found on this PC, not reviewed (what the Optimize page lists under each company)");
                foreach (var o in opts.Where(x => x.Discovered).OrderBy(x => x.Category).ThenBy(x => x.Title))
                {
                    sb.AppendLine("- [" + o.Category + "] " + o.Kind + ": " + o.Title + " — " + o.State + ", running " + o.RunningCount + " process(es) " + o.LiveMB.ToString("0") + " MB");
                    foreach (var p in o.ExePaths) sb.AppendLine("    program: " + p);
                    sb.AppendLine("    ids: " + string.Join(", ", o.Ids.Select(kv => kv.Key + "=" + kv.Value)) + (o.Knowledge != null ? "  → reviewed entry " + o.Knowledge.Id : "  → no reviewed entry"));
                    foreach (var f in o.Facts) sb.AppendLine("    fact: " + f);
                    foreach (var d in o.Details) sb.AppendLine("    " + d);
                }
            });

            // Raw lists that help add reviewed catalog entries for new brands (no personal data: program paths are redacted below).
            h("Services from other companies (every start type)");
            Safe(sb, () =>
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
                using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    foreach (var n in root.GetSubKeyNames().OrderBy(x => x))
                        using (var k = root.OpenSubKey(n))
                        {
                            if (k == null || (Convert.ToInt32(k.GetValue("Type", 0)) & 0x30) == 0) continue;
                            string exe = Publisher.ExeFromCommand(k.GetValue("ImagePath") as string);
                            if (exe == null || exe.StartsWith(win, StringComparison.OrdinalIgnoreCase)) continue;
                            string brand = Publisher.Of(exe);
                            if (Publisher.IsMicrosoft(brand)) continue;
                            int start = Convert.ToInt32(k.GetValue("Start", -1));
                            string startText = start == 2 ? (Convert.ToInt32(k.GetValue("DelayedAutostart", 0)) == 1 ? "auto (delayed)" : "auto") : start == 3 ? "manual" : start == 4 ? "disabled" : start.ToString();
                            sb.AppendLine("- [" + brand + "] " + n + " — " + startText + (Engine.ServiceRunning(n) ? ", running" : ", stopped") + " — " + exe);
                        }
            });

            h("Memory by company right now");
            Safe(sb, () =>
            {
                foreach (var g in ProcScan.Group(ProcScan.Processes()).Where(g => g.Count > 0).OrderByDescending(g => g.PrivMB))
                    sb.AppendLine(string.Format("- {0}: {1} process(es), {2:0} MB private — {3}", g.Group, g.Count, g.PrivMB, g.Names));
            });

            h("Backups");
            Safe(sb, () => { foreach (var b in Engine.ListBackups()) sb.AppendLine(b.Time + "  " + b.PlanName + "  (" + b.Count + " settings)"); });

            h("Recent activity (last 150 lines)");
            Safe(sb, () => Tail(sb, Path.Combine(Paths.Logs, "activity.log"), 150));

            h("Recent crashes (last 80 lines)");
            Safe(sb, () => Tail(sb, Path.Combine(Paths.Logs, "crash.log"), 80));

            return Privacy.Redact(sb.ToString());
        }

        static void Tail(StringBuilder sb, string file, int n)
        {
            if (!File.Exists(file)) { sb.AppendLine("(none)"); return; }
            foreach (var l in File.ReadAllLines(file).Reverse().Take(n).Reverse()) sb.AppendLine(l);
        }

        static void Safe(StringBuilder sb, Action a)
        {
            try { a(); } catch (Exception ex) { sb.AppendLine("(couldn't collect: " + ex.Message + ")"); }
        }

        /// <summary>Builds the report, asks where to save it, then opens it so the user can read it before sharing.</summary>
        public static async Task Export(IWin32Window owner)
        {
            string text = await Task.Run(() => Build());
            using (var dlg = new SaveFileDialog
            {
                Title = "Save diagnostics report", Filter = "Text file (*.txt)|*.txt",
                FileName = "featherweight-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, text, new UTF8Encoding(false));
                try { Process.Start("notepad.exe", "\"" + dlg.FileName + "\""); } catch { }
            }
        }
    }

    /// <summary>Friendly handling for unexpected errors: log, copy details, offer to report.</summary>
    static class Crash
    {
        public static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception, true);
            TaskScheduler.UnobservedTaskException += (s, e) => { Write(e.Exception, "background task"); e.SetObserved(); };
        }

        static string Write(Exception ex, string kind)
        {
            string details = "Featherweight " + MainForm.AppVersion + ", " + Environment.OSVersion + ", " + DateTime.Now.ToString("s") + " (" + kind + ")\r\n" + ex;
            details = Privacy.Redact(details);
            try { Directory.CreateDirectory(Paths.Logs); File.AppendAllText(Path.Combine(Paths.Logs, "crash.log"), details + "\r\n\r\n"); } catch { }
            return details;
        }

        public static void Report(Exception ex, bool fatal)
        {
            if (ex == null) return;
            string details = Write(ex, fatal ? "fatal" : "error");
            try { Clipboard.SetText(details); } catch { }
            var answer = MessageBox.Show(
                (fatal ? "Featherweight ran into a problem and has to close." : "Featherweight ran into a problem and couldn't finish that action.") +
                "\n\nAny settings that were already changed are saved on the Backups / Undo page, so they can be undone.\n\n" +
                "The error details were saved and copied to your clipboard (personal names removed). " +
                "Would you like to report it on GitHub? Just paste the details into the form.",
                "Something went wrong", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
            if (answer == DialogResult.Yes) { try { Process.Start(Updates.ProjectPage + "/issues/new?template=bug_report.yml"); } catch { } }
        }
    }
}
