using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using WinUITableView = Microsoft.UI.Xaml.Controls.TableView;
using TableViewSelectionMode = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;
using TableViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.TableViewSelectionChangedEventArgs;

namespace Reactor.Controls;

/// <summary>
/// A column definition for <see cref="TableViewElement"/>: a header plus the
/// item property the column binds to.
/// </summary>
public sealed record TableColumn(string Header, string PropertyPath);

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

    /// <summary>One-way selected row index. When <c>null</c>, selection is left to the user.</summary>
    public int? SelectedIndex { get; init; }

    /// <summary>Raised when the native control's selection changes (added/removed items).</summary>
    public Action<TableViewSelectionChangedEventArgs>? OnSelectionChanged { get; init; }

    /// <summary>Raw control setters applied after typed properties (escape hatch).</summary>
    public Action<WinUITableView>[] Setters { get; init; } = Array.Empty<Action<WinUITableView>>();

    internal TableViewElement() { }
}
