using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// Resolves which local PID owns a listening TCP socket on a given loopback
/// port. Used to defend against same-user lockfile spoofing — without this
/// check, an attacker can plant a fake server + matching lockfile and route
/// CLI traffic through it. TASK-004 / TASK-030.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PortOwnership
{
    /// <summary>
    /// Returns true iff the local IPv4 listening socket on
    /// <paramref name="port"/> is owned by <paramref name="pid"/>. Returns
    /// false if there's no LISTEN row for the port at all.
    /// </summary>
    public static bool IsPortOwnedBy(int port, int pid)
    {
        if (port <= 0 || port > 65535) return false;
        if (pid <= 0) return false;
        if (!TryGetTcpTable(out var rows)) return false;
        foreach (var row in rows)
        {
            if (row.State != MIB_TCP_STATE.LISTENING) continue;
            // Local addr is in network-byte-order; compare port the same way.
            var localPort = ((row.LocalPort >> 8) & 0xFF) | ((row.LocalPort & 0xFF) << 8);
            if (localPort != port) continue;
            return row.OwningPid == (uint)pid;
        }
        return false;
    }

    private static bool TryGetTcpTable(out MIB_TCPROW_OWNER_PID[] rows)
    {
        rows = Array.Empty<MIB_TCPROW_OWNER_PID>();
        int size = 0;
        // First call: get required buffer size.
        const uint AF_INET = 2;
        // 0x05 == TCP_TABLE_OWNER_PID_LISTENER.
        var ret = GetExtendedTcpTable(IntPtr.Zero, ref size, sort: false, AF_INET, 5, reserved: 0);
        if (ret != 0 && ret != 122 /* ERROR_INSUFFICIENT_BUFFER */) return false;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedTcpTable(buffer, ref size, sort: false, AF_INET, 5, reserved: 0);
            if (ret != 0) return false;
            int count = Marshal.ReadInt32(buffer);
            var entrySize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            rows = new MIB_TCPROW_OWNER_PID[count];
            // dwNumEntries (DWORD) is followed by the table entries; alignment
            // matches a DWORD on x64 because the next field is also DWORD-sized.
            for (int i = 0; i < count; i++)
            {
                var entryPtr = IntPtr.Add(buffer, sizeof(int) + i * entrySize);
                rows[i] = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(entryPtr);
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

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
