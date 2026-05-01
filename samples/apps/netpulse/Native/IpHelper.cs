using System.Net;
using System.Runtime.InteropServices;

namespace NetPulse.Native;

/// <summary>
/// P/Invoke wrappers for iphlpapi.dll — IP Helper API.
/// Provides access to the TCP connection table, UDP endpoint table,
/// and network interface statistics via low-level NT calls.
/// </summary>
static unsafe class IpHelper
{
    const int AF_INET = 2;
    const int TCP_TABLE_OWNER_PID_ALL = 5;
    const int UDP_TABLE_OWNER_PID = 1;
    const uint NO_ERROR = 0;
    const uint ERROR_INSUFFICIENT_BUFFER = 122;

    // MIB_IF_ROW2 is 1352 bytes on x64 Windows 10+
    const int MIB_IF_ROW2_SIZE = 1352;

    // Field offsets within MIB_IF_ROW2
    const int IF_ROW2_OFFSET_INDEX = 8;
    const int IF_ROW2_OFFSET_ALIAS = 28;       // WCHAR[257]
    const int IF_ROW2_OFFSET_TYPE = 1128;
    const int IF_ROW2_OFFSET_OPER_STATUS = 1156;
    const int IF_ROW2_OFFSET_TRANSMIT_SPEED = 1192;
    const int IF_ROW2_OFFSET_IN_OCTETS = 1208;
    const int IF_ROW2_OFFSET_OUT_OCTETS = 1280;

    // ── P/Invoke declarations ───────────────────────────────────────

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, int TableClass, uint Reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int pdwSize, bool bOrder,
        int ulAf, int TableClass, uint Reserved);

    [DllImport("iphlpapi.dll")]
    static extern uint GetIfTable2(out IntPtr pTable);

    [DllImport("iphlpapi.dll")]
    static extern void FreeMibTable(IntPtr pTable);

    // ── TCP table ───────────────────────────────────────────────────

    // MIB_TCPROW_OWNER_PID: 6 DWORDs = 24 bytes
    // [dwState][dwLocalAddr][dwLocalPort][dwRemoteAddr][dwRemotePort][dwOwningPid]

    public static TcpConn[] GetTcpConnections()
    {
        int size = 0;
        uint ret = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR) return [];

        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret != NO_ERROR) return [];

            int count = Marshal.ReadInt32(buf);
            var result = new TcpConn[count];
            byte* ptr = (byte*)buf + 4; // skip dwNumEntries

            for (int i = 0; i < count; i++)
            {
                uint* row = (uint*)(ptr + i * 24);
                result[i] = new TcpConn(
                    LocalAddr: FormatIp(row[1]),
                    LocalPort: NtoHs(row[2]),
                    RemoteAddr: FormatIp(row[3]),
                    RemotePort: NtoHs(row[4]),
                    State: (TcpState)row[0],
                    Pid: row[5]);
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // ── UDP table ───────────────────────────────────────────────────

    // MIB_UDPROW_OWNER_PID: 3 DWORDs = 12 bytes
    // [dwLocalAddr][dwLocalPort][dwOwningPid]

    public static UdpEndpoint[] GetUdpEndpoints()
    {
        int size = 0;
        uint ret = GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR) return [];

        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            ret = GetExtendedUdpTable(buf, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (ret != NO_ERROR) return [];

            int count = Marshal.ReadInt32(buf);
            var result = new UdpEndpoint[count];
            byte* ptr = (byte*)buf + 4;

            for (int i = 0; i < count; i++)
            {
                uint* row = (uint*)(ptr + i * 12);
                result[i] = new UdpEndpoint(
                    LocalAddr: FormatIp(row[0]),
                    LocalPort: NtoHs(row[1]),
                    Pid: row[2]);
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // ── Interface statistics ────────────────────────────────────────

    public record InterfaceSnapshot(uint Index, string Alias, ulong InOctets, ulong OutOctets, ulong Speed);

    public static List<InterfaceSnapshot> GetInterfaceSnapshots()
    {
        IntPtr table;
        if (GetIfTable2(out table) != NO_ERROR) return [];
        try
        {
            int count = Marshal.ReadInt32(table);
            // SECURITY (TASK-076): MIB_IF_ROW2 layout is hard-coded (1352 bytes
            // and offsets above). Bound the count to defend against an OS
            // version returning an unexpected size that would have us wander
            // past the buffer end into adjacent memory.
            if (count <= 0 || count > 16384) return [];
            var result = new List<InterfaceSnapshot>();
            // MIB_IF_TABLE2: ULONG NumEntries (4 bytes) + padding to 8 = 8 bytes header
            byte* basePtr = (byte*)table + 8;

            for (int i = 0; i < count; i++)
            {
                byte* row = basePtr + (long)i * MIB_IF_ROW2_SIZE;
                uint type = *(uint*)(row + IF_ROW2_OFFSET_TYPE);
                uint operStatus = *(uint*)(row + IF_ROW2_OFFSET_OPER_STATUS);

                // Only include operational, non-loopback, non-tunnel interfaces
                // type 24 = loopback, type 131 = tunnel
                if (operStatus != 1 || type == 24 || type == 131) continue;

                uint index = *(uint*)(row + IF_ROW2_OFFSET_INDEX);
                ulong inOctets = *(ulong*)(row + IF_ROW2_OFFSET_IN_OCTETS);
                ulong outOctets = *(ulong*)(row + IF_ROW2_OFFSET_OUT_OCTETS);
                ulong speed = *(ulong*)(row + IF_ROW2_OFFSET_TRANSMIT_SPEED);

                // Read alias (WCHAR[257])
                string alias = new string((char*)(row + IF_ROW2_OFFSET_ALIAS), 0, 256).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(alias)) alias = $"Interface #{index}";

                result.Add(new InterfaceSnapshot(index, alias, inOctets, outOctets, speed));
            }
            return result;
        }
        finally
        {
            FreeMibTable(table);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    static string FormatIp(uint addr)
    {
        return new IPAddress(addr).ToString();
    }

    static ushort NtoHs(uint networkPort)
    {
        // Port is stored in network byte order in the low 16 bits
        ushort raw = (ushort)(networkPort & 0xFFFF);
        return (ushort)IPAddress.NetworkToHostOrder((short)raw);
    }
}
