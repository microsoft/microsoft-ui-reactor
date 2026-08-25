
# WPF Interop

**Status: coming soon.** A first-class Microsoft.UI.Reactor (Reactor) WPF host control
(`Reactor.Interop.Wpf`) is on the roadmap but not yet shipped. The only
host wrapper in the box today is `Reactor.Interop.WinForms`.

## Workaround for today

Host a Reactor component tree from WPF by embedding
[`DesktopWindowXamlSource`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.desktopwindowxamlsource)
directly — the same WinAppSDK primitive that
[WinForms Interop](winforms-interop.md) wraps in `XamlIslandControl`.
WPF adopts a foreign HWND through `HwndHost`, so the island lives inside
an `HwndHost` subclass:

```csharp
// WPF hosts foreign HWNDs through HwndHost. DesktopWindowXamlSource owns the
// island HWND; ReactorHostControl is the WinUI element mounted inside it.
//
// ReactorHostControl has no ComponentType property — that one belongs to the
// WinForms XamlIslandControl. On the WinUI side you either hand it a
// ComponentFactory or call Mount(...) directly.
sealed class ReactorWpfIsland : HwndHost
{
    private DesktopWindowXamlSource? _source;
    private ReactorHostControl? _host;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _source = new DesktopWindowXamlSource();
        _source.Initialize(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwndParent.Handle));
        _source.ShouldConstrainPopupsToWorkArea = true;

        _host = new ReactorHostControl { ComponentFactory = () => new WpfHostedDashboard() };
        _source.Content = _host;

        var bridgeHwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(_source.SiteBridge.WindowId);
        return new HandleRef(this, bridgeHwnd);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        // ActualWidth/Height are WPF DIPs; MoveAndResize sizes the child HWND
        // in physical pixels. Scale by the current DPI or the island is too
        // small at anything above 100%, and wrong after a monitor DPI change.
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        _source?.SiteBridge.MoveAndResize(new RectInt32(
            0, 0,
            (int)(ActualWidth * scale.DpiScaleX),
            (int)(ActualHeight * scale.DpiScaleY)));
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // The source's Dispose does not dispose its content — release the
        // Reactor host explicitly or the reconciler and its effects leak.
        _host?.Dispose();
        _host = null;
        _source?.Dispose();
        _source = null;
    }
}
```

Inside the island, the WinUI element you mount is a `ReactorHostControl`.

> **`ReactorHostControl` has no `ComponentType` property.** That property
> belongs to the WinForms `XamlIslandControl` wrapper, not to the host
> control itself. On the WinUI side, set `ComponentFactory` (parameterless
> components) or call `Mount(component)` / `Mount(renderFunc)`.

```csharp
// Mount(...) is the alternative when the component needs constructor
// arguments — ComponentFactory covers the parameterless case.
static class DirectMount
{
    public static ReactorHostControl Create(string title)
    {
        var host = new ReactorHostControl();
        host.Mount(new TitledDashboard(title));
        return host;
    }
}

class TitledDashboard(string title) : Component
{
    public override Element Render() => Heading(title);
}
```

The Reactor side of the boundary is identical to any other host:
components, hooks, modifiers, and [`UseObservable<T>`](hooks.md) for
bridging an `INotifyPropertyChanged` view-model all work unchanged.

```csharp
class WpfHostedDashboard : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            Heading("Reactor inside WPF"),
            TextBlock($"Count: {count}"),
            Button("+1", () => setCount(count + 1))
        ).Padding(24);
    }
}
```

Two lifetime rules the WinForms wrapper handles for you and an
`HwndHost` does not:

- `DesktopWindowXamlSource.Dispose()` does **not** dispose its content.
  Dispose the `ReactorHostControl` yourself in `DestroyWindowCore`, or the
  reconciler and every live effect leak for the process lifetime.
- Re-create the source if WPF recreates the host window; reusing a
  disposed source throws.

The WPF `Dispatcher` and WinUI `DispatcherQueue` are distinct objects
on the same UI thread, so plain property writes from WPF event handlers
into Reactor setters work without marshalling — see
[Threading and Dispatch](threading-and-dispatch.md) for the invariants
the WinUI side enforces.

Keyboard input, theming, and popup constraint are the parts
`XamlIslandControl` invests in that this recipe does not: WinUI needs a
`DispatcherQueue` and an `Application` instance on the UI thread before
the first island is created, and `ContentPreTranslateMessage` must be
pumped for keys to reach XAML. Read
[`XamlIslandBootstrap`](winforms-interop.md) as the reference for that
setup — a WPF host has to reproduce it.

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
