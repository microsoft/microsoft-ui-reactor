using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Its own file: see LateNullRootHostMount (ReactorSource_NullRenderDropsLateConflictRootHooks
// makes the file holding this Mount call unattributable).
internal static class LateNullRootControlMount
{
    internal static Action<bool>? SetShown;

    public static void Mount(ReactorHostControl control) => control.Mount(ctx =>
    {
        var (shown, set) = ctx.UseState(true);
        SetShown = set;
        return shown ? TextBlock("late-null-root-control") : null!;
    });
}
