using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// Debug-time detector for the <c>HStack</c>/<c>VStack</c>-in-a-<c>Grid</c>-<c>Auto</c>-track
/// layout footgun (issue #345).
/// </summary>
/// <remarks>
/// <para>
/// A <c>StackPanel</c> placed in a <c>Grid</c> <see cref="GridSize.Auto"/> track measures its
/// children with <em>infinite</em> available space on the stacking axis. Children that rely on
/// stretch sizing (no explicit size) therefore report a desired size of <c>0</c>, the stack
/// accumulates <c>0</c>, and the whole row/column silently collapses to <c>0×0</c>. This is
/// generic XAML measure semantics — not a Reactor reconciler bug — but the failure is invisible:
/// the developer just sees their content vanish.
/// </para>
/// <para>
/// Reactor can detect it because it holds the declarative tree: it can correlate "an <c>HStack</c>
/// was just placed at <c>Grid</c> column N" with "column N's track is <c>Auto</c>". This detector
/// runs at mount <em>and</em> update (see <c>Reconciler.Mount</c> / <c>Reconciler.Update</c>, so a
/// dynamic track or size change can still be caught) and emits a one-time warning when <b>all</b>
/// of the following hold for a stack child:
/// </para>
/// <list type="bullet">
///   <item>it lands in a <c>Grid</c> track that is <c>Auto</c> on the stacking axis (column for
///   <c>HStack</c>, row for <c>VStack</c>),</item>
///   <item>neither the stack nor any single-child wrapper (e.g. a <c>Border</c>) around it carries
///   an explicit size on that axis (a fixed <c>Width</c>/<c>Height</c> or a <c>MinWidth</c>/
///   <c>MinHeight</c>, both of which clamp the Measure pass), and</item>
///   <item>none of the stack's first-generation children carry an explicit size (fixed or minimum)
///   on that axis.</item>
/// </list>
/// <para>
/// Detection is gated by <see cref="ReactorFeatureFlags.WarnLayoutFootguns"/> (always on in
/// <c>DEBUG</c>); each distinct placement warns at most once per process. The detection only
/// inspects the immutable element tree (modifier chain + grid definition) — it never reads back
/// realized control dimensions, so it stays cheap and side-effect free.
/// </para>
/// </remarks>
internal static class LayoutFootgunDetector
{
    /// <summary>
    /// <c>true</c> when compiled in a <c>DEBUG</c> configuration. A compile-time constant so the
    /// JIT can fold the call-site guard away entirely in <c>Release</c> builds.
    /// </summary>
    internal const bool AlwaysOnInDebug =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Optional diagnostic sink. When set, each warning message is delivered here (in addition to
    /// the <c>Debug</c> channel). Used by tests and devtools surfaces to observe
    /// warnings without scraping the debug channel.
    /// </summary>
#pragma warning disable CS0649 // Assigned via InternalsVisibleTo (tests / devtools), never inside this assembly.
    internal static Action<string>? Sink;
#pragma warning restore CS0649

    private static readonly HashSet<string> s_warned = new(StringComparer.Ordinal);
    private static readonly object s_gate = new();

    /// <summary>
    /// Inspects a freshly mounted element for the Grid-<c>Auto</c>-track stack footgun and emits a
    /// one-time warning if it matches. A no-op for non-<see cref="GridElement"/> elements and when
    /// detection is disabled (Release build with the flag off).
    /// </summary>
    internal static void Inspect(Element element)
    {
        if (!(AlwaysOnInDebug || ReactorFeatureFlags.WarnLayoutFootguns))
            return;
        if (element is not GridElement grid)
            return;

        InspectGrid(grid);
    }

    /// <summary>
    /// Core detection over a <see cref="GridElement"/>. Exposed to tests; callers on the mount hot
    /// path should go through <see cref="Inspect"/> so the enable check is honored.
    /// </summary>
    internal static void InspectGrid(GridElement grid)
    {
        var children = grid.Children;
        if (children is null || children.Length == 0)
            return;

        var columns = grid.Definition?.Columns;
        var rows = grid.Definition?.Rows;

        foreach (var child in children.Where(static c => c is not null))
        {
            // Walk down through single-child wrappers (e.g. Border) to the stack, accumulating any
            // explicit size seen along the way. Wrapping a collapsing stack in a Border does NOT
            // fix the collapse (the Border sizes to its 0-sized child), so we keep descending.
            var current = child;
            bool chainHasWidth = false;
            bool chainHasHeight = false;
            string? locationKey = null;

            while (true)
            {
                locationKey ??= current.Key;
                var mods = current.Modifiers;
                // MinWidth/MinHeight clamp the desired size during Measure too, so an explicit
                // minimum on the stack (or any wrapper) prevents the stretch→0 collapse just like
                // a fixed Width/Height does. Treat both as an explicit size to avoid false warnings.
                if (mods?.Width is not null || mods?.MinWidth is not null) chainHasWidth = true;
                if (mods?.Height is not null || mods?.MinHeight is not null) chainHasHeight = true;

                if (current is StackElement stack)
                {
                    InspectStack(grid, child, stack, columns, rows, chainHasWidth, chainHasHeight, locationKey);
                    break;
                }

                if (current is BorderElement { Child: { } inner })
                {
                    current = inner;
                    continue;
                }

                // Any other leaf/wrapper: not a stack we model — leave it alone.
                break;
            }
        }
    }

    private static void InspectStack(
        GridElement grid,
        Element gridChild,
        StackElement stack,
        GridSize[]? columns,
        GridSize[]? rows,
        bool chainHasWidth,
        bool chainHasHeight,
        string? locationKey)
    {
        var stackChildren = stack.Children;
        if (stackChildren is null || stackChildren.Length == 0)
            return; // An empty stack collapsing to 0 is expected, not a footgun.

        bool horizontal = stack.Orientation == WinUI.Orientation.Horizontal;

        var placement = gridChild.GetAttached<GridAttached>();
        int row = placement?.Row ?? 0;
        int col = placement?.Column ?? 0;

        if (horizontal)
        {
            if (!TrackIsAuto(columns, col)) return;
            if (chainHasWidth) return;
            if (AnyChildHasExplicitWidth(stackChildren)) return;
            Emit(
                BuildDedupKey("HStack", row, col, locationKey),
                BuildMessage("HStack", axisIsColumn: true, index: col, locationKey: locationKey));
        }
        else
        {
            if (!TrackIsAuto(rows, row)) return;
            if (chainHasHeight) return;
            if (AnyChildHasExplicitHeight(stackChildren)) return;
            Emit(
                BuildDedupKey("VStack", row, col, locationKey),
                BuildMessage("VStack", axisIsColumn: false, index: row, locationKey: locationKey));
        }
    }

    private static bool AnyChildHasExplicitWidth(Element[] children)
        // MinWidth also prevents the stretch→0 desired-size case during Measure, so it counts.
        => children.Any(static c => c?.Modifiers?.Width is not null || c?.Modifiers?.MinWidth is not null);

    private static bool AnyChildHasExplicitHeight(Element[] children)
        // MinHeight also clamps the desired size during Measure, so it counts as an explicit size.
        => children.Any(static c => c?.Modifiers?.Height is not null || c?.Modifiers?.MinHeight is not null);

    private static bool TrackIsAuto(GridSize[]? tracks, int index)
    {
        if (tracks is null || index < 0 || index >= tracks.Length)
            return false;
        var track = tracks[index];
        return track.Type == GridUnitType.Auto;
    }

    /// <summary>
    /// Builds the emit-once dedup key for an offending placement. The key always encodes the stack
    /// type plus its concrete grid placement (row/column); the author-supplied
    /// <see cref="Element.Key"/> is appended as an extra discriminator when present. Because
    /// <see cref="Element.Key"/> uniqueness is only guaranteed among siblings, keying on it alone
    /// could wrongly collapse distinct offenders (the same key reused under different grids, or a
    /// keyed stack that moves to a new row/column). Including the placement keeps each distinct
    /// position warning once while staying stable across re-renders (records produce
    /// fresh-but-equal instances).
    /// <para>
    /// Known limitation: the dedup set is process-global and the element tree carries no parent
    /// pointer, so two <em>unkeyed</em> offenders that share the same stack type and row/column but
    /// live in <em>different</em> Grids hash to the same key — only the first warns. This is an
    /// accepted tradeoff for a DEBUG-only diagnostic; assign distinct <see cref="Element.Key"/>s
    /// (e.g. via <c>.WithKey(...)</c>) to the stacks to surface every offender.
    /// </para>
    /// </summary>
    private static string BuildDedupKey(string stackName, int row, int col, string? locationKey)
        => locationKey is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, "{0}@r{1}c{2}|key:{3}", stackName, row, col, locationKey)
            : string.Format(CultureInfo.InvariantCulture, "{0}@r{1}c{2}", stackName, row, col);

    private static string BuildMessage(string stackName, bool axisIsColumn, int index, string? locationKey)
    {
        string axis = axisIsColumn ? "column" : "row";
        string sizeModifier = axisIsColumn ? "Width" : "Height";
        string minSizeModifier = axisIsColumn ? "MinWidth" : "MinHeight";
        string starTrack = axisIsColumn ? "Star (\"*\") column" : "Star (\"*\") row";
        string location = locationKey is { Length: > 0 } ? $" (key: \"{locationKey}\")" : string.Empty;

        return string.Format(
            CultureInfo.InvariantCulture,
            "[Reactor] {0}{1} is in Grid {2} {3} (Auto) with no explicit {4} and no explicitly-sized " +
            "children. The measure pass may return 0\u00d70 (collapsed). Fix it by using a {5}, setting " +
            ".{4}(...) or .{6}(...) on the {0}, or giving a child an explicit size.",
            stackName, location, axis, index, sizeModifier, starTrack, minSizeModifier);
    }

    private static void Emit(string dedupKey, string message)
    {
        lock (s_gate)
        {
            if (!s_warned.Add(dedupKey))
                return;
        }

        Sink?.Invoke(message);
        global::System.Diagnostics.Debug.WriteLine(message);
    }

    /// <summary>
    /// Clears the emit-once dedup set. Test-only hook so each test observes warnings
    /// independently; not part of the supported API surface.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (s_gate)
        {
            s_warned.Clear();
        }
    }
}
