using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetCreator.Services;

/// <summary>
/// Independent, host-side confirmation that a widget really is contained
/// (threat-model C-2). MXC is the entire runtime security bar, yet the host
/// otherwise derives "was it sandboxed?" purely by substring-matching
/// <c>wxc-exec</c> stdout — which is fail-open (a partial setup, an ignored
/// policy field, or spoofed output all read as "contained"). This verifier adds
/// two out-of-band signals that do NOT trust the untrusted run's stdout:
/// <list type="number">
///   <item><b>Tier probe</b> — a dedicated <c>wxc-exec --probe</c> invocation
///   (runs no widget code) reports the strongest containment tier available on
///   this host, so we can optionally <b>fail closed</b> when only a weak tier is
///   available for untrusted code.</item>
///   <item><b>Token check</b> — after launch we locate the sandboxed widget
///   process from the host and inspect its access token directly
///   (<c>TokenIsAppContainer</c> + integrity level). A process running at the
///   host's own medium integrity with no AppContainer is a definite fail-open;
///   we kill it rather than believe stdout that claimed it was sandboxed.</item>
/// </list>
/// </summary>
public static class MxcContainmentVerifier
{
    /// <summary>Opt-in (truthy env): refuse to run untrusted code unless the
    /// strong BaseContainer tier is confirmed available. Off by default because
    /// AppContainer+DACL is still a real sandbox and is the only tier on some
    /// hosts; strict mode is for deployments that require the stronger boundary.</summary>
    public const string RequireBaseContainerVar = "WIDGET_CREATOR_REQUIRE_BASE_CONTAINER";

    public static bool StrictBaseContainer =>
        Environment.GetEnvironmentVariable(RequireBaseContainerVar) is { Length: > 0 } v &&
        (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    // ── tier probe ──────────────────────────────────────────────────────────

    /// <summary>Result of a dedicated <c>wxc-exec --probe</c> run (no widget code).</summary>
    public sealed record TierProbe(bool Ran, bool BaseContainerActive, string Raw)
    {
        /// <summary>True when we have positive evidence the host supports the
        /// strong BaseContainer tier.</summary>
        public bool StrongTierConfirmed => Ran && BaseContainerActive;
    }

    static readonly SemaphoreSlim _probeGate = new(1, 1);
    static TierProbe? _cachedProbe;

    /// <summary>Probe the effective containment tier once (cached). Best-effort:
    /// if <c>--probe</c> is unavailable the result is <see cref="TierProbe.Ran"/>
    /// = false and callers treat the strong tier as unconfirmed.</summary>
    public static async Task<TierProbe> ProbeTierAsync(string wxcExecPath, CancellationToken ct)
    {
        if (_cachedProbe is { } cached) return cached;
        await _probeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedProbe is { } c2) return c2;
            _cachedProbe = await RunProbeAsync(wxcExecPath, ct).ConfigureAwait(false);
            SessionLog.Write(_cachedProbe.Ran
                ? $"[Containment] tier probe: baseContainer={_cachedProbe.BaseContainerActive}"
                : "[Containment] tier probe did not run (wxc-exec --probe unavailable)");
            return _cachedProbe;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    static async Task<TierProbe> RunProbeAsync(string wxcExecPath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = wxcExecPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--probe");

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try { await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* gone */ }
                }
            }

            var raw = stdout + "\n" + stderr;
            // The probe reports the preferred tier; BaseContainer is the strong
            // tier, "appcontainer"/"dacl" is the weaker fallback. Treat an active
            // BaseContainer as confirmed only when it is not flagged unavailable.
            var lower = raw.ToLowerInvariant();
            var baseActive =
                (lower.Contains("base-container") || lower.Contains("basecontainer"))
                && !lower.Contains("not preferred")
                && !lower.Contains("unavailable")
                && !lower.Contains("bfscompiledin: false");
            return new TierProbe(Ran: true, BaseContainerActive: baseActive, Raw: raw.Trim());
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Containment] tier probe failed: {ex.Message}");
            return new TierProbe(Ran: false, BaseContainerActive: false, Raw: ex.Message);
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
    /// Poll for the sandboxed widget process (a descendant of <paramref name="rootPid"/>,
    /// i.e. the <c>wxc-exec</c> we launched) and inspect its token. Returns as soon
    /// as a matching process is found and checked, the deadline passes, or
    /// cancellation fires. Best-effort by design.
    /// </summary>
    public static TokenContainment VerifyWidgetProcess(
        int rootPid, string widgetExeName, TimeSpan deadline, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < deadline && !ct.IsCancellationRequested)
        {
            var pid = FindDescendantByName(rootPid, widgetExeName);
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

    static int FindDescendantByName(int rootPid, string exeName)
    {
        var (children, names) = SnapshotProcessTree();
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
