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
/// <c>ListView.ItemClick</c> event exactly once (unconditionally at Mount) and dispatches
/// through the current element via <c>GetElementTag</c>. A handler that stays present but
/// changes identity every render (the idiomatic <c>idx =&gt; setState(...)</c> lambda) must
/// therefore NOT cause a re-subscribe. If the reconciler regressed to <c>ItemClick +=</c> on
/// every render, a single real pointer click would invoke the callback N+1 times.</para>
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

    // Shared row labels for the #779 toggle fixtures below.
    private static readonly string[] ToggleLabels = ["Alpha", "Bravo", "Charlie", "Delta"];

    /// <summary>
    /// E2E fixture for issue #779 — the ListView <c>OnItemClick</c> "toggle-path"
    /// double-subscribe guard (<see cref="GridViewToggleComponent"/> is the symmetric case).
    ///
    /// <para>ListView/GridView update <b>in place</b>, so toggling <c>OnItemClick</c>
    /// off (present→null) then on (null→present) used to leave the Mount-time native
    /// <c>ItemClick</c> subscription live AND add a second one on the null→present
    /// Update, so a single real click dispatched the callback twice (and stacked
    /// another handler on every further off→on cycle). The fix subscribes
    /// <c>ItemClick</c> unconditionally at Mount and never re-subscribes on Update.</para>
    ///
    /// <para>This scene toggles a real <c>OnItemClick</c> handler with a button
    /// (<c>hasHandler ? (idx =&gt; fires++) : null</c>) and exposes an authoritative
    /// <see cref="Ref{T}"/> fire counter as UIA-readable <c>Fires</c> text (a Ref, not
    /// just state, so a synchronous double-dispatch can't be masked by state batching).
    /// The E2E test starts ON → toggles OFF → toggles ON → real-clicks a row and asserts
    /// the callback fired EXACTLY once.</para>
    /// </summary>
    internal class ListViewToggleComponent : Component
    {
        public override Element Render()
        {
            var (hasHandler, setHasHandler) = UseState(true);
            var (firesDisplay, setFiresDisplay) = UseState(0);
            var (lastIndex, setLastIndex) = UseState(-1);

            // Authoritative fire count — a native double-subscription fires both
            // handlers synchronously for one click, so counting on a Ref guarantees
            // the second dispatch is observed even if the two setState calls collapse.
            var fires = UseRef(0);

            // Memoize the rows so Items stays reference-stable across toggle re-renders
            // (ListViewHandler.Update sees ReferenceEquals → no ItemsSource rebuild),
            // isolating the pure OnItemClick null↔present toggle path — exactly the
            // in-place-update sequence that leaked the second subscription.
            var rows = UseMemo(() => ToggleLabels
                .Select((label, i) => (Element)TextBlock($"{i}: {label}").AutomationId($"LvToggleItem_{i}"))
                .ToArray(), 0);

            // The idiomatic conditional-handler pattern: OnItemClick is a real handler
            // while enabled, null while disabled. Toggling it drives the present→null→present
            // in-place Update that used to leak a second native subscription.
            Action<int>? onItemClick = hasHandler
                ? idx =>
                {
                    fires.Current += 1;
                    setFiresDisplay(fires.Current);
                    setLastIndex(idx);
                }
                : null;

            return VStack(8,
                TextBlock($"HasHandler: {hasHandler}").AutomationId("LvToggleState"),
                Button("ToggleHandler", () => setHasHandler(!hasHandler)).AutomationId("LvToggleBtn"),

                ListView(rows)
                    .ItemClick(onItemClick)
                    .Height(220),

                TextBlock($"Fires: {firesDisplay}").AutomationId("LvToggleFires"),
                TextBlock($"LastIndex: {lastIndex}").AutomationId("LvToggleLastIndex")
            );
        }
    }

    internal class GridViewToggleComponent : Component
    {
        public override Element Render()
        {
            var (hasHandler, setHasHandler) = UseState(true);
            var (firesDisplay, setFiresDisplay) = UseState(0);
            var (lastIndex, setLastIndex) = UseState(-1);
            var fires = UseRef(0);

            var rows = UseMemo(() => ToggleLabels
                .Select((label, i) => (Element)TextBlock($"{i}: {label}").AutomationId($"GvToggleItem_{i}"))
                .ToArray(), 0);

            Action<int>? onItemClick = hasHandler
                ? idx =>
                {
                    fires.Current += 1;
                    setFiresDisplay(fires.Current);
                    setLastIndex(idx);
                }
                : null;

            return VStack(8,
                TextBlock($"HasHandler: {hasHandler}").AutomationId("GvToggleState"),
                Button("ToggleHandler", () => setHasHandler(!hasHandler)).AutomationId("GvToggleBtn"),

                GridView(rows)
                    .ItemClick(onItemClick)
                    .Height(220),

                TextBlock($"Fires: {firesDisplay}").AutomationId("GvToggleFires"),
                TextBlock($"LastIndex: {lastIndex}").AutomationId("GvToggleLastIndex")
            );
        }
    }

    internal static Element ToggleListView(RenderContext ctx) => Component<ListViewToggleComponent>();

    internal static Element ToggleGridView(RenderContext ctx) => Component<GridViewToggleComponent>();
}
