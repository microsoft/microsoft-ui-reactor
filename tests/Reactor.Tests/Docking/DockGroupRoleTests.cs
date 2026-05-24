using System;
using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.1 — <see cref="DockGroupRole"/> + the new
/// <see cref="DockTabGroup.Role"/> positional parameter. Verifies record
/// equality is unaffected by default vs. explicit <see cref="DockGroupRole.General"/>
/// and that <c>with</c>-expressions preserve the role.
/// </summary>
public class DockGroupRoleTests
{
    [Fact]
    public void DefaultRole_IsGeneral()
    {
        var grp = new DockTabGroup(Array.Empty<DockableContent>());
        Assert.Equal(DockGroupRole.General, grp.Role);
    }

    [Fact]
    public void ExplicitGeneral_EqualsDefault()
    {
        var implicitDefault = new DockTabGroup(Array.Empty<DockableContent>());
        var explicitGeneral = new DockTabGroup(
            Array.Empty<DockableContent>(),
            Role: DockGroupRole.General);
        Assert.Equal(implicitDefault, explicitGeneral);
    }

    [Fact]
    public void Role_RoundTripsThroughWith()
    {
        var initial = new DockTabGroup(Array.Empty<DockableContent>());
        var mutated = initial with { Role = DockGroupRole.DocumentArea };
        Assert.Equal(DockGroupRole.DocumentArea, mutated.Role);
        // Original is immutable.
        Assert.Equal(DockGroupRole.General, initial.Role);
    }

    [Fact]
    public void DifferentRoles_AreNotEqual()
    {
        var a = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        var b = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SourceCompat_PositionalConstructorWithoutRole_DefaultsGeneral()
    {
        // Mirrors a P1/P2 caller that passes every positional arg up to
        // and including TabChrome but omits Role. Must still compile and
        // default Role to General.
        var grp = new DockTabGroup(
            Documents: Array.Empty<DockableContent>(),
            TabPosition: TabPosition.Bottom,
            CompactTabs: true,
            ShowWhenEmpty: false,
            SelectedIndex: -1,
            Width: 320,
            Height: null,
            TabChrome: TabChrome.Flat);
        Assert.Equal(DockGroupRole.General, grp.Role);
    }

    [Fact]
    public void AllThreeRoleValues_ExistAndAreDistinct()
    {
        Assert.NotEqual((int)DockGroupRole.General, (int)DockGroupRole.DocumentArea);
        Assert.NotEqual((int)DockGroupRole.DocumentArea, (int)DockGroupRole.ToolWindowStrip);
        Assert.NotEqual((int)DockGroupRole.General, (int)DockGroupRole.ToolWindowStrip);
    }
}
