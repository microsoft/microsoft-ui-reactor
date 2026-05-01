using InteropFirst.Components;
using InteropFirst.Models;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InteropFirst;

public sealed partial class MainWindow : Window
{
    public MainPageViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new MainPageViewModel();
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1100, 700));

        // Bind the XAML ListView to the same ObservableCollection that drives
        // the Reactor side. WinUI updates the ListView in place when the
        // collection raises CollectionChanged. The ItemTemplate is authored
        // declaratively in MainWindow.xaml via x:Bind / x:DataType — the
        // conventional XAML/MVVM data-binding shape.
        XamlOrdersList.ItemsSource = ViewModel.Items;

        // Mount the Reactor side. Resolves the shared brushes from
        // App.Resources so the Reactor side renders with the same accent
        // and subtle colors the XAML side uses (spec §7 — "shared resources").
        var accent = TryGetResource<Brush>("AccentSampleBrush");
        var subtle = TryGetResource<Brush>("SubtleSampleBrush");

        var props = new OrdersDataGridProps(
            Items: ViewModel.Items,
            OnSelect: order =>
            {
                ViewModel.SelectedOrder = order;
                if (order is not null)
                    XamlOrdersList.SelectedItem = order;
            },
            AccentBrush: accent,
            SubtleBrush: subtle,
            AddCommand: ViewModel.AddCommand,
            DeleteCommand: ViewModel.DeleteCommand);

        // Place the ReactorHostControl in code-behind. WinAppSDK 2.0 preview's
        // XAML compiler trips when the control appears directly in markup;
        // this is the same pattern the ReactorHostControlDemo uses.
        var reactorHost = new ReactorHostControl();
        reactorHost.Mount(_ =>
            Factories.Component<OrdersDataGrid, OrdersDataGridProps>(props));
        ReactorHostContainer.Child = reactorHost;

        Closed += OnClosed;
    }

    private void OnXamlSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedOrder = XamlOrdersList.SelectedItem as Order;
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.AddCommand.Execute(null);
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteCommand.Execute(null);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.Dispose();
    }

    private static T? TryGetResource<T>(string key) where T : class
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return null;
    }
}
