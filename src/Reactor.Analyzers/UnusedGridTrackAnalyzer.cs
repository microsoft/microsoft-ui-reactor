using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_GRID_001</c> — flags a declared <c>Grid</c> track (a <see cref="GridSize"/>
/// in the <c>columns</c>/<c>rows</c> array of the typed
/// <c>Factories.Grid(GridSize[], GridSize[], params Element?[])</c> factory) that no child
/// occupies: the "unused column"/"unused row" symptom (layout.md:555, spec 060 §12).
/// </summary>
/// <remarks>
/// <para>
/// A child's cell is the outermost <c>.Grid(row:, column:, rowSpan:, columnSpan:)</c> modifier
/// in its chain (<c>GridExtensions.Grid</c>, GridExtensions.cs); a child with no <c>.Grid()</c>
/// defaults to <c>(row 0, column 0)</c> (<c>GridAttached</c>, Element.cs). A track index that
/// is covered by no child's [row..row+rowSpan-1] × [column..column+columnSpan-1] range is
/// unused. Intent-heavy — ship at Warning, <b>no auto-fix</b> (the author may want to remove the
/// track or place a child there).
/// </para>
/// <para>
/// <b>False-positive discipline (the rule only fires when it can prove a track is unused).</b>
/// Because occupancy is a negative claim ("no child is here"), the analyzer bails — reports
/// nothing for the whole grid — the moment any child's placement is not statically visible in
/// the same call:
/// <list type="bullet">
/// <item>a bare variable / parameter / field child (e.g. <c>titleBar</c>) may have been placed
/// with <c>.Grid(...)</c> elsewhere, so its cell is unknown;</item>
/// <item>a conditional child (<c>cond ? a.Grid(col:2) : b.Grid(col:2)</c>) has a
/// branch-dependent cell;</item>
/// <item>a non-constant placement arg (<c>.Grid(column: i)</c> / <c>columnSpan: n</c>) could
/// cover the very track we would flag;</item>
/// <item>a spread/variable <c>columns</c>/<c>rows</c> array or a children array we cannot
/// enumerate hides both the track count and the placements;</item>
/// <item>a receiverless call that is <b>not</b> a Reactor DSL factory (e.g. a
/// <c>Cell(r, c) =&gt; e.Grid(r, c)</c> helper that hides its <c>.Grid(...)</c> inside its
/// body) — its cell is not provable, so it is treated as opaque, not as <c>(0,0)</c>.</item>
/// </list>
/// A child with no <c>.Grid()</c> only counts as the framework default <c>(0,0)</c> when its
/// root is a known <c>Microsoft.UI.Reactor.Factories</c> factory (<c>Text(...)</c>,
/// <c>Button(...)</c>, a nested <c>Grid(...)</c>, …) — those return a fresh, unplaced element,
/// so <c>(0,0)</c> is provable, matching the documented "a child with no explicit column is at
/// column 0" model. The two axes are judged independently: an unused row is still reported even
/// when the <c>columns</c> array is opaque, and vice versa.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnusedGridTrackAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_GRID_001";

    private const string FactoriesType = "Microsoft.UI.Reactor.Factories";
    private const string GridExtensionsType = "Microsoft.UI.Reactor.GridExtensions";
    private const string GridSizeType = "Microsoft.UI.Reactor.GridSize";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Grid declares a track that no child occupies",
        "This Grid declares {0} {1} (0-based) but no child is placed in it. Remove the unused track or place a child there.",
        "Reactor.Layout",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "The typed Grid factory sizes its tracks from the columns/rows arrays, and each child " +
            "picks a cell via the .Grid(row:, column:, rowSpan:, columnSpan:) modifier (a child " +
            "with no .Grid() defaults to row 0, column 0). A declared track that no child's " +
            "row/column range covers renders empty — usually a leftover track after a child was " +
            "removed, or a child that was never placed. The analyzer only fires when every " +
            "child's placement is statically visible in the same call: it stays silent on grids " +
            "with variable/conditional children, dynamic (non-constant) placement, or " +
            "spread/variable track arrays, because it cannot then prove the track is unused.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Cheap syntactic gate — the callee is named "Grid" and has at least columns + rows.
        if (GetInvokedSimpleName(invocation.Expression) != "Grid")
            return;
        if (invocation.ArgumentList.Arguments.Count < 2)
            return;

        // Semantic confirm — the typed Reactor Grid factory (not the .Grid modifier, GridView,
        // a user's Grid, or the obsolete string-track overload).
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation op)
            return;
        if (!IsTypedGridFactory(op.TargetMethod))
            return;

        IOperation? columnsVal = null;
        IOperation? rowsVal = null;
        IOperation? childrenVal = null;
        foreach (var arg in op.Arguments)
        {
            if (arg.Parameter is null)
                continue;
            if (arg.Parameter.IsParams)
                childrenVal = arg.Value;
            else if (arg.Parameter.Name == "columns")
                columnsVal = arg.Value;
            else if (arg.Parameter.Name == "rows")
                rowsVal = arg.Value;
        }

        if (childrenVal is null)
            return;

        // Count the declared tracks up front (per axis, independently). A track array that is a
        // spread/variable — anything we cannot enumerate — leaves that axis unknown, and an axis
        // we cannot count is one we never report. If neither axis is countable there is nothing
        // to say. Counting first also bounds the occupancy loops below by the declared track
        // count, so a hostile/typo span (e.g. columnSpan: int.MaxValue) can never drive an
        // unbounded loop that would hang the compiler.
        var hasColumns = TryGetTrackLocations(columnsVal, out var columnLocations);
        var hasRows = TryGetTrackLocations(rowsVal, out var rowLocations);
        if (!hasColumns && !hasRows)
            return;

        var columnCount = hasColumns ? columnLocations.Count : 0;
        var rowCount = hasRows ? rowLocations.Count : 0;

        // Children must be an inline array we can fully enumerate. A variable/opaque children
        // array means we cannot see every placement → cannot prove any track unused.
        if (!TryGetChildOperations(childrenVal, out var children))
            return;
        if (children.Count == 0)
            return;

        // Resolve every child's placement. A single opaque child aborts the whole grid.
        var occupiedRows = new HashSet<int>();
        var occupiedCols = new HashSet<int>();
        foreach (var child in children)
        {
            var placement = ResolvePlacement(child);
            if (placement.Kind == PlacementKind.Bail)
                return;
            if (placement.Kind == PlacementKind.Skip)
                continue;

            // A start index at or beyond the declared track count is out of range on a countable
            // axis. WinUI clamps such a child into the last track, so we can no longer prove which
            // in-range track it does NOT occupy — bail the whole grid rather than risk a false
            // "unused" claim. (An in-range start whose span overshoots is fine — the span clamps.)
            if (hasColumns && placement.Column >= columnCount)
                return;
            if (hasRows && placement.Row >= rowCount)
                return;

            if (hasColumns)
                MarkOccupied(occupiedCols, placement.Column, placement.ColumnSpan, columnCount);
            if (hasRows)
                MarkOccupied(occupiedRows, placement.Row, placement.RowSpan, rowCount);
        }

        if (hasColumns)
            ReportUnusedTracks(context, columnLocations, occupiedCols, "column");
        if (hasRows)
            ReportUnusedTracks(context, rowLocations, occupiedRows, "row");
    }

    // Marks the [start, start + span - 1] range within the declared track bounds [0, count - 1].
    // The upper bound is computed in long and clamped, so a span that overshoots (or overflows
    // int) can never iterate more than <paramref name="count"/> times.
    private static void MarkOccupied(HashSet<int> occupied, int start, int span, int count)
    {
        var from = System.Math.Max(0, start);
        var to = (int)System.Math.Min(count - 1L, (long)start + span - 1);
        for (var i = from; i <= to; i++)
            occupied.Add(i);
    }

    private static void ReportUnusedTracks(
        SyntaxNodeAnalysisContext context,
        IReadOnlyList<Location> trackLocations,
        HashSet<int> occupied,
        string axis)
    {
        for (var i = 0; i < trackLocations.Count; i++)
        {
            if (!occupied.Contains(i))
                context.ReportDiagnostic(Diagnostic.Create(Rule, trackLocations[i], axis, i));
        }
    }

    // ── Grid factory / modifier recognition ────────────────────────────────

    private static bool IsTypedGridFactory(IMethodSymbol? method)
    {
        if (method is null || method.Name != "Grid")
            return false;
        if (method.ContainingType?.ToDisplayString() != FactoriesType)
            return false;

        var ps = method.Parameters;
        if (ps.Length < 3)
            return false;
        if (ps[0].Name != "columns" || ps[1].Name != "rows" || !ps[ps.Length - 1].IsParams)
            return false;

        // Typed overload only — a non-GridSize Grid shape is out of scope.
        return ps[0].Type is IArrayTypeSymbol { ElementType: INamedTypeSymbol element }
            && element.ToDisplayString() == GridSizeType;
    }

    private static bool IsGridModifier(IMethodSymbol? method)
    {
        var m = method?.ReducedFrom ?? method;
        return m is not null
            && m.Name == "Grid"
            && m.ContainingType?.ToDisplayString() == GridExtensionsType;
    }

    // A receiverless call whose target is a Reactor DSL factory (Factories.*) returns a fresh,
    // unplaced element, so its cell is provably the framework default (0,0). An arbitrary
    // receiverless helper could hide a .Grid(...) inside its body, so it is NOT assumed to be
    // (0,0) — it is treated as opaque and bails the grid.
    private static bool IsReactorFactory(IMethodSymbol? method) =>
        method?.ContainingType?.ToDisplayString() == FactoriesType;

    // ── Child placement resolution ─────────────────────────────────────────

    private enum PlacementKind
    {
        /// <summary>A statically known cell range.</summary>
        Known,

        /// <summary>A <c>null</c> child (filtered at runtime) — occupies nothing.</summary>
        Skip,

        /// <summary>Placement not provable — abort the whole grid.</summary>
        Bail,
    }

    private readonly struct Placement
    {
        public readonly PlacementKind Kind;
        public readonly int Row;
        public readonly int Column;
        public readonly int RowSpan;
        public readonly int ColumnSpan;

        private Placement(PlacementKind kind, int row, int column, int rowSpan, int columnSpan)
        {
            Kind = kind;
            Row = row;
            Column = column;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
        }

        public static readonly Placement Bail = new(PlacementKind.Bail, 0, 0, 0, 0);
        public static readonly Placement Skip = new(PlacementKind.Skip, 0, 0, 0, 0);
        public static readonly Placement Default = Cell(0, 0, 1, 1);

        public static Placement Cell(int row, int column, int rowSpan, int columnSpan) =>
            new(PlacementKind.Known, row, column, rowSpan, columnSpan);
    }

    /// <summary>
    /// Walk a child's fluent chain to its outermost <c>.Grid(...)</c> placement (the last one
    /// applied wins). If the chain has no <c>.Grid()</c>, only a Reactor DSL factory root
    /// (<c>Factories.*</c>) is provably the default <c>(0,0)</c>; anything else — a variable/field
    /// reference, a conditional, a raw object creation, an unknown receiverless helper, or a
    /// non-constant placement argument — is not provable and returns <see cref="Placement.Bail"/>.
    /// </summary>
    private static Placement ResolvePlacement(IOperation childOperation)
    {
        var current = Unwrap(childOperation);

        while (true)
        {
            if (current is IInvocationOperation invocation)
            {
                if (IsGridModifier(invocation.TargetMethod))
                    return ReadGridPlacement(invocation);

                var receiver = invocation.Instance;
                if (receiver is null
                    && invocation.TargetMethod.IsExtensionMethod
                    && invocation.Arguments.Length > 0)
                {
                    // Extension methods surface in unreduced form: the receiver is argument 0.
                    receiver = invocation.Arguments[0].Value;
                }

                if (receiver is null)
                {
                    // No receiver → a static call. A Reactor DSL factory (Text(...), a nested
                    // Grid(...), Component<..>(..)) returns a fresh unplaced element → default cell.
                    // Any other receiverless call could hide a .Grid(...) → not provable.
                    return IsReactorFactory(invocation.TargetMethod)
                        ? Placement.Default
                        : Placement.Bail;
                }

                current = Unwrap(receiver);
                continue;
            }

            // Any compile-time-constant null child (the literal `null`, `default`,
            // `default(Element)`, or a const-null reference) is filtered at runtime, so it
            // occupies nothing — skip it rather than bailing an otherwise-provable grid.
            if (current.ConstantValue is { HasValue: true, Value: null })
                return Placement.Skip;

            // Variable/parameter/field/property reference, conditional, object creation, etc. —
            // the cell is not provable from this call site.
            return Placement.Bail;
        }
    }

    private static Placement ReadGridPlacement(IInvocationOperation gridModifier)
    {
        int row = 0, column = 0, rowSpan = 1, columnSpan = 1;

        foreach (var arg in gridModifier.Arguments)
        {
            // Omitted optionals keep their defaults; only explicit args override. The extension
            // receiver ("el") and any other parameter are ignored.
            if (arg.ArgumentKind != ArgumentKind.Explicit)
                continue;

            switch (arg.Parameter?.Name)
            {
                case "row":
                    if (!TryGetConstInt(arg.Value, out row)) return Placement.Bail;
                    break;
                case "column":
                    if (!TryGetConstInt(arg.Value, out column)) return Placement.Bail;
                    break;
                case "rowSpan":
                    if (!TryGetConstInt(arg.Value, out rowSpan)) return Placement.Bail;
                    break;
                case "columnSpan":
                    if (!TryGetConstInt(arg.Value, out columnSpan)) return Placement.Bail;
                    break;
            }
        }

        // A negative index or a non-positive span is invalid/ambiguous — bail. (An oversized span
        // and an out-of-range start are handled by the caller: the span is clamped to the declared
        // range when marking occupancy, and an out-of-range start bails the whole grid there.)
        if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1)
            return Placement.Bail;

        return Placement.Cell(row, column, rowSpan, columnSpan);
    }

    // ── Track array enumeration ────────────────────────────────────────────

    private static bool TryGetChildOperations(IOperation childrenValue, out IReadOnlyList<IOperation> children)
    {
        // Both the params-expanded form (Grid(cols, rows, a, b)) and an explicit inline array
        // (Grid(cols, rows, new Element[] { a, b })) surface as an array creation with an
        // initializer we can enumerate. A variable array, Array.Empty<>(), or a spread does not.
        if (Unwrap(childrenValue) is IArrayCreationOperation { Initializer: { } initializer })
        {
            children = initializer.ElementValues;
            return true;
        }

        children = System.Array.Empty<IOperation>();
        return false;
    }

    private static bool TryGetTrackLocations(IOperation? trackValue, out IReadOnlyList<Location> locations)
    {
        locations = System.Array.Empty<Location>();

        if (trackValue is not null)
        {
            switch (Unwrap(trackValue).Syntax)
            {
                case CollectionExpressionSyntax collection
                    when collection.Elements.All(e => e is ExpressionElementSyntax):
                    // [GridSize.Auto, GridSize.Star()] — a spread (..x) fails the guard and falls
                    // through to the non-reportable return below.
                    locations = collection.Elements.Select(e => e.GetLocation()).ToArray();
                    break;

                case ArrayCreationExpressionSyntax { Initializer: { } arrayInit }:
                    locations = arrayInit.Expressions.Select(e => e.GetLocation()).ToArray();
                    break;

                case ImplicitArrayCreationExpressionSyntax { Initializer: { } implicitInit }:
                    locations = implicitInit.Expressions.Select(e => e.GetLocation()).ToArray();
                    break;
            }
        }

        // An empty declared track list ([]) has nothing to report AND must not poison the other
        // axis's analysis or the out-of-range guard (columnCount 0 would make 0 >= 0 bail every
        // child) — treat it as a non-reportable axis, exactly like an opaque/variable track array.
        return locations.Count > 0;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static bool TryGetConstInt(IOperation operation, out int value)
    {
        var constant = operation.ConstantValue;
        if (constant.HasValue && constant.Value is int i)
        {
            value = i;
            return true;
        }

        value = 0;
        return false;
    }

    private static string? GetInvokedSimpleName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name switch
        {
            GenericNameSyntax genericMember => genericMember.Identifier.ValueText,
            { } simple => simple.Identifier.ValueText,
        },
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => null,
    };
}
