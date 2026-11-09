using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Featherweight
{
    class SysMem
    {
        public double TotalMB, AvailMB, UsedMB, CommitMB, CommitLimitMB, NonpagedMB, PagedMB, CacheMB;
        public int Processes;
        public double UsedPct { get { return TotalMB > 0 ? 100.0 * UsedMB / TotalMB : 0; } }
        public static double UptimeMin { get { return Native.GetTickCount64() / 60000.0; } }

        public static SysMem Read()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            Native.GlobalMemoryStatusEx(ref m);
            Native.PERFORMANCE_INFORMATION pi;
            Native.GetPerformanceInfo(out pi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION)));
            double page = (double)pi.PageSize.ToUInt64();
            Func<UIntPtr, double> pages = p => p.ToUInt64() * page / 1048576.0;
            var s = new SysMem
            {
                TotalMB = m.ullTotalPhys / 1048576.0,
                AvailMB = m.ullAvailPhys / 1048576.0,
                CommitMB = pages(pi.CommitTotal),
                CommitLimitMB = pages(pi.CommitLimit),
                NonpagedMB = pages(pi.KernelNonpaged),
                PagedMB = pages(pi.KernelPaged),
                CacheMB = pages(pi.SystemCache),
                Processes = (int)pi.ProcessCount
            };
            s.UsedMB = s.TotalMB - s.AvailMB;
            return s;
        }
    }

    // Page-list breakdown (free / standby / modified) read straight from the kernel, so it works in every
    // Windows display language (performance-counter names are translated). Needs admin; Read() returns null otherwise.
    class MemLists
    {
        public double FreeMB, ModifiedMB, StandbyMB;

        public static MemLists Read()
        {
            Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
            int n = IntPtr.Size, len = n * 22; // SYSTEM_MEMORY_LIST_INFORMATION: 5 counters + 8 by priority + 8 repurposed + 1
            IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(len);
            try
            {
                int ret;
                if (Native.NtQuerySystemInformation(Native.SystemMemoryListInformation, buf, len, out ret) != 0) return null;
                Func<int, double> at = i => (double)(ulong)System.Runtime.InteropServices.Marshal.ReadIntPtr(buf, i * n).ToInt64();
                double page = Environment.SystemPageSize / 1048576.0, standby = 0;
                for (int i = 5; i < 13; i++) standby += at(i);
                return new MemLists { FreeMB = (at(0) + at(1)) * page, ModifiedMB = (at(2) + at(3)) * page, StandbyMB = standby * page };
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
        }
    }

    class ProcInfo { public string Name; public int Id; public double PrivMB, WSMB; }
    class GroupInfo { public string Group; public int Count; public double PrivMB, WSMB; public string Names; }

    static class ProcScan
    {
        // Process-name regexes used to group memory by app/vendor (same as Featherweight.ps1).
        public static readonly KeyValuePair<string, Regex>[] Groups =
        {
            G("Claude", "^claude$|^cowork-svc$"),
            G("Dell/Alienware", "Dell|Alien|AWCC|AWPerformance|OCControl|SupportAssist|ServiceShell|TechHub|FusionService"),
            G("NVIDIA", "^nv|NVIDIA|NVDisplay"),
            G("Intel", "Intel|IGCC|XtuService|jhi_service|esif_uf|igfxCUI|WMIRegistrationService|PresentMon"),
            G("Google Play Games", "GooglePlay"),
            G("Phone Link", "PhoneExperienceHost|CrossDevice"),
            G("PC Manager", "MSPCManager"),
            G("Defender", "^MsMpEng$|^NisSrv$|MpDefenderCoreService|SecurityHealth|DefenderAi|DefenderSession"),
            G("Search/Start", "^SearchHost$|StartMenuExperienceHost|msedgewebview2|SearchIndexer"),
            G("Edge", "^msedge$"),
        };
        static KeyValuePair<string, Regex> G(string n, string rx) { return new KeyValuePair<string, Regex>(n, new Regex(rx, RegexOptions.IgnoreCase)); }

        public static List<ProcInfo> Processes()
        {
            var list = new List<ProcInfo>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0) continue;
                    list.Add(new ProcInfo { Name = p.ProcessName, Id = p.Id, PrivMB = p.PrivateMemorySize64 / 1048576.0, WSMB = p.WorkingSet64 / 1048576.0 });
                }
                catch { }
                finally { p.Dispose(); }
            }
            return list;
        }

        // Program path per process id, remembered while the process lives (the name guards against a reused id).
        static readonly Dictionary<int, KeyValuePair<string, string>> paths = new Dictionary<int, KeyValuePair<string, string>>();

        /// <summary>Full program path of a process, or null when Windows doesn't tell us (protected system processes).</summary>
        public static string PathOf(ProcInfo p)
        {
            lock (paths)
            {
                KeyValuePair<string, string> hit;
                if (paths.TryGetValue(p.Id, out hit) && hit.Key == p.Name) return hit.Value;
                string path = Native.ProcessPath(p.Id);
                if (paths.Count > 4000) paths.Clear();
                paths[p.Id] = new KeyValuePair<string, string>(p.Name, path);
                return path;
            }
        }

        public const string WindowsGroup = "Windows & Microsoft";

        // Program name → full path for every installed service: without admin rights Windows hides the path of
        // services running as SYSTEM, but the service list still says which program each one runs.
        static Dictionary<string, string> servicePrograms;
        static Dictionary<string, string> ServicePrograms()
        {
            if (servicePrograms != null) return servicePrograms;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    foreach (var n in root.GetSubKeyNames())
                        using (var k = root.OpenSubKey(n))
                        {
                            string exe = k == null ? null : Publisher.ExeFromCommand(k.GetValue("ImagePath") as string);
                            if (exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && exe.Contains("\\"))
                            {
                                string name = System.IO.Path.GetFileNameWithoutExtension(exe);
                                if (!map.ContainsKey(name)) map[name] = exe;
                            }
                        }
            }
            catch { }
            return servicePrograms = map;
        }

        /// <summary>Best guess at a process's program when Windows won't tell us its path.</summary>
        static string GuessPath(ProcInfo p)
        {
            string path;
            if (ServicePrograms().TryGetValue(p.Name, out path)) return path;
            string sys = System.IO.Path.Combine(Environment.SystemDirectory, p.Name + ".exe");
            if (System.IO.File.Exists(sys)) return sys;
            return null;
        }

        public static List<GroupInfo> Group(List<ProcInfo> procs)
        {
            var taken = new HashSet<int>();
            var sets = new List<KeyValuePair<string, List<ProcInfo>>>();
            foreach (var g in Groups)
            {
                var m = procs.Where(p => !taken.Contains(p.Id) && g.Value.IsMatch(p.Name)).ToList();
                foreach (var p in m) taken.Add(p.Id);
                sets.Add(new KeyValuePair<string, List<ProcInfo>>(g.Key, m));
            }
            // Everything else is grouped by the company that made it, so any brand (HP, Lenovo, ASUS…) shows by name.
            var rest = new List<ProcInfo>();
            foreach (var p in procs.Where(p => !taken.Contains(p.Id)))
            {
                string path = PathOf(p) ?? GuessPath(p);
                string brand = path == null ? null : Publisher.Of(path, false);
                bool inWindows = path != null && path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase);
                // Drivers install their helpers inside the Windows folder too (AMD, Realtek, HP…): only Microsoft's own
                // programs, or ones that name no company at all there, count as Windows.
                if (brand == null || brand == Publisher.Unknown) { if (inWindows) brand = WindowsGroup; else { rest.Add(p); continue; } }
                else if (Publisher.IsMicrosoft(brand)) brand = WindowsGroup;
                // join a fixed group of the same brand ("Dell" → "Dell/Alienware")
                int i = sets.FindIndex(s => s.Key.Equals(brand, StringComparison.OrdinalIgnoreCase) || s.Key.StartsWith(brand + "/", StringComparison.OrdinalIgnoreCase));
                if (i < 0) { sets.Add(new KeyValuePair<string, List<ProcInfo>>(brand, new List<ProcInfo>())); i = sets.Count - 1; }
                sets[i].Value.Add(p);
            }
            var res = sets.Select(s => new GroupInfo
            {
                Group = s.Key, Count = s.Value.Count, PrivMB = s.Value.Sum(p => p.PrivMB), WSMB = s.Value.Sum(p => p.WSMB),
                Names = string.Join(", ", s.Value.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n))
            }).ToList();
            res.Add(new GroupInfo { Group = "Everything else", Count = rest.Count, PrivMB = rest.Sum(p => p.PrivMB), WSMB = rest.Sum(p => p.WSMB),
                                    Names = string.Join(", ", rest.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n)) });
            return res;
        }
    }
}
