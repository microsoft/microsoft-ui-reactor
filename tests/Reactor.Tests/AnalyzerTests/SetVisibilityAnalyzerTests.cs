using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <c>REACTOR_VIS_001</c> — the Visibility arm of
/// <see cref="PoolResetSetAnalyzer"/> and its <see cref="SetVisibilityCodeFix"/>. Stubs a
/// UIElement-derived native control with a <c>Visibility</c> property and the
/// <c>.IsVisible(bool)</c> modifier, covering literal, conditional, and non-mappable RHS
/// forms plus the non-UIElement near-miss.
/// </summary>
public class SetVisibilityAnalyzerTests
{
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public class UIElement { public Visibility Visibility { get; set; } }
    public class FrameworkElement : UIElement { }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Border : Microsoft.UI.Xaml.FrameworkElement { }
}

namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Xaml.Controls;

    public record BorderElement;
    public record WidgetElement;

    // A native shape that has a Visibility property but does NOT derive from UIElement.
    public class PlainThing { public Microsoft.UI.Xaml.Visibility Visibility { get; set; } }

    public static class Ext
    {
        public static BorderElement Set(this BorderElement el, Action<Border> configure) => el;
        public static WidgetElement Set(this WidgetElement el, Action<PlainThing> configure) => el;
        public static T IsVisible<T>(this T el, bool isVisible) => el;
    }
}
";

    [Fact]
    public async Task Fires_And_Fixes_Collapsed()
    {
        var before = Stubs + @"
class C
{
    BorderElement M(BorderElement b) => {|REACTOR_VIS_001:b.Set(c => c.Visibility = Visibility.Collapsed)|};
}";
        var after = Stubs + @"
class C
{
    BorderElement M(BorderElement b) => b.IsVisible(false);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, SetVisibilityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_And_Fixes_Collapsed_Global_Qualified()
    {
        // global::Microsoft.UI.Xaml.Visibility.Collapsed still resolves to the Visibility enum.
        var before = Stubs + @"
class C
{
    BorderElement M(BorderElement b) => {|REACTOR_VIS_001:b.Set(c => c.Visibility = global::Microsoft.UI.Xaml.Visibility.Collapsed)|};
}";
        var after = Stubs + @"
class C
{
    BorderElement M(BorderElement b) => b.IsVisible(false);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, SetVisibilityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_And_Fixes_Visible()
    {
        var before = Stubs + @"
class C
{
    BorderElement M(BorderElement b) => {|REACTOR_VIS_001:b.Set(c => c.Visibility = Visibility.Visible)|};
}";
        var after = Stubs + @"
class C
{
    BorderElement M(BorderElement b) => b.IsVisible(true);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, SetVisibilityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_And_Fixes_Conditional_By_Polarity()
    {
        // cond ? Collapsed : Visible  →  IsVisible(!cond)
        var before = Stubs + @"
class C
{
    BorderElement M(BorderElement b, bool cond) => {|REACTOR_VIS_001:b.Set(c => c.Visibility = cond ? Visibility.Collapsed : Visibility.Visible)|};
}";
        var after = Stubs + @"
class C
{
    BorderElement M(BorderElement b, bool cond) => b.IsVisible(!cond);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, SetVisibilityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_And_Fixes_Conditional_Reverse_Polarity()
    {
        // cond ? Visible : Collapsed  →  IsVisible(cond)
        var before = Stubs + @"
class C
{
    BorderElement M(BorderElement b, bool cond) => {|REACTOR_VIS_001:b.Set(c => c.Visibility = cond ? Visibility.Visible : Visibility.Collapsed)|};
}";
        var after = Stubs + @"
class C
{
    BorderElement M(BorderElement b, bool cond) => b.IsVisible(cond);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, SetVisibilityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_But_No_Fix_For_Variable_Rhs()
    {
        // Non-mappable RHS (a Visibility variable): the analyzer flags the trap, but no
        // rewrite is offered (TestCode == FixedCode).
        var code = Stubs + @"
class C
{
    BorderElement M(BorderElement b, Visibility v) => {|REACTOR_VIS_001:b.Set(c => c.Visibility = v)|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, SetVisibilityCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonUIElement_Visibility()
    {
        // Near-miss: PlainThing has a Visibility property but does not derive from
        // UIElement, so the '.IsVisible' rewrite would be unsound — do not fire.
        var source = Stubs + @"
class C
{
    WidgetElement M(WidgetElement w) => w.Set(c => c.Visibility = Visibility.Collapsed);
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
