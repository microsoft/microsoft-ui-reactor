# InteropFirst sample (spec 033 §7)

A vanilla WinUI 3 application that hosts a Microsoft.UI.Reactor (Reactor) component inside a XAML
page. Demonstrates the side-by-side interop story for XAML/MVVM teams adopting
Reactor incrementally.

## What this sample shows

- **XAML page hosts Reactor.** `MainPage.xaml` is a normal `Page`. The Reactor
  side is rendered inside a `<reactor:ReactorHostControl>` placed in the right
  column of the page's `Grid`.
- **One source of truth, two consumers.** `MainPageViewModel` owns an
  `ObservableCollection<Order>`. The XAML `ListView` binds to it via `x:Bind`;
  the Reactor `OrdersDataGrid` reads it through props and bridges
  `ObservableCollection.CollectionChanged` to its hook state via `UseEffect`.
- **Shared resources.** `App.xaml` defines `AccentSampleBrush` and
  `SubtleSampleBrush` once. The XAML side resolves them as `StaticResource`;
  the Reactor side receives them through the props the host page populates by
  reading `Application.Current.Resources`.
- **Shared commanding.** The XAML `CommandBar` binds to the ViewModel's
  `ICommand` properties. The same `ICommand` instances flow into the Reactor
  toolbar via `Microsoft.UI.Reactor.Core.CommandInterop.FromCommand` — a
  single source of truth for both sides.
- **Selection round-trip.** Clicking a Reactor row sets the ViewModel's
  `SelectedOrder` (Reactor → host); changing it from the XAML `ListView`
  flows back into the Reactor side on the next render.

## Layout note

The sample collapses the spec's `Page`/`Frame`/`NavigationView` into a single
`Window`. The "interop story" the spec emphasizes — XAML hosting Reactor —
does not depend on the navigation chrome, so we keep the demonstration tight.
A `Page`-based variant is straightforward to add.

The `ListView`'s `ItemTemplate` is authored declaratively in `MainWindow.xaml`
via `x:Bind` and `x:DataType="models:Order"` — the conventional XAML/MVVM data-
binding shape, with compile-time-checked bindings.

The `ReactorHostControl` itself is constructed in code-behind (matching the
convention from `samples/ReactorHostControlDemo`) rather than placed directly
in markup; this keeps WinAppSDK 2.0 preview's XAML compiler happy and keeps
the host's lifecycle visible to the window that owns the ViewModel.

## What this sample does *not* show

- A `ReactorApp` bootstrap. `MainWindow.xaml` is a vanilla WinUI 3 `Window`;
  Reactor is the *guest*, not the host.
- A fake business scenario. The model is intentionally generic (`Order`).
- The full Reactor `DataGrid` factory. The "OrdersDataGrid" component renders
  rows with simple `HStack`/`TextBlock` primitives so the interop story stays
  visible. Real apps can use `Microsoft.UI.Reactor.Factories.DataGrid<T>(...)`
  with a `FieldDescriptor` set when row virtualization or column-header
  features are required.
- Right-to-left layout, full localization, or a packaged-app manifest.

## Running

```sh
dotnet build samples/InteropFirst/InteropFirst.csproj
dotnet run --project samples/InteropFirst/InteropFirst.csproj
```

The app opens a 1100×700 window. Click **Add** in the bottom command bar to
append an order to both lists. Click a row in either list to flow selection
to the other side. Click **Delete** to remove the selected row.

## Files

- `App.xaml` / `App.xaml.cs` — vanilla WinUI 3 application shell with the
  shared `AccentSampleBrush` / `SubtleSampleBrush` resources.
- `MainWindow.xaml` / `.xaml.cs` — vanilla WinUI 3 `Window` with the split
  layout, the XAML `ListView`, the code-behind-mounted `ReactorHostControl`,
  and the bottom `Add` / `Delete` buttons.
- `MainPageViewModel.cs` — `INotifyPropertyChanged`,
  `ObservableCollection<Order>`, `Add` / `Delete` commands.
- `Bridges/RelayCommand.cs` — minimal `ICommand` so the sample doesn't pull
  in Community Toolkit MVVM.
- `Models/Order.cs` — sample record.
- `Components/OrdersDataGrid.cs` — Reactor `Component<TProps>` rendering the
  collection and demonstrating the prop-in / callback-out / shared-resource
  pattern.
