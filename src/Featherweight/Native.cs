using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Featherweight
{
    // Win32 / NT APIs used by the app. Everything here is a plain P/Invoke declaration or a thin wrapper.
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable,
                           SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pi, uint cb);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        // ---- processes ----
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_SET_QUOTA = 0x0100;
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder buf, ref int size);

        public static string ProcessPath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        // ---- system memory lists (what RAMMap's "Empty" menu uses) ----
        [DllImport("ntdll.dll")]
        public static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
        public const int SystemMemoryListInformation = 80;
        public const int MemoryEmptyWorkingSets = 2, MemoryFlushModifiedList = 3,
                         MemoryPurgeStandbyList = 4, MemoryPurgeLowPriorityStandbyList = 5;
        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        // ---- process owner (who is signed in to this desktop) ----
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);

        public static string ProcessOwnerSid(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            IntPtr tok = IntPtr.Zero, buf = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(h, 0x0008 /*TOKEN_QUERY*/, out tok)) return null;
                int len; GetTokenInformation(tok, 1 /*TokenUser*/, IntPtr.Zero, 0, out len);
                if (len <= 0) return null;
                buf = Marshal.AllocHGlobal(len);
                if (!GetTokenInformation(tok, 1, buf, len, out len)) return null;
                return new System.Security.Principal.SecurityIdentifier(Marshal.ReadIntPtr(buf)).Value; // TOKEN_USER.User.Sid
            }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); if (tok != IntPtr.Zero) CloseHandle(tok); CloseHandle(h); }
        }

        // ---- listening TCP ports (language-independent replacement for parsing netstat) ----
        [DllImport("iphlpapi.dll")]
        static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

        /// <returns>(address:port, owning pid) for every listening TCP socket, IPv4 and IPv6.</returns>
        public static System.Collections.Generic.List<Tuple<string, int>> TcpListeners()
        {
            var res = new System.Collections.Generic.List<Tuple<string, int>>();
            foreach (int af in new[] { 2 /*AF_INET*/, 23 /*AF_INET6*/ })
            {
                int size = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 3 /*TCP_TABLE_OWNER_PID_LISTENER*/, 0);
                if (size <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buf, ref size, false, af, 3, 0) != 0) continue;
                    int count = Marshal.ReadInt32(buf), rowSize = af == 2 ? 24 : 56;
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr row = IntPtr.Add(buf, 4 + i * rowSize);
                        string addr; int port, pid;
                        if (af == 2)
                        {
                            addr = new System.Net.IPAddress((long)(uint)Marshal.ReadInt32(row, 4)).ToString();
                            port = Port(Marshal.ReadInt32(row, 8)); pid = Marshal.ReadInt32(row, 20);
                        }
                        else
                        {
                            var b = new byte[16]; Marshal.Copy(row, b, 0, 16);
                            addr = "[" + new System.Net.IPAddress(b) + "]";
                            port = Port(Marshal.ReadInt32(row, 20)); pid = Marshal.ReadInt32(row, 52);
                        }
                        res.Add(Tuple.Create(addr + ":" + port, pid));
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return res;
        }
        static int Port(int raw) { return ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF); } // network byte order

        /// <summary>Number of open (established) TCP connections to other computers, per process id. Counts only: no addresses.</summary>
        public static System.Collections.Generic.Dictionary<int, int> TcpConnectionsByPid()
        {
            var res = new System.Collections.Generic.Dictionary<int, int>();
            foreach (int af in new[] { 2, 23 })
            {
                int size = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 5 /*TCP_TABLE_OWNER_PID_ALL*/, 0);
                if (size <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buf, ref size, false, af, 5, 0) != 0) continue;
                    int count = Marshal.ReadInt32(buf), rowSize = af == 2 ? 24 : 56;
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr row = IntPtr.Add(buf, 4 + i * rowSize);
                        int state, pid; bool loopback;
                        if (af == 2)
                        {
                            state = Marshal.ReadInt32(row, 0); pid = Marshal.ReadInt32(row, 20);
                            loopback = (Marshal.ReadInt32(row, 12) & 0xFF) == 127;
                        }
                        else
                        {
                            var b = new byte[16]; Marshal.Copy(IntPtr.Add(row, 24), b, 0, 16);
                            state = Marshal.ReadInt32(row, 48); pid = Marshal.ReadInt32(row, 52);
                            loopback = System.Net.IPAddress.IsLoopback(new System.Net.IPAddress(b));
                        }
                        if (state != 5 /*ESTABLISHED*/ || loopback) continue;
                        int n; res.TryGetValue(pid, out n); res[pid] = n + 1;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return res;
        }

        // ---- "@file.dll,-123" and "@{Package?ms-resource://…}" strings, as Windows shows them in its own language ----
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHLoadIndirectString(string source, StringBuilder outBuf, int outSize, IntPtr reserved);

        public static string IndirectString(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (!source.StartsWith("@")) return source;
            var sb = new StringBuilder(2048);
            return SHLoadIndirectString(source, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0 ? sb.ToString() : null;
        }

        // ---- privileges ----
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValue(string sys, string name, out LUID luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, uint len, IntPtr prev, IntPtr retLen);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        public static bool EnablePrivilege(string name)
        {
            IntPtr tok;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 /*ADJUST*/ | 0x0008 /*QUERY*/, out tok)) return false;
            try
            {
                LUID l;
                if (!LookupPrivilegeValue(null, name, out l)) return false;
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = l, Attributes = 2 /*SE_PRIVILEGE_ENABLED*/ };
                if (!AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
                return Marshal.GetLastWin32Error() == 0; // 1300 = ERROR_NOT_ALL_ASSIGNED
            }
            finally { CloseHandle(tok); }
        }

        // ---- Authenticode ----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData;
            public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
            public uint dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
        }
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        /// <summary>True when the file carries a valid embedded Authenticode signature (no revocation check).</summary>
        public static bool HasValidSignature(string path)
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = path };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                var wd = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,            // WTD_UI_NONE
                    fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                    dwUnionChoice = 1,         // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwProvFlags = 0x10 | 0x1000 // REVOCATION_CHECK_NONE | CACHE_ONLY_URL_RETRIEVAL
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                return WinVerifyTrust(IntPtr.Zero, ref action, ref wd) == 0;
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }
        }
    }
}
