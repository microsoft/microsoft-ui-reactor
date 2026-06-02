using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

/// <summary>
/// Handler update behavior requires live Win2D <c>CanvasAnimatedControl</c>
/// instances, which require WinUI activation/dispatcher state that is
/// intentionally absent from this headless xUnit project. Phase 3 selftest
/// fixtures cover these scenarios in a real WinUI window.
/// </summary>
public sealed class Win2DAnimatedCanvasHandlerUpdateTests
{
    [Fact(Skip = "Requires live Win2D CanvasAnimatedControl activation; covered by Phase 3 selftest fixtures.")]
    public void Paused_TargetElapsedTime_And_DrawState_Diffs_Are_Covered_By_Selftests()
    {
    }
}
