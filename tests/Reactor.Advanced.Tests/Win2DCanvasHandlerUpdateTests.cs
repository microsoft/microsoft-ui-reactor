using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

/// <summary>
/// Handler update behavior requires live Win2D <c>CanvasControl</c> instances,
/// which require WinUI activation/dispatcher state that is intentionally absent
/// from this headless xUnit project. Phase 3 selftest fixtures cover these
/// scenarios in a real WinUI window.
/// </summary>
public sealed class Win2DCanvasHandlerUpdateTests
{
    [Fact(Skip = "Requires live Win2D CanvasControl activation; covered by Phase 3 selftest fixtures.")]
    public void RedrawKey_And_ClearColor_Diffs_Are_Covered_By_Selftests()
    {
    }
}
