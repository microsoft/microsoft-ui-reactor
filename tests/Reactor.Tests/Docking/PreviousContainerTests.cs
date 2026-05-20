using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for <see cref="PreviousContainerTracker"/> — the
/// "show-panel-where-you-left-it" state for spec 045 §5.3.9 (tracking §2.15).
/// </summary>
public class PreviousContainerTests
{
    [Fact]
    public void Set_RecordsContainer_GetPreviousReadsBack()
    {
        var pane = new ToolWindow { Title = "X", Key = "x" };
        var container = new DockTabGroup(new[] { pane });

        PreviousContainerTracker.Set(pane, container);

        Assert.Same(container, PreviousContainerTracker.GetPrevious(pane));
    }

    [Fact]
    public void GetPrevious_BeforeSet_ReturnsNull()
    {
        var pane = new ToolWindow { Title = "X", Key = "x" };
        Assert.Null(PreviousContainerTracker.GetPrevious(pane));
    }

    [Fact]
    public void Set_Twice_OverwritesContainer()
    {
        var pane = new ToolWindow { Title = "X", Key = "x" };
        var firstGroup = new DockTabGroup(new[] { pane });
        var secondGroup = new DockTabGroup(new[] { pane });

        PreviousContainerTracker.Set(pane, firstGroup);
        PreviousContainerTracker.Set(pane, secondGroup);

        Assert.Same(secondGroup, PreviousContainerTracker.GetPrevious(pane));
    }

    [Fact]
    public void Clear_RemovesEntry()
    {
        var pane = new ToolWindow { Title = "X", Key = "x" };
        var container = new DockTabGroup(new[] { pane });

        PreviousContainerTracker.Set(pane, container);
        PreviousContainerTracker.Clear(pane);

        Assert.Null(PreviousContainerTracker.GetPrevious(pane));
    }

    [Fact]
    public void DistinctPaneInstances_HaveIndependentTracking()
    {
        // Same Key, different instances — the tracker uses ConditionalWeakTable
        // (reference-equality keys), so each instance has its own history.
        var paneA = new ToolWindow { Title = "X", Key = "shared" };
        var paneB = new ToolWindow { Title = "X", Key = "shared" };
        var groupA = new DockTabGroup(new[] { paneA });

        PreviousContainerTracker.Set(paneA, groupA);

        Assert.Same(groupA, PreviousContainerTracker.GetPrevious(paneA));
        Assert.Null(PreviousContainerTracker.GetPrevious(paneB));
    }

    [Fact]
    public void Set_RejectsNullArguments()
    {
        var pane = new ToolWindow { Title = "X", Key = "x" };
        var container = new DockTabGroup(new[] { pane });
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.Set(null!, container));
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.Set(pane, null!));
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.GetPrevious(null!));
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.Clear(null!));
    }

    [Fact]
    public void HideShowCycle_PreservesContainerIdentity()
    {
        // Simulate the spec §5.3.9 scenario: pane was in group A; hidden;
        // re-shown should land back in group A (not the default insertion).
        var pane = new ToolWindow { Title = "Output", Key = "out" };
        var groupA = new DockTabGroup(new[] { pane });
        var groupB = new DockTabGroup(Array.Empty<DockableContent>()); // default insertion target

        // Host records the container when the pane is hidden.
        PreviousContainerTracker.Set(pane, groupA);

        // Some time later, app shows the pane. Host queries:
        var remembered = PreviousContainerTracker.GetPrevious(pane);

        Assert.Same(groupA, remembered);
        Assert.NotSame(groupB, remembered);
    }
}
