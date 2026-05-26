using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Compile.Analyzer;
using Xunit;

namespace Microsoft.UI.Reactor.Compile.Analyzer.Tests;

/// <summary>
/// Fixtures for REACTOR1002. The analyzer is active in Phase 1 because
/// the <c>ReactorBinding&lt;T&gt;.OnCustomEvent&lt;TArgs&gt;</c> shape
/// ships in Phase 1 (1.6).
///
/// <para>
/// Why we exercise the <em>wrapped</em> form for the "fires" case: a
/// bare <c>+= h</c> against an event whose delegate type isn't
/// <c>EventHandler&lt;TArgs&gt;</c> is rejected by the C# compiler
/// before the analyzer can see it (CS0029). The analyzer's value comes
/// in the wrapped form — <c>+= new RoutedEventHandler(h)</c> — which
/// compiles cleanly but silently bridges between EventArgs types. That
/// is the bug shape REACTOR1002 catches.
/// </para>
/// </summary>
public class CustomEventDelegateTypeAnalyzerTests
{
    /// <summary>
    /// Shared stubs: minimal Reactor + WinUI surface needed for the
    /// analyzer's symbol queries to succeed.
    /// </summary>
    private const string Stubs = @"
namespace Microsoft.UI.Xaml
{
    public class FrameworkElement { }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class RoutedEventArgs { }

    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);

    public class ToggleSwitch : Microsoft.UI.Xaml.FrameworkElement
    {
        public event RoutedEventHandler? Toggled;
        public void Raise() => Toggled?.Invoke(this, new RoutedEventArgs());
    }
}

namespace WidgetLib
{
    public sealed class WidgetEventArgs { }

    public sealed class Widget : Microsoft.UI.Xaml.FrameworkElement
    {
        public event System.EventHandler<WidgetEventArgs>? Pinged;
        public void Raise() => Pinged?.Invoke(this, new WidgetEventArgs());
    }
}

namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element { }

    public readonly struct ReactorBinding<TElement> where TElement : Element
    {
        public void OnCustomEvent<TArgs>(
            System.Action<Microsoft.UI.Xaml.FrameworkElement, System.EventHandler<TArgs>> subscribe,
            System.Action<Microsoft.UI.Xaml.FrameworkElement, System.EventHandler<TArgs>> unsubscribe,
            System.Action<TElement, TArgs> handler)
        {
            // body irrelevant for analyzer tests
            _ = subscribe; _ = unsubscribe; _ = handler;
        }
    }

    public sealed record ToggleSwitchElement : Element;
    public sealed record WidgetElement : Element;
}
";

    // ── REACTOR1002 should FIRE ───────────────────────────────────────

    /// <summary>
    /// The author wraps the bare handler in <c>new RoutedEventHandler(h)</c>
    /// to satisfy the C# compiler, but the EventArgs of
    /// <c>RoutedEventHandler</c> is <c>RoutedEventArgs</c> while
    /// <c>TArgs</c> is declared as <c>WidgetEventArgs</c>. The wrap
    /// makes the code compile; the rule catches the silent EventArgs
    /// mismatch.
    /// </summary>
    [Fact]
    public async Task Fires_When_Wrapped_Event_EventArgs_Differs_From_TArgs()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml.Controls;
    using WidgetLib;

    public static class C
    {
        public static void Wire(ReactorBinding<WidgetElement> bind)
        {
            bind.OnCustomEvent<WidgetEventArgs>(
                (c, h) => {|REACTOR1002:((ToggleSwitch)c).Toggled += new RoutedEventHandler((s, e) => h(s, new WidgetEventArgs()))|},
                (c, h) => {|REACTOR1002:((ToggleSwitch)c).Toggled -= new RoutedEventHandler((s, e) => h(s, new WidgetEventArgs()))|},
                (el, args) => { });
        }
    }
}
";

        await new CSharpAnalyzerTest<CustomEventDelegateTypeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync();
    }

    // ── REACTOR1002 should NOT fire ────────────────────────────────────

    /// <summary>
    /// The wrap is across the same EventArgs — <c>TArgs</c> is
    /// <c>RoutedEventArgs</c> and the event's EventArgs is also
    /// <c>RoutedEventArgs</c>. Wrap is fine; rule stays quiet.
    /// </summary>
    [Fact]
    public async Task Does_Not_Fire_On_Wrap_With_Matching_EventArgs()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml.Controls;

    public static class C
    {
        public static void Wire(ReactorBinding<ToggleSwitchElement> bind)
        {
            bind.OnCustomEvent<RoutedEventArgs>(
                (c, h) => ((ToggleSwitch)c).Toggled += new RoutedEventHandler(h),
                (c, h) => ((ToggleSwitch)c).Toggled -= new RoutedEventHandler(h),
                (el, args) => { });
        }
    }
}
";

        await new CSharpAnalyzerTest<CustomEventDelegateTypeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync();
    }

    /// <summary>
    /// The straight-through case: the event itself is
    /// <c>EventHandler&lt;TArgs&gt;</c>, so <c>+= h</c> with no wrap is
    /// fine and the rule must stay quiet.
    /// </summary>
    [Fact]
    public async Task Does_Not_Fire_When_Event_Is_Generic_EventHandler_Matching_TArgs()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    using WidgetLib;

    public static class C
    {
        public static void Wire(ReactorBinding<WidgetElement> bind)
        {
            bind.OnCustomEvent<WidgetEventArgs>(
                (c, h) => ((Widget)c).Pinged += h,
                (c, h) => ((Widget)c).Pinged -= h,
                (el, args) => { });
        }
    }
}
";

        await new CSharpAnalyzerTest<CustomEventDelegateTypeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync();
    }

    [Fact]
    public void DiagnosticDescriptor_Is_Registered()
    {
        var analyzer = new CustomEventDelegateTypeAnalyzer();
        var descriptors = analyzer.SupportedDiagnostics;
        Assert.Single(descriptors);
        Assert.Equal("REACTOR1002", descriptors[0].Id);
        Assert.Equal(DiagnosticSeverity.Error, descriptors[0].DefaultSeverity);
    }
}
