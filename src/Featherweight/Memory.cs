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

        public static List<GroupInfo> Group(List<ProcInfo> procs)
        {
            var taken = new HashSet<int>();
            var res = new List<GroupInfo>();
            foreach (var g in Groups)
            {
                var m = procs.Where(p => !taken.Contains(p.Id) && g.Value.IsMatch(p.Name)).ToList();
                foreach (var p in m) taken.Add(p.Id);
                res.Add(new GroupInfo
                {
                    Group = g.Key, Count = m.Count, PrivMB = m.Sum(p => p.PrivMB), WSMB = m.Sum(p => p.WSMB),
                    Names = string.Join(", ", m.Select(p => p.Name).Distinct().OrderBy(n => n))
                });
            }
            var rest = procs.Where(p => !taken.Contains(p.Id)).ToList();
            res.Add(new GroupInfo { Group = "Everything else", Count = rest.Count, PrivMB = rest.Sum(p => p.PrivMB), WSMB = rest.Sum(p => p.WSMB), Names = "" });
            return res;
        }
    }
}
