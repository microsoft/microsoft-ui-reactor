using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Keyed children that mount through a wrapper must keep their identity through
/// <c>ChildReconciler</c>'s keyed-middle pass. The wrappers are <c>Component</c>,
/// <c>RenderEachTime</c>, <c>Memo(render, deps)</c>, <c>ErrorBoundary</c>, and the transparent
/// <c>Memo(key, factory)</c>.
///
/// <para>That pass finds each surviving child by reading the key off the element tag of the live
/// control (<c>ReconcileKeyedMiddle</c>). A survivor whose control has no tag, or a tag without
/// the child's key, is skipped: it is neither moved nor patched. The Grid then re-applies
/// <c>Grid.Row</c>/<c>Grid.Column</c> by index, so the stale control shows up at another child's
/// cell. That is what the Minesweeper sample did when it switched from Beginner (9×9) to Expert
/// (16×30): 72 cells showed Beginner content at Expert positions. #1147 fixed it for the four
/// Border-backed wrappers by tagging the wrapper, and these fixtures keep that fix in place.
/// <c>Memo(key, …)</c> mounts no wrapper, so its control carries the inner element's tag; the
/// inner element now inherits the wrapper's key (<c>Reconciler.WithWrapperKey</c>).</para>
///
/// <para>Each fixture mounts a 3×3 Grid and grows it to 4×5, which is the Minesweeper shape at a
/// smaller size. The first row stays put as the keyed prefix, and rows 1 and 2 survive in the
/// middle at new indices, with new cells inserted between and after them. The fixture then
/// reverses the child order, so every survivor has to move. After each step, every child's
/// content must name its own Grid cell and show the current generation, and every survivor
/// must still be the same control.</para>
/// </summary>
internal static class KeyedWrapperChildrenFixtures
{
    private sealed record CellProps(int Row, int Column, int Generation);

    private sealed class CellProbe : Component<CellProps>
    {
        public override Element Render() => TextBlock(Label(Props.Row, Props.Column, Props.Generation));
    }

    private static string Label(int row, int column, int generation) => $"{row},{column}@{generation}";

    private readonly record struct Cell(UIElement Control, int GridRow, int GridColumn, string? Text);

    internal abstract class GrowAndReverseFixture(Harness h) : SelfTestFixtureBase(h)
    {
        /// <summary>Stem for this fixture's check names.</summary>
        protected abstract string Name { get; }

        /// <summary>
        /// Builds one cell's wrapper. The fixture adds the key and the Grid position, and the
        /// wrapped content must be a TextBlock reading <c>row,column</c>, optionally followed by
        /// <c>@generation</c>.
        /// </summary>
        protected abstract Element Cell(int row, int column, int generation);

        /// <summary>
        /// False when the content is a pure function of the cell. A <c>Memo(key, …)</c> keyed on
        /// the cell must not be re-invoked for a survivor, so it cannot show the generation.
        /// </summary>
        protected virtual bool RendersGeneration => true;

        private readonly Dictionary<(int Row, int Column), int> _builds = new();

        /// <summary>
        /// Counts a content build for a <c>Memo(key, …)</c> factory, which runs once per mount.
        /// Its content is a pooled control, and the pool is LIFO, so a remounted survivor can get
        /// its own control back and pass the reference check; a second build still gives it away.
        /// </summary>
        protected Element CountBuild(int row, int column, Element content)
        {
            _builds[(row, column)] = _builds.GetValueOrDefault((row, column)) + 1;
            return content;
        }

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (step, setStep) = ctx.UseState(0);
                var (rows, columns) = step == 0 ? (3, 3) : (4, 5);
                var cells = new List<Element>(rows * columns);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < columns; c++)
                        cells.Add(Cell(r, c, step).Grid(row: r, column: c).WithKey($"cell-{r}-{c}"));
                if (step == 2)
                    cells.Reverse();

                return VStack(
                    Button("Grow", () => setStep(1)),
                    Button("Reverse", () => setStep(2)),
                    Grid(Tracks(columns), Tracks(rows), cells.ToArray()).AutomationId("kwc_grid"));
            });

            await Harness.Render();
            var mounted = Snapshot();
            Verify("Mount", mounted, rows: 3, columns: 3, generation: 0);

            H.ClickButton("Grow");
            await Harness.Render();
            var grown = Snapshot();
            Verify("Grow", grown, rows: 4, columns: 5, generation: 1);
            CheckSurvivorsKeptTheirControl("Grow", mounted, grown);

            H.ClickButton("Reverse");
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
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(g) == "kwc_grid");
            var cells = new List<Cell>();
            if (grid is null) return cells;
            foreach (var child in grid.Children)
            {
                cells.Add(new Cell(
                    child,
                    WinXC.Grid.GetRow((FrameworkElement)child),
                    WinXC.Grid.GetColumn((FrameworkElement)child),
                    TextOf(child)));
            }
            return cells;
        }

        // Component, RenderEachTime, Memo(deps) and ErrorBoundary mount a wrapper Border
        // around their content; Memo(key, …) mounts its content directly.
        private static string? TextOf(UIElement control)
        {
            while (control is WinXC.Border { Child: { } inner })
                control = inner;
            return (control as WinXC.TextBlock)?.Text;
        }

        private static bool TryParse(string? text, out int row, out int column, out int? generation)
        {
            row = column = -1;
            generation = null;
            if (text is null) return false;
            var at = text.IndexOf('@');
            var cell = at < 0 ? text : text[..at];
            if (at >= 0)
            {
                if (!int.TryParse(text[(at + 1)..], out var g)) return false;
                generation = g;
            }
            var comma = cell.IndexOf(',');
            return comma > 0
                && int.TryParse(cell[..comma], out row)
                && int.TryParse(cell[(comma + 1)..], out column);
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
                if (RendersGeneration && shown != generation)
                    stale.Add(at);
            }

            H.Check($"{Name}_{step}_ContentMatchesGridCell", cells.Count > 0 && misplaced.Count == 0,
                $"{misplaced.Count} of {cells.Count} children show another cell's content: {Summarize(misplaced)}");
            if (RendersGeneration)
            {
                H.Check($"{Name}_{step}_ContentIsCurrent", cells.Count > 0 && stale.Count == 0,
                    $"{stale.Count} of {cells.Count} children still show an older generation than {generation}: {Summarize(stale)}");
            }
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
                if (!ReferenceEquals(control, current) || _builds.GetValueOrDefault(cell) > 1)
                    remounted.Add($"({cell.Item1},{cell.Item2})");
            }

            H.Check($"{Name}_{step}_SurvivorsKeptTheirControl", survivors == was.Count && remounted.Count == 0,
                $"{survivors} of {was.Count} cells survived; remounted: {Summarize(remounted)}");
        }

        private static string Summarize(List<string> items) =>
            items.Count == 0 ? "none" : string.Join("; ", items.Take(6)) + (items.Count > 6 ? "; ..." : "");
    }

    internal sealed class ComponentChildren(Harness h) : GrowAndReverseFixture(h)
    {
        protected override string Name => "KeyedWrapper_Component";

        protected override Element Cell(int row, int column, int generation) =>
            Component<CellProbe, CellProps>(new CellProps(row, column, generation));
    }

    internal sealed class RenderEachTimeChildren(Harness h) : GrowAndReverseFixture(h)
    {
        protected override string Name => "KeyedWrapper_RenderEachTime";

        protected override Element Cell(int row, int column, int generation) =>
            RenderEachTime(_ => TextBlock(Label(row, column, generation)));
    }

    internal sealed class MemoDepsChildren(Harness h) : GrowAndReverseFixture(h)
    {
        protected override string Name => "KeyedWrapper_MemoDeps";

        protected override Element Cell(int row, int column, int generation) =>
            Memo(_ => TextBlock(Label(row, column, generation)), row, column, generation);
    }

    internal sealed class ErrorBoundaryChildren(Harness h) : GrowAndReverseFixture(h)
    {
        protected override string Name => "KeyedWrapper_ErrorBoundary";

        protected override Element Cell(int row, int column, int generation) =>
            ErrorBoundary(TextBlock(Label(row, column, generation)), _ => TextBlock("error"));
    }

    internal sealed class KeyedMemoChildren(Harness h) : GrowAndReverseFixture(h)
    {
        protected override string Name => "KeyedWrapper_KeyedMemo";

        protected override bool RendersGeneration => false;

        protected override Element Cell(int row, int column, int generation) =>
            Memo((row, column), () => CountBuild(row, column, TextBlock($"{row},{column}")));
    }

    // The factory output carries its own key. The parent diffs on the wrapper's key, so that is
    // the key the mounted control has to answer to.
    internal sealed class KeyedMemoInnerKeyChildren(Harness h) : GrowAndReverseFixture(h)
    {
        protected override string Name => "KeyedWrapper_KeyedMemoInnerKey";

        protected override bool RendersGeneration => false;

        protected override Element Cell(int row, int column, int generation) =>
            Memo((row, column), () => CountBuild(row, column, TextBlock($"{row},{column}").WithKey($"inner-{row}-{column}")));
    }
}
