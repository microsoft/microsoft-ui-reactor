using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E fixture for issue #679 (a) — the ListView <c>OnItemClick</c> "once-fire" guard.
///
/// <para>The production <c>ListViewHandler</c> subscribes the native
/// <c>ListView.ItemClick</c> event exactly once (Mount when <c>OnItemClick</c> is present,
/// or the Update transition <c>null → non-null</c>) and dispatches through the current
/// element via <c>GetElementTag</c>. A handler that stays present but changes identity every
/// render (the idiomatic <c>idx =&gt; setState(...)</c> lambda) must therefore NOT cause a
/// re-subscribe. If the reconciler regressed to <c>ItemClick +=</c> on every render, a single
/// real pointer click would invoke the callback N+1 times.</para>
///
/// <para>This scene exposes that as UIA-readable state: an authoritative <see cref="Ref{T}"/>
/// counter (incremented on every dispatch, so a double-fire can't be masked by state
/// batching) mirrored into <c>Fires</c>, plus the clicked <c>LastIndex</c>. "Rerender" forces
/// re-renders with the handler continuously present (memoized items → no ItemsSource rebuild);
/// "ShuffleItems" changes the items array (the #495 rebuild path). The E2E test drives real
/// pointer input and asserts the callback fires EXACTLY once with the correct index.</para>
/// </summary>
internal static class ItemClickE2EFixtures
{
    private static readonly string[] LabelsA = ["Alpha", "Bravo", "Charlie", "Delta"];
    private static readonly string[] LabelsB = ["Delta", "Charlie", "Bravo", "Alpha"];

    internal class OnceFireComponent : Component
    {
        public override Element Render()
        {
            var (rev, setRev) = UseState(0);
            var (shuffleGen, setShuffleGen) = UseState(0);
            var (firesDisplay, setFiresDisplay) = UseState(0);
            var (lastIndex, setLastIndex) = UseState(-1);

            // Authoritative fire count. A native double-subscription fires both handlers
            // synchronously for one click, so counting on a Ref (not just state) guarantees
            // the second dispatch is observed even if the two setState calls collapse.
            var fires = UseRef(0);

            // Memoize the item elements so a plain "Rerender" keeps the SAME Items array
            // reference (ListViewHandler.Update sees ReferenceEquals → no ItemsSource
            // rebuild), isolating the "handler present across re-render" path. "ShuffleItems"
            // bumps shuffleGen → a new array → the items-change rebuild path.
            var rows = UseMemo(() =>
            {
                var labels = shuffleGen % 2 == 0 ? LabelsA : LabelsB;
                return labels
                    .Select((label, i) => (Element)TextBlock($"{i}: {label}").AutomationId($"LvItem_{i}"))
                    .ToArray();
            }, shuffleGen);

            return VStack(8,
                TextBlock($"rev: {rev} shuffle: {shuffleGen}").AutomationId("LvRev"),
                HStack(8,
                    Button("Rerender", () => setRev(rev + 1)).AutomationId("LvRerenderBtn"),
                    Button("ShuffleItems", () => setShuffleGen(shuffleGen + 1)).AutomationId("LvShuffleBtn")
                ),

                // NEW lambda every render (identity changes) while OnItemClick stays non-null.
                ListView(rows)
                    .ItemClick(idx =>
                    {
                        fires.Current += 1;
                        setFiresDisplay(fires.Current);
                        setLastIndex(idx);
                    })
                    .Height(220),

                TextBlock($"Fires: {firesDisplay}").AutomationId("LvFires"),
                TextBlock($"LastIndex: {lastIndex}").AutomationId("LvLastIndex")
            );
        }
    }

    internal static Element OnceFire(RenderContext ctx) => Component<OnceFireComponent>();
}
