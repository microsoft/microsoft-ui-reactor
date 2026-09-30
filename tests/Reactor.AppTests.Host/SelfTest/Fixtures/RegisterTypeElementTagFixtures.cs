using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// A control mounted through the per-host <c>Reconciler.RegisterType</c> must carry its element
/// tag (<c>ReactorAttached.StateProperty</c> → <c>ReactorState.Element</c>) whenever something
/// reads it back, without the registration's callbacks calling <c>Reconciler.SetElementTag</c>
/// themselves. None of the registrations below tag by hand.
///
/// <para>Three readers depend on the tag. <c>ChildReconciler.ReconcileKeyedMiddle</c> finds a
/// surviving keyed child by the key on its tag; an untagged survivor is neither moved nor
/// patched, so after a grow or a reorder the control keeps stale content while the Grid assigns
/// it another child's cell. <c>UnmountRecursive</c> / <c>UnmountAndCollect</c> find a
/// registration's <c>unmount</c> callback through the tag, so without it the callback never ran.
/// <c>CleanupReferenceStateForUnmount</c> clears a <c>.Ref(...)</c> through the tag, so without
/// it the ref kept pointing at the removed control.</para>
///
/// <para>The reconciler used to tag only the V1 arm and the composition wrappers. The
/// registered-type arm left tagging to the author, which in-tree only <c>XamlInterop</c> did.
/// These fixtures cover each reader, both unmount paths, both update outcomes (patched in place
/// and replaced), and the allocation gate: an unkeyed control whose registration has no
/// <c>unmount</c> is still not tagged. Two more pin what tagging must not do: clear a ref
/// that has already moved to a replacement, or retag a control that another element
/// owns. One more pins that an <c>unmount</c> callback can unmount its own control through
/// the reconciler without calling itself again.</para>
/// </summary>
internal static class RegisterTypeElementTagFixtures
{
    // ── Keyed children: grow a Grid, then reverse it ────────────────────

    private sealed record CellElement(int Row, int Column, int Generation) : Element;

    private readonly record struct Cell(UIElement Control, int GridRow, int GridColumn, string? Text);

    private static string CellLabel(CellElement cell) => $"{cell.Row},{cell.Column}@{cell.Generation}";

    /// <summary>
    /// Mounts a 3×3 Grid of keyed registered-type cells, grows it to 4×5 and then reverses the
    /// child order, the same shape as <c>KeyedWrapperChildrenFixtures</c>. Row 0 stays put as the
    /// keyed prefix; rows 1 and 2 survive in the keyed middle at new indices. After each step
    /// every cell's text must name its own Grid cell at the current generation, and every
    /// survivor must still be the control that was mounted for it.
    /// </summary>
    internal sealed class KeyedGridGrowAndReverse(Harness h) : SelfTestFixtureBase(h)
    {
        private const string Name = "RegisterTypeTag_KeyedGrid";

        private readonly Dictionary<(int Row, int Column), int> _mounts = new();

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            // No unmount callback, so only the key can be the reason this control is tagged.
            host.Reconciler.RegisterType<CellElement, WinXC.TextBlock>(
                mount: (_, cell, _) =>
                {
                    _mounts[(cell.Row, cell.Column)] = _mounts.GetValueOrDefault((cell.Row, cell.Column)) + 1;
                    return new WinXC.TextBlock { Text = CellLabel(cell) };
                },
                update: (_, _, cell, text, _) =>
                {
                    text.Text = CellLabel(cell);
                    return null;
                });

            host.Mount(ctx =>
            {
                var (step, setStep) = ctx.UseState(0);
                var (rows, columns) = step == 0 ? (3, 3) : (4, 5);
                var cells = new List<Element>(rows * columns);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < columns; c++)
                        cells.Add(new CellElement(r, c, step).Grid(row: r, column: c).WithKey($"cell-{r}-{c}"));
                if (step == 2)
                    cells.Reverse();

                return VStack(
                    Button("RTT Grow", () => setStep(1)),
                    Button("RTT Reverse", () => setStep(2)),
                    Grid(Tracks(columns), Tracks(rows), cells.ToArray()).AutomationId("rtt_grid"));
            });

            await Harness.Render();
            var mounted = Snapshot();
            Verify("Mount", mounted, rows: 3, columns: 3, generation: 0);

            H.ClickButton("RTT Grow");
            await Harness.Render();
            var grown = Snapshot();
            Verify("Grow", grown, rows: 4, columns: 5, generation: 1);
            CheckSurvivorsKeptTheirControl("Grow", mounted, grown);

            H.ClickButton("RTT Reverse");
            await Harness.Render();
            var reversed = Snapshot();
            Verify("Reverse", reversed, rows: 4, columns: 5, generation: 2);
            CheckSurvivorsKeptTheirControl("Reverse", grown, reversed);
        }

        private static GridSize[] Tracks(int count)
        {
            var tracks = new GridSize[count];
            for (int i = 0; i < count; i++) tracks[i] = GridSize.Px(40);
            return tracks;
        }

        private List<Cell> Snapshot()
        {
            var grid = H.FindControl<WinXC.Grid>(g =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(g) == "rtt_grid");
            var cells = new List<Cell>();
            if (grid is null) return cells;
            foreach (var child in grid.Children)
            {
                cells.Add(new Cell(
                    child,
                    WinXC.Grid.GetRow((FrameworkElement)child),
                    WinXC.Grid.GetColumn((FrameworkElement)child),
                    (child as WinXC.TextBlock)?.Text));
            }
            return cells;
        }

        private static bool TryParse(string? text, out int row, out int column, out int generation)
        {
            row = column = generation = -1;
            if (text is null) return false;
            var at = text.IndexOf('@');
            var comma = text.IndexOf(',');
            return comma > 0 && at > comma
                && int.TryParse(text[..comma], out row)
                && int.TryParse(text[(comma + 1)..at], out column)
                && int.TryParse(text[(at + 1)..], out generation);
        }

        private void Verify(string step, List<Cell> cells, int rows, int columns, int generation)
        {
            H.Check($"{Name}_{step}_ChildCount", cells.Count == rows * columns,
                $"expected {rows * columns} children, found {cells.Count}");

            var misplaced = new List<string>();
            var stale = new List<string>();
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                var at = $"child {i} '{cell.Text}' at ({cell.GridRow},{cell.GridColumn})";
                if (!TryParse(cell.Text, out var row, out var column, out var shown))
                {
                    misplaced.Add(at);
                    continue;
                }
                if (row != cell.GridRow || column != cell.GridColumn)
                    misplaced.Add(at);
                if (shown != generation)
                    stale.Add(at);
            }

            H.Check($"{Name}_{step}_ContentMatchesGridCell", cells.Count > 0 && misplaced.Count == 0,
                $"{misplaced.Count} of {cells.Count} children show another cell's content: {Summarize(misplaced)}");
            H.Check($"{Name}_{step}_ContentIsCurrent", cells.Count > 0 && stale.Count == 0,
                $"{stale.Count} of {cells.Count} children still show an older generation than {generation}: {Summarize(stale)}");
        }

        private void CheckSurvivorsKeptTheirControl(string step, List<Cell> before, List<Cell> after)
        {
            static Dictionary<(int, int), UIElement> ByCell(List<Cell> cells)
            {
                var shown = cells
                    .Select(c => (c.Control, Parsed: TryParse(c.Text, out var row, out var column, out _), Row: row, Column: column))
                    .Where(c => c.Parsed);
                // Indexer writes, not ToDictionary: two controls showing one cell must not throw
                // here, because Verify has already failed the step for it.
                var map = new Dictionary<(int, int), UIElement>();
                foreach (var s in shown)
                    map[(s.Row, s.Column)] = s.Control;
                return map;
            }

            var was = ByCell(before);
            var now = ByCell(after);
            int survivors = 0;
            var remounted = new List<string>();
            foreach (var (cell, control) in was)
            {
                if (!now.TryGetValue(cell, out var current)) continue;
                survivors++;
                if (!ReferenceEquals(control, current) || _mounts.GetValueOrDefault(cell) != 1)
                    remounted.Add($"({cell.Item1},{cell.Item2}) x{_mounts.GetValueOrDefault(cell)}");
            }

            H.Check($"{Name}_{step}_SurvivorsKeptTheirControl", survivors == was.Count && remounted.Count == 0,
                $"{survivors} of {was.Count} cells survived; remounted: {Summarize(remounted)}");
        }

        private static string Summarize(List<string> items) =>
            items.Count == 0 ? "none" : string.Join("; ", items.Take(6)) + (items.Count > 6 ? "; ..." : "");
    }

    // ── Unmount callback ────────────────────────────────────────────────

    private sealed record ProbeElement(string Label) : Element;

    /// <summary>
    /// Registers <see cref="ProbeElement"/> as a Border around a TextBlock, the shape of the
    /// docking host, with an <c>unmount</c> callback that records the control it was handed.
    /// </summary>
    internal abstract class UnmountProbeFixture(Harness h) : SelfTestFixtureBase(h)
    {
        /// <summary>Every control a callback created, mount first, in creation order.</summary>
        protected List<WinXC.Border> Created { get; } = new();

        protected List<WinXC.Border> Unmounted { get; } = new();

        /// <summary>
        /// How the registration's <c>update</c> callback answers when the label changes:
        /// null (patched in place), the same control, or a new control.
        /// </summary>
        protected virtual UpdateResult OnRename => UpdateResult.PatchInPlace;

        protected enum UpdateResult { PatchInPlace, SameControl, NewControl }

        protected ReactorHost CreateHost()
        {
            var host = H.CreateHost();
            host.Reconciler.RegisterType<ProbeElement, WinXC.Border>(
                mount: (_, probe, _) => Create(probe.Label),
                update: (_, oldProbe, newProbe, border, _) =>
                {
                    if (oldProbe.Label == newProbe.Label) return null;
                    switch (OnRename)
                    {
                        case UpdateResult.NewControl:
                            return Create(newProbe.Label);
                        case UpdateResult.SameControl:
                            ((WinXC.TextBlock)border.Child).Text = newProbe.Label;
                            return border;
                        default:
                            ((WinXC.TextBlock)border.Child).Text = newProbe.Label;
                            return null;
                    }
                },
                unmount: (_, border) => Unmounted.Add(border));
            return host;
        }

        private WinXC.Border Create(string label)
        {
            var border = new WinXC.Border { Child = new WinXC.TextBlock { Text = label } };
            Created.Add(border);
            return border;
        }

        /// <summary>The Border hosting the TextBlock that shows <paramref name="label"/>, if mounted.</summary>
        protected WinXC.Border? FindProbe(string label) =>
            H.FindText(label)?.Parent as WinXC.Border;

        protected static string Describe(List<WinXC.Border> controls, List<WinXC.Border> created) =>
            controls.Count == 0
                ? "none"
                : string.Join(", ", controls.Select(c => created.IndexOf(c) is var i and >= 0 ? $"#{i}" : "foreign"));
    }

    /// <summary>
    /// Renames an unkeyed probe (an in-place update) and then removes it from its panel, which
    /// tears it down through <c>RemoveChildWithExitTransition</c> → <c>UnmountAndCollect</c>.
    /// Unkeyed and callback-free, the element fails every <c>NeedsTag</c> test, so only the
    /// registration's <c>unmount</c> callback can be the reason it is tagged.
    /// </summary>
    internal sealed class UnkeyedRemoved(Harness h) : UnmountProbeFixture(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Unmount_UnkeyedRemoved";
            var host = CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("rtt-unkeyed-a");
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element>
                {
                    Button("RTT Rename", () => setLabel("rtt-unkeyed-b")),
                    Button("RTT Remove", () => setShow(false)),
                };
                if (show) children.Add(new ProbeElement(label));
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_Mounted", Created.Count == 1 && FindProbe("rtt-unkeyed-a") is { } first
                && ReferenceEquals(first, Created[0]), $"created {Created.Count}");

            H.ClickButton("RTT Rename");
            H.Check($"{Name}_Renamed", await Harness.WaitFor(() => FindProbe("rtt-unkeyed-b") is not null));
            H.Check($"{Name}_UpdatedInPlace", Created.Count == 1 && ReferenceEquals(FindProbe("rtt-unkeyed-b"), Created[0]),
                $"created {Created.Count}");
            H.Check($"{Name}_NotUnmountedByUpdate", Unmounted.Count == 0,
                $"unmounted: {Describe(Unmounted, Created)}");

            H.ClickButton("RTT Remove");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => FindProbe("rtt-unkeyed-b") is null));
            H.Check($"{Name}_UnmountCallbackRanOnce",
                Unmounted.Count == 1 && ReferenceEquals(Unmounted[0], Created[0]),
                $"unmount callback saw: {Describe(Unmounted, Created)}; expected #0 exactly once");
        }
    }

    /// <summary>
    /// A keyed probe removed from a keyed panel. The key alone qualifies it for a tag.
    /// </summary>
    internal sealed class KeyedRemoved(Harness h) : UnmountProbeFixture(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Unmount_KeyedRemoved";
            var host = CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element>
                {
                    Button("RTT Remove keyed", () => setShow(false)).WithKey("remove"),
                    TextBlock("rtt-keyed-sibling").WithKey("sibling"),
                };
                if (show) children.Insert(1, new ProbeElement("rtt-keyed").WithKey("probe"));
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_Mounted", Created.Count == 1 && ReferenceEquals(FindProbe("rtt-keyed"), Created[0]),
                $"created {Created.Count}");

            H.ClickButton("RTT Remove keyed");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => FindProbe("rtt-keyed") is null));
            H.Check($"{Name}_SiblingKept", H.FindText("rtt-keyed-sibling") is not null);
            H.Check($"{Name}_UnmountCallbackRanOnce",
                Unmounted.Count == 1 && ReferenceEquals(Unmounted[0], Created[0]),
                $"unmount callback saw: {Describe(Unmounted, Created)}; expected #0 exactly once");
        }
    }

    /// <summary>
    /// The probe's slot switches to another element type, so the positional reconcile mounts
    /// the replacement and tears the probe down through <c>UnmountRecursive</c>.
    /// </summary>
    internal sealed class ReplacedByOtherType(Harness h) : UnmountProbeFixture(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Unmount_ReplacedByOtherType";
            var host = CreateHost();
            host.Mount(ctx =>
            {
                var (swapped, setSwapped) = ctx.UseState(false);
                return VStack(
                    Button("RTT Swap", () => setSwapped(true)),
                    swapped ? (Element)TextBlock("rtt-replacement") : new ProbeElement("rtt-replaced"));
            });

            await Harness.Render();
            H.Check($"{Name}_Mounted", Created.Count == 1 && ReferenceEquals(FindProbe("rtt-replaced"), Created[0]),
                $"created {Created.Count}");

            H.ClickButton("RTT Swap");
            H.Check($"{Name}_Swapped", await Harness.WaitFor(() => H.FindText("rtt-replacement") is not null
                && FindProbe("rtt-replaced") is null));
            H.Check($"{Name}_UnmountCallbackRanOnce",
                Unmounted.Count == 1 && ReferenceEquals(Unmounted[0], Created[0]),
                $"unmount callback saw: {Describe(Unmounted, Created)}; expected #0 exactly once");
        }
    }

    /// <summary>
    /// The probe is the host's root and the root is replaced, which is how an app swaps a whole
    /// scene: <c>Reconcile</c> → <c>ReconcileImperative</c> → <c>Unmount</c>.
    /// </summary>
    internal sealed class RootReplaced(Harness h) : UnmountProbeFixture(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Unmount_RootReplaced";
            var host = CreateHost();
            host.Mount(_ => new ProbeElement("rtt-root"));
            await Harness.Render();
            H.Check($"{Name}_Mounted", Created.Count == 1 && ReferenceEquals(FindProbe("rtt-root"), Created[0]),
                $"created {Created.Count}");

            host.Mount(_ => TextBlock("rtt-root-replaced"));
            H.Check($"{Name}_Swapped", await Harness.WaitFor(() => H.FindText("rtt-root-replaced") is not null));
            H.Check($"{Name}_UnmountCallbackRanOnce",
                Unmounted.Count == 1 && ReferenceEquals(Unmounted[0], Created[0]),
                $"unmount callback saw: {Describe(Unmounted, Created)}; expected #0 exactly once");
        }
    }

    private sealed record ContainerElement : Element;

    private sealed record DeclaredUIElementElement(string Label) : Element;

    /// <summary>
    /// <c>RegisterType</c> lets <c>TControl</c> be declared as <see cref="UIElement"/>. The tag
    /// lives on <see cref="FrameworkElement"/>, which in WinUI 3 every UIElement is, so such a
    /// registration is tagged and gets its <c>unmount</c> like any other.
    /// </summary>
    internal sealed class DeclaredUIElementControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_DeclaredUIElementControl";
            UIElement? mounted = null;
            var unmounted = new List<UIElement>();
            var host = H.CreateHost();
            host.Reconciler.RegisterType<DeclaredUIElementElement, UIElement>(
                mount: (_, el, _) =>
                {
                    UIElement text = new WinXC.TextBlock { Text = el.Label };
                    mounted = text;
                    return text;
                },
                update: (_, _, el, control, _) =>
                {
                    ((WinXC.TextBlock)control).Text = el.Label;
                    return null;
                },
                unmount: (_, control) => unmounted.Add(control));
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element> { Button("RTT Remove declared", () => setShow(false)) };
                if (show) children.Add(new DeclaredUIElementElement("rtt-declared-uielement"));
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_Tagged",
                mounted is FrameworkElement fe && Reconciler.GetElementTag(fe) is DeclaredUIElementElement,
                $"mounted {mounted?.GetType().Name ?? "nothing"}, tag {(mounted is FrameworkElement f ? Reconciler.GetElementTag(f)?.GetType().Name ?? "null" : "n/a")}");

            H.ClickButton("RTT Remove declared");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => H.FindText("rtt-declared-uielement") is null));
            H.Check($"{Name}_UnmountCallbackRanOnce", unmounted.Count == 1 && ReferenceEquals(unmounted[0], mounted),
                $"unmount callback ran {unmounted.Count} times");
        }
    }

    private sealed class ContainedProbe : Component
    {
        public static int Cleanups;

        public override Element Render()
        {
            UseEffect(() => { return () => { Cleanups++; }; });
            return TextBlock("rtt-contained-child");
        }
    }

    /// <summary>
    /// A registration mounts a child through the reconciler into its own Border, and its
    /// <c>unmount</c> tears that child down by unmounting the Border itself. That re-enters the
    /// unmount path for the same control, which must walk the Border's children once instead
    /// of calling <c>unmount</c> again, recursively and without end.
    /// </summary>
    internal sealed class UnmountCallbackUnmountsItsControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Unmount_CallbackUnmountsItsControl";
            ContainedProbe.Cleanups = 0;
            int unmounts = 0;
            var host = H.CreateHost();
            host.Reconciler.RegisterType<ContainerElement, WinXC.Border>(
                mount: (r, _, rerender) => new WinXC.Border { Child = r.Mount(Component<ContainedProbe>(), rerender)! },
                update: (_, _, _, _, _) => null,
                unmount: (r, border) =>
                {
                    unmounts++;
                    r.UnmountChild(border);
                });
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element> { Button("RTT Remove container", () => setShow(false)) };
                if (show) children.Add(new ContainerElement());
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_Mounted", H.FindText("rtt-contained-child") is not null);

            H.ClickButton("RTT Remove container");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => H.FindText("rtt-contained-child") is null));
            H.Check($"{Name}_UnmountCallbackRanOnce", unmounts == 1, $"unmount callback ran {unmounts} times");
            H.Check($"{Name}_ChildCleanedUpOnce", ContainedProbe.Cleanups == 1,
                $"the contained component's effect cleanup ran {ContainedProbe.Cleanups} times");
        }
    }

    /// <summary>
    /// An <c>update</c> callback that returns the control it was given, instead of null, patched
    /// that control in place. The child reconcilers read any non-null result as a replacement
    /// and unmount the control they hold, so without normalizing the result the now-reachable
    /// <c>unmount</c> callback would run against a control that stays mounted.
    /// </summary>
    internal sealed class UpdateReturnsSameControl(Harness h) : UnmountProbeFixture(h)
    {
        protected override UpdateResult OnRename => UpdateResult.SameControl;

        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Update_SameControlStaysMounted";
            var host = CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("rtt-same-a");
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element>
                {
                    Button("RTT Rename same", () => setLabel("rtt-same-b")),
                    Button("RTT Remove same", () => setShow(false)),
                };
                if (show) children.Add(new ProbeElement(label));
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_Mounted", Created.Count == 1 && ReferenceEquals(FindProbe("rtt-same-a"), Created[0]),
                $"created {Created.Count}");

            H.ClickButton("RTT Rename same");
            H.Check($"{Name}_Renamed", await Harness.WaitFor(() => FindProbe("rtt-same-b") is not null));
            H.Check($"{Name}_StillTheSameControl", ReferenceEquals(FindProbe("rtt-same-b"), Created[0]));
            H.Check($"{Name}_NotUnmountedByUpdate", Unmounted.Count == 0,
                $"unmount callback ran for a control that is still mounted: {Describe(Unmounted, Created)}");

            H.ClickButton("RTT Remove same");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => FindProbe("rtt-same-b") is null));
            H.Check($"{Name}_UnmountCallbackRanOnce",
                Unmounted.Count == 1 && ReferenceEquals(Unmounted[0], Created[0]),
                $"unmount callback saw: {Describe(Unmounted, Created)}; expected #0 exactly once");
        }
    }

    /// <summary>
    /// An <c>update</c> callback that returns a new control replaces the old one. The old control
    /// is unmounted at once; the replacement, which the callback built rather than
    /// <c>mount</c>, has to be tagged too, or its own <c>unmount</c> never runs.
    /// </summary>
    internal sealed class UpdateReturnsReplacement(Harness h) : UnmountProbeFixture(h)
    {
        protected override UpdateResult OnRename => UpdateResult.NewControl;

        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Update_ReplacementUnmountedLater";
            var host = CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("rtt-new-a");
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element>
                {
                    Button("RTT Rename new", () => setLabel("rtt-new-b")),
                    Button("RTT Remove new", () => setShow(false)),
                };
                if (show) children.Add(new ProbeElement(label));
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_Mounted", Created.Count == 1 && ReferenceEquals(FindProbe("rtt-new-a"), Created[0]),
                $"created {Created.Count}");

            H.ClickButton("RTT Rename new");
            H.Check($"{Name}_Renamed", await Harness.WaitFor(() => FindProbe("rtt-new-b") is not null));
            H.Check($"{Name}_ReplacementInstalled", Created.Count == 2 && ReferenceEquals(FindProbe("rtt-new-b"), Created[1]),
                $"created {Created.Count}");
            H.Check($"{Name}_ReplacedControlUnmounted",
                Unmounted.Count == 1 && ReferenceEquals(Unmounted[0], Created[0]),
                $"unmount callback saw: {Describe(Unmounted, Created)}; expected #0");

            H.ClickButton("RTT Remove new");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => FindProbe("rtt-new-b") is null));
            H.Check($"{Name}_ReplacementUnmounted",
                Unmounted.Count == 2 && ReferenceEquals(Unmounted[1], Created[1]),
                $"unmount callback saw: {Describe(Unmounted, Created)}; expected #0 then #1");
        }
    }

    // ── .Ref(...) on a registered type ──────────────────────────────────

    private sealed record PlainElement(string Label) : Element;

    /// <summary>
    /// <c>ElementRef.Current</c> is documented to go null when the referenced element unmounts
    /// without a replacement. The unmount path clears it through the element tag, and the
    /// registration has no <c>unmount</c>, so the reference modifier is the only reason to tag.
    /// </summary>
    internal sealed class RefClearedOnUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Ref_ClearedOnUnmount";
            WinXC.TextBlock? mounted = null;
            var target = new ElementRef();
            var host = H.CreateHost();
            host.Reconciler.RegisterType<PlainElement, WinXC.TextBlock>(
                mount: (_, plain, _) =>
                {
                    var text = new WinXC.TextBlock { Text = plain.Label };
                    mounted = text;
                    return text;
                },
                update: (_, _, plain, text, _) =>
                {
                    text.Text = plain.Label;
                    return null;
                });
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element> { Button("RTT Remove ref", () => setShow(false)) };
                if (show) children.Add(new PlainElement("rtt-ref-target").Ref(target));
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_RefSetOnMount", mounted is not null && ReferenceEquals(target.Current, mounted));

            H.ClickButton("RTT Remove ref");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => H.FindText("rtt-ref-target") is null));
            H.Check($"{Name}_RefClearedOnUnmount", target.Current is null,
                $"ref still points at a {target.Current?.GetType().Name ?? "null"} showing '{(target.Current as WinXC.TextBlock)?.Text}'");
        }
    }

    /// <summary>
    /// When <c>update</c> replaces the control, the reconciler points the element's ref at the
    /// replacement before the child reconciler unmounts the old control. Now that the old
    /// control is tagged, its unmount must leave a ref that has already moved on alone.
    /// </summary>
    internal sealed class RefFollowsReplacement(Harness h) : UnmountProbeFixture(h)
    {
        protected override UpdateResult OnRename => UpdateResult.NewControl;

        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_Ref_FollowsReplacement";
            var target = new ElementRef();
            var host = CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("rtt-refnew-a");
                var (show, setShow) = ctx.UseState(true);
                var children = new List<Element>
                {
                    Button("RTT Rename refnew", () => setLabel("rtt-refnew-b")),
                    Button("RTT Remove refnew", () => setShow(false)),
                };
                if (show) children.Add(new ProbeElement(label).Ref(target));
                return VStack(children.ToArray());
            });

            await Harness.Render();
            H.Check($"{Name}_RefSetOnMount", Created.Count == 1 && ReferenceEquals(target.Current, Created[0]),
                $"ref points at {DescribeRef(target)}");

            H.ClickButton("RTT Rename refnew");
            H.Check($"{Name}_Renamed", await Harness.WaitFor(() => FindProbe("rtt-refnew-b") is not null));
            H.Check($"{Name}_RefFollowsReplacement", Created.Count == 2 && ReferenceEquals(target.Current, Created[1]),
                $"ref points at {DescribeRef(target)}; expected #1");

            H.ClickButton("RTT Remove refnew");
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => FindProbe("rtt-refnew-b") is null));
            H.Check($"{Name}_RefClearedOnUnmount", target.Current is null,
                $"ref points at {DescribeRef(target)}");
        }

        private string DescribeRef(ElementRef target) => target.Current switch
        {
            null => "null",
            WinXC.Border border => Describe([border], Created),
            var other => other.GetType().Name,
        };
    }

    // ── A control that belongs to another element ───────────────────────

    private sealed record DelegatingElement(string Label, Action OnClick) : Element;

    /// <summary>
    /// A registration may hand back the control the reconciler mounted for another element,
    /// here a Button. That control's tag belongs to the <c>ButtonElement</c>: its click
    /// trampoline resolves the live element through it. Tagging it with the registered
    /// element would silence the click, so the reconciler leaves the tag alone. The
    /// registration has an <c>unmount</c> callback, so it would otherwise always tag.
    /// </summary>
    internal sealed class DelegatedControlKeepsItsTag(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string Name = "RegisterTypeTag_DelegatedControl_KeepsItsTag";
            int clicks = 0;
            static Element Inner(DelegatingElement el) => Button(el.Label, el.OnClick);

            var host = H.CreateHost();
            host.Reconciler.RegisterType<DelegatingElement, UIElement>(
                mount: (r, el, rerender) => r.Mount(Inner(el), rerender)!,
                update: (r, oldEl, newEl, control, rerender) => r.UpdateChild(Inner(oldEl), Inner(newEl), control, rerender),
                unmount: (_, _) => { });
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("rtt-delegated-a");
                return VStack(
                    Button("RTT Rename delegated", () => setLabel("rtt-delegated-b")),
                    new DelegatingElement(label, () => clicks++));
            });

            await Harness.Render();
            CheckButtonTag($"{Name}_Mount_KeepsButtonTag", "rtt-delegated-a");
            H.ClickButton("rtt-delegated-a");
            H.Check($"{Name}_Mount_ClickDispatches", await Harness.WaitFor(() => clicks == 1), $"clicks: {clicks}");

            H.ClickButton("RTT Rename delegated");
            H.Check($"{Name}_Renamed", await Harness.WaitFor(() => H.FindButton("rtt-delegated-b") is not null));
            CheckButtonTag($"{Name}_Update_KeepsButtonTag", "rtt-delegated-b");
            H.ClickButton("rtt-delegated-b");
            H.Check($"{Name}_Update_ClickDispatches", await Harness.WaitFor(() => clicks == 2), $"clicks: {clicks}");
        }

        private void CheckButtonTag(string check, string label)
        {
            var button = H.FindButton(label);
            var tag = button is null ? null : Reconciler.GetElementTag(button);
            H.Check(check, tag is ButtonElement,
                button is null ? "button not mounted" : $"tag is {tag?.GetType().Name ?? "null"}");
        }
    }

    // ── Which registered controls carry a tag ───────────────────────────

    private sealed record SlotElement(string Slot, int Generation) : Element;

    private sealed record ManualTagElement(string Label) : Element;

    /// <summary>
    /// Reads the tag directly. A keyed element and an element whose registration has an
    /// <c>unmount</c> callback are tagged at mount and re-tagged with the new element on update.
    /// An unkeyed, modifier-free element whose registration has no <c>unmount</c> is not tagged:
    /// nothing reads it back, and a <c>ReactorState</c> per leaf is the allocation #468 removed.
    /// A registration that tags by hand, as <c>XamlInterop</c> does, keeps working.
    /// </summary>
    internal sealed class TagOnlyWhereRead(Harness h) : SelfTestFixtureBase(h)
    {
        private const string Name = "RegisterTypeTag_TagOnlyWhereRead";

        private readonly Dictionary<string, WinXC.TextBlock> _slots = new();
        private readonly Dictionary<string, Element> _rendered = new();
        private WinXC.Border? _probe;
        private WinXC.TextBlock? _manual;

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Reconciler.RegisterType<SlotElement, WinXC.TextBlock>(
                mount: (_, slot, _) =>
                {
                    var text = new WinXC.TextBlock { Text = $"{slot.Slot}@{slot.Generation}" };
                    _slots[slot.Slot] = text;
                    return text;
                },
                update: (_, _, slot, text, _) =>
                {
                    text.Text = $"{slot.Slot}@{slot.Generation}";
                    return null;
                });
            host.Reconciler.RegisterType<ProbeElement, WinXC.Border>(
                mount: (_, probe, _) =>
                {
                    var border = new WinXC.Border { Child = new WinXC.TextBlock { Text = probe.Label } };
                    _probe = border;
                    return border;
                },
                update: (_, _, probe, border, _) =>
                {
                    ((WinXC.TextBlock)border.Child).Text = probe.Label;
                    return null;
                },
                unmount: (_, _) => { });
            host.Reconciler.RegisterType<ManualTagElement, WinXC.TextBlock>(
                mount: (_, el, _) =>
                {
                    var text = new WinXC.TextBlock { Text = el.Label };
                    Reconciler.SetElementTag(text, el);
                    _manual = text;
                    return text;
                },
                update: (_, _, el, text, _) =>
                {
                    text.Text = el.Label;
                    Reconciler.SetElementTag(text, el);
                    return null;
                });

            host.Mount(ctx =>
            {
                var (generation, setGeneration) = ctx.UseState(0);
                return VStack(
                    Button("RTT Rerender", () => setGeneration(1)),
                    Track("plain", new SlotElement("plain", generation)),
                    Track("keyed", new SlotElement("keyed", generation).WithKey("keyed")),
                    Track("unmount", new ProbeElement($"rtt-with-unmount@{generation}")),
                    Track("manual", new ManualTagElement($"rtt-manual@{generation}")));
            });

            await Harness.Render();
            Verify("Mount");

            H.ClickButton("RTT Rerender");
            H.Check($"{Name}_Rerendered", await Harness.WaitFor(() =>
                _slots["plain"].Text == "plain@1" && _slots["keyed"].Text == "keyed@1"
                && H.FindText("rtt-with-unmount@1") is not null && _manual?.Text == "rtt-manual@1"));
            Verify("Update");
        }

        private Element Track(string slot, Element element)
        {
            _rendered[slot] = element;
            return element;
        }

        private void Verify(string step)
        {
            H.Check($"{Name}_{step}_UnkeyedWithoutUnmount_NoReactorState",
                !Reconciler.TryGetReactorState(_slots["plain"], out var state),
                $"allocated a ReactorState tagged with {state?.Element?.GetType().Name ?? "null"}");
            CheckTag($"{Name}_{step}_Keyed_TaggedWithCurrentElement", _slots["keyed"], _rendered["keyed"]);
            CheckTag($"{Name}_{step}_WithUnmount_TaggedWithCurrentElement", _probe, _rendered["unmount"]);
            CheckTag($"{Name}_{step}_ManualTag_TaggedWithCurrentElement", _manual, _rendered["manual"]);
        }

        private void CheckTag(string check, FrameworkElement? control, Element expected)
        {
            var tag = control is null ? null : Reconciler.GetElementTag(control);
            H.Check(check, control is not null && ReferenceEquals(tag, expected),
                control is null
                    ? "control never mounted"
                    : $"tag is {(tag is null ? "null" : ReferenceEquals(tag, expected) ? "current" : "a stale or foreign " + tag.GetType().Name)}");
        }
    }
}
