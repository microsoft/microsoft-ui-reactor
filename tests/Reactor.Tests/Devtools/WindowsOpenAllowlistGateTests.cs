using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// W-3 hardening (threat model 2026-05-08): the windows.open allowlist gate
/// lives in the framework's MCP tool layer, not in each host's
/// <c>OpenWindowByAllowlistedComponent</c> callback, so a host implementation
/// cannot silently weaken the gate. These tests pin the helper's contract.
/// The live wire-up of the gate inside <c>Register_WindowsOpen</c> is exercised
/// by the Phase-9 selftest matrix; here we cover the helper in isolation.
/// </summary>
public class WindowsOpenAllowlistGateTests
{
    private static readonly string[] s_allowed = { "DemoOne", "DemoTwo", "ContextDemo" };

    [Fact]
    public void Allowed_Component_Passes()
    {
        // No exception means the gate let it through — there is nothing to
        // assert positively (the helper is void on success).
        DevtoolsTools.EnsureComponentAllowlisted(s_allowed, "DemoOne");
    }

    [Fact]
    public void Allowlist_Match_Is_Ordinal_Ignore_Case()
    {
        // switchComponent's existing comparison is ordinal-ignore-case; this
        // gate must agree so a name that switchComponent would accept also
        // works for windows.open.
        DevtoolsTools.EnsureComponentAllowlisted(s_allowed, "demoone");
        DevtoolsTools.EnsureComponentAllowlisted(s_allowed, "DEMOTWO");
    }

    [Fact]
    public void Disallowed_Component_Throws_Unknown_Component()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            DevtoolsTools.EnsureComponentAllowlisted(s_allowed, "EvilComponent"));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);
        Assert.NotNull(ex.Payload);

        // The payload shape (`{ code = "unknown-component", available = [...] }`)
        // is what agents rely on to recover; lock it in via reflection so the
        // anonymous-type contract doesn't drift unnoticed.
        var payloadType = ex.Payload!.GetType();
        var codeProp = payloadType.GetProperty("code");
        var availProp = payloadType.GetProperty("available");
        Assert.NotNull(codeProp);
        Assert.NotNull(availProp);
        Assert.Equal("unknown-component", codeProp!.GetValue(ex.Payload));
        Assert.Equal(s_allowed, (string[])availProp!.GetValue(ex.Payload)!);
    }

    [Fact]
    public void Empty_Allowlist_Rejects_Any_Component()
    {
        // Pin the boundary case: an empty allowlist denies everything rather
        // than (e.g.) defaulting to allow-all. Defends against a bug where a
        // host wires an uninitialised list and accidentally opens the gate.
        Assert.Throws<McpToolException>(() =>
            DevtoolsTools.EnsureComponentAllowlisted(global::System.Array.Empty<string>(), "Anything"));
    }

    [Fact]
    public void Null_Component_Throws_ArgumentNullException()
    {
        Assert.Throws<global::System.ArgumentNullException>(() =>
            DevtoolsTools.EnsureComponentAllowlisted(s_allowed, null!));
    }

    [Fact]
    public void Null_Allowlist_Throws_ArgumentNullException()
    {
        Assert.Throws<global::System.ArgumentNullException>(() =>
            DevtoolsTools.EnsureComponentAllowlisted(null!, "DemoOne"));
    }
}
