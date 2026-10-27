using System.Diagnostics.Tracing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <c>ReactorEventSource</c> events that an out-of-process inspector (winapp DevTools'
/// <c>logs</c> and error badge) relies on, found missing or duplicated while testing it:
/// a component that throws on re-render outside any <c>ErrorBoundary</c> is reported once,
/// a commit-phase failure that puts the error screen up is reported at all, and windows
/// report their open and close.
/// </summary>
internal static class DiagnosticEventsFixtures
{
    private sealed record Captured(string Name, object?[] Payload);

    private sealed class Capture : IDisposable
    {
        private readonly List<Captured> _events = new();
        private readonly IDisposable _subscription;

        public Capture(EventKeywords keywords, EventLevel level = EventLevel.Informational)
        {
            _subscription = ReactorTrace.Subscribe(
                e =>
                {
                    lock (_events) _events.Add(new Captured(e.EventName ?? string.Empty, e.Payload.ToArray()));
                },
                level,
                keywords);
        }

        public List<Captured> Take()
        {
            lock (_events)
            {
                var copy = _events.ToList();
                _events.Clear();
                return copy;
            }
        }

        public void Dispose() => _subscription.Dispose();
    }

    /// <summary>
    /// The tester's repro: a panel with a bounded and a bare <c>Bomb</c>; the bare one is
    /// re-rendered with <c>Explode = true</c>. Exactly one <c>RenderError</c> must name it.
    /// </summary>
    internal class BareChildRenderErrorReportedOnce(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunOnce("ErrorsOnly", EventLevel.Error, ReactorEventSource.Keywords.Errors, publishSource: false);
            // What an inspector enables: every keyword at Verbose (render spans, ComponentRendered)
            // and published ReactorSource values.
            await RunOnce("Inspector", EventLevel.Verbose, (EventKeywords)(-1), publishSource: true);
            // How the tester triggered it: an inspector state write (ReactorDiagnostics.TrySetState)
            // on the panel, not a click.
            await RunOnce("InspectorSetState", EventLevel.Verbose, (EventKeywords)(-1), publishSource: true, viaSetState: true);
        }

        private async Task RunOnce(string mode, EventLevel level, EventKeywords keywords, bool publishSource, bool viaSetState = false)
        {
            using var capture = new Capture(keywords, level);
            if (!ReactorEventSource.Log.IsEnabled(EventLevel.Error, ReactorEventSource.Keywords.Errors))
            {
                H.Skip("DiagnosticEvents_BareChildRenderErrorReportedOnce", "EventSource disabled (NativeAOT)");
                return;
            }

            bool previousPublish = ReactorSourcePublisher.IsEnabled;
            ReactorSourcePublisher.IsEnabled = publishSource;
            try
            {
                var host = H.CreateHost();
                // Nested like the tester's app: the panel is a child component of the root,
                // so its state change re-renders it below the root.
                host.Mount(new BombPanelRoot());
                await Harness.Render();
                capture.Take();

                if (viaSetState)
                {
                    // The panel's wrapper: button → HStack → the panel's VStack → wrapper Border.
                    var button = H.FindControl<Microsoft.UI.Xaml.Controls.Button>(b => b.Content as string == "boom bare");
                    Microsoft.UI.Xaml.DependencyObject? wrapper = button;
                    for (int i = 0; i < 3 && wrapper is not null; i++)
                        wrapper = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(wrapper);
                    var described = wrapper is Microsoft.UI.Xaml.UIElement w ? ReactorDiagnostics.DescribeComponent(w) : null;
                    H.Check($"DiagnosticEvents_BareChildRenderError_{mode}_PanelFound", described?.Name == nameof(BombPanel));
                    if (described is null) return;
                    H.Check($"DiagnosticEvents_BareChildRenderError_{mode}_StateSet",
                        ReactorDiagnostics.TrySetState((Microsoft.UI.Xaml.UIElement)wrapper!, 1, "true", out _));
                }
                else
                {
                    H.ClickButton("boom bare");
                }
                await Harness.Render();
                await Harness.Render(100);

                var all = capture.Take();
                var errors = all.Where(e => e.Name == nameof(ReactorEventSource.RenderError)).ToList();
                if (viaSetState)
                    Console.WriteLine($"# {mode} events: " + string.Join(", ", all.Select(e => e.Name + (e.Payload.Length > 0 ? "(" + e.Payload[0] + ")" : ""))));
                Console.WriteLine($"# {mode} bare RenderError: " + string.Join(", ", errors.Select(e => e.Payload[0])));
                H.Check($"DiagnosticEvents_BareChildRenderError_{mode}_ReportedOnce",
                    errors.Count(e => e.Payload[0] as string == nameof(DiagEventsBomb)) == 1);
                H.Check($"DiagnosticEvents_BareChildRenderError_{mode}_NothingElseReported", errors.Count == 1);

                H.ClickButton("boom bounded");
                await Harness.Render();
                await Harness.Render(100);
                var bounded = capture.Take().Where(e => e.Name == nameof(ReactorEventSource.RenderError)).ToList();
                Console.WriteLine($"# {mode} bounded RenderError: " + string.Join(", ", bounded.Select(e => e.Payload[0])));
                H.Check($"DiagnosticEvents_BoundedChildRenderError_{mode}_ReportedOnce",
                    bounded.Count(e => e.Payload[0] as string == nameof(DiagEventsBomb)) == 1);
                H.SetContent(null);
            }
            finally
            {
                ReactorSourcePublisher.IsEnabled = previousPublish;
            }
        }
    }

    /// <summary>
    /// A failure while reconciling the tree into native controls replaces the host's content
    /// with the error screen. It has no owning component, so no <c>RenderError</c> — but a
    /// <c>CommitError</c> naming the phase and the host's root must be raised, on both
    /// <c>ReactorHost</c> and <c>ReactorHostControl</c>.
    /// </summary>
    internal class CommitErrorReported(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var capture = new Capture(ReactorEventSource.Keywords.Errors, EventLevel.Error);
            if (!ReactorEventSource.Log.IsEnabled(EventLevel.Error, ReactorEventSource.Keywords.Errors))
            {
                H.Skip("DiagnosticEvents_CommitErrorReported", "EventSource disabled (NativeAOT)");
                return;
            }

            var host = H.CreateHost();
            host.Mount(new CommitFailureRoot());
            await Harness.Render();
            await Harness.Render(50);

            var events = capture.Take();
            Console.WriteLine("# host commit failure: " + string.Join(", ",
                events.Select(e => e.Name + "(" + string.Join("/", e.Payload) + ")")));
            var commit = events.Where(e => e.Name == nameof(ReactorEventSource.CommitError)).ToList();
            H.Check("DiagnosticEvents_CommitError_Host_Reported",
                commit.Count == 1
                && commit[0].Payload[0] as string == nameof(RenderErrorSource.Reconcile)
                && commit[0].Payload[1] as string == nameof(CommitFailureRoot)
                && commit[0].Payload[2] as string == nameof(CommitFailureProbeException));
            H.Check("DiagnosticEvents_CommitError_Host_NotARenderError",
                !events.Any(e => e.Name == nameof(ReactorEventSource.RenderError)));

            var control = new ReactorHostControl();
            control.Mount(new CommitFailureRoot());
            H.SetContent(control);
            await Harness.Render(200);
            var controlEvents = capture.Take();
            Console.WriteLine("# host control commit failure: " + string.Join(", ",
                controlEvents.Select(e => e.Name + "(" + string.Join("/", e.Payload) + ")")));
            H.Check("DiagnosticEvents_CommitError_HostControl_Reported",
                controlEvents.Count(e => e.Name == nameof(ReactorEventSource.CommitError)
                    && e.Payload[1] as string == nameof(CommitFailureRoot)) == 1);

            control.Dispose();
            H.SetContent(null);
        }
    }

    /// <summary>
    /// <c>WindowOpened</c> / <c>WindowClosed</c> were declared on the event source but never
    /// raised. A window opened with <c>ReactorApp.OpenWindow</c> reports both, naming its root
    /// component and its HWND.
    /// </summary>
    internal class WindowOpenedAndClosedReported(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var capture = new Capture(ReactorEventSource.Keywords.Hosting);
            if (!ReactorEventSource.Log.IsEnabled(EventLevel.Informational, ReactorEventSource.Keywords.Hosting))
            {
                H.Skip("DiagnosticEvents_WindowOpenedAndClosed", "EventSource disabled (NativeAOT)");
                return;
            }

            if (ReactorApp.UIDispatcher is null)
                ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
            var previousPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? win = null;
            try
            {
                win = ReactorApp.OpenWindow(
                    new WindowSpec { Title = "Diagnostic events window", Width = 240, Height = 160 },
                    static () => new DiagEventsWindowRoot());
                await win.Host.WaitForIdleAsync();
                await Harness.Render(50);
                long hwnd = win.Hwnd;

                var opened = capture.Take().Where(e => e.Name == nameof(ReactorEventSource.WindowOpened)).ToList();
                Console.WriteLine("# WindowOpened: " + string.Join(", ", opened.Select(e => string.Join("/", e.Payload))));
                H.Check("DiagnosticEvents_WindowOpened_Reported",
                    opened.Count(e => e.Payload[0] as string == nameof(DiagEventsWindowRoot)
                        && e.Payload[1] is long hw && hw == hwnd && hw != 0) == 1);

                win.Close();
                win = null;
                await Task.Delay(80);
                await Harness.Render(50);

                var closed = capture.Take().Where(e => e.Name == nameof(ReactorEventSource.WindowClosed)).ToList();
                Console.WriteLine("# WindowClosed: " + string.Join(", ", closed.Select(e => string.Join("/", e.Payload))));
                H.Check("DiagnosticEvents_WindowClosed_Reported",
                    closed.Count(e => e.Payload[0] as string == nameof(DiagEventsWindowRoot)
                        && e.Payload[1] is long hw && hw == hwnd) == 1);
            }
            finally
            {
                try { win?.Close(); } catch { }
                ReactorApp.ShutdownPolicy = previousPolicy;
            }
        }
    }
}

internal sealed record DiagEventsBombProps(bool Explode, string Label);

internal sealed class DiagEventsBomb : Component<DiagEventsBombProps>
{
    public override Element Render()
    {
        if (Props.Explode) throw new RenderErrorProbeException("render failed in " + Props.Label);
        return TextBlock("Bomb " + Props.Label + " ok");
    }
}

internal sealed class BombPanel : Component
{
    public override Element Render()
    {
        var (crashBounded, setCrashBounded) = UseState(false);
        var (crashBare, setCrashBare) = UseState(false);
        return VStack(2,
            ErrorBoundary(
                Component<DiagEventsBomb, DiagEventsBombProps>(new DiagEventsBombProps(crashBounded, "Bounded")),
                (Exception ex) => TextBlock("Boundary caught: " + ex.GetType().Name)),
            Component<DiagEventsBomb, DiagEventsBombProps>(new DiagEventsBombProps(crashBare, "Bare")),
            HStack(4,
                Button("boom bounded", () => setCrashBounded(true)),
                Button("boom bare", () => setCrashBare(true))));
    }
}

internal sealed class BombPanelRoot : Component
{
    public override Element Render()
    {
        var (show, setShow) = UseState(true);
        return ScrollView(VStack(10,
            CheckBox(show, setShow, label: "Show transient"),
            show ? TextBlock("transient") : Empty(),
            Component<BombPanel>(),
            TextBlock("after panel"))).Padding(16);
    }
}

internal sealed class CommitFailureProbeException(string message) : Exception(message);

// Its Render() succeeds; mounting the hosted control throws, during the commit phase.
internal sealed class CommitFailureRoot : Component
{
    public override Element Render()
        => VStack(TextBlock("before"), XamlHost(static () => throw new CommitFailureProbeException("commit failed")));
}

internal sealed class DiagEventsWindowRoot : Component
{
    public override Element Render() => TextBlock("diagnostic events window");
}
