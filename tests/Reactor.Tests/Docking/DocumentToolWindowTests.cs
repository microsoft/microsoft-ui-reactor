using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for the Phase-2 subclasses of <see cref="DockableContent"/> —
/// <see cref="Document"/>, <see cref="ToolWindow"/>, and the typed
/// <see cref="Document{TState}"/>. Spec 045 §5.3.1, §5.3.2, §5.3.8;
/// tracking §2.8, §2.9, §2.14.
/// </summary>
public class DocumentToolWindowTests
{
    // ── Document defaults (spec §5.3.1, §5.3.8) ──────────────────────────

    [Fact]
    public void Document_DefaultPermissions_ClosableNotPinnable()
    {
        var doc = new Document { Title = "MainView.xaml", Key = "main" };
        Assert.True(doc.CanClose);    // documents close (X button)
        Assert.False(doc.CanPin);     // documents don't pin to sides
        Assert.False(doc.CanDockAsToolWindow);
        Assert.True(doc.CanFloat);    // base default
        Assert.True(doc.CanMove);     // base default
    }

    [Fact]
    public void Document_IsADockableContent()
    {
        DockableContent dc = new Document { Title = "X" };
        Assert.IsType<Document>(dc);
    }

    // ── ToolWindow defaults (spec §5.3.1, §5.3.8) ────────────────────────

    [Fact]
    public void ToolWindow_DefaultPermissions_HideablePinnable()
    {
        var tw = new ToolWindow { Title = "Solution Explorer", Key = "se" };
        Assert.False(tw.CanClose);      // X button hides, not closes
        Assert.True(tw.CanPin);
        Assert.True(tw.CanHide);
        Assert.True(tw.CanAutoHide);
        Assert.True(tw.CanDockAsDocument);
        Assert.True(tw.CanFloat);
        Assert.True(tw.CanMove);
    }

    [Fact]
    public void ToolWindow_IsADockableContent()
    {
        DockableContent dc = new ToolWindow { Title = "Output" };
        Assert.IsType<ToolWindow>(dc);
    }

    // ── Document<TState> (spec §5.3.2) ───────────────────────────────────

    private sealed record EditorState(int ScrollOffset, (int Line, int Col) Caret);

    [Fact]
    public void DocumentTState_CarriesTypedState()
    {
        var state = new EditorState(ScrollOffset: 1024, Caret: (42, 7));
        var doc = new Document<EditorState>
        {
            Title = "MainView.xaml",
            Key   = "file:main",
            State = state,
        };
        Assert.Equal(state, doc.State);
        Assert.True(doc.CanClose);
        Assert.False(doc.CanPin);
    }

    [Fact]
    public void DocumentTState_NullableState_AllowedAtDefault()
    {
        var doc = new Document<EditorState> { Title = "Blank", Key = "k" };
        Assert.Null(doc.State);
    }

    // ── With-expression carries new fields (spec §5.3 records) ───────────

    [Fact]
    public void Document_With_PreservesOverriddenDefaults()
    {
        var a = new Document { Title = "Foo", Key = "foo" };
        var b = a with { Title = "Bar" };

        Assert.True(b.CanClose);
        Assert.Equal("Bar", b.Title);
        Assert.Equal("foo", b.Key);
    }

    [Fact]
    public void ToolWindow_With_CanOverridePermissions()
    {
        var a = new ToolWindow { Title = "Output", Key = "out" };
        var b = a with { CanHide = false };

        Assert.False(b.CanHide);
        Assert.True(b.CanAutoHide); // unaffected
        Assert.Equal("out", b.Key);
    }

    // ── Per-pane permission flags on the base (spec §5.3.8) ──────────────

    [Fact]
    public void DockableContent_CanFloatDefault_IsTrue()
    {
        var pane = new DockableContent("X");
        Assert.True(pane.CanFloat);
    }

    [Fact]
    public void DockableContent_CanMoveDefault_IsTrue()
    {
        var pane = new DockableContent("X");
        Assert.True(pane.CanMove);
    }

    [Fact]
    public void DockableContent_CanFloat_CanBeDisabled()
    {
        var pane = new DockableContent("X") { CanFloat = false };
        Assert.False(pane.CanFloat);
    }

    [Fact]
    public void DockableContent_CanMove_CanBeDisabled()
    {
        var pane = new DockableContent("X") { CanMove = false };
        Assert.False(pane.CanMove);
    }
}
