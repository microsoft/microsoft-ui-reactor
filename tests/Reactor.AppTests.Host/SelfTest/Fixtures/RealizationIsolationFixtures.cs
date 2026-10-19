using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Elements;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1386 — a virtualized host builds rows from WinUI layout callbacks, which can
/// run re-entrantly in the middle of an unrelated Reactor update (any synchronous
/// <c>UpdateLayout()</c>: an app effect, a control's own property setter). Row building
/// must not join that update's bookkeeping: its ErrorBoundary rollback lists, its
/// boundary depth (error routing), its window host. And a host's provider snapshot must
/// go stale on a provider change even when the host is only reached in a later pass.
/// </summary>
internal static class RealizationIsolationFixtures
{
    private static readonly Context<string> Ctx = new("default");

    // ── Re-entrant realization during a failing ErrorBoundary mount ──────────

    private static int s_rowEffects;
    private static int s_rowCleanups;
    private static Action? s_forceLayout;

    private sealed record IndexProps(int Index);

    private sealed class EffectRow : Component<IndexProps>
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                s_rowEffects++;
                return () => s_rowCleanups++;
            });
            return TextBlock($"erow{Props.Index}").Height(30);
        }
    }

    private sealed class ThrowingRow : Component<IndexProps>
    {
        public override Element Render()
            => Props.Index >= 6
                ? throw new InvalidOperationException($"row-boom-{Props.Index}")
                : TextBlock($"trow{Props.Index}").Height(30);
    }

    private sealed class LayoutForcer : Component
    {
        public override Element Render()
        {
            UseEffect(() => s_forceLayout?.Invoke());
            return TextBlock("forcer");
        }
    }

    private sealed class Thrower : Component
    {
        public override Element Render() => throw new InvalidOperationException("boundary child failed");
    }

    private static Action ForceLayoutOf(Harness h) => () =>
    {
        var sv = h.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater);
        (sv?.XamlRoot?.Content as UIElement)?.UpdateLayout();
    };

    internal sealed class RowsRealizedDuringFailedBoundaryMount_StayAlive(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_rowEffects = s_rowCleanups = 0;
            s_forceLayout = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                IReadOnlyList<int> items = Enumerable.Range(0, phase == 0 ? 2 : 20).ToList();
                return VStack(
                    Button("IsoBoundaryGo", () => setPhase(1)),
                    LazyVStack<int>(items, i => i.ToString(), (i, _) =>
                        Component<EffectRow, IndexProps>(new IndexProps(i))).Height(300),
                    phase == 0
                        ? TextBlock("iso-idle")
                        : ErrorBoundary(
                            VStack(Component<LayoutForcer>(), Component<Thrower>()),
                            TextBlock("iso-fallback")));
            });
            await Harness.Render();

            int before = s_rowEffects;
            int realizedDuringPass = -1;
            var force = ForceLayoutOf(H);
            s_forceLayout = () => { force(); realizedDuringPass = s_rowEffects - before; };

            H.ClickButton("IsoBoundaryGo");
            await Harness.Render();
            s_forceLayout = null;

            int visible = H.FindAllControls<TextBlock>(tb => tb.Text?.StartsWith("erow", StringComparison.Ordinal) == true).Count;
            var detail = $"realizedDuringPass={realizedDuringPass} effects={s_rowEffects} cleanups={s_rowCleanups} visible={visible}";
            H.Check("Iso_Boundary_FallbackShown", H.FindText("iso-fallback") is not null);
            // Precondition: the scenario really realized rows inside the failing pass.
            H.Check("Iso_Boundary_RowsRealizedReentrantly", realizedDuringPass > 0, detail);
            H.Check("Iso_Boundary_LiveRowsNotTornDown", s_rowCleanups == 0 && visible > 0, detail);
        }
    }

    // A row that throws while realized re-entrantly under a mounting boundary is the
    // row's own failure: it must not be routed to (and fail) that unrelated boundary.
    internal sealed class ThrowingRowRealizedDuringBoundaryMount_DoesNotTripBoundary(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_forceLayout = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                IReadOnlyList<int> items = Enumerable.Range(0, phase == 0 ? 2 : 20).ToList();
                return VStack(
                    Button("IsoThrowGo", () => setPhase(1)),
                    LazyVStack<int>(items, i => i.ToString(), (i, _) =>
                        Component<ThrowingRow, IndexProps>(new IndexProps(i))).Height(300),
                    phase == 0
                        ? TextBlock("iso-throw-idle")
                        : ErrorBoundary(
                            VStack(Component<LayoutForcer>(), TextBlock("iso-boundary-ok")),
                            TextBlock("iso-throw-fallback")));
            });
            await Harness.Render();
            s_forceLayout = ForceLayoutOf(H);

            H.ClickButton("IsoThrowGo");
            await Harness.Render();
            s_forceLayout = null;

            H.Check("Iso_Throw_BoundaryChildRendered", H.FindText("iso-boundary-ok") is not null);
            H.Check("Iso_Throw_BoundaryNotTripped", H.FindText("iso-throw-fallback") is null);
            H.Check("Iso_Throw_RowShowsItsOwnFallback",
                H.FindTextContaining("row-boom-") is not null,
                H.FindTextContaining("Render error")?.Text ?? "<no in-row fallback>");
        }
    }

    // ── Window host during realization ───────────────────────────────────────

    private static readonly List<ReactorHost?> s_seenHosts = new();

    private sealed class HostProbeRow : Component<IndexProps>
    {
        public override Element Render()
        {
            s_seenHosts.Add(ReactorApp.ActiveHostInternal);
            return TextBlock($"hrow{Props.Index}").Height(30);
        }
    }

    // ActiveHostInternal is only pushed for a host's own render pass; between passes it
    // is whichever host was constructed last, or null after any host is disposed. Rows
    // realized while scrolling must still resolve their own host (window-scoped hooks).
    internal sealed class RowsRealizedOutsideRender_SeeOwningHost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_seenHosts.Clear();
            var host = H.CreateHost();
            host.Mount(_ => LazyVStack<int>(Enumerable.Range(0, 300).ToList(), i => i.ToString(), (i, _) =>
                Component<HostProbeRow, IndexProps>(new IndexProps(i))).Height(300));
            await Harness.Render();

            var previous = ReactorApp.ActiveHostInternal;
            try
            {
                // What ReactorHost.Dispose() of any other window's host leaves behind.
                ReactorApp.ActiveHostInternal = null;
                s_seenHosts.Clear();
                var sv = H.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater)!;
                sv.ChangeView(null, 150 * 30, null, disableAnimation: true);
                sv.UpdateLayout();
                (sv.XamlRoot?.Content as UIElement)?.UpdateLayout();

                int total = s_seenHosts.Count;
                int own = s_seenHosts.Count(x => ReferenceEquals(x, host));
                H.Check("Iso_Host_RowsRealized", total > 0, $"realized={total}");
                H.Check("Iso_Host_RowsSeeOwningHost", total > 0 && own == total, $"own={own}/{total}");
                H.Check("Iso_Host_RestoredAfterRealization", ReactorApp.ActiveHostInternal is null);
            }
            finally
            {
                ReactorApp.ActiveHostInternal = previous;
            }
            await Harness.Render();
        }
    }

    // ── Nested host inside a parked row, provider removed meanwhile ──────────

    private sealed record NestedProps(int Outer, int Inner);

    private sealed class NestedConsumer : Component<NestedProps>
    {
        public override Element Render()
            => TextBlock($"n{Props.Outer}.{Props.Inner}:{UseContext(Ctx)}").Height(30);
    }

    // Inner rows below 10 do not consume context, so when the outer row comes back
    // nothing realized inside the nested host can reveal that the provider is gone —
    // only the nested host's own snapshot can.
    private static Element InnerList(int outer)
        => LazyVStack<int>(Enumerable.Range(0, 40).ToList(), j => j.ToString(), (j, _) =>
                j < 10
                    ? TextBlock($"n{outer}.{j} plain").Height(30)
                    : Component<NestedConsumer, NestedProps>(new NestedProps(outer, j)))
            .Height(120);

    internal sealed class NestedHostInRecycledRow_SeesProviderRemoval(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var inner = Enumerable.Range(0, 200).Select(InnerList).ToArray();
            var outerList = LazyVStack<int>(Enumerable.Range(0, 200).ToList(), i => i.ToString(), (i, _) =>
                    Border(inner[i]).Height(130))
                .Height(300);

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (on, setOn) = ctx.UseState(true);
                var body = VStack(
                    Button("IsoNestedDrop", () => setOn(false)),
                    TextBlock($"iso-nested-on:{on}"),
                    outerList);
                return on ? body.Provide(Ctx, "removed") : body;
            });
            await Harness.Render();

            ScrollViewer Outer() => H.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater && global::System.Math.Abs(s.Height - 300.0) < 0.001)!;
            H.Check("Iso_Nested_Row0Realized", H.FindText("n0.0 plain") is not null);

            // Park outer row 0 (with its nested host) by scrolling far away.
            Outer().ChangeView(null, 100 * 130, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();
            H.Check("Iso_Nested_Row0Parked", H.FindAllControls<TextBlock>(t => t.Text == "n0.0 plain")
                .All(t => !IsEffectivelyVisible(t)));

            H.ClickButton("IsoNestedDrop");
            await Harness.Render();
            H.Check("Iso_Nested_ProviderRemoved", H.FindText("iso-nested-on:False") is not null);

            // Bring row 0 back: the repeater reuses its parked container.
            Outer().ChangeView(null, 0, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();

            var innerSv = H.FindAllControls<TextBlock>(t => t.Text == "n0.0 plain" && IsEffectivelyVisible(t))
                .Select(AncestorScrollViewer).FirstOrDefault(s => s is not null);
            H.Check("Iso_Nested_Row0Back", innerSv is not null);
            if (innerSv is null) return;

            // Realize consumer rows inside the nested host.
            innerSv.ChangeView(null, 20 * 30, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();
            var consumers = H.FindAllControls<TextBlock>(t => t.Text?.StartsWith("n0.", StringComparison.Ordinal) == true
                    && t.Text.Contains(':') && IsEffectivelyVisible(t))
                .Select(t => t.Text).ToList();
            H.Check("Iso_Nested_ConsumersSeeDefault",
                consumers.Count > 0 && consumers.All(t => t.EndsWith(":default", StringComparison.Ordinal)),
                consumers.Count == 0 ? "<none realized>" : string.Join(",", consumers.Take(5)));
        }

        private static bool IsEffectivelyVisible(DependencyObject d)
        {
            for (var cur = d; cur is not null; cur = VisualTreeHelper.GetParent(cur))
                if (cur is UIElement { Visibility: Visibility.Collapsed }) return false;
            return true;
        }

        private static ScrollViewer? AncestorScrollViewer(DependencyObject d)
        {
            for (var cur = VisualTreeHelper.GetParent(d); cur is not null; cur = VisualTreeHelper.GetParent(cur))
                if (cur is ScrollViewer { Content: Microsoft.UI.Xaml.Controls.ItemsRepeater } sv) return sv;
            return null;
        }
    }

    // ── Retired host releases its provider values ─────────────────────────────

    private sealed record Payload(string Name);
    private static readonly Context<Payload?> PayloadCtx = new(defaultValue: null);

    internal sealed class DetachedHost_ReleasesFactoryCapture(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                    LazyVStack<int>(Enumerable.Range(0, 20).ToList(), i => i.ToString(), (i, _) =>
                        TextBlock($"d{i}").Height(30)).Height(200))
                .Provide(PayloadCtx, new Payload("retained")));
            await Harness.Render();

            var sv = H.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater)!;
            var factory = (sv.Content as ItemsRepeater)?.ItemTemplate as IContextCapturingFactory;
            var capture = factory?.ContextCapture;
            H.Check("Iso_Detach_FactoryHasCapture", capture is not null && capture.Entries.Length > 0);
            if (capture is null) return;

            // What retiring a parked outer row does to every control in it.
            Reconciler.DetachReactorState(sv);
            H.Check("Iso_Detach_FactoryAliasReleased", capture.Entries.Length == 0,
                $"entries={capture.Entries.Length}");
        }
    }

    // ── Nested host mounted while an outdated snapshot is replayed ────────────

    // A provider is removed, and before the traversal refreshes the outer host's snapshot,
    // a row is realized from the outdated snapshot and mounts a nested host. That nested
    // host's snapshot holds the removed provider, so it must not record the current
    // generation — otherwise a later skip check treats it as current and its later rows
    // read the removed value. Drives a standalone outer factory so the ordering is exact.
    internal sealed class NestedHostMountedDuringStaleReplay_IsNotCurrent(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var reconciler = new Reconciler();
            Func<int, int, Element> builder = (i, _) =>
                LazyVStack<int>(Enumerable.Range(0, 30).ToList(), j => j.ToString(), (j, _) =>
                    TextBlock($"sr{i}.{j}").Height(30)).Height(120);

            ElementFactory<int> outer;
            using (reconciler.PushContextDisposable(Ctx, "removed"))
                outer = new ElementFactory<int>(Enumerable.Range(0, 5).ToList(), builder, reconciler, static () => { }, pool: null);

            // The pass that removes the provider: a non-skip Update whose own provided
            // values changed. Inside it, an earlier sibling forces layout, so a row is
            // realized from the outer snapshot before the traversal reaches the outer
            // host; then the traversal reaches the (reference-stable) nested host.
            reconciler.EnterProviderChange();
            try
            {
                var row = ((IElementFactory)outer).GetElement(new ElementFactoryGetArgs { Data = 0 });
                var nestedSv = row as ScrollViewer;
                var nested = (nestedSv?.Content as ItemsRepeater)?.ItemTemplate as IContextCapturingFactory;
                H.Check("Iso_StaleReplay_NestedHostMounted", nested?.ContextCapture is { Entries.Length: 1 },
                    $"entries={nested?.ContextCapture?.Entries.Length}");
                if (nestedSv is null) return Task.CompletedTask;

                // The live stack is now empty (the removal happened): the nested host's
                // snapshot still holds the removed provider, so it must decline the skip.
                H.Check("Iso_StaleReplay_NestedHostDeclinesSkip", reconciler.ShouldDeclineSkip(nestedSv));
            }
            finally
            {
                reconciler.ExitProviderChange();
            }
            return Task.CompletedTask;
        }
    }

    // ── Reused row drops its own provider around a stable nested host ─────────

    // A recycled row is reused for the same item, and the new row element no longer
    // carries its own .Provide. The nested list is reference-stable, so the row's Update
    // reaches it through the skip check while the outer snapshot is being replayed. The
    // provider change inside the replay must make the nested snapshot stale.
    internal sealed class ReusedRowDropsOwnProvider_NestedHostRefreshes(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var reconciler = new Reconciler();
            var stableInner = LazyVStack<int>(Enumerable.Range(0, 30).ToList(), j => j.ToString(), (j, _) =>
                TextBlock($"ru{j}").Height(30)).Height(120);
            var items = Enumerable.Range(0, 5).ToList();
            Func<int, int, Element> withProvider = (_, _) => VStack(stableInner).Provide(Ctx, "row-local");
            Func<int, int, Element> withoutProvider = (_, _) => VStack(stableInner);

            var outer = new ElementFactory<int>(items, withProvider, reconciler, static () => { }, pool: null);
            IElementFactory f = outer;

            var first = f.GetElement(new ElementFactoryGetArgs { Data = 0 });
            ContextCapture? NestedCapture(UIElement row)
                => ((row as StackPanel ?? (row as Panel))?.Children.OfType<ScrollViewer>().FirstOrDefault()?.Content
                        as ItemsRepeater)?.ItemTemplate is IContextCapturingFactory c ? c.ContextCapture : null;
            var nestedBefore = NestedCapture(first);
            H.Check("Iso_RowLocal_NestedSeesRowProvider", nestedBefore is { Entries.Length: 1 },
                $"entries={nestedBefore?.Entries.Length} root={first.GetType().Name}");

            f.RecycleElement(new ElementFactoryRecycleArgs { Element = first });
            outer.UpdateInPlace(items, withoutProvider);
            var again = f.GetElement(new ElementFactoryGetArgs { Data = 0 });

            H.Check("Iso_RowLocal_ContainerReused", ReferenceEquals(first, again));
            var nestedAfter = NestedCapture(again);
            H.Check("Iso_RowLocal_NestedDroppedRemovedProvider", nestedAfter is { Entries.Length: 0 },
                $"entries={nestedAfter?.Entries.Length}");
            return Task.CompletedTask;
        }
    }

    // ── Ancestor theme changes in the same pass that realizes rows ───────────

    private sealed class ForcerWithDep : Component<IndexProps>
    {
        public override Element Render()
        {
            UseEffect(() => s_forceLayout?.Invoke(), Props.Index);
            return TextBlock("forcer-dep");
        }
    }

    // The native RequestedTheme modifier on the ancestor lands only after its children
    // are dispatched, so rows realized re-entrantly by a LATER sibling in that same pass
    // must use the pass's pending theme, not the host's still-old ActualTheme.
    internal sealed class RowsRealizedDuringThemeChangingPass_UsePendingTheme(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "IsoPassThemeSrcBrush";
        private const string TargetKey = "IsoPassThemeTargetBrush";

        public override async Task RunAsync()
        {
            var light = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 0, 0, 200));
            var dark = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 200, 0, 0));
            var dict = new ResourceDictionary();
            dict.ThemeDictionaries["Light"] = new ResourceDictionary { [SrcKey] = light };
            dict.ThemeDictionaries["Dark"] = new ResourceDictionary { [SrcKey] = dark };
            Application.Current.Resources.MergedDictionaries.Add(dict);
            ThemeRef.InvalidateResolutionCache();
            s_forceLayout = null;
            try
            {
                bool appIsDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                var first = appIsDark ? ElementTheme.Light : ElementTheme.Dark;
                var second = appIsDark ? ElementTheme.Dark : ElementTheme.Light;
                SolidColorBrush BrushFor(ElementTheme t) => t == ElementTheme.Dark ? dark : light;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (phase, setPhase) = ctx.UseState(0);
                    IReadOnlyList<int> items = Enumerable.Range(0, phase == 0 ? 2 : 20).ToList();
                    return VStack(
                        Button("IsoPassThemeGo", () => setPhase(1)),
                        VStack(
                                LazyVStack<int>(items, i => i.ToString(), (i, _) =>
                                        TextBlock($"pt{i}").Height(30).Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                                    .Height(300),
                                Component<ForcerWithDep, IndexProps>(new IndexProps(phase)))
                            .RequestedTheme(phase == 0 ? first : second));
                });
                await Harness.Render();

                var forceLayout = ForceLayoutOf(H);
                var realizedInPass = new List<string>();
                s_forceLayout = () =>
                {
                    forceLayout();
                    realizedInPass.AddRange(H.FindAllControls<TextBlock>(t => t.Text?.StartsWith("pt", StringComparison.Ordinal) == true
                            && int.TryParse(t.Text.AsSpan(2), out var n) && n >= 2)
                        .Select(t => t.Text));
                };

                H.ClickButton("IsoPassThemeGo");
                await Harness.Render();
                s_forceLayout = null;

                var rows = H.FindAllControls<TextBlock>(t => t.Text?.StartsWith("pt", StringComparison.Ordinal) == true
                        && int.TryParse(t.Text.AsSpan(2), out var n) && n >= 2)
                    .ToList();
                int right = rows.Count(t => t.Resources.TryGetValue(TargetKey, out var b) && ReferenceEquals(b, BrushFor(second)));
                H.Check("Iso_PassTheme_RowsRealizedInPass", realizedInPass.Count > 0, $"inPass={realizedInPass.Count}");
                H.Check("Iso_PassTheme_RowsUseNewTheme", rows.Count > 0 && right == rows.Count, $"right={right}/{rows.Count}");
            }
            finally
            {
                s_forceLayout = null;
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // ── Ancestor theme changes while the host is skipped ─────────────────────

    // No Context providers anywhere: the provider checks are not even consulted. The
    // list is reference-stable, so a theme toggle on its parent skips it, and rows
    // realized afterwards must still resolve ThemeRef overrides against the NEW theme.
    internal sealed class AncestorThemeToggledWhileSkipped_RowsResolveNewTheme(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "IsoThemeSrcBrush";
        private const string TargetKey = "IsoThemeTargetBrush";

        public override async Task RunAsync()
        {
            var light = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 0, 0, 220));
            var dark = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 220, 0, 0));
            var dict = new ResourceDictionary();
            dict.ThemeDictionaries["Light"] = new ResourceDictionary { [SrcKey] = light };
            dict.ThemeDictionaries["Dark"] = new ResourceDictionary { [SrcKey] = dark };
            Application.Current.Resources.MergedDictionaries.Add(dict);
            ThemeRef.InvalidateResolutionCache();
            try
            {
                bool appIsDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                var first = appIsDark ? ElementTheme.Light : ElementTheme.Dark;
                var second = appIsDark ? ElementTheme.Dark : ElementTheme.Light;
                SolidColorBrush BrushFor(ElementTheme t) => t == ElementTheme.Dark ? dark : light;

                var stableList = LazyVStack<int>(Enumerable.Range(0, 300).ToList(), i => i.ToString(), (i, _) =>
                        TextBlock($"th{i}").Height(30).Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                    .Height(300);

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (flipped, setFlipped) = ctx.UseState(false);
                    return VStack(
                            Button("IsoThemeFlip", () => setFlipped(true)),
                            TextBlock($"iso-theme:{flipped}"),
                            stableList)
                        .RequestedTheme(flipped ? second : first);
                });
                await Harness.Render();

                var row0 = H.FindText("th0");
                H.Check("Iso_Theme_MountResolvesAncestor",
                    row0?.Resources.TryGetValue(TargetKey, out var b0) == true && ReferenceEquals(b0, BrushFor(first)));

                H.ClickButton("IsoThemeFlip");
                await Harness.Render();
                H.Check("Iso_Theme_Flipped", H.FindText("iso-theme:True") is not null);

                var sv = H.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater)!;
                sv.ChangeView(null, 150 * 30, null, disableAnimation: true);
                await Harness.Render();
                await Harness.Render();

                var scrolled = H.FindAllControls<TextBlock>(t => t.Text?.StartsWith("th1", StringComparison.Ordinal) == true
                        && int.TryParse(t.Text.AsSpan(2), out var n) && n >= 100)
                    .ToList();
                int right = scrolled.Count(t => t.Resources.TryGetValue(TargetKey, out var b) && ReferenceEquals(b, BrushFor(second)));
                H.Check("Iso_Theme_ScrolledRowsResolveNewTheme", scrolled.Count > 0 && right == scrolled.Count,
                    $"right={right}/{scrolled.Count}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }
}
