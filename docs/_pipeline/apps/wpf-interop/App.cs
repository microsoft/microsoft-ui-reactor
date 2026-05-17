using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using INotifyPropertyChanged = System.ComponentModel.INotifyPropertyChanged;

// The Reactor.Interop.Wpf host control does not exist yet (see template
// front-matter). This doc app illustrates the components a future WPF host
// would mount, plus the data-flow / threading patterns that already work
// today through DesktopWindowXamlSource and ReactorHostControl directly.
//
// To stay runnable while the WPF surface is being designed, this app uses
// the same ReactorApp.Run host as the rest of the docset. The "WPF" code
// shapes appear as comment blocks inside snippet regions — they document
// the intent of the future API without compiling against types that don't
// ship.

ReactorApp.Run<WpfHostDemo>("WPF Interop (hypothetical)", width: 600, height: 400
#if DEBUG
    , preview: true
#endif
);

// <snippet:bootstrap>
// Roadmap shape — Reactor.Interop.Wpf is not shipped yet. The proposed
// API mirrors XamlIslandBootstrap from Reactor.Interop.WinForms, swapping
// the System.Windows.Forms message loop for System.Windows.Application.
//
//   using Microsoft.UI.Reactor.Interop.Wpf;
//   using System.Windows;
//
//   [STAThread]
//   public static void Main()
//   {
//       WpfXamlIslandBootstrap.Run(() =>
//       {
//           var window = new Window
//           {
//               Title = "My WPF + Reactor App",
//               Width = 800,
//               Height = 500,
//           };
//
//           var island = new WpfXamlIslandControl
//           {
//               ComponentType = typeof(WpfHostDemo),
//           };
//
//           window.Content = island;
//           window.Show();
//
//           new System.Windows.Application().Run(window);
//       });
//   }
//
// Today: until the WPF host ships, host Reactor content from WPF by
// embedding ReactorHostControl through DesktopWindowXamlSource directly
// (the same primitive XamlIslandControl wraps for WinForms). The
// XamlIslandBootstrap shape on the WinForms page applies — substitute the
// WPF dispatcher for the WinForms message loop.
// </snippet:bootstrap>

// <snippet:host-element>
// The Component that a WPF host (now: DesktopWindowXamlSource; future:
// WpfXamlIslandControl.ComponentType) mounts. This is identical to any
// other Reactor component — interop happens at the host boundary, not
// inside the component.
class WpfHostDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            Heading("Reactor inside WPF"),
            TextBlock($"Count: {count}").FontSize(24),
            Button("+1", () => setCount(count + 1))
        ).Padding(24).Background(SolidBackground);
    }
}
// </snippet:host-element>

// <snippet:data-flow>
// Bridging a WPF MVVM view-model into Reactor. The view-model is plain
// INotifyPropertyChanged — the same class your existing WPF bindings
// observe. UseObservable subscribes Reactor's re-render to that channel
// so a property change from the WPF side re-renders the island.
//
// The view-model instance is passed via Props so the same component
// works against any IUserViewModel implementation — easier to test and
// avoids capturing a singleton in the closure.

interface IUserViewModel : INotifyPropertyChanged
{
    string Name { get; set; }
    int UnreadCount { get; }
}

record UserPanelProps(IUserViewModel ViewModel);

class UserPanel : Component<UserPanelProps>
{
    public override Element Render()
    {
        var vm = UseObservable(Props.ViewModel);

        return VStack(8,
            Heading(vm.Name),
            TextBlock($"{vm.UnreadCount} unread")
                .Foreground(vm.UnreadCount > 0 ? AccentText : SecondaryText),
            TextField(vm.Name, next => vm.Name = next, placeholder: "Display name")
        ).Padding(16).Background(SolidBackground);
    }
}
// </snippet:data-flow>

// <snippet:threading>
// Threading note for any cross-framework host: WPF's Dispatcher (System
// .Windows.Threading.Dispatcher) and WinUI's DispatcherQueue are NOT the
// same instance — even when both run on the same UI thread. A property
// write that originates on the WPF side (a Click handler, a Loaded
// event, a Binding update) is on WPF's Dispatcher; it has to reach
// Reactor through a hook setter, which auto-marshals onto the WinUI
// DispatcherQueue captured at island bootstrap.
//
// The "free" path: call setX from anywhere — RenderContext detects the
// off-dispatcher call and posts onto the captured WinUI dispatcher. No
// app code needed.

class ThreadingDemo : Component
{
    public override Element Render()
    {
        var (ticks, setTicks) = UseState(0);

        UseEffect(() =>
        {
            // Background timer feeds the setter from a worker thread.
            // The setter detects the off-dispatcher call and queues
            // itself back onto the captured WinUI DispatcherQueue —
            // no manual TryEnqueue needed.
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(1000, cts.Token);
                    setTicks(ticks + 1);
                }
            }, cts.Token);
            return () => cts.Cancel();
        });

        return VStack(8,
            Heading("Threading"),
            TextBlock($"Ticks: {ticks}")
        ).Padding(16).Background(SolidBackground);
    }
}
// </snippet:threading>

// <snippet:accessibility>
// Accessibility surface inside a WPF island is the same as in a pure
// Reactor window: AutomationName, HeadingLevel, Landmark, and the
// UseFocusTrap/UseAnnounce hooks all work unchanged because the
// automation tree hangs off DesktopWindowXamlSource, not the WPF host.
//
// The one cross-host concern: WPF's UIAutomation peers and the WinUI
// automation tree are siblings — a screen reader walks each tree via
// its window peer. Inside the island, build the automation tree with
// Reactor's modifiers; do not try to reach across the boundary from
// either side.

class AccessibleWpfDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");

        return VStack(12,
            Heading("Profile")
                .HeadingLevel(AutomationHeadingLevel.Level1),
            TextField(name, setName, header: "Display name")
                .AutomationName("Display name")
                .TabIndex(0),
            Button("Save", () => { })
                .AutomationName("Save profile")
                .TabIndex(1)
        ).Padding(24)
         .Landmark(AutomationLandmarkType.Form)
         .Background(SolidBackground);
    }
}
// </snippet:accessibility>
