using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<DiagnosticsApp>("Diagnostics Demo");

class DiagnosticsApp : Component
{
    public override Element Render() =>
        VStack(8,
            TextBlock("Diagnostics"),
            TextBlock("Subscribe to Microsoft-UI-Reactor events while the app runs."))
        .Padding(16);
}

// <snippet:navigation-overlay>
public sealed class NavigationOverlay : IDisposable
{
    // ReactorEventSource is internal to Reactor, so an app names the
    // keyword by its documented bit value rather than by symbol.
    private const EventKeywords NavigationKeyword = (EventKeywords)0x200;

    private readonly IDisposable _subscription;
    private readonly Queue<string> _ring = new();

    public NavigationOverlay()
    {
        _subscription = ReactorTrace.Subscribe(
            evt =>
            {
                var line = $"{evt.EventName} {string.Join(' ',
                    Enumerable.Range(0, evt.Payload.Count)
                        .Select(i => $"{evt.PayloadNames[i]}={evt.Payload[i]}"))}";
                lock (_ring)
                {
                    _ring.Enqueue(line);
                    while (_ring.Count > 50) _ring.Dequeue();
                }
            },
            level: EventLevel.Verbose,
            keywords: NavigationKeyword);
    }

    public void Dispose() => _subscription.Dispose();
}
// </snippet:navigation-overlay>

public static class ComponentInspector
{
    public static void Inspect(UIElement wrapper, UIElement control)
    {
        // <snippet:inspect-component>
        // The Border wrapper every Component<T>() / RenderEachTime / Memo mounts into,
        // or a host's root content (window.Content, a ReactorHostControl).
        if (ReactorDiagnostics.DescribeComponent(wrapper) is { } component)
        {
            // component.Name: "Counter", "Memo in MainPage.Render", "render in App.Main"
            foreach (var hook in component.State)
                Console.WriteLine($"{hook.Index} {hook.Kind} {hook.Type} = {hook.Value}");

            // Parsed to the hook's type; same semantics as calling the UseState setter.
            if (!ReactorDiagnostics.TrySetState(wrapper, 0, "42", out var error))
                Console.WriteLine(error);   // "'abc' is not a valid int", "... is a ref hook; ..."

            ReactorDiagnostics.Rerender(wrapper);   // bypasses memoization, like a state change
        }

        // Which WinUI properties this control's common modifiers set (.Width, .Margin, ...).
        var owned = ReactorDiagnostics.GetAppliedProperties(control);
        // </snippet:inspect-component>
        _ = owned;
    }
}
