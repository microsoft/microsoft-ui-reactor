using System.Collections.Immutable;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// Spec 045 §2.6 — fixtures that exercise the tear-off pipeline under
/// real WinAppDriver / Appium pointer input. Each fixture exposes UIA-
/// queryable state (AutomationId'd <see cref="TextBlock"/>s that mirror
/// the live layout snapshot + per-pane content state) so the matching
/// tests in <c>tests/Reactor.AppTests/Tests/DockingTearOffE2ETests.cs</c>
/// can assert against the observable end state without poking at
/// process internals.
/// </summary>
internal static class DockingTearOffE2EFixtures
{
    /// <summary>
    /// Three-tab single-group host with a large empty "drop-outside zone"
    /// below. Each pane has its own TextBox + state TextBlock so a test
    /// can verify that controlled-input state survives the layout
    /// mutation. A summary TextBlock exposes the location of every pane
    /// (host or floating) via UIA so the test can string-match the
    /// post-drag state without traversing the visual tree.
    /// </summary>
    internal class TearOffFlowComponent : Component
    {
        public override Element Render()
        {
            // Per-pane text content — controlled state so the test can
            // observe whether typed values survive across tear-off /
            // re-dock by reading the *_State TextBlocks.
            var (textA, setTextA) = UseState(string.Empty);
            var (textB, setTextB) = UseState(string.Empty);
            var (textC, setTextC) = UseState(string.Empty);

            // Pane-location tracker: which panes are currently floating
            // (the rest are in the host layout). Updated by the manager's
            // OnContentFloated / OnContentDocked event surface. UseReducer
            // gives us the prev-based update pattern so concurrent event
            // fires can't clobber each other's state (rare in practice
            // but cheap insurance).
            var (floatingKeys, updateFloatingKeys) =
                UseReducer<ImmutableHashSet<string>>(ImmutableHashSet<string>.Empty);

            // Floating window lifecycle counter — the count of OPEN
            // floating windows owned by this manager. Test reads this to
            // verify tear-off opened a new window and re-dock closed it.
            var (floatingWindowCount, updateFloatingWindowCount) = UseReducer(0);

            // Diagnostic counters surfaced to UIA so a failing E2E test
            // can tell us WHICH stage of the pipeline didn't fire.
            //   - floatingFireCount: OnContentFloating events (gate
            //     check — fires before any layout mutation).
            //   - floatedFireCount: OnContentFloated events (post
            //     mutation — only fires if tear-off committed).
            //   - dockedFireCount: OnContentDocked events (re-dock).
            // If a test's summary doesn't change after a drag, these
            // counters reveal whether the press hook + threshold + gate
            // ran at all (vs the layout mutation failing downstream).
            var (floatingFireCount, bumpFloatingFire) = UseReducer(0);
            var (floatedFireCount, bumpFloatedFire) = UseReducer(0);
            var (dockedFireCount, bumpDockedFire) = UseReducer(0);

            // Tear-off pipeline diagnostic ring buffer — keep the LAST
            // few DiagnosticSink messages so a failing E2E test can see
            // exactly which stage of the pipeline fired (Press / Move /
            // threshold-crossed / Tracker.Start etc.). The sink is a
            // process-wide static; we chain via the saved previous so
            // the host's wiring (which also subscribes) is preserved.
            var (lastTrace, updateLastTrace) =
                UseReducer<ImmutableList<string>>(ImmutableList<string>.Empty);
            UseEffect(() =>
            {
                var previous = DockTabTearOff.DiagnosticSink;
                DockTabTearOff.DiagnosticSink = msg =>
                {
                    previous?.Invoke(msg);
                    updateLastTrace(prev =>
                    {
                        var next = prev.Add(msg);
                        return next.Count > 6 ? next.RemoveRange(0, next.Count - 6) : next;
                    });
                };
                return () => { DockTabTearOff.DiagnosticSink = previous; };
            });

            DockableContent BuildPane(string key, string title, string current, Action<string> setter, string idPrefix) =>
                new(Title: title, Key: key, CanClose: true,
                    Content: VStack(6,
                        TextBox(current, setter, placeholderText: $"{title} input")
                            .AutomationId($"{idPrefix}_Input"),
                        TextBlock($"{title} state: {current}").AutomationId($"{idPrefix}_State")
                    ).Padding(12));

            var paneA = BuildPane("tearoff:a", "EditorA", textA, setTextA, "EditorA");
            var paneB = BuildPane("tearoff:b", "EditorB", textB, setTextB, "EditorB");
            var paneC = BuildPane("tearoff:c", "EditorC", textC, setTextC, "EditorC");

            // Summary string the test reads via UIA. Format:
            //   "host:A,B,C  float:0"
            // Encoded compactly so a single string-match covers the
            // observable end state.
            var hostKeys = new[] { "tearoff:a", "tearoff:b", "tearoff:c" }
                .Where(k => !floatingKeys.Contains(k))
                .Select(k => k switch
                {
                    "tearoff:a" => "A",
                    "tearoff:b" => "B",
                    "tearoff:c" => "C",
                    _ => "?",
                })
                .ToArray();
            var hostList = hostKeys.Length == 0 ? "" : string.Join(",", hostKeys);
            var floatList = floatingKeys.Count == 0 ? "" :
                string.Join(",", floatingKeys
                    .Select(k => k switch
                    {
                        "tearoff:a" => "A",
                        "tearoff:b" => "B",
                        "tearoff:c" => "C",
                        _ => "?",
                    })
                    .OrderBy(s => s));
            var summary = $"host:{hostList}  float:{floatList}  windows:{floatingWindowCount}";
            var counters = $"floating:{floatingFireCount}  floated:{floatedFireCount}  docked:{dockedFireCount}";
            var traceText = lastTrace.IsEmpty
                ? "(no trace)"
                : string.Join(" | ", lastTrace);

            var manager = new DockManager
            {
                PersistenceId = "apptest:tearoff-flow",
                Layout = new DockTabGroup(new DockableContent[] { paneA, paneB, paneC }),
                OnContentFloating = _ => bumpFloatingFire(n => n + 1),
                OnContentFloated = args =>
                {
                    bumpFloatedFire(n => n + 1);
                    if (args.Content?.Key is string key)
                        updateFloatingKeys(prev => prev.Add(key));
                },
                OnContentDocked = args =>
                {
                    bumpDockedFire(n => n + 1);
                    if (args.Content?.Key is string key)
                        updateFloatingKeys(prev => prev.Remove(key));
                },
                OnFloatingWindowCreated = _ => updateFloatingWindowCount(n => n + 1),
                OnFloatingWindowClosed = _ => updateFloatingWindowCount(n => Math.Max(0, n - 1)),
            };

            // Layout:
            //   row 0: status header (summary + per-pane state)
            //   row 1: DockManager (the host)
            //   row 2: large "TearOffDropZone" — empty space below the host
            //          so a drag MoveToElement on it lands the cursor
            //          outside every Dock-edge button. Drop-outside path.
            return Grid(
                new[] { GridSize.Auto, GridSize.Star(1), GridSize.Px(300) },
                new[] { GridSize.Star(1) },
                VStack(2,
                    TextBlock(summary)
                        .AutomationId("TearOff_Layout_Summary"),
                    TextBlock(counters)
                        .AutomationId("TearOff_Event_Counters")
                        .Opacity(0.7).FontSize(11),
                    TextBlock(traceText)
                        .AutomationId("TearOff_Trace")
                        .Opacity(0.6).FontSize(10)
                )
                    .Padding(12, 6, 12, 6)
                    .Grid(row: 0),
                manager.Grid(row: 1),
                // The "drop-outside" landing zone. AutomationId on the
                // inner TextBlock — BorderElement.AutomationId doesn't
                // reliably surface through UIA TreeWalker (the Border
                // peer may be filtered out as "no content" unless we
                // give the inner element an automation identity). We
                // wrap with a stretching Grid so MoveToElement lands in
                // the *middle* of the zone, comfortably outside the
                // host's bottom Dock-edge button.
                Grid(
                    new[] { GridSize.Star(1) },
                    new[] { GridSize.Star(1) },
                    TextBlock("(drop-outside zone — drag tabs here to tear off)")
                        .AutomationId("TearOff_DropOutsideZone")
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center)
                        .Opacity(0.5)
                ).Grid(row: 2)
            );
        }
    }

    internal static Element TearOffFlowTest(RenderContext ctx) =>
        Component<TearOffFlowComponent>();
}
