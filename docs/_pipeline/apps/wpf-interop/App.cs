using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `wpf-interop.md`. There is no Reactor.Interop.Wpf host control
// yet, so this app compiles the documented DesktopWindowXamlSource workaround
// instead: a WPF HwndHost that owns a XAML island and mounts a Reactor
// component tree inside it.
//
// This project deliberately does NOT call ReactorApp.Run — the WPF Application
// owns the message loop. WinUI still needs a DispatcherQueue and an
// Application instance on the UI thread before the island is created; see the
// bootstrap notes in the guide.
_ = typeof(ReactorWpfIsland);

// <snippet:reactor-component>
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
// </snippet:reactor-component>

// <snippet:hwnd-host>
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
        _source?.SiteBridge.MoveAndResize(
            new RectInt32(0, 0, (int)ActualWidth, (int)ActualHeight));
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
// </snippet:hwnd-host>

// <snippet:mount-directly>
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
// </snippet:mount-directly>
