using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Tests.Spec047.V1Protocol.Ports;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Spec047.V1Protocol;

/// <summary>
/// Spec 047 §14 Phase 1 (1.1) — feature-flag transport tests.
///
/// Validates the three transport rules:
///   1. Default Reconciler has UseV1Protocol == false.
///   2. Per-instance ctor flag is honored.
///   3. AppContext switch "Reactor.UseV1Protocol" is honored as fallback.
///   4. Ctor flag wins over AppContext switch when both are set.
///
/// TODO(1.11): when ToggleSwitch is ported, add the "structurally identical
/// control tree" parity test from the spec task — mount the same element
/// type with the flag ON and OFF and diff DP values + child counts.
///
/// <para>Pinned to <see cref="Spec047V1FlagCollection"/> — these tests mutate
/// the process-wide AppContext switch <c>Reactor.UseV1Protocol</c>. Without
/// the collection attribute, xUnit's default per-class parallelism would
/// race them against each other and against
/// <see cref="Ports.V1OnRegistrationTests"/>, which already serializes on
/// the same collection.</para>
/// </summary>
[Collection(nameof(Spec047V1FlagCollection))]
public class V1FeatureFlagTests
{
    private const string SwitchName = "Reactor.UseV1Protocol";

    [Fact]
    public void Default_Reconciler_Has_V1Protocol_Off()
    {
        // Save and clear the switch so this test isn't affected by global state.
        // AppContext switches can't be removed, only set; the contract is that
        // unset == false on the consumer side.
        AppContext.SetSwitch(SwitchName, false);

        var rec = new Reconciler();
        Assert.False(rec.UseV1Protocol);
    }

    [Fact]
    public void Ctor_Flag_True_Is_Honored()
    {
        AppContext.SetSwitch(SwitchName, false);

        var rec = new Reconciler(logger: null, useV1Protocol: true);
        Assert.True(rec.UseV1Protocol);
    }

    [Fact]
    public void Ctor_Flag_False_Is_Honored()
    {
        AppContext.SetSwitch(SwitchName, true);

        var rec = new Reconciler(logger: null, useV1Protocol: false);
        Assert.False(rec.UseV1Protocol);
    }

    [Fact]
    public void AppContext_Switch_True_Is_Honored_When_Ctor_Flag_Omitted()
    {
        AppContext.SetSwitch(SwitchName, true);
        try
        {
            var rec = new Reconciler();
            Assert.True(rec.UseV1Protocol);
        }
        finally
        {
            AppContext.SetSwitch(SwitchName, false);
        }
    }

    [Fact]
    public void Ctor_Flag_Overrides_AppContext_Switch()
    {
        // Switch says ON, ctor says OFF — ctor wins.
        AppContext.SetSwitch(SwitchName, true);
        try
        {
            var rec = new Reconciler(logger: null, useV1Protocol: false);
            Assert.False(rec.UseV1Protocol);
        }
        finally
        {
            AppContext.SetSwitch(SwitchName, false);
        }
    }
}
