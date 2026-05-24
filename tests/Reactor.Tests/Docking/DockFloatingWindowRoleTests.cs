using System;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.1 — role inference for the floating window's internal
/// tab group. Document-only panes → <see cref="DockGroupRole.DocumentArea"/>;
/// any non-Document → <see cref="DockGroupRole.General"/>. Floating
/// windows never get <see cref="DockGroupRole.ToolWindowStrip"/> (no
/// edges).
/// </summary>
public class DockFloatingWindowRoleTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key) => new() { Title = key, Key = key };
    private static DockableContent Untyped(string key) => new(Title: key, Key: key);

    [Fact]
    public void Empty_DefaultsToGeneral()
    {
        Assert.Equal(DockGroupRole.General, DockFloatingWindow.InferFloatingGroupRole(Array.Empty<DockableContent>()));
    }

    [Fact]
    public void SingleDocument_IsDocumentArea()
    {
        Assert.Equal(DockGroupRole.DocumentArea,
            DockFloatingWindow.InferFloatingGroupRole(new DockableContent[] { Doc("d1") }));
    }

    [Fact]
    public void MultipleDocuments_IsDocumentArea()
    {
        Assert.Equal(DockGroupRole.DocumentArea,
            DockFloatingWindow.InferFloatingGroupRole(new DockableContent[] { Doc("d1"), Doc("d2") }));
    }

    [Fact]
    public void SingleToolWindow_IsGeneral_NotStrip()
    {
        // Spec §6.1 — never promote to ToolWindowStrip in floating context.
        Assert.Equal(DockGroupRole.General,
            DockFloatingWindow.InferFloatingGroupRole(new DockableContent[] { Tool("t1") }));
    }

    [Fact]
    public void Untyped_IsGeneral()
    {
        Assert.Equal(DockGroupRole.General,
            DockFloatingWindow.InferFloatingGroupRole(new DockableContent[] { Untyped("u") }));
    }

    [Fact]
    public void Mixed_DocumentAndToolWindow_IsGeneral()
    {
        Assert.Equal(DockGroupRole.General,
            DockFloatingWindow.InferFloatingGroupRole(new DockableContent[] { Doc("d1"), Tool("t1") }));
    }
}
