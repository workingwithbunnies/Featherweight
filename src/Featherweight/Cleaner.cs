using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Featherweight
{
    // One-click RAM cleaner. Uses the same kernel calls as Sysinternals RAMMap's "Empty" menu.
    static class Cleaner
    {
        public class Result
        {
            public double UsedBefore, UsedAfter, AvailBefore, AvailAfter;
            public double FreeBefore, FreeAfter, StandbyBefore, StandbyAfter, ModifiedBefore, ModifiedAfter;
            public List<string> Log = new List<string>();
            public double Freed { get { return UsedBefore - UsedAfter; } }
        }

        /// <param name="purgeStandby">Also drop the standby cache (file cache). Frees "Free" memory but apps reload from disk once.</param>
        public static Result Run(bool purgeStandby)
        {
            var r = new Result();
            {
                var s = SysMem.Read();
                r.UsedBefore = s.UsedMB; r.AvailBefore = s.AvailMB;
                var lists = MemLists.Read(); // null without admin: page-list rows then show "–"
                r.FreeBefore = lists != null ? lists.FreeMB : double.NaN;
                r.StandbyBefore = lists != null ? lists.StandbyMB : double.NaN;
                r.ModifiedBefore = lists != null ? lists.ModifiedMB : double.NaN;

                bool priv = Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
                Native.EnablePrivilege("SeIncreaseQuotaPrivilege");

                // 1. Trim every process's working set (pages move to standby/modified lists, nothing is lost).
                if (priv && Call(Native.MemoryEmptyWorkingSets, "Trim all working sets", r)) { }
                else TrimEachProcess(r);

                // 2. Write modified pages out so they become reusable.
                if (priv) Call(Native.MemoryFlushModifiedList, "Flush modified page list", r);

                // 3. Optionally drop the standby (cache) list.
                if (purgeStandby && priv) Call(Native.MemoryPurgeStandbyList, "Purge standby cache", r);

                Thread.Sleep(700);
                s = SysMem.Read();
                r.UsedAfter = s.UsedMB; r.AvailAfter = s.AvailMB;
                lists = MemLists.Read();
                r.FreeAfter = lists != null ? lists.FreeMB : double.NaN;
                r.StandbyAfter = lists != null ? lists.StandbyMB : double.NaN;
                r.ModifiedAfter = lists != null ? lists.ModifiedMB : double.NaN;
            }
            return r;
        }

        static bool Call(int command, string label, Result r)
        {
            int cmd = command;
            int status = Native.NtSetSystemInformation(Native.SystemMemoryListInformation, ref cmd, sizeof(int));
            r.Log.Add(status == 0 ? "OK    " + label : string.Format("FAIL  {0} (NTSTATUS 0x{1:X8})", label, status));
            return status == 0;
        }

        static void TrimEachProcess(Result r)
        {
            int ok = 0, fail = 0;
            foreach (var p in Process.GetProcesses())
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_SET_QUOTA | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (h != IntPtr.Zero) { if (Native.EmptyWorkingSet(h)) ok++; else fail++; Native.CloseHandle(h); }
                else fail++;
                p.Dispose();
            }
            r.Log.Add(string.Format("OK    Trimmed {0} processes individually ({1} not accessible)", ok, fail));
        }
    }
}
