using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// Resolves which local PID owns a listening TCP socket on a given loopback
/// port. Used to defend against same-user lockfile spoofing — without this
/// check, an attacker can plant a fake server + matching lockfile and route
/// CLI traffic through it. TASK-004 / TASK-030.
///
/// <para>HTTP.SYS caveat: Reactor's MCP server uses
/// <see cref="System.Net.HttpListener"/>, which routes through Windows'
/// kernel-mode HTTP driver. The listening TCP socket is therefore owned by
/// the System process (pid 4 / kernel) rather than the user-mode app, so a
/// strict "row.OwningPid == lockfile.pid" check rejects every HTTP.SYS-backed
/// session. We treat <see cref="HttpSysPid"/> as a kernel-attribution sentinel
/// and accept it: same-user spoof resistance for HTTP.SYS ports falls back to
/// the per-launch bearer token enforced by the HTTP probe (spec 025 §5,
/// TASK-001). Direct <see cref="System.Net.Sockets.TcpListener"/> users still
/// get strict pid attribution. Spec 025 §11 / spec 035 implementation note.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PortOwnership
{
    /// <summary>
    /// Sentinel PID for the Windows System process / kernel. Listening sockets
    /// registered through HTTP.SYS (e.g. <see cref="System.Net.HttpListener"/>)
    /// surface in the TCP table under this PID rather than the registering
    /// user-mode process.
    /// </summary>
    public const int HttpSysPid = 4;

    /// <summary>
    /// Returns true iff the local IPv4 or IPv6 listening socket on
    /// <paramref name="port"/> is owned by <paramref name="pid"/>, or by
    /// <see cref="HttpSysPid"/> (HTTP.SYS) on at least one address family while
    /// no other user-mode process holds a competing listener on the same port.
    /// Returns false when no LISTEN row exists for the port at all.
    /// <para>
    /// <b>Failure mode for partial TCP-table enumeration:</b> if either
    /// <c>AF_INET</c> or <c>AF_INET6</c> enumeration fails (rare; e.g. locked-down
    /// loopback policy, kernel resource pressure), the HTTP.SYS-only fallback
    /// is disabled — we'd otherwise accept a session whose only HTTP.SYS row is
    /// on the visible family while a competing user-mode listener could exist
    /// on the unseen family. Direct pid match is still honored on whatever
    /// rows we did get back. This keeps the spoof defense intact under
    /// degraded enumeration.
    /// </para>
    /// </summary>
    public static bool IsPortOwnedBy(int port, int pid)
    {
        if (port <= 0 || port > 65535) return false;
        if (pid <= 0) return false;

        var rows = new List<TcpListenerRow>();
        bool v4Ok = TryGetTcpTable(AF_INET, out var v4);
        if (v4Ok) rows.AddRange(v4);
        bool v6Ok = TryGetTcpTable(AF_INET6, out var v6);
        if (v6Ok) rows.AddRange(v6);
        if (rows.Count == 0) return false;

        bool enumerationComplete = v4Ok && v6Ok;
        return MatchListener(rows, port, pid, enumerationComplete);
    }

    /// <summary>
    /// Pure matching predicate over a pre-collected listener table. Extracted
    /// so the policy can be unit-tested without poking the live TCP table.
    /// <para>
    /// The lockfile pid passes when:
    /// (1) any LISTEN row on <paramref name="port"/> directly attributes to
    /// <paramref name="pid"/>, OR
    /// (2) <paramref name="enumerationComplete"/> is true AND every LISTEN
    /// row on <paramref name="port"/> attributes to <see cref="HttpSysPid"/>
    /// (HTTP.SYS holds the socket and no user-mode process is competing on
    /// the same port). Mixed ownership — HTTP.SYS plus a different user-mode
    /// pid — is rejected as ambiguous.
    /// </para>
    /// <para>
    /// When <paramref name="enumerationComplete"/> is false (a TCP-table
    /// query for one address family failed), the HTTP.SYS-only fallback is
    /// disabled to prevent acceptance based on a partial view: a
    /// competing user-mode listener on the unseen family would be invisible
    /// to this check. Strict pid attribution still works on the visible
    /// rows.
    /// </para>
    /// </summary>
    internal static bool MatchListener(IReadOnlyList<TcpListenerRow> rows, int port, int pid, bool enumerationComplete)
    {
        bool anyForPort = false;
        bool anyHttpSys = false;
        bool anyOtherUserMode = false;

        foreach (var row in rows)
        {
            if (row.LocalPort != port) continue;
            anyForPort = true;
            if (row.OwningPid == (uint)pid) return true;
            if (row.OwningPid == (uint)HttpSysPid) anyHttpSys = true;
            else anyOtherUserMode = true;
        }

        if (!anyForPort) return false;
        if (!enumerationComplete) return false;
        return anyHttpSys && !anyOtherUserMode;
    }

    /// <summary>
    /// One LISTEN-state entry from the TCP table, normalised to host byte
    /// order for the port and stripped of address-family details we don't
    /// match on.
    /// </summary>
    internal readonly record struct TcpListenerRow(int LocalPort, uint OwningPid);

    private const uint AF_INET = 2;
    private const uint AF_INET6 = 23;
    // 0x05 == TCP_TABLE_OWNER_PID_LISTENER for IPv4 / IPv6.
    private const int TCP_TABLE_OWNER_PID_LISTENER = 5;

    private static bool TryGetTcpTable(uint family, out TcpListenerRow[] rows)
    {
        rows = Array.Empty<TcpListenerRow>();
        int size = 0;
        var ret = GetExtendedTcpTable(IntPtr.Zero, ref size, sort: false, family, TCP_TABLE_OWNER_PID_LISTENER, reserved: 0);
        if (ret != 0 && ret != 122 /* ERROR_INSUFFICIENT_BUFFER */) return false;
        if (size <= 0) return true; // empty table

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedTcpTable(buffer, ref size, sort: false, family, TCP_TABLE_OWNER_PID_LISTENER, reserved: 0);
            if (ret != 0) return false;
            int count = Marshal.ReadInt32(buffer);
            if (count <= 0) return true;

            rows = family == AF_INET
                ? ReadIPv4(buffer, count)
                : ReadIPv6(buffer, count);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static TcpListenerRow[] ReadIPv4(IntPtr buffer, int count)
    {
        var entrySize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
        var rows = new TcpListenerRow[count];
        for (int i = 0; i < count; i++)
        {
            var entryPtr = IntPtr.Add(buffer, sizeof(int) + i * entrySize);
            var entry = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(entryPtr);
            if (entry.State != MIB_TCP_STATE.LISTENING) { rows[i] = default; continue; }
            rows[i] = new TcpListenerRow(NetworkPortToHost(entry.LocalPort), entry.OwningPid);
        }
        return rows;
    }

    private static TcpListenerRow[] ReadIPv6(IntPtr buffer, int count)
    {
        var entrySize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
        var rows = new TcpListenerRow[count];
        for (int i = 0; i < count; i++)
        {
            var entryPtr = IntPtr.Add(buffer, sizeof(int) + i * entrySize);
            var entry = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(entryPtr);
            if (entry.State != MIB_TCP_STATE.LISTENING) { rows[i] = default; continue; }
            rows[i] = new TcpListenerRow(NetworkPortToHost(entry.LocalPort), entry.OwningPid);
        }
        return rows;
    }

    private static int NetworkPortToHost(uint nbo) =>
        (int)(((nbo >> 8) & 0xFF) | ((nbo & 0xFF) << 8));

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool sort,
        uint ipVersion,
        int tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public MIB_TCP_STATE State;
        public uint LocalAddr;
        public uint LocalPort; // network byte order, low 16 bits
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    // 16-byte address + 4-byte scope id; struct layout for AF_INET6.
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort; // network byte order
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public MIB_TCP_STATE State;
        public uint OwningPid;
    }

    private enum MIB_TCP_STATE
    {
        CLOSED = 1,
        LISTENING = 2,
        SYN_SENT = 3,
        SYN_RCVD = 4,
        ESTAB = 5,
        FIN_WAIT1 = 6,
        FIN_WAIT2 = 7,
        CLOSE_WAIT = 8,
        CLOSING = 9,
        LAST_ACK = 10,
        TIME_WAIT = 11,
        DELETE_TCB = 12,
    }
}
