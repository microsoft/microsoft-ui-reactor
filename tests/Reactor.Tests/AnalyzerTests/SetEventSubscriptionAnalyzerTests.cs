using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="SetEventSubscriptionAnalyzer"/> (<c>REACTOR_EVENT_001</c>) and its
/// <see cref="SetEventSubscriptionCodeFix"/>. This rule reconciles the former
/// <c>REACTOR_LIFECYCLE_001</c> (broad semantic detection + <c>.OnMountAdd</c>/<c>.OnUnmountAdd</c>
/// fix) into the shipped <c>REACTOR_EVENT_001</c> (declarative-modifier fix): it fires on any
/// event on a <c>FrameworkElement</c> wired through Reactor's <c>.Set</c>, and offers the
/// declarative <c>.On*</c> modifier when one exists, falling back to the mount/unmount rewrite.
/// </summary>
public class SetEventSubscriptionAnalyzerTests
{
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml.Controls;
#pragma warning disable CS0067 // event declared but never raised (stub controls)

namespace Microsoft.UI.Xaml
{
    public class UIElement { }
    public class FrameworkElement : UIElement { public event EventHandler Loaded; }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class TappedRoutedEventArgs : System.EventArgs { }
    public delegate void TappedEventHandler(object sender, TappedRoutedEventArgs e);

    public class Button : Microsoft.UI.Xaml.FrameworkElement
    {
        public event EventHandler Click;        // no declarative modifier -> OnMountAdd fix path
        public event TappedEventHandler Tapped; // distinct native delegate + declarative modifier
        public double Opacity;                  // numeric compound-assignment near-miss
        public EventHandler Callback;           // non-event delegate FIELD near-miss
    }
}

namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;

    public record ButtonElement;

    public static class Ext
    {
        public static ButtonElement Set(this ButtonElement el, Action<Button> configure) => el;
        public static T OnMount<T>(this T el, Action<FrameworkElement> action) => el;
        public static T OnUnmount<T>(this T el, Action<FrameworkElement> action) => el;
        public static T OnMountAdd<T>(this T el, Action<FrameworkElement> action) => el;
        public static T OnUnmountAdd<T>(this T el, Action<FrameworkElement> action) => el;
        public static ButtonElement OnTapped(this ButtonElement el, Action<object, TappedRoutedEventArgs> handler) => el;
    }
}
";

    // ---- detection ----

    [Fact]
    public async Task Fires_For_Event_Without_Declarative_Modifier()
    {
        var source = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Click += OnClick)|};
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Event_With_Declarative_Modifier()
    {
        var source = Stubs + @"
class C
{
    static void OnTapped(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Tapped += OnTapped)|};
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Unsubscribe()
    {
        // '-=' via .Set is also imperative event wiring that replays each render (flagged),
        // but only the '+=' shape has a mechanical fix.
        var source = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Click -= OnClick)|};
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Block_Bodied_Subscription()
    {
        var source = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => { c.Click += OnClick; })|};
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Numeric_Compound_Assignment()
    {
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Set(c => c.Opacity += 0.1);
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonEvent_Delegate_Field()
    {
        // Callback is a delegate FIELD, not an event — the mandatory event-symbol check
        // keeps the rule from firing (a fix would not compile).
        var source = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => b.Set(c => c.Callback += OnClick);
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonReactor_Set_Helper()
    {
        // A '.Set' that is not a Reactor DSL setter (different namespace) must not fire even
        // though the control derives from FrameworkElement and has a real event.
        var source = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    RawElement M(RawElement r) => r.Set(c => c.Click += OnClick);
}

public record RawElement;
public static class GlobalRawExt
{
    public static RawElement Set(this RawElement el, System.Action<Microsoft.UI.Xaml.Controls.Button> configure) => el;
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Member_Chain_Subscription()
    {
        // The subscription target is not the lambda parameter, so it is not the .Set receiver.
        var source = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b, Microsoft.UI.Xaml.Controls.Button other)
        => b.Set(c => other.Click += OnClick);
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Multi_Statement_Block_Lambda()
    {
        var source = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    static void Log() { }
    ButtonElement M(ButtonElement b) => b.Set(c => { Log(); c.Click += OnClick; });
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_OnMount_Subscription()
    {
        // Wiring through .OnMount (not .Set) is already render-safe — must not fire.
        var source = Stubs + @"
class C
{
    static void OnLoaded(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => b.OnMount(c => c.Loaded += OnLoaded);
}";
        await new CSharpAnalyzerTest<SetEventSubscriptionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ---- code fix ----

    [Fact]
    public async Task CodeFix_Rewrites_To_OnMountAdd_For_Static_Handler_Without_Modifier()
    {
        var before = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Click += OnClick)|};
}";
        var after = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => b.OnMountAdd(c => ((Button)c).Click += OnClick).OnUnmountAdd(c => ((Button)c).Click -= OnClick);
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_To_OnMountAdd_For_Field_Handler()
    {
        var before = Stubs + @"
class C
{
    System.EventHandler _handler;
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Click += _handler)|};
}";
        var after = Stubs + @"
class C
{
    System.EventHandler _handler;
    ButtonElement M(ButtonElement b) => b.OnMountAdd(c => ((Button)c).Click += _handler).OnUnmountAdd(c => ((Button)c).Click -= _handler);
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Offers_Declarative_Modifier_For_Known_Event()
    {
        // Tapped has a declarative modifier: fix #0 rewrites to .OnTapped(...).
        var before = Stubs + @"
class C
{
    static void OnTapped(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Tapped += OnTapped)|};
}";
        var after = Stubs + @"
class C
{
    static void OnTapped(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => b.OnTapped(OnTapped);
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionIndex = 0,
            CodeActionEquivalenceKey = SetEventSubscriptionAnalyzer.DiagnosticId + ":modifier:OnTapped",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Also_Offers_OnMountAdd_For_Known_Event_With_Stable_Handler()
    {
        // Tapped + static handler also offers the mount/unmount rewrite as fix #1.
        var before = Stubs + @"
class C
{
    static void OnTapped(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Tapped += OnTapped)|};
}";
        var after = Stubs + @"
class C
{
    static void OnTapped(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => b.OnMountAdd(c => ((Button)c).Tapped += OnTapped).OnUnmountAdd(c => ((Button)c).Tapped -= OnTapped);
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionIndex = 1,
            CodeActionEquivalenceKey = SetEventSubscriptionAnalyzer.DiagnosticId + ":mount",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declarative_Modifier_Offered_Even_For_Lambda_Handler()
    {
        // The declarative modifier owns the subscription lifecycle, so it is offered even for
        // an inline lambda (the mount/unmount rewrite would be withheld as unstable).
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Tapped += (s, e) => { })|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.OnTapped((s, e) => { });
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_But_No_Fix_For_Lambda_Handler_Without_Modifier()
    {
        // Click has no declarative modifier and the inline lambda is unstable — nudge only.
        var code = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Click += (s, e) => { })|};
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_But_No_Fix_For_Property_Handler_Without_Modifier()
    {
        // A property getter can recompute the delegate, so '+=' at mount and '-=' at unmount
        // could reference different delegates — the mount/unmount fix is withheld.
        var code = Stubs + @"
class C
{
    System.EventHandler Handler => (s, e) => { };
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Click += Handler)|};
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_But_No_Fix_For_Unsubscribe()
    {
        // '-=' is flagged but has no mechanical rewrite.
        var code = Stubs + @"
class C
{
    static void OnClick(object s, EventArgs e) { }
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Click -= OnClick)|};
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Declarative_Modifier_Withheld_For_Delegate_Typed_Parameter()
    {
        // The native event delegate (TappedEventHandler) has no conversion to the modifier's
        // Action<object, TArgs>, so a delegate-typed *value* handler must NOT get the .OnTapped
        // rewrite (it would be CS1503). A parameter is also unstable, so no fix at all — nudge.
        var code = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b, Microsoft.UI.Xaml.Controls.TappedEventHandler h)
        => {|REACTOR_EVENT_001:b.Set(c => c.Tapped += h)|};
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Known_Event_With_Delegate_Field_Offers_Only_OnMountAdd()
    {
        // A delegate-typed FIELD is a value (no conversion -> .OnTapped(field) would not
        // compile), so the declarative modifier fix is withheld, but the field is stable so the
        // mount/unmount rewrite is offered — and it is the only (default) fix.
        var before = Stubs + @"
class C
{
    Microsoft.UI.Xaml.Controls.TappedEventHandler _h;
    ButtonElement M(ButtonElement b) => {|REACTOR_EVENT_001:b.Set(c => c.Tapped += _h)|};
}";
        var after = Stubs + @"
class C
{
    Microsoft.UI.Xaml.Controls.TappedEventHandler _h;
    ButtonElement M(ButtonElement b) => b.OnMountAdd(c => ((Button)c).Tapped += _h).OnUnmountAdd(c => ((Button)c).Tapped -= _h);
}";
        await new CSharpCodeFixTest<SetEventSubscriptionAnalyzer, SetEventSubscriptionCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
