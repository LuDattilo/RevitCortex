using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RevitCortex.Server;

/// <summary>
/// Force-exit when our parent process dies.
///
/// Why this exists: when Claude Desktop reconnects to its MCP children,
/// the existing servers should terminate (their stdin closes). In
/// practice they linger — Microsoft.Extensions.Hosting +
/// WithStdioServerTransport doesn't always treat stdin EOF as a stop
/// signal. Without this watchdog, every Claude Desktop reconnect leaves
/// a ~100 MB orphan; after a day of dev work the user can end up with
/// a dozen RevitCortex.Server.exe processes running in parallel.
///
/// Implementation: read parent PID via NtQueryInformationProcess, then
/// fire-and-forget a Task that awaits parent exit and calls
/// Environment.Exit(0). Best-effort — every failure path is silently
/// swallowed; we'd rather run as a potential orphan than crash on
/// startup because the watchdog init failed.
/// </summary>
public static class ParentWatchdog
{
    public static void Start()
    {
        try
        {
            var ppid = GetParentProcessId();
            if (ppid <= 0) return;

            Process parent;
            try { parent = Process.GetProcessById(ppid); }
            catch { return; }

            _ = Task.Run(async () =>
            {
                try { await parent.WaitForExitAsync(); } catch { }
                // Parent died → we're an orphan, exit immediately so we
                // don't accumulate. Skip graceful host shutdown — there's
                // nobody on the other end of stdio to talk to anyway.
                Environment.Exit(0);
            });
        }
        catch { }
    }

    private static int GetParentProcessId()
    {
        try
        {
            var info = default(PROCESS_BASIC_INFORMATION);
            var status = NtQueryInformationProcess(
                Process.GetCurrentProcess().Handle, 0, ref info,
                Marshal.SizeOf(info), out _);
            return status == 0 ? info.InheritedFromUniqueProcessId.ToInt32() : -1;
        }
        catch { return -1; }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr handle, int infoClass, ref PROCESS_BASIC_INFORMATION info,
        int size, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
