using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using InteropFirst.Models;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace InteropFirst.Components;

/// <summary>
/// Spec 033 §7 — props for the <see cref="OrdersDataGrid"/> Reactor component.
/// Carries the host's <see cref="ObservableCollection{T}"/>, the selection
/// callback, the brushes resolved from the host's resource dictionaries, and
/// (optionally) an <see cref="ICommand"/> bridged from the host's ViewModel
/// commands so the Reactor side can invoke the same actions the XAML toolbar
/// invokes.
/// </summary>
public sealed record OrdersDataGridProps(
    ObservableCollection<Order> Items,
    Action<Order?>? OnSelect = null,
    Brush? AccentBrush = null,
    Brush? SubtleBrush = null,
    ICommand? AddCommand = null,
    ICommand? DeleteCommand = null);

/// <summary>
/// Reactor component that renders the same <c>ObservableCollection&lt;Order&gt;</c>
/// the XAML <c>ListView</c> binds to. Demonstrates:
/// </summary>
/// <list type="bullet">
///   <item><description>Data flow in via typed props (<see cref="OrdersDataGridProps"/>).</description></item>
///   <item><description>Selection flow out via a callback prop.</description></item>
///   <item><description>Shared theme resources resolved from <c>App.xaml</c> by the XAML host (passed in as brushes — Reactor renders them through <c>.Foreground(...)</c>).</description></item>
///   <item><description>Shared commanding via <c>CommandInterop.FromCommand(ICommand, ...)</c> so the Reactor toolbar invokes the host's ViewModel commands.</description></item>
///   <item><description><c>UseEffect</c> with cleanup that subscribes to <see cref="ObservableCollection{T}.CollectionChanged"/> on mount and unsubscribes on unmount — leak-free.</description></item>
/// </list>
public sealed class OrdersDataGrid : Component<OrdersDataGridProps>
{
    public override Element Render()
    {
        // Mirror the host's collection into local state so structural changes
        // (Add / Remove) re-render the Reactor side. The reconciler does not
        // listen to ObservableCollection.CollectionChanged on its own — that
        // is intentional, but it means we have to bridge.
        var (snapshot, setSnapshot) = UseState<IReadOnlyList<Order>>(Props.Items.ToList());
        var (selectedId, setSelectedId) = UseState<int?>(null);

        // Subscribe on mount, unsubscribe on unmount. Capture Items locally so
        // a prop swap (rare in this sample, but possible in real apps) doesn't
        // leak the previous subscription.
        var items = Props.Items;
        UseEffect(() =>
        {
            void Handler(object? sender, NotifyCollectionChangedEventArgs e) =>
                setSnapshot(items.ToList());
            items.CollectionChanged += Handler;
            // Re-sync once on subscribe in case the collection changed between
            // initial render and effect attachment.
            setSnapshot(items.ToList());
            return () => items.CollectionChanged -= Handler;
        }, items);

        var accent = Props.AccentBrush;
        var subtle = Props.SubtleBrush;

        var headerRow = HStack(12,
            HeaderCell("Id", accent).Width(60),
            HeaderCell("Customer", accent),
            HeaderCell("Amount", accent).Width(120),
            HeaderCell("Placed", accent).Width(180));

        var rows = snapshot.Count == 0
            ? new[] { TextBlock("No orders yet — click Add.").Foreground(subtle ?? new SolidColorBrush(Microsoft.UI.Colors.Gray)).Margin(horizontal: 0, vertical: 16) }
            : snapshot.Select(o => Row(o, isSelected: o.Id == selectedId, subtle, OnRowTap: () =>
                {
                    setSelectedId(o.Id);
                    Props.OnSelect?.Invoke(o);
                })).ToArray();

        var rowsStack = VStack(4, rows);

        // Optional Reactor-side toolbar driven by the same ICommand instances
        // the XAML CommandBar uses. Demonstrates the spec §7 "shared
        // commanding" requirement via CommandInterop.FromCommand.
        var toolbar = HStack(8,
            Props.AddCommand is { } add
                ? Button(CommandInterop.FromCommand(add, "Add (Reactor side)"))
                : Empty(),
            Props.DeleteCommand is { } delete
                ? Button(CommandInterop.FromCommand(delete, "Delete (Reactor side)"))
                : Empty());

        return VStack(8,
            toolbar,
            headerRow,
            rowsStack);
    }

    private static Element HeaderCell(string label, Brush? accent) =>
        accent is not null
            ? TextBlock(label).Bold().Foreground(accent)
            : TextBlock(label).Bold();

    private static Element Row(Order o, bool isSelected, Brush? subtle, Action OnRowTap)
    {
        var amount = o.Amount.ToString("C", CultureInfo.CurrentCulture);
        var placed = o.PlacedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

        var row = HStack(12,
            TextBlock(o.Id.ToString(CultureInfo.InvariantCulture)).Width(60),
            TextBlock(o.CustomerName),
            TextBlock(amount).Width(120),
            (subtle is not null
                ? TextBlock(placed).Foreground(subtle)
                : TextBlock(placed)).Width(180));

        // Make the row tappable so the Reactor side can flow selection back
        // out to the host ViewModel via the OnSelect callback.
        var styled = row
            .OnTapped((_, _) => OnRowTap())
            .Padding(horizontal: 8, vertical: 4);
        return isSelected
            ? styled.Background(new SolidColorBrush(Microsoft.UI.Colors.LightBlue))
            : styled;
    }
}
