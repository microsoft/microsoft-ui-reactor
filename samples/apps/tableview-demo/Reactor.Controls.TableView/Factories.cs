using System.Collections;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core.V1Protocol;
using WinUITableView = Microsoft.UI.Xaml.Controls.TableView;

namespace Reactor.Controls;

/// <summary>
/// DSL factories for the native TableView Reactor control. Import alongside the
/// core Reactor factories:
/// <code>
/// using static Microsoft.UI.Reactor.Factories;
/// using static Reactor.Controls.Factories;
/// </code>
/// </summary>
/// <remarks>
/// Registration model — per-library trim unit. The static constructor registers
/// the <see cref="TableViewHandler"/> with <see cref="ControlRegistry"/> on first
/// touch of any factory below, so an app that never references this library has
/// the whole control surface trimmed away. This mirrors the recommended pattern
/// documented on <c>Microsoft.UI.Reactor.Advanced.Factories</c>.
/// </remarks>
public static partial class Factories
{
    static Factories()
    {
        ControlRegistry.Register<TableViewElement, WinUITableView>(
            static () => new TableViewHandler());
        // Register the satellite control's XAML metadata provider so the WinUI XAML loader can
        // resolve the advanced types when the control's style closure is parsed (code-only host).
        TableViewStyles.RegisterMetadata();
    }

    /// <summary>
    /// A native TableView bound to <paramref name="items"/>, with columns
    /// auto-generated from the first item's public properties.
    /// </summary>
    public static TableViewElement TableView(IEnumerable items, double height = 360) =>
        new() { Items = items, Height = height };

    /// <summary>
    /// A native TableView bound to <paramref name="items"/> with explicit
    /// <paramref name="columns"/>.
    /// </summary>
    public static TableViewElement TableView(
        IEnumerable items,
        IReadOnlyList<TableColumn> columns,
        double height = 360) =>
        new() { Items = items, Columns = columns, Height = height };
}
