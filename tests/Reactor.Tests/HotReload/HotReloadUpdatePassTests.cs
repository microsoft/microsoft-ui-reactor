using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.HotReload;

/// <summary>
/// Unit tests for the tree-wide hot-reload pass primitive
/// (<see cref="HotReloadService.BeginUpdatePass"/> /
/// <see cref="HotReloadService.WithinUpdatePass"/>) added in spec 049 Phase 1.
///
/// The full reconciler child-recovery behavior (a non-root component whose
/// hooks were reordered recovers in one pass) needs a live WinUI control tree
/// and is covered by a selftest fixture — see
/// <c>tests/Reactor.AppTests.Host/SelfTest/Fixtures</c>. These tests pin the
/// pass flag's lifecycle, which is the contract the reconciler reads.
/// </summary>
[Collection("HotReload")]
public class HotReloadUpdatePassTests
{
    [Fact]
    public void WithinUpdatePass_Is_False_By_Default()
    {
        Assert.False(HotReloadService.WithinUpdatePass);
    }

    [Fact]
    public void BeginUpdatePass_Sets_Flag_For_Scope_Duration()
    {
        Assert.False(HotReloadService.WithinUpdatePass);

        using (HotReloadService.BeginUpdatePass())
        {
            Assert.True(HotReloadService.WithinUpdatePass);
        }

        Assert.False(HotReloadService.WithinUpdatePass);
    }

    [Fact]
    public void BeginUpdatePass_Clears_Flag_On_Exception_Unwind()
    {
        Assert.False(HotReloadService.WithinUpdatePass);

        try
        {
            using (HotReloadService.BeginUpdatePass())
            {
                Assert.True(HotReloadService.WithinUpdatePass);
                throw new InvalidOperationException("boom");
            }
        }
        catch (InvalidOperationException)
        {
            // The using's dispose runs during stack unwind.
        }

        Assert.False(HotReloadService.WithinUpdatePass);
    }

    [Fact]
    public void BeginUpdatePass_Clears_Flag_On_Early_Return()
    {
        Assert.False(HotReloadService.WithinUpdatePass);
        EarlyReturn();
        Assert.False(HotReloadService.WithinUpdatePass);

        static void EarlyReturn()
        {
            using (HotReloadService.BeginUpdatePass())
            {
                Assert.True(HotReloadService.WithinUpdatePass);
                return;
            }
        }
    }

    [Fact]
    public void BeginUpdatePass_Restores_False_After_Disposing_Last_Scope()
    {
        // A second pass after the first has been disposed observes a clean
        // slate — the flag does not "stick" across passes.
        using (HotReloadService.BeginUpdatePass())
        {
            Assert.True(HotReloadService.WithinUpdatePass);
        }
        Assert.False(HotReloadService.WithinUpdatePass);

        using (HotReloadService.BeginUpdatePass())
        {
            Assert.True(HotReloadService.WithinUpdatePass);
        }
        Assert.False(HotReloadService.WithinUpdatePass);
    }
}
