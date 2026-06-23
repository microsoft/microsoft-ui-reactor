using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using WinUITableView = Microsoft.UI.Xaml.Controls.TableView;
using TableViewSelectionMode = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;
using TableViewSelectionUnit = Microsoft.UI.Xaml.Controls.TableViewSelectionUnit;
using TableViewGridLinesVisibility = Microsoft.UI.Xaml.Controls.TableViewGridLinesVisibility;
using TableViewHeadersVisibility = Microsoft.UI.Xaml.Controls.TableViewHeadersVisibility;
using TableViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.TableViewSelectionChangedEventArgs;

namespace Reactor.Controls;

/// <summary>
/// A column definition for <see cref="TableViewElement"/>: a header, the item property the column
/// binds to, an optional cell <see cref="CellStyle"/> (text / pill / chip / tint), and an optional
/// pixel width.
/// </summary>
public sealed record TableColumn(string Header, string PropertyPath, CellStyle Style = CellStyle.Text, double Width = double.NaN);

/// <summary>
/// First-class Reactor element for the native C++/WinRT
/// <c>Microsoft.UI.Xaml.Controls.TableView</c> (separate-binary control,
/// projected vs public WinAppSDK 2.0.1).
/// </summary>
/// <remarks>
/// Unlike the raw <c>XamlHostElement</c> hatch, this element is reconciled by a
/// real <see cref="TableViewHandler"/>: typed properties, column definitions,
/// selection, and reactive <c>ItemsSource</c> updates are diffed and applied as
/// minimal writes on a single pooled control instance.
/// </remarks>
public sealed record TableViewElement : Element
{
    /// <summary>The data items bound to the table (sets the native control's <c>ItemsSource</c>).</summary>
    public IEnumerable? Items { get; init; }

    /// <summary>
    /// Explicit columns. When <c>null</c>, columns are auto-generated from the
    /// first item's public properties (convenient for demos; pass explicit
    /// columns for control over headers/order/binding).
    /// </summary>
    public IReadOnlyList<TableColumn>? Columns { get; init; }

    /// <summary>Layout height of the hosted control.</summary>
    public double Height { get; init; } = 360;

    /// <summary>Minimum layout width of the hosted control.</summary>
    public double MinWidth { get; init; } = 520;

    /// <summary>Selection mode. When <c>null</c>, the control's default is used.</summary>
    public TableViewSelectionMode? SelectionMode { get; init; }

    /// <summary>Selection unit (row / cell / cell-or-row). When <c>null</c>, the control's default is used.</summary>
    public TableViewSelectionUnit? SelectionUnit { get; init; }

    /// <summary>Grid-line visibility (none / horizontal / vertical / all).</summary>
    public TableViewGridLinesVisibility? GridLinesVisibility { get; init; }

    /// <summary>Header visibility (none / column / row / all).</summary>
    public TableViewHeadersVisibility? HeadersVisibility { get; init; }

    /// <summary>Allow the user to sort by clicking column headers.</summary>
    public bool? CanSortColumns { get; init; }

    /// <summary>Allow the user to filter columns via the header funnels.</summary>
    public bool? CanFilterColumns { get; init; }

    /// <summary>Allow the user to reorder columns by dragging headers.</summary>
    public bool? CanReorderColumns { get; init; }

    /// <summary>Allow the user to resize columns by dragging header edges.</summary>
    public bool? CanResizeColumns { get; init; }

    /// <summary>Show the leading selection gutter (checkbox column).</summary>
    public bool? IsSelectionGutterVisible { get; init; }

    /// <summary>Freeze the first N columns to the leading edge (pinned during horizontal scroll).</summary>
    public int? FrozenColumnCount { get; init; }

    /// <summary>One-way selected row index. When <c>null</c>, selection is left to the user.</summary>
    public int? SelectedIndex { get; init; }

    /// <summary>Raised when the native control's selection changes (added/removed items).</summary>
    public Action<TableViewSelectionChangedEventArgs>? OnSelectionChanged { get; init; }

    /// <summary>Raw control setters applied after typed properties (escape hatch).</summary>
    public Action<WinUITableView>[] Setters { get; init; } = Array.Empty<Action<WinUITableView>>();

    internal TableViewElement() { }
}
