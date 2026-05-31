using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace HelloWorldAot;

/// <summary>
/// Smallest possible Reactor app: one text element. Exists to measure the
/// floor binary size of an AOT-published, fully-trimmed, framework-dependent
/// Reactor application.
/// </summary>
public sealed class HelloWorldApp : Component
{
    public override Element Render() => TextBlock("Hello, world!");
}
