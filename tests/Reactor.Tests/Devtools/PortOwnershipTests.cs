using Microsoft.UI.Reactor.Cli.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Spec 025 §11 / spec 035 implementation note. The original strict
/// "row.OwningPid == lockfile.pid" check rejected every <c>HttpListener</c>-
/// backed devtools session because Windows attributes the listening socket
/// to the System process (pid 4) when HTTP.SYS holds the request queue. The
/// pure <see cref="PortOwnership.MatchListener"/> predicate is the unit-
/// testable seam — these tests pin its accept/reject policy without hitting
/// the live TCP table.
/// </summary>
public class PortOwnershipTests
{
    static PortOwnership.TcpListenerRow Row(int port, uint pid) => new(port, pid);

    [Fact]
    public void Match_DirectUserModeOwner_Accepts()
    {
        // Direct TcpListener user-mode bind — the legacy happy path.
        var rows = new[] { Row(port: 50001, pid: 12345) };
        Assert.True(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_NoListenerForPort_Rejects()
    {
        // No row at all for the asked port — port is not bound.
        var rows = new[] { Row(port: 50002, pid: 12345) };
        Assert.False(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_DifferentUserModePid_Rejects()
    {
        // Some other user-mode pid owns the port — classic spoof signature.
        var rows = new[] { Row(port: 50001, pid: 99999) };
        Assert.False(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_HttpSysOnly_Accepts()
    {
        // HttpListener case: HTTP.SYS (pid 4) holds the socket; the legitimate
        // user-mode pid is the request-queue owner that mur authenticates
        // against via the bearer token. Single HTTP.SYS row → accept.
        var rows = new[] { Row(port: 50001, pid: PortOwnership.HttpSysPid) };
        Assert.True(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_HttpSysOnBothFamilies_Accepts()
    {
        // HttpListener typically registers under both AF_INET and AF_INET6;
        // our enumeration unions the two tables. Both showing as PID 4 is
        // still a clean HTTP.SYS attribution.
        var rows = new[]
        {
            Row(port: 50001, pid: PortOwnership.HttpSysPid),
            Row(port: 50001, pid: PortOwnership.HttpSysPid),
        };
        Assert.True(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_HttpSysPlusCompetingUserMode_RejectsAsAmbiguous()
    {
        // Defense-in-depth: if some OTHER user-mode pid is also listening on
        // the same port, the lockfile claim is ambiguous. The HTTP.SYS row
        // alone is no longer a clean attribution — refuse to authenticate.
        var rows = new[]
        {
            Row(port: 50001, pid: PortOwnership.HttpSysPid),
            Row(port: 50001, pid: 99999),
        };
        Assert.False(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_DirectUserModeOwnerWinsOverHttpSys()
    {
        // Direct match short-circuits even when HTTP.SYS also holds the
        // socket on a different family.
        var rows = new[]
        {
            Row(port: 50001, pid: PortOwnership.HttpSysPid),
            Row(port: 50001, pid: 12345),
        };
        Assert.True(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_OtherPortsIgnored()
    {
        // Rows for other ports never contribute — the predicate only considers
        // rows where row.LocalPort == port.
        var rows = new[]
        {
            Row(port: 50000, pid: 99999),                   // unrelated port
            Row(port: 50001, pid: PortOwnership.HttpSysPid),
            Row(port: 50002, pid: 99999),                   // unrelated port
        };
        Assert.True(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: true));
    }

    [Fact]
    public void Match_PartialEnumeration_HttpSysOnly_RejectedAsUnsafe()
    {
        // PR #128 review (Copilot): if the AF_INET or AF_INET6 TCP-table
        // query failed, the rows we have are a partial view. Accepting an
        // HTTP.SYS-only signature on the visible family would let a
        // competing user-mode listener on the unseen family go undetected
        // — a same-user spoof opportunity. Direct match still works on
        // partial data; HTTP.SYS-only does not.
        var rows = new[]
        {
            Row(port: 50001, pid: PortOwnership.HttpSysPid),
        };
        Assert.False(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: false));
    }

    [Fact]
    public void Match_PartialEnumeration_DirectMatch_StillAccepted()
    {
        // Even if one address-family query failed, a direct row attributing
        // to the lockfile pid is unambiguous — accept.
        var rows = new[]
        {
            Row(port: 50001, pid: 12345),
        };
        Assert.True(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: false));
    }

    [Fact]
    public void Match_PartialEnumeration_DirectMatchAlongsideHttpSys_StillAccepted()
    {
        // Mixed but the lockfile pid is among the rows we did get back —
        // direct match wins regardless of enumeration completeness.
        var rows = new[]
        {
            Row(port: 50001, pid: PortOwnership.HttpSysPid),
            Row(port: 50001, pid: 12345),
        };
        Assert.True(PortOwnership.MatchListener(rows, port: 50001, pid: 12345, enumerationComplete: false));
    }

    [Fact]
    public void IsPortOwnedBy_RejectsInvalidArgs()
    {
        Assert.False(PortOwnership.IsPortOwnedBy(port: 0, pid: 1));
        Assert.False(PortOwnership.IsPortOwnedBy(port: 70000, pid: 1));
        Assert.False(PortOwnership.IsPortOwnedBy(port: 5000, pid: 0));
        Assert.False(PortOwnership.IsPortOwnedBy(port: 5000, pid: -1));
    }
}
