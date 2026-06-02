using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #495 — `ListView` with `SelectedIndex` bound to `UseState` enters a
/// re-render storm after the first selection.
///
/// <para>The customer pattern reallocates an <c>Element[]</c> on every render
/// (idiomatic Reactor — no memoization is expected). Today's
/// <see cref="Microsoft.UI.Reactor.Core.V1Protocol.Handlers.ListViewHandler"/>
/// Update path rebuilds <c>ItemsSource</c> when
/// <c>!ReferenceEquals(o.Items, n.Items)</c>, which transiently drops
/// <c>SelectedIndex</c> to <c>-1</c> and fires <c>SelectionChanged(-1)</c>. The
/// unsuppressed unconditional <c>lv.SelectedIndex = n.SelectedIndex</c> write
/// then fires a second <c>SelectionChanged</c> echo. Both leak into the user
/// callback, which calls <c>setIndex</c>, queueing another render — and the
/// cycle repeats dozens of times before <c>UseState</c>'s equality
/// short-circuit drains the queue.</para>
///
/// <para>Final state is consistent (the user's index sticks), but the render
/// and callback thrash is severe. This fixture programmatically drives a
/// single selection change and asserts a bounded number of follow-up renders
/// and callbacks.</para>
///
/// <para>The same root cause repeats in <c>GridViewHandler.Update</c> (rebuild
/// gate + unsuppressed <c>SelectedIndex</c> write) and in the typed templated
/// list path (<c>TemplatedListLifecycle.UpdateListView/UpdateGridView/UpdateFlipView</c>:
/// the typed peers diff items via an OC pipeline so no transient -1 leaks, but
/// the unconditional <c>SelectedIndex = …</c> write fires a deferred
/// <c>SelectionChanged</c> echo into the user callback). All three are
/// covered by the fixtures below.</para>
/// </summary>
internal static class ListViewLoopReproFixtures
{
    private class StateBoundListViewComponent : Component
    {
        public static int RenderCount;
        public static int CallbackCount;
        public static int LastIndex = -1;

        public static void Reset()
        {
            RenderCount = 0;
            CallbackCount = 0;
            LastIndex = -1;
        }

        public override Element Render()
        {
            RenderCount++;
            var (index, setIndex) = UseState(0);
            // Fresh array allocation every render — this is the customer's
            // idiomatic pattern and the trigger for the ItemsSource rebuild.
            return new ListViewElement(new Element[]
            {
                TextBlock("1"),
                TextBlock("2"),
                TextBlock("3"),
            })
            {
                SelectedIndex = index,
                OnSelectedIndexChanged = s =>
                {
                    CallbackCount++;
                    LastIndex = s;
                    setIndex(s);
                },
            }.Set(l => l.Name = "lvLoop");
        }
    }

    internal class StateBound_NoLoopAfterSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            StateBoundListViewComponent.Reset();

            var host = H.CreateHost();
            host.Mount(new StateBoundListViewComponent());
            await Harness.Render();

            var lv = H.FindControl<ListView>(l => l.Name == "lvLoop");
            H.Check("ListViewLoop_Mounted", lv is not null);
            if (lv is null) return;

            // Baseline after initial mount (mount-time SelectionChanged for the
            // initial SelectedIndex=0 write may have fired one callback).
            int rendersBaseline = StateBoundListViewComponent.RenderCount;
            int callbacksBaseline = StateBoundListViewComponent.CallbackCount;

            // Drive a single programmatic selection change. Without the fix
            // this triggers a re-render storm (~23 renders / ~47 callbacks).
            lv.SelectedIndex = 1;
            await Harness.Render();

            int rendersDelta = StateBoundListViewComponent.RenderCount - rendersBaseline;
            int callbacksDelta = StateBoundListViewComponent.CallbackCount - callbacksBaseline;

            Console.WriteLine(
                $"# ListViewLoop diag: renders+={rendersDelta} callbacks+={callbacksDelta} " +
                $"lastIndex={StateBoundListViewComponent.LastIndex} finalControlIndex={lv.SelectedIndex}");

            // Expected with fix: 1 user-driven callback + 1 re-render whose
            // Update is a no-op (control already at 1, state already at 1).
            // Allow a small slack for dispatcher-driven SelectionChanged ordering
            // but reject the 23-render / 47-callback storm.
            H.Check("ListViewLoop_BoundedRenders", rendersDelta <= 4);
            H.Check("ListViewLoop_BoundedCallbacks", callbacksDelta <= 3);
            H.Check("ListViewLoop_FinalIndexIsUserSelection", StateBoundListViewComponent.LastIndex == 1);
            H.Check("ListViewLoop_StateMatchesControl", lv.SelectedIndex == 1);
        }
    }

    // ───────────────────────────── GridView ────────────────────────────────

    private class StateBoundGridViewComponent : Component
    {
        public static int RenderCount;
        public static int CallbackCount;
        public static int LastIndex = -1;

        public static void Reset()
        {
            RenderCount = 0;
            CallbackCount = 0;
            LastIndex = -1;
        }

        public override Element Render()
        {
            RenderCount++;
            var (index, setIndex) = UseState(0);
            return new GridViewElement(new Element[]
            {
                TextBlock("1"),
                TextBlock("2"),
                TextBlock("3"),
            })
            {
                SelectedIndex = index,
                OnSelectedIndexChanged = s =>
                {
                    CallbackCount++;
                    LastIndex = s;
                    setIndex(s);
                },
            }.Set(g => g.Name = "gvLoop");
        }
    }

    internal class GridView_StateBound_NoLoopAfterSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            StateBoundGridViewComponent.Reset();

            var host = H.CreateHost();
            host.Mount(new StateBoundGridViewComponent());
            await Harness.Render();

            var gv = H.FindControl<GridView>(g => g.Name == "gvLoop");
            H.Check("GridViewLoop_Mounted", gv is not null);
            if (gv is null) return;

            int rendersBaseline = StateBoundGridViewComponent.RenderCount;
            int callbacksBaseline = StateBoundGridViewComponent.CallbackCount;

            gv.SelectedIndex = 1;
            await Harness.Render();

            int rendersDelta = StateBoundGridViewComponent.RenderCount - rendersBaseline;
            int callbacksDelta = StateBoundGridViewComponent.CallbackCount - callbacksBaseline;

            Console.WriteLine(
                $"# GridViewLoop diag: renders+={rendersDelta} callbacks+={callbacksDelta} " +
                $"lastIndex={StateBoundGridViewComponent.LastIndex} finalControlIndex={gv.SelectedIndex}");

            H.Check("GridViewLoop_BoundedRenders", rendersDelta <= 4);
            H.Check("GridViewLoop_BoundedCallbacks", callbacksDelta <= 3);
            H.Check("GridViewLoop_FinalIndexIsUserSelection", StateBoundGridViewComponent.LastIndex == 1);
            H.Check("GridViewLoop_StateMatchesControl", gv.SelectedIndex == 1);
        }
    }

    // ─────────────────────── Templated ListView<T> ─────────────────────────

    private record Row(int Index, string Label);

    private class StateBoundTypedListViewComponent : Component
    {
        public static int RenderCount;
        public static int CallbackCount;
        public static int LastIndex = -1;

        public static void Reset()
        {
            RenderCount = 0;
            CallbackCount = 0;
            LastIndex = -1;
        }

        public override Element Render()
        {
            RenderCount++;
            var (index, setIndex) = UseState(0);
            // Fresh data array every render (idiomatic Reactor — no memoization).
            var rows = new[]
            {
                new Row(0, "A"),
                new Row(1, "B"),
                new Row(2, "C"),
            };
            return (ListView<Row>(rows, r => r.Index.ToString(), (r, _) => TextBlock(r.Label)) with
            {
                SelectedIndex = index,
                OnSelectedIndexChanged = s =>
                {
                    CallbackCount++;
                    LastIndex = s;
                    setIndex(s);
                },
            }).Set(l => l.Name = "lvTypedLoop");
        }
    }

    internal class TypedListView_StateBound_NoLoopAfterSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            StateBoundTypedListViewComponent.Reset();

            var host = H.CreateHost();
            host.Mount(new StateBoundTypedListViewComponent());
            await Harness.Render();

            var lv = H.FindControl<ListView>(l => l.Name == "lvTypedLoop");
            H.Check("TypedListViewLoop_Mounted", lv is not null);
            if (lv is null) return;

            int rendersBaseline = StateBoundTypedListViewComponent.RenderCount;
            int callbacksBaseline = StateBoundTypedListViewComponent.CallbackCount;

            lv.SelectedIndex = 1;
            await Harness.Render();

            int rendersDelta = StateBoundTypedListViewComponent.RenderCount - rendersBaseline;
            int callbacksDelta = StateBoundTypedListViewComponent.CallbackCount - callbacksBaseline;

            Console.WriteLine(
                $"# TypedListViewLoop diag: renders+={rendersDelta} callbacks+={callbacksDelta} " +
                $"lastIndex={StateBoundTypedListViewComponent.LastIndex} finalControlIndex={lv.SelectedIndex}");

            H.Check("TypedListViewLoop_BoundedRenders", rendersDelta <= 4);
            H.Check("TypedListViewLoop_BoundedCallbacks", callbacksDelta <= 3);
            H.Check("TypedListViewLoop_FinalIndexIsUserSelection", StateBoundTypedListViewComponent.LastIndex == 1);
            H.Check("TypedListViewLoop_StateMatchesControl", lv.SelectedIndex == 1);
        }
    }

    // ─────────────────────── Templated GridView<T> ─────────────────────────

    private class StateBoundTypedGridViewComponent : Component
    {
        public static int RenderCount;
        public static int CallbackCount;
        public static int LastIndex = -1;

        public static void Reset()
        {
            RenderCount = 0;
            CallbackCount = 0;
            LastIndex = -1;
        }

        public override Element Render()
        {
            RenderCount++;
            var (index, setIndex) = UseState(0);
            var rows = new[]
            {
                new Row(0, "A"),
                new Row(1, "B"),
                new Row(2, "C"),
            };
            return (GridView<Row>(rows, r => r.Index.ToString(), (r, _) => TextBlock(r.Label)) with
            {
                SelectedIndex = index,
                OnSelectedIndexChanged = s =>
                {
                    CallbackCount++;
                    LastIndex = s;
                    setIndex(s);
                },
            }).Set(g => g.Name = "gvTypedLoop");
        }
    }

    internal class TypedGridView_StateBound_NoLoopAfterSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            StateBoundTypedGridViewComponent.Reset();

            var host = H.CreateHost();
            host.Mount(new StateBoundTypedGridViewComponent());
            await Harness.Render();

            var gv = H.FindControl<GridView>(g => g.Name == "gvTypedLoop");
            H.Check("TypedGridViewLoop_Mounted", gv is not null);
            if (gv is null) return;

            int rendersBaseline = StateBoundTypedGridViewComponent.RenderCount;
            int callbacksBaseline = StateBoundTypedGridViewComponent.CallbackCount;

            gv.SelectedIndex = 1;
            await Harness.Render();

            int rendersDelta = StateBoundTypedGridViewComponent.RenderCount - rendersBaseline;
            int callbacksDelta = StateBoundTypedGridViewComponent.CallbackCount - callbacksBaseline;

            Console.WriteLine(
                $"# TypedGridViewLoop diag: renders+={rendersDelta} callbacks+={callbacksDelta} " +
                $"lastIndex={StateBoundTypedGridViewComponent.LastIndex} finalControlIndex={gv.SelectedIndex}");

            H.Check("TypedGridViewLoop_BoundedRenders", rendersDelta <= 4);
            H.Check("TypedGridViewLoop_BoundedCallbacks", callbacksDelta <= 3);
            H.Check("TypedGridViewLoop_FinalIndexIsUserSelection", StateBoundTypedGridViewComponent.LastIndex == 1);
            H.Check("TypedGridViewLoop_StateMatchesControl", gv.SelectedIndex == 1);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Regression guard: same-length content updates must refresh containers.
    //
    //  The naïve "fix" for #495 — gate ItemsSource rebuild on length only —
    //  would silently freeze visible items when the customer's array stays the
    //  same length but the per-item content changes (e.g. a list of names where
    //  the names change but the row count is stable). The hand-coded
    //  ListView/GridView handlers have `Children = null` and never reconcile
    //  realized child controls; ContainerContentChanging only re-fires on
    //  realize/recycle. So if we skip the ItemsSource rebuild, already-realized
    //  containers keep showing the old TextBlocks.
    //
    //  The actual fix wraps the ItemsSource swap (and the subsequent
    //  SelectedIndex write) in `ChangeEchoSuppressor.BeginSuppress` so the
    //  transient `SelectionChanged(-1)` doesn't leak back into user state.
    //  These fixtures lock that contract down.
    // ───────────────────────────────────────────────────────────────────────

    private class SameLengthContentChangeListViewComponent : Component
    {
        public static int LabelGeneration;

        public override Element Render()
        {
            int gen = LabelGeneration;
            return new ListViewElement(new Element[]
            {
                TextBlock($"row0-gen{gen}"),
                TextBlock($"row1-gen{gen}"),
                TextBlock($"row2-gen{gen}"),
            }).Set(l => l.Name = "lvSameLen");
        }
    }

    internal class ListView_SameLengthContentChange_RefreshesContainers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            SameLengthContentChangeListViewComponent.LabelGeneration = 0;
            var component = new SameLengthContentChangeListViewComponent();

            var host = H.CreateHost();
            host.Mount(component);
            await Harness.Render();

            var lv = H.FindControl<ListView>(l => l.Name == "lvSameLen");
            H.Check("SameLenContent_ListView_Mounted", lv is not null);
            if (lv is null) return;

            H.Check("SameLenContent_ListView_InitialGen0Visible", H.FindTextContaining("row0-gen0") is not null);

            // Bump generation and force a re-render. Same length, different
            // content. With the length-only "fix" this would leave the
            // realized containers showing "row0-gen0" forever.
            SameLengthContentChangeListViewComponent.LabelGeneration = 1;
            host.RequestRender(force: true);
            await Harness.Render();

            H.Check("SameLenContent_ListView_Gen1Visible", H.FindTextContaining("row0-gen1") is not null);
            H.Check("SameLenContent_ListView_Gen0Replaced", H.FindTextContaining("row0-gen0") is null);
        }
    }

    private class SameLengthContentChangeGridViewComponent : Component
    {
        public static int LabelGeneration;

        public override Element Render()
        {
            int gen = LabelGeneration;
            return new GridViewElement(new Element[]
            {
                TextBlock($"gv-row0-gen{gen}"),
                TextBlock($"gv-row1-gen{gen}"),
                TextBlock($"gv-row2-gen{gen}"),
            }).Set(g => g.Name = "gvSameLen");
        }
    }

    internal class GridView_SameLengthContentChange_RefreshesContainers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            SameLengthContentChangeGridViewComponent.LabelGeneration = 0;
            var component = new SameLengthContentChangeGridViewComponent();

            var host = H.CreateHost();
            host.Mount(component);
            await Harness.Render();

            var gv = H.FindControl<GridView>(g => g.Name == "gvSameLen");
            H.Check("SameLenContent_GridView_Mounted", gv is not null);
            if (gv is null) return;

            H.Check("SameLenContent_GridView_InitialGen0Visible", H.FindTextContaining("gv-row0-gen0") is not null);

            SameLengthContentChangeGridViewComponent.LabelGeneration = 1;
            host.RequestRender(force: true);
            await Harness.Render();

            H.Check("SameLenContent_GridView_Gen1Visible", H.FindTextContaining("gv-row0-gen1") is not null);
            H.Check("SameLenContent_GridView_Gen0Replaced", H.FindTextContaining("gv-row0-gen0") is null);
        }
    }
}
