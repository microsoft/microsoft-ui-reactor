using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<DiagnosticsApp>("Diagnostics Demo", width: 360, height: 240);

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
