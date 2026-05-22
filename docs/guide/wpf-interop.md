
# WPF Interop

**Status: coming soon.** A first-class Microsoft.UI.Reactor (Reactor) WPF host control
(`Reactor.Interop.Wpf`) is on the roadmap but not yet shipped. The only
host wrapper in the box today is `Reactor.Interop.WinForms`.

## Workaround for today

Host a Reactor component tree from WPF by embedding
[`DesktopWindowXamlSource`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.desktopwindowxamlsource)
directly — the same WinAppSDK primitive that
[WinForms Interop](winforms-interop.md) wraps in `XamlIslandControl`.
Inside the island, mount a `ReactorHostControl` and assign it a
`ComponentType`. The Reactor side of the boundary is identical to any
other host: components, hooks, modifiers, and
[`UseObservable<T>`](hooks.md) for bridging an `INotifyPropertyChanged`
view-model all work unchanged.

The WPF `Dispatcher` and WinUI `DispatcherQueue` are distinct objects
on the same UI thread, so plain property writes from WPF event handlers
into Reactor setters work without marshalling — see
[Threading and Dispatch](threading-and-dispatch.md) for the invariants
the WinUI side enforces.

## Next Steps

- **[WinForms Interop](winforms-interop.md)** — The shipping parallel
  host. Read this first; the WPF surface will mirror it.
- **[Hooks](hooks.md)** — `UseObservable`, `UseObservableTree`, and
  `UseObservableProperty` for bridging `INotifyPropertyChanged`
  view-models from WPF.
- **[Threading and Dispatch](threading-and-dispatch.md)** — How Reactor's
  hook setters auto-marshal across dispatchers.
- **[XAML Developers](xaml-developers.md)** — Migration cookbook for
  WPF/XAML pages moving to Reactor's declarative shell.
