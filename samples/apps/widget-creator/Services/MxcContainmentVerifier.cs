using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Mxc.Sdk.V1;

namespace WidgetCreator.Services;

/// <summary>
/// Independent, host-side confirmation that a widget really is contained
/// (threat-model C-2). MXC is the entire runtime security bar, so the host does
/// not take "it was sandboxed" on faith from a successful launch. This verifier
/// adds two out-of-band signals:
/// <list type="number">
///   <item><b>Tier probe</b> — <see cref="MxcContainer.Probe"/> (runs no widget
///   code) reports which Windows ProcessContainer isolation tier would serve the
///   request on this host, so we can optionally <b>fail closed</b> when only a
///   weaker tier is available for untrusted code.</item>
///   <item><b>Token check</b> — after launch we take the contained process id
///   reported by the SDK (<see cref="MxcProcess.Id"/>) and inspect its access
///   token directly from the host (<c>TokenIsAppContainer</c> + integrity level).
///   A process running at the host's own medium integrity with no AppContainer
///   is a definite fail-open; we kill it rather than trust the launch.</item>
/// </list>
/// </summary>
public static class MxcContainmentVerifier
{
    /// <summary>Opt-in (truthy env): refuse to run untrusted code unless the
    /// strong BaseContainer tier is confirmed available. Off by default because
    /// AppContainer (BFS or DACL) is still a real sandbox and is the only tier on
    /// some hosts; strict mode is for deployments that require the stronger
    /// boundary.</summary>
    public const string RequireBaseContainerVar = "WIDGET_CREATOR_REQUIRE_BASE_CONTAINER";

    public static bool StrictBaseContainer =>
        Environment.GetEnvironmentVariable(RequireBaseContainerVar) is { Length: > 0 } v &&
        (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    // ── tier probe ──────────────────────────────────────────────────────────

    /// <summary>Result of <see cref="MxcContainer.Probe"/> for a request (no widget code runs).</summary>
    public sealed record TierProbe(bool Ran, IsolationTier? Tier, string Raw)
    {
        /// <summary>True when we have positive evidence the host serves this
        /// request with the strong BaseContainer tier.</summary>
        public bool StrongTierConfirmed => Ran && Tier == IsolationTier.BaseContainer;
    }

    /// <summary>
    /// Ask MXC which ProcessContainer tier would serve <paramref name="request"/>.
    /// Best-effort: on failure <see cref="TierProbe.Ran"/> is false and callers
    /// treat the strong tier as unconfirmed.
    /// </summary>
    public static TierProbe ProbeTier(ContainerRequest request)
    {
        try
        {
            var probe = MxcContainer.Probe(request);
            var raw = $"tier={probe.Tier?.ToString() ?? "none"}"
                + (probe.Error is { Length: > 0 } e ? $" error={e}" : "")
                + (probe.Warnings.Count > 0 ? $" warnings=[{string.Join("; ", probe.Warnings)}]" : "");
            SessionLog.Write($"[Containment] tier probe: {raw}");
            return new TierProbe(Ran: probe.Error is null && probe.Tier is not null, probe.Tier, raw);
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Containment] tier probe failed: {ex.Message}");
            return new TierProbe(Ran: false, Tier: null, Raw: ex.Message);
        }
    }

    // ── host-side token verification ─────────────────────────────────────────

    /// <summary>Outcome of inspecting the sandboxed widget's access token.</summary>
    public sealed record TokenContainment(bool Checked, bool IsAppContainer, int IntegrityRid, string? Detail)
    {
        /// <summary>True only when we positively confirmed the process is NOT
        /// contained (medium+ integrity and not an AppContainer). Inability to
        /// determine is deliberately NOT a fail — we never kill on uncertainty.</summary>
        public bool ConfirmedUncontained =>
            Checked && !IsAppContainer && IntegrityRid >= SECURITY_MANDATORY_MEDIUM_RID;

        /// <summary>True when we positively confirmed the process IS contained.</summary>
        public bool ConfirmedContained =>
            Checked && (IsAppContainer || IntegrityRid < SECURITY_MANDATORY_MEDIUM_RID);
    }

    /// <summary>
    /// Locate the sandboxed widget process — <paramref name="rootPid"/> itself (the
    /// contained process the SDK spawned) or, if MXC interposed a launcher, a
    /// descendant named <paramref name="widgetExeName"/> — and inspect its token.
    /// Returns as soon as a matching process is checked, the deadline passes, or
    /// cancellation fires. Best-effort by design.
    /// </summary>
    public static TokenContainment VerifyWidgetProcess(
        int rootPid, string widgetExeName, TimeSpan deadline, CancellationToken ct)
    {
        if (rootPid <= 0)
            return new TokenContainment(Checked: false, IsAppContainer: false, IntegrityRid: 0,
                Detail: "MXC reported no process id");

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < deadline && !ct.IsCancellationRequested)
        {
            var pid = FindWidgetProcess(rootPid, widgetExeName);
            if (pid != 0)
                return InspectToken(pid);
            Thread.Sleep(150);
        }
        return new TokenContainment(Checked: false, IsAppContainer: false, IntegrityRid: 0,
            Detail: "widget process not found before deadline");
    }

    static TokenContainment InspectToken(int pid)
    {
        var hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (hProc == IntPtr.Zero)
            return new TokenContainment(false, false, 0, $"OpenProcess failed ({Marshal.GetLastWin32Error()})");
        try
        {
            if (!OpenProcessToken(hProc, TOKEN_QUERY, out var hTok) || hTok == IntPtr.Zero)
                return new TokenContainment(false, false, 0, $"OpenProcessToken failed ({Marshal.GetLastWin32Error()})");
            try
            {
                var isAppContainer = QueryIsAppContainer(hTok);
                var rid = QueryIntegrityRid(hTok);
                var detail = $"pid={pid} appContainer={isAppContainer} integrityRid=0x{rid:X}";
                return new TokenContainment(Checked: true, IsAppContainer: isAppContainer, IntegrityRid: rid, Detail: detail);
            }
            finally { CloseHandle(hTok); }
        }
        finally { CloseHandle(hProc); }
    }

    static bool QueryIsAppContainer(IntPtr token)
    {
        // TokenIsAppContainer (29) → a single DWORD, non-zero when the token runs
        // inside an AppContainer.
        var buf = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            if (GetTokenInformation(token, TOKEN_IS_APPCONTAINER, buf, sizeof(uint), out _))
                return Marshal.ReadInt32(buf) != 0;
            return false;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static int QueryIntegrityRid(IntPtr token)
    {
        // TokenIntegrityLevel (25) → TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES }.
        GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, IntPtr.Zero, 0, out var len);
        if (len == 0) return -1;
        var buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, buf, len, out _))
                return -1;
            // TOKEN_MANDATORY_LABEL.Label.Sid is the first pointer in the struct.
            var pSid = Marshal.ReadIntPtr(buf);
            if (pSid == IntPtr.Zero) return -1;
            var countPtr = GetSidSubAuthorityCount(pSid);
            if (countPtr == IntPtr.Zero) return -1;
            int count = Marshal.ReadByte(countPtr);
            if (count == 0) return -1;
            var ridPtr = GetSidSubAuthority(pSid, (uint)(count - 1));
            return Marshal.ReadInt32(ridPtr);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ── process-tree walk (toolhelp snapshot) ────────────────────────────────

    static int FindWidgetProcess(int rootPid, string exeName)
    {
        var (children, names) = SnapshotProcessTree();
        if (names.TryGetValue(rootPid, out var rootName) &&
            string.Equals(rootName, exeName, StringComparison.OrdinalIgnoreCase))
            return rootPid;
        if (children.Count == 0) return 0;

        var stack = new Stack<int>();
        stack.Push(rootPid);
        var seen = new HashSet<int> { rootPid };
        while (stack.Count > 0)
        {
            var pid = stack.Pop();
            if (children.TryGetValue(pid, out var kids))
            {
                foreach (var kid in kids.Where(seen.Add))
                {
                    if (names.TryGetValue(kid, out var name) &&
                        string.Equals(name, exeName, StringComparison.OrdinalIgnoreCase))
                        return kid;
                    stack.Push(kid);
                }
            }
        }
        return 0;
    }

    static (Dictionary<int, List<int>> Children, Dictionary<int, string> Names) SnapshotProcessTree()
    {
        var children = new Dictionary<int, List<int>>();
        var names = new Dictionary<int, string>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == INVALID_HANDLE_VALUE) return (children, names);
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snap, ref entry)) return (children, names);
            do
            {
                var pid = (int)entry.th32ProcessID;
                var ppid = (int)entry.th32ParentProcessID;
                names[pid] = entry.szExeFile ?? "";
                if (!children.TryGetValue(ppid, out var list))
                    children[ppid] = list = new List<int>();
                list.Add(pid);
            } while (Process32Next(snap, ref entry));
        }
        finally { CloseHandle(snap); }
        return (children, names);
    }

    // ── native interop ───────────────────────────────────────────────────────

    const int SECURITY_MANDATORY_MEDIUM_RID = 0x2000;

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TOKEN_QUERY = 0x0008;
    const int TOKEN_INTEGRITY_LEVEL = 25;
    const int TOKEN_IS_APPCONTAINER = 29;

    const uint TH32CS_SNAPPROCESS = 0x00000002;
    static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint index);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
