using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

namespace Featherweight
{
    // User data lives in %LocalAppData%\Featherweight, so the app works from Program Files.
    // Portable mode: a file named "portable" (or "portable.txt") next to the exe keeps everything beside it.
    static class Paths
    {
        public static readonly string AppDir = Path.GetDirectoryName(typeof(Paths).Assembly.Location);
        public static readonly bool Portable = File.Exists(Path.Combine(AppDir, "portable")) || File.Exists(Path.Combine(AppDir, "portable.txt"));
        public static readonly string Root = Portable ? AppDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Featherweight");
        public static string Backups { get { return Path.Combine(Root, "backups"); } }
        public static string Snapshots { get { return Path.Combine(Root, "snapshots"); } }
        public static string Logs { get { return Path.Combine(Root, "logs"); } }
        public static string CustomCatalog { get { return Path.Combine(Root, "custom-optimizations.json"); } }
        /// <summary>Data folder as shown on screen: no user name in it, since people share screenshots.</summary>
        public static string Display { get { return Portable ? "the folder next to Featherweight.exe" : @"%LocalAppData%\Featherweight"; } }
        public static void Ensure() { foreach (var d in new[] { Backups, Snapshots, Logs }) Directory.CreateDirectory(d); }
        public static void Open(string dir) { Directory.CreateDirectory(dir); Process.Start("explorer.exe", "\"" + dir + "\""); }
    }

    static class Admin
    {
        public static readonly bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        /// <summary>
        /// SID of the person signed in to this desktop (owner of explorer.exe in our session). When a standard user
        /// elevates with a different admin account, HKCU would point at the admin's hive; per-user settings must
        /// go to this SID instead.
        /// </summary>
        public static readonly string DesktopUserSid = FindDesktopUser();
        public static bool DifferentUser { get { return DesktopUserSid != null && DesktopUserSid != WindowsIdentity.GetCurrent().User.Value; } }

        static string FindDesktopUser()
        {
            try
            {
                int session = Process.GetCurrentProcess().SessionId;
                foreach (var p in Process.GetProcessesByName("explorer"))
                    using (p) if (p.SessionId == session) { var sid = Native.ProcessOwnerSid(p.Id); if (sid != null) return sid; }
            }
            catch { }
            return null;
        }

        /// <summary>Restart elevated, reopening the given page. Returns false if the user declined UAC.</summary>
        public static bool RestartElevated(string page)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--page=" + page) { UseShellExecute = true, Verb = "runas" });
                Application.Exit();
                return true;
            }
            catch { return false; }
        }

        /// <summary>For actions that need admin: returns true if we already are, otherwise offers to restart elevated.</summary>
        public static bool Require(IWin32Window owner, string page, string what)
        {
            if (IsAdmin) return true;
            var r = MessageBox.Show(owner, what + " needs administrator rights.\n\nRestart Featherweight as administrator now?",
                "Administrator needed", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) RestartElevated(page);
            return false;
        }
    }

    // Snapshots use the same JSON shape as Featherweight.ps1 so both tools can compare each other's files.
    static class Snapshots
    {
        public static string Take(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) label = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach (var c in Path.GetInvalidFileNameChars()) label = label.Replace(c, '_');
            var s = SysMem.Read();
            var procs = ProcScan.Processes();
            var groups = ProcScan.Group(procs).Select(g => (object)new Dictionary<string, object> {
                { "Group", g.Group }, { "Count", g.Count }, { "PrivMB", Math.Round(g.PrivMB) }, { "WSMB", Math.Round(g.WSMB) }, { "Names", g.Names } }).ToList();
            var top = procs.OrderByDescending(p => p.PrivMB).Take(40).Select(p => (object)new Dictionary<string, object> {
                { "Name", p.Name }, { "Id", p.Id }, { "PrivMB", Math.Round(p.PrivMB) }, { "WSMB", Math.Round(p.WSMB) } }).ToList();
            var doc = new Dictionary<string, object>
            {
                { "Label", label }, { "Time", DateTime.Now.ToString("s") }, { "UptimeMin", Math.Round(SysMem.UptimeMin) },
                { "System", new Dictionary<string, object> {
                    { "TotalMB", Math.Round(s.TotalMB) }, { "AvailableMB", Math.Round(s.AvailMB) }, { "UsedMB", Math.Round(s.UsedMB) },
                    { "CommitMB", Math.Round(s.CommitMB) }, { "CommitLimitMB", Math.Round(s.CommitLimitMB) },
                    { "NonpagedMB", Math.Round(s.NonpagedMB) }, { "PagedMB", Math.Round(s.PagedMB) }, { "Processes", s.Processes } } },
                { "Groups", groups }, { "Top", top }
            };
            string path = Path.Combine(Paths.Snapshots, label + ".json");
            Json.WriteFile(path, doc);
            return path;
        }

        public static List<string> List()
        {
            if (!Directory.Exists(Paths.Snapshots)) return new List<string>();
            return Directory.GetFiles(Paths.Snapshots, "*.json").OrderByDescending(File.GetLastWriteTime).ToList();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Crash.Install(); // friendly error dialog + crash.log instead of a raw .NET error box
            Paths.Ensure();
            Settings.Load();
            if (!Settings.FirstRunDone) using (var w = new WelcomeForm()) w.ShowDialog();
            string page = args.Where(a => a.StartsWith("--page=")).Select(a => a.Substring(7)).FirstOrDefault();
            Application.Run(new MainForm(page));
        }
    }
}
