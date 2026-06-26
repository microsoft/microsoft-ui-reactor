using System.Collections.Generic;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using Desc = Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

namespace Microsoft.UI.Reactor.Core;

// Spec 058 §15 (P5.19) — attached-property panels (Grid, VariableSizedWrapGrid,
// RelativePanel). Each is a generated descriptor: [WrapPanelChildren] wires the
// generated Panel children strategy's per-child / two-pass attached-prop hook to the
// static methods below, replacing the hand-written descriptor + strategy holder. The
// hook bodies are reproduced verbatim from the deleted Grid/WrapGrid/RelativePanel
// descriptors. Bespoke value props (Grid.Definition rebuild; WrapGrid sentinel-guarded
// props) stay in each Customize hook.

public partial record GridElement
{
    // Per-child Grid.SetRow/SetColumn/SetRowSpan/SetColumnSpan (resets on null for pooled reuse).
    private static void ApplyGridAttached(WinUI.Grid grid, UIElement ui, Element childEl)
    {
        if (ui is not FrameworkElement fe) return;
        var ga = childEl.GetAttached<GridAttached>();
        if (ga is null)
        {
            fe.ClearValue(WinUI.Grid.RowProperty);
            fe.ClearValue(WinUI.Grid.ColumnProperty);
            fe.ClearValue(WinUI.Grid.RowSpanProperty);
            fe.ClearValue(WinUI.Grid.ColumnSpanProperty);
            return;
        }
        WinUI.Grid.SetRow(fe, ga.Row);
        WinUI.Grid.SetColumn(fe, ga.Column);
        if (ga.RowSpan > 1) WinUI.Grid.SetRowSpan(fe, ga.RowSpan);
        else fe.ClearValue(WinUI.Grid.RowSpanProperty);
        if (ga.ColumnSpan > 1) WinUI.Grid.SetColumnSpan(fe, ga.ColumnSpan);
        else fe.ClearValue(WinUI.Grid.ColumnSpanProperty);
    }

    // Definition rebuilds RowDefinitions/ColumnDefinitions through descriptor-owned parsers,
    // gated on reference identity so the rebuild only fires when the Definition instance changes.
    private static partial Desc.ControlDescriptor<GridElement, WinUI.Grid> Customize(
        Desc.ControlDescriptor<GridElement, WinUI.Grid> d)
        => d.OneWay<GridDefinition>(
            get: static e => e.Definition,
            set: static (c, v) =>
            {
                c.ColumnDefinitions.Clear();
                c.RowDefinitions.Clear();
                if (v is null) return;
                foreach (var col in v.Columns)
                    c.ColumnDefinitions.Add(ParseColumnDef(col));
                foreach (var row in v.Rows)
                    c.RowDefinitions.Add(ParseRowDef(row));
            },
            comparer: GridDefinitionReferenceComparer.Instance);

    private static WinUI.ColumnDefinition ParseColumnDef(string def) => def switch
    {
        "*" => new WinUI.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
        "Auto" or "auto" => new WinUI.ColumnDefinition { Width = GridLength.Auto },
        _ when double.TryParse(def, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var px) => new WinUI.ColumnDefinition { Width = new GridLength(px) },
        _ when def.EndsWith('*') && double.TryParse(def[..^1], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var stars) =>
            new WinUI.ColumnDefinition { Width = new GridLength(stars, GridUnitType.Star) },
        _ => new WinUI.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
    };

    private static WinUI.RowDefinition ParseRowDef(string def) => def switch
    {
        "*" => new WinUI.RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
        "Auto" or "auto" => new WinUI.RowDefinition { Height = GridLength.Auto },
        _ when double.TryParse(def, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var px) => new WinUI.RowDefinition { Height = new GridLength(px) },
        _ when def.EndsWith('*') && double.TryParse(def[..^1], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var stars) =>
            new WinUI.RowDefinition { Height = new GridLength(stars, GridUnitType.Star) },
        _ => new WinUI.RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
    };

    private sealed class GridDefinitionReferenceComparer : IEqualityComparer<GridDefinition>
    {
        public static readonly GridDefinitionReferenceComparer Instance = new();
        public bool Equals(GridDefinition? x, GridDefinition? y) => ReferenceEquals(x, y);
        public int GetHashCode(GridDefinition obj) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

public partial record WrapGridElement
{
    // Per-child VariableSizedWrapGrid.SetRowSpan/SetColumnSpan (> 1 only; resets otherwise).
    private static void ApplyWrapGridAttached(WinUI.VariableSizedWrapGrid grid, UIElement ui, Element childEl)
    {
        if (ui is not FrameworkElement fe) return;
        var wga = childEl.GetAttached<WrapGridAttached>();
        if (wga is null)
        {
            fe.ClearValue(WinUI.VariableSizedWrapGrid.RowSpanProperty);
            fe.ClearValue(WinUI.VariableSizedWrapGrid.ColumnSpanProperty);
            return;
        }
        if (wga.RowSpan > 1) WinUI.VariableSizedWrapGrid.SetRowSpan(fe, wga.RowSpan);
        else fe.ClearValue(WinUI.VariableSizedWrapGrid.RowSpanProperty);
        if (wga.ColumnSpan > 1) WinUI.VariableSizedWrapGrid.SetColumnSpan(fe, wga.ColumnSpan);
        else fe.ClearValue(WinUI.VariableSizedWrapGrid.ColumnSpanProperty);
    }

    // Sentinel-guarded one-way props the record-type-driven channel can't express
    // (MaximumRowsOrColumns ≥ 0, ItemWidth/ItemHeight non-NaN). Orientation auto-maps.
    private static partial Desc.ControlDescriptor<WrapGridElement, WinUI.VariableSizedWrapGrid> Customize(
        Desc.ControlDescriptor<WrapGridElement, WinUI.VariableSizedWrapGrid> d)
        => d.OneWayConditional(
                get:         static e => e.MaximumRowsOrColumns,
                set:         static (c, v) => c.MaximumRowsOrColumns = v,
                shouldWrite: static e => e.MaximumRowsOrColumns >= 0)
            .OneWayConditional(
                get:         static e => e.ItemWidth,
                set:         static (c, v) => c.ItemWidth = v,
                shouldWrite: static e => !double.IsNaN(e.ItemWidth))
            .OneWayConditional(
                get:         static e => e.ItemHeight,
                set:         static (c, v) => c.ItemHeight = v,
                shouldWrite: static e => !double.IsNaN(e.ItemHeight));
}

public partial record RelativePanelElement
{
    // #60: reconcile can run very frequently for panels with many children; the
    // name→control map was allocated fresh on every pass. Pool it per-thread and
    // clear-and-reuse instead. RelativePanels can nest, so reconcile may re-enter
    // on the same thread mid-build — the reentrancy guard hands nested calls a
    // private instance and leaves the pooled one untouched.
    [global::System.ThreadStatic] private static Dictionary<string, UIElement>? _nameMapPool;
    [global::System.ThreadStatic] private static bool _nameMapInUse;

    // Two-pass: build a name → control map across siblings, then write the
    // RelativePanel sibling-referencing + panel-alignment attached DPs.
    private static void ApplyRelativePanelAttachedProps(
        WinUI.RelativePanel panel,
        IReadOnlyList<(UIElement Mounted, Element ChildElement)> pairs)
    {
        Dictionary<string, UIElement> nameMap;
        bool usingPool;
        if (_nameMapInUse)
        {
            nameMap = new Dictionary<string, UIElement>(pairs.Count, global::System.StringComparer.Ordinal);
            usingPool = false;
        }
        else
        {
            // Seed capacity on first creation to match the non-pool path above and keep
            // the first reconcile resize-free; Clear() retains capacity, so later passes
            // reuse it (the high-water mark) without re-seeding.
            nameMap = _nameMapPool ??= new Dictionary<string, UIElement>(pairs.Count, global::System.StringComparer.Ordinal);
            nameMap.Clear();
            _nameMapInUse = true;
            usingPool = true;
        }

        try
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                var (mounted, child) = pairs[i];
                ClearRelativePanelAttached(mounted);

                var rpa = child.GetAttached<RelativePanelAttached>();
                if (mounted is FrameworkElement fe)
                    fe.Name = rpa?.Name ?? string.Empty;
                if (rpa is not null)
                    nameMap[rpa.Name] = mounted;
            }

            for (int i = 0; i < pairs.Count; i++)
            {
                var (mounted, child) = pairs[i];
                var rpa = child.GetAttached<RelativePanelAttached>();
                if (rpa is null) continue;

                if (rpa.RightOf is not null && nameMap.TryGetValue(rpa.RightOf, out var rightOf))
                    WinUI.RelativePanel.SetRightOf(mounted, rightOf);
                if (rpa.Below is not null && nameMap.TryGetValue(rpa.Below, out var below))
                    WinUI.RelativePanel.SetBelow(mounted, below);
                if (rpa.LeftOf is not null && nameMap.TryGetValue(rpa.LeftOf, out var leftOf))
                    WinUI.RelativePanel.SetLeftOf(mounted, leftOf);
                if (rpa.Above is not null && nameMap.TryGetValue(rpa.Above, out var above))
                    WinUI.RelativePanel.SetAbove(mounted, above);
                if (rpa.AlignLeftWith is not null && nameMap.TryGetValue(rpa.AlignLeftWith, out var alw))
                    WinUI.RelativePanel.SetAlignLeftWith(mounted, alw);
                if (rpa.AlignRightWith is not null && nameMap.TryGetValue(rpa.AlignRightWith, out var arw))
                    WinUI.RelativePanel.SetAlignRightWith(mounted, arw);
                if (rpa.AlignTopWith is not null && nameMap.TryGetValue(rpa.AlignTopWith, out var atw))
                    WinUI.RelativePanel.SetAlignTopWith(mounted, atw);
                if (rpa.AlignBottomWith is not null && nameMap.TryGetValue(rpa.AlignBottomWith, out var abw))
                    WinUI.RelativePanel.SetAlignBottomWith(mounted, abw);
                if (rpa.AlignHorizontalCenterWith is not null && nameMap.TryGetValue(rpa.AlignHorizontalCenterWith, out var ahcw))
                    WinUI.RelativePanel.SetAlignHorizontalCenterWith(mounted, ahcw);
                if (rpa.AlignVerticalCenterWith is not null && nameMap.TryGetValue(rpa.AlignVerticalCenterWith, out var avcw))
                    WinUI.RelativePanel.SetAlignVerticalCenterWith(mounted, avcw);

                WinUI.RelativePanel.SetAlignLeftWithPanel(mounted, rpa.AlignLeftWithPanel);
                WinUI.RelativePanel.SetAlignRightWithPanel(mounted, rpa.AlignRightWithPanel);
                WinUI.RelativePanel.SetAlignTopWithPanel(mounted, rpa.AlignTopWithPanel);
                WinUI.RelativePanel.SetAlignBottomWithPanel(mounted, rpa.AlignBottomWithPanel);
                WinUI.RelativePanel.SetAlignHorizontalCenterWithPanel(mounted, rpa.AlignHorizontalCenterWithPanel);
                WinUI.RelativePanel.SetAlignVerticalCenterWithPanel(mounted, rpa.AlignVerticalCenterWithPanel);
            }
        }
        finally
        {
            if (usingPool)
            {
                // Drop UIElement references promptly so the pooled map doesn't pin
                // mounted controls alive between reconciles.
                nameMap.Clear();
                _nameMapInUse = false;
            }
        }
    }

    private static void ClearRelativePanelAttached(UIElement ctrl)
    {
        ctrl.ClearValue(WinUI.RelativePanel.RightOfProperty);
        ctrl.ClearValue(WinUI.RelativePanel.BelowProperty);
        ctrl.ClearValue(WinUI.RelativePanel.LeftOfProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AboveProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignLeftWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignRightWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignTopWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignBottomWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignHorizontalCenterWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignVerticalCenterWithProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignLeftWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignRightWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignTopWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignBottomWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignHorizontalCenterWithPanelProperty);
        ctrl.ClearValue(WinUI.RelativePanel.AlignVerticalCenterWithPanelProperty);
    }
}
