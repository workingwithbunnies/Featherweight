using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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

    /// <summary>What the person chose to include in a diagnostics report. App and Windows version are always in it.</summary>
    class DiagnosticsOptions
    {
        public bool Pc = true;          // PC model, processor, memory totals, antivirus
        public bool Software;           // what Optimize found + other companies' services (needed to support a new brand)
        public bool Memory;             // memory by company and program right now
        public bool Activity = true;    // Featherweight's own backups, recent activity and errors
    }

    /// <summary>"Export diagnostics": a plain-text report for bug reports, with personal details removed.</summary>
    static class Diagnostics
    {
        public static string Build() { return Build(new DiagnosticsOptions { Software = true, Memory = true }); }

        public static string Build(DiagnosticsOptions opt)
        {
            var sb = new StringBuilder();
            Action<string> h = t => sb.AppendLine().AppendLine("== " + t + " ==");
            sb.AppendLine("Featherweight diagnostics, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Please read it before sharing. Your user name, PC name, account IDs and email addresses were removed.");
            sb.AppendLine("Included: app and Windows version" + (opt.Pc ? ", PC details" : "") + (opt.Software ? ", background software" : "") +
                          (opt.Memory ? ", memory by program" : "") + (opt.Activity ? ", Featherweight activity" : "") + ".");

            h("App");
            sb.AppendLine("Version: " + MainForm.AppVersion + (Paths.Portable ? " (portable)" : ""));
            sb.AppendLine("Running as administrator: " + Admin.IsAdmin + (Admin.DifferentUser ? " (elevated with a different account than the signed-in user)" : ""));
            sb.AppendLine("Data folder: " + Paths.Root);
            sb.AppendLine("Settings: update check " + (Settings.CheckUpdates ? "on" : "off") + ", restore points " + (Settings.RestorePoints ? "on" : "off") +
                          ", Windows System Protection " + (RestorePoint.ProtectionOff ? "OFF" : "on"));

            h("Windows");
            Safe(sb, () =>
            {
                sb.AppendLine(WindowsVersion());
                sb.AppendLine("64-bit: " + Environment.Is64BitOperatingSystem + ", display language: " + System.Globalization.CultureInfo.CurrentUICulture.Name);
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                    sb.AppendLine(".NET Framework release: " + (k == null ? "?" : k.GetValue("Release")));
            });

            if (opt.Pc)
            {
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
            }

            if (opt.Software)
            {
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
            }

            if (opt.Memory)
            {
            h("Memory by company right now");
            Safe(sb, () =>
            {
                foreach (var g in ProcScan.Group(ProcScan.Processes()).Where(g => g.Count > 0).OrderByDescending(g => g.PrivMB))
                    sb.AppendLine(string.Format("- {0}: {1} process(es), {2:0} MB private — {3}", g.Group, g.Count, g.PrivMB, g.Names));
            });
            }

            if (opt.Activity)
            {
            h("Backups");
            Safe(sb, () => { foreach (var b in Engine.ListBackups()) sb.AppendLine(b.Time + "  " + b.PlanName + "  (" + b.Count + " settings)"); });

            h("Recent activity (last 150 lines)");
            Safe(sb, () => Tail(sb, Path.Combine(Paths.Logs, "activity.log"), 150));

            h("Recent crashes (last 80 lines)");
            Safe(sb, () => Tail(sb, Path.Combine(Paths.Logs, "crash.log"), 80));
            }

            return Privacy.Redact(sb.ToString());
        }

        /// <summary>"Windows 11 Pro 24H2, build 26100.1234, edition Professional".</summary>
        public static string WindowsVersion()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int build; int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out build);
                string name = Convert.ToString(k.GetValue("ProductName"));
                if (build >= 22000) name = name.Replace("Windows 10", "Windows 11"); // the registry name still says 10 on Windows 11
                return name + " " + k.GetValue("DisplayVersion") + ", build " + build + "." + k.GetValue("UBR") + ", edition " + k.GetValue("EditionID");
            }
        }

        /// <summary>
        /// Just one Optimize item, for a "describe this app" request: its identifiers, facts and what Featherweight would
        /// change. Nothing else from the PC. Personal details are removed.
        /// </summary>
        public static string ItemReport(Optimization o)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Featherweight " + MainForm.AppVersion + " · " + SafeText(WindowsVersion));
            sb.AppendLine("App: " + o.Title + (o.Kind != null ? " (" + o.Kind.ToLowerInvariant() + ")" : "") + ", company: " + o.Category);
            if (o.Ids.Count > 0) sb.AppendLine("Identifiers: " + string.Join(", ", o.Ids.Select(kv => kv.Key + "=" + kv.Value)));
            else sb.AppendLine("Catalog item: " + o.Id);
            sb.AppendLine("Reviewed entry: " + (o.Knowledge != null ? o.Knowledge.Id + " (knowledge base v" + Knowledge.Version + ")" : o.Discovered ? "none yet" : "built-in catalog"));
            sb.AppendLine("Status: " + o.State + (o.ViewOnly ? " (information only)" : "") + ", running " + o.RunningCount + " process(es), " + o.LiveMB.ToString("0") + " MB");
            if (o.Facts.Count > 0) { sb.AppendLine("On this PC:"); foreach (var f in o.Facts) sb.AppendLine("- " + f); }
            if (o.Changes.Count > 0) { sb.AppendLine("What Featherweight would change:"); foreach (var c in o.Changes) sb.AppendLine(c); }
            return Privacy.Redact(sb.ToString());
        }

        static string SafeText(Func<string> f) { try { return f(); } catch { return "Windows version unknown"; } }

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
            DiagnosticsOptions opt;
            using (var ask = new DiagnosticsDialog()) { if (ask.ShowDialog(owner) != DialogResult.OK) return; opt = ask.Options; }
            string text = await Task.Run(() => Build(opt));
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

    /// <summary>Asks what to include in a diagnostics report, with an honest note about where it might end up.</summary>
    class DiagnosticsDialog : Form
    {
        readonly CheckBox pc = Box("PC details", "Maker and model, processor, memory totals, antivirus name.", true);
        readonly CheckBox software = Box("Background software on this PC",
            "What the Optimize page found and other companies' services, with program locations. Needed when asking for your PC's apps to be supported.", false);
        readonly CheckBox memory = Box("Memory by program", "How much memory each company's programs use right now.", false);
        readonly CheckBox activity = Box("Featherweight's activity", "Its backups, recent activity log and error details. Usually all a bug report needs.", true);
        readonly List<Label> notes = new List<Label>();

        public DiagnosticsOptions Options { get { return new DiagnosticsOptions { Pc = pc.Checked, Software = software.Checked, Memory = memory.Checked, Activity = activity.Checked }; } }

        public DiagnosticsDialog()
        {
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Export diagnostics"; Font = Theme.Base; BackColor = Theme.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(24, 20, 24, 20);
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Card };
            stack.Controls.Add(Theme.Label("What should the report include?", Theme.H2));
            stack.Controls.Add(Note("The app and Windows version are always included. Tick only what your report needs.", Theme.Muted));
            var always = Theme.Label("✓  App and Windows version", Theme.Bold, Theme.Muted); always.Margin = new Padding(0, 10, 0, 2); stack.Controls.Add(always);
            foreach (var c in new[] { pc, software, memory, activity }) { stack.Controls.Add(c); stack.Controls.Add(Note((string)c.Tag, Theme.Muted, 20)); }
            var warn = Note("Anything you attach to a GitHub issue is public: anyone can read it. Featherweight removes your user name, PC name, " +
                            "account IDs and email addresses, and the file opens so you can check it before you share it.", Theme.Warn);
            warn.Margin = new Padding(0, 12, 0, 4); stack.Controls.Add(warn);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 0) };
            var ok = Theme.Button("Create report", true); ok.DialogResult = DialogResult.OK;
            var cancel = Theme.Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
            row.Controls.Add(ok); row.Controls.Add(cancel);
            stack.Controls.Add(row);
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(stack);
        }

        static CheckBox Box(string text, string note, bool on)
        {
            return new CheckBox { Text = text, Checked = on, AutoSize = true, Font = Theme.Bold, Margin = new Padding(0, 10, 0, 0), Tag = note, BackColor = Theme.Card };
        }

        static Label Note(string text, Color color, int indent = 0)
        {
            var l = Theme.Label(text, Theme.Small, color); l.MaximumSize = new Size(520, 0); l.Margin = new Padding(indent, 0, 0, 2); l.BackColor = Theme.Card; return l;
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
