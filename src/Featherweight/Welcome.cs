using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Featherweight
{
    /// <summary>First-start screen: what the app does, what it never does, honest expectations, and the update-check choice.</summary>
    class WelcomeForm : Form
    {
        readonly CheckBox updates = new CheckBox
        {
            Text = "Check GitHub for new versions when Featherweight starts", Font = Theme.Bold,
            AutoSize = true, Checked = Settings.CheckUpdates, Margin = new Padding(0, 16, 0, 2)
        };

        public WelcomeForm()
        {
            Text = "Welcome to Featherweight"; Font = Theme.Base; BackColor = Theme.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(30, 28, 30, 24);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Card };
            stack.Controls.Add(Theme.Label("Welcome to Featherweight", Theme.H1));
            stack.Controls.Add(Para("See what's using your PC's memory, switch off the background apps you don't need, and undo anything, any time.", Theme.Muted));

            stack.Controls.Add(Heading("What it does"));
            stack.Controls.Add(Bullet("Shows live memory use, grouped by app and vendor."));
            stack.Controls.Add(Bullet("Finds background apps installed on this PC and suggests what's safe to switch off, based on how this PC is set up."));
            stack.Controls.Add(Bullet("Saves every original setting before changing it, and makes a System Restore point too."));

            stack.Controls.Add(Heading("What it never does"));
            stack.Controls.Add(Bullet("Change anything without showing you first and asking for administrator permission."));
            stack.Controls.Add(Bullet("Turn off Windows Update, Microsoft Defender or other security features."));
            stack.Controls.Add(Bullet("Collect or send data about you. There's no tracking of any kind."));

            stack.Controls.Add(Heading("Good to know"));
            stack.Controls.Add(Bullet("\"Clean RAM\" is a short-term boost, handy before launching a game. The lasting win is fewer background apps (the Optimize page)."));
            stack.Controls.Add(Bullet("Viewing works without admin rights. Changing things asks to restart Featherweight as administrator."));
            stack.Controls.Add(Bullet("Your backups and logs are kept in " + Paths.Display + "."));

            stack.Controls.Add(updates);
            var updNote = Para("At most once a day, it asks GitHub for the latest version number. That's Featherweight's only internet connection; nothing about you or your PC is sent. You can change this later in Settings & about.", Theme.Muted);
            updNote.Margin = new Padding(18, 0, 0, 0); updNote.Font = Theme.Small;
            stack.Controls.Add(updNote);
            var start = Theme.Button("Get started", true);
            start.Margin = new Padding(0, 18, 0, 0);
            start.Click += (s, e) => Close();
            stack.Controls.Add(start);
            AcceptButton = start;

            Controls.Add(stack);
            FormClosed += (s, e) => { Settings.CheckUpdates = updates.Checked; Settings.FirstRunDone = true; Settings.Save(); };
        }

        static Label Para(string text, Color color) { var l = Theme.Label(text, Theme.Base, color); l.MaximumSize = new Size(580, 0); l.Margin = new Padding(0, 2, 0, 6); return l; }
        static Label Heading(string text) { var l = Theme.Label(text, Theme.H2); l.Margin = new Padding(0, 12, 0, 4); return l; }
        static Label Bullet(string text) { var l = Theme.Label("•  " + text); l.MaximumSize = new Size(580, 0); l.Margin = new Padding(6, 0, 0, 4); return l; }
    }

    /// <summary>Settings and information about the app: version, updates, data, diagnostics, links.</summary>
    class SettingsPage : Page
    {
        readonly CheckBox updates = new CheckBox { Text = "Check for new versions when Featherweight starts (at most once a day)", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        readonly CheckBox restore = new CheckBox { Text = "Create a System Restore point before changing settings (recommended)", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        readonly Label updateStatus = Theme.Label("", Theme.Base, Theme.Muted);
        readonly Button checkNow = Theme.Button("Check for updates now");

        public SettingsPage()
        {
            AutoScroll = true;
            var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };

            stack.Controls.Add(Section("Version"));
            stack.Controls.Add(Theme.Label("Featherweight " + MainForm.AppVersion + (Paths.Portable ? "  (portable)" : ""), Theme.Bold));
            var row = Theme.ButtonRow(); row.Dock = DockStyle.None;
            checkNow.Click += async (s, e) => await CheckNow();
            var releases = Theme.Button("All releases and what's new"); releases.Click += (s, e) => Open(Updates.ReleasesPage);
            row.Controls.Add(checkNow); row.Controls.Add(releases);
            stack.Controls.Add(row);
            stack.Controls.Add(updateStatus);

            stack.Controls.Add(Section("Options"));
            updates.Checked = Settings.CheckUpdates;
            restore.Checked = Settings.RestorePoints;
            updates.CheckedChanged += (s, e) => { Settings.CheckUpdates = updates.Checked; Settings.Save(); };
            restore.CheckedChanged += (s, e) => { Settings.RestorePoints = restore.Checked; Settings.Save(); };
            stack.Controls.Add(updates);
            stack.Controls.Add(Note("The update check is Featherweight's only internet connection. It asks GitHub for the latest version number and never downloads anything by itself."));
            stack.Controls.Add(restore);
            stack.Controls.Add(Note("Windows keeps at most one restore point per day, so a recent one is reused. Featherweight's own backups protect every change either way."));

            stack.Controls.Add(Section("Your data"));
            stack.Controls.Add(Theme.Label(Paths.Display));
            stack.Controls.Add(Note(Paths.Portable ? "Portable mode: everything is kept next to Featherweight.exe." :
                "Backups, logs and settings. To make Featherweight portable, put an empty file named \"portable\" next to Featherweight.exe."));
            var dataRow = Theme.ButtonRow(); dataRow.Dock = DockStyle.None;
            var openData = Theme.Button("Open data folder"); openData.Click += (s, e) => Paths.Open(Paths.Root);
            dataRow.Controls.Add(openData);
            stack.Controls.Add(dataRow);

            stack.Controls.Add(Section("Help and feedback"));
            var helpRow = Theme.ButtonRow(); helpRow.Dock = DockStyle.None;
            var diag = Theme.Button("Export diagnostics…", true); diag.Click += async (s, e) => await Diagnostics.Export(FindForm());
            var report = Theme.Button("Report a problem"); report.Click += (s, e) => Open(Updates.NewIssuePage);
            var project = Theme.Button("Project page on GitHub"); project.Click += (s, e) => Open(Updates.ProjectPage);
            var welcome = Theme.Button("Show welcome screen"); welcome.Click += (s, e) => { using (var w = new WelcomeForm()) w.ShowDialog(FindForm()); updates.Checked = Settings.CheckUpdates; };
            helpRow.Controls.AddRange(new Control[] { diag, report, project, welcome });
            stack.Controls.Add(helpRow);
            stack.Controls.Add(Note("Diagnostics creates a text file for bug reports, with your user name and PC name removed. It opens so you can read it before sharing."));

            stack.Controls.Add(Section("About"));
            stack.Controls.Add(Note("Free and open source under the MIT License. Made by @workingwithbunnies together with Claude, Anthropic's AI coding assistant. No warranty: read what each change does before applying it."));

            Controls.Add(stack);
            Controls.Add(Theme.Header("Settings & about", "Options, updates, your data, and how to get help."));
        }

        static Label Section(string text) { var l = Theme.Label(text, Theme.H2); l.Margin = new Padding(0, 18, 0, 6); return l; }
        static Label Note(string text) { var l = Theme.Label(text, Theme.Small, Theme.Muted); l.MaximumSize = new Size(760, 0); l.Margin = new Padding(20, 0, 0, 8); return l; }
        static void Open(string url) { try { Process.Start(url); } catch { } }

        async Task CheckNow()
        {
            checkNow.Enabled = false; updateStatus.Text = "Checking…"; updateStatus.ForeColor = Theme.Muted;
            string error = null;
            var info = await Task.Run(() => Updates.Check(out error));
            checkNow.Enabled = true;
            Settings.LastUpdateCheck = DateTime.Now.ToString("s"); Settings.Save();
            if (info == null) { updateStatus.Text = error; return; }
            if (Updates.IsNewer(info.Version, MainForm.AppVersion))
            {
                updateStatus.Text = "Version " + info.Version + " is available."; updateStatus.ForeColor = Theme.Info;
                if (MessageBox.Show(FindForm(), "Featherweight " + info.Version + " is available (you have " + MainForm.AppVersion + ").\n\nOpen the download page?",
                        "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Open(info.Url);
            }
            else { updateStatus.Text = "You have the latest version."; updateStatus.ForeColor = Theme.Ok; }
        }
    }
}
