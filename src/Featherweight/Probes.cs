using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace Featherweight
{
    class ProbeResult
    {
        public bool Value;  // the yes/no answer the catalog's "checks" compare against
        public string Text; // human-readable evidence, shown to the user ("{text}" in catalog notes)
    }

    /// <summary>
    /// Questions about this particular PC that decide whether an optimization is a good idea here.
    /// Each probe runs at most once per scan (they can be slow, e.g. Windows Update history).
    /// </summary>
    static class Probes
    {
        static readonly Dictionary<string, ProbeResult> cache = new Dictionary<string, ProbeResult>(StringComparer.OrdinalIgnoreCase);

        public static void Reset() { lock (cache) cache.Clear(); }

        public static ProbeResult Get(string name)
        {
            lock (cache)
            {
                ProbeResult r;
                if (cache.TryGetValue(name, out r)) return r;
                try
                {
                    switch (name)
                    {
                        case "hypervVms": r = HyperVVms(); break;
                        case "firmwareViaWindowsUpdate": r = FirmwareViaWindowsUpdate(); break;
                        case "phonePaired": r = PhonePaired(); break;
                        default: r = null; break;
                    }
                }
                catch (Exception ex) { r = new ProbeResult { Value = false, Text = "couldn't check (" + ex.Message + ")" }; }
                cache[name] = r;
                return r;
            }
        }

        // Hyper-V VMs: ask Hyper-V's WMI provider (needs admin); otherwise look for VM config files.
        static ProbeResult HyperVVms()
        {
            var names = new List<string>();
            bool viaWmi = false;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\virtualization\v2", "SELECT ElementName FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'"))
                    foreach (ManagementObject o in s.Get()) names.Add(Convert.ToString(o["ElementName"]));
                viaWmi = true;
            }
            catch { }
            int count = names.Count;
            if (!viaWmi)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Hyper-V\Virtual Machines");
                try { if (Directory.Exists(dir)) count = Directory.GetFiles(dir, "*.vmcx", SearchOption.AllDirectories).Length; } catch { }
            }
            return new ProbeResult
            {
                Value = count > 0,
                Text = count == 0 ? "no Hyper-V virtual machines" + (viaWmi ? "" : " found in the default folder (run as administrator for a full check)")
                                  : count + " Hyper-V virtual machine(s)" + (names.Count > 0 ? " (" + string.Join(", ", names) + ")" : "")
            };
        }

        // Has Windows Update ever installed firmware (BIOS/UEFI) for this PC's maker?
        static ProbeResult FirmwareViaWindowsUpdate()
        {
            string maker = "";
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in s.Get()) maker = Convert.ToString(o["Manufacturer"]);
            }
            catch { }
            string makerWord = Regex.Match(maker ?? "", @"[A-Za-z]{2,}").Value; // "Dell Inc." -> "Dell"

            dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
            dynamic searcher = session.CreateUpdateSearcher();
            int total = searcher.GetTotalHistoryCount();
            string best = null; DateTime bestDate = DateTime.MinValue;
            if (total > 0)
            {
                dynamic hist = searcher.QueryHistory(0, Math.Min(total, 2000));
                int n = hist.Count;
                for (int i = 0; i < n; i++)
                {
                    dynamic h = hist.Item(i);
                    string title = h.Title ?? "";
                    int rc = h.ResultCode; // 2 = succeeded, 3 = succeeded with errors
                    if ((rc == 2 || rc == 3) && title.IndexOf("Firmware", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        ((makerWord.Length > 0 && title.IndexOf(makerWord, StringComparison.OrdinalIgnoreCase) >= 0) || title.IndexOf("System", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        DateTime d = h.Date;
                        if (d > bestDate) { bestDate = d; best = title; }
                    }
                }
            }
            return new ProbeResult
            {
                Value = best != null,
                Text = best != null ? "Windows Update installed \"" + best + "\" on " + bestDate.ToLocalTime().ToString("d MMM yyyy")
                                    : "Windows Update hasn't installed firmware for this PC yet"
            };
        }

        // Phone Link: has a phone ever been paired with this PC?
        static ProbeResult PhonePaired()
        {
            using (var k = Engine.OpenUserKey(@"Software\Microsoft\Windows\CurrentVersion\Mobility"))
            {
                bool paired = k != null && Convert.ToInt32(k.GetValue("HasPreviouslyPaired", 0)) == 1;
                return new ProbeResult { Value = paired, Text = paired ? "a phone has been paired with this PC" : "no phone has been paired with this PC" };
            }
        }
    }
}
