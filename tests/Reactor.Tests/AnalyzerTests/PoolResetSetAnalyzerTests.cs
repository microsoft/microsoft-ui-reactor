using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="PoolResetSetAnalyzer"/> (<c>REACTOR_POOL_001</c>) and its
/// <see cref="PoolResetSetCodeFix"/>. Stubs a minimal Reactor-shaped fluent
/// element so the analyzer's syntactic match against <c>.Set(fe =&gt; fe.PROP = ...)</c>
/// fires without pulling the framework in.
/// </summary>
public class PoolResetSetAnalyzerTests
{
    // Mirrors the real Reactor shape: FakeElement carries the raw FE properties
    // that .Set writes to, and the modifiers (MaxHeight/Margin/HorizontalAlignment/...)
    // are extension methods — same as ElementExtensions.cs in src/Reactor.
    private const string Stubs = @"
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor;

namespace Microsoft.UI.Xaml
{
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum VerticalAlignment { Top, Center, Bottom, Stretch }
    public struct Thickness
    {
        public Thickness(double u) {}
        public Thickness(double l, double t, double r, double b) {}
    }
}

namespace Microsoft.UI.Xaml.Controls
{
using Microsoft.UI.Xaml;

// The .Set receiver is a Button, not a synthetic type, because REACTOR_POOL_001 claims the
// write is unwound on pool return — true only of a control ElementPool actually recycles.
// Button is in PoolableTypes; anything outside it reports REACTOR_MOD_002 instead. Every
// real .Set overload takes a concrete WinUI control for the same reason.
public class Button
{
    public double MaxHeight;
    public double MinHeight;
    public double MaxWidth;
    public double MinWidth;
    public double Width;
    public double Height;
    public double Opacity;
    public Thickness Margin;
    public string AccessKey = string.Empty;
    public HorizontalAlignment HorizontalAlignment;
    public VerticalAlignment VerticalAlignment;

    // Unrelated property — should never trigger.
    public string Text = string.Empty;
}
}

namespace Microsoft.UI.Reactor
{
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public class FakeElement
{
    public FakeElement Set(Action<Button> configure) { configure(new Button()); return this; }
    public FakeElement Apply(Action<Button> configure) { configure(new Button()); return this; }
}

public static class FakeElementExtensions
{
    public static FakeElement MaxHeight(this FakeElement el, double v) => el;
    public static FakeElement MinHeight(this FakeElement el, double v) => el;
    public static FakeElement MaxWidth(this FakeElement el, double v) => el;
    public static FakeElement MinWidth(this FakeElement el, double v) => el;
    public static FakeElement Width(this FakeElement el, double v) => el;
    public static FakeElement Height(this FakeElement el, double v) => el;
    public static FakeElement Opacity(this FakeElement el, double v) => el;
    public static FakeElement AccessKey(this FakeElement el, string v) => el;
    public static FakeElement Margin(this FakeElement el, double u) => el;
    public static FakeElement Margin(this FakeElement el, double l, double t, double r, double b) => el;
    public static FakeElement HorizontalAlignment(this FakeElement el, HorizontalAlignment a) => el;
    public static FakeElement VerticalAlignment(this FakeElement el, VerticalAlignment a) => el;
}
}
";

    [Fact]
    public async Task Fires_For_MaxHeight()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.MaxHeight = 260)|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_HorizontalAlignment()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.HorizontalAlignment = HorizontalAlignment.Center)|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_With_Parenthesized_Lambda()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set((fe) => fe.MinWidth = 100)|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Untrapped_Property()
    {
        // .Text is not in ElementPool.CleanElement's FE-prop reset list and has
        // no equivalent modifier — .Set is legitimate here.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Set(fe => fe.Text = ""hi"");
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Non_Set_Method()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Apply(fe => fe.MaxHeight = 260);
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_MaxHeight()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.MaxHeight = 260)|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.MaxHeight(260);
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_HorizontalAlignment()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.HorizontalAlignment = HorizontalAlignment.Center)|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.HorizontalAlignment(HorizontalAlignment.Center);
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Assigning_A_Captured_Objects_Property()
    {
        // The trapped property is set on a *captured* object, not the .Set lambda
        // parameter, so the pooled-control modifier rewrite would not apply — must not fire.
        // 'other' is deliberately the same poolable control type as the lambda parameter, so
        // being-the-wrong-receiver is the only reason left for the analyzer to stay silent.
        var source = Stubs + @"
class C
{
    void M(Microsoft.UI.Xaml.Controls.Button other)
    {
        var el = new FakeElement();
        el.Set(fe => other.MaxHeight = 260);
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonReactor_Set_Helper()
    {
        // A '.Set' that isn't a Reactor DSL setter (different namespace) must not fire even
        // for a trapped property — the '.Margin(...)' etc. modifiers only exist for Reactor
        // elements, so the fix would not compile.
        var source = Stubs + @"
class C
{
    void M(RawThing r)
    {
        r.Set(x => x.MaxHeight = 260);
    }
}

public class RawThing
{
    public double MaxHeight;
    public RawThing Set(System.Action<RawThing> configure) { configure(this); return this; }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Block-bodied lambdas ────────────────────────────────────────────

    [Fact]
    public async Task Fires_For_Block_Bodied_Lambda_With_Single_Statement()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => { fe.MaxHeight = 260; })|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Diagnostic_For_Block_Bodied_Lambda_With_Multiple_Statements()
    {
        // Flipped from No_Diagnostic_For_Block_Bodied_Lambda_With_Multiple_Statements, which
        // scoped detection to the codefix's reach and said: "If a future PR adds multi-stmt
        // support, this test should flip to a positive case." This is that change.
        //
        // Both halves of that support landed: the analyzer reports every modifier-backed
        // assignment in the body, and PoolResetSetCodeFix rewrites the whole body into a
        // modifier chain when every statement is convertible (see
        // ModifierAvailableAnalyzerTests.CodeFix_Rewrites_Multi_Statement_Block_Into_A_Chain).
        //
        // Detection is still deliberately wider than the fix: a body that mixes convertible
        // and non-convertible statements is reported but not auto-fixed, because a partial
        // extraction would reorder the extracted write against the ones left in .Set. That
        // asymmetry matters — this shape hid live bugs, and the widening immediately surfaced
        // MaxWidth/MaxHeight writes in minesweeper's App.cs that were silently lost on pool
        // reuse.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:{|REACTOR_POOL_001:el.Set(fe => { fe.MaxHeight = 260; fe.MinHeight = 100; })|}|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Block_Bodied_Lambda()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => { fe.MaxHeight = 260; })|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.MaxHeight(260);
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Margin / Thickness translation ──────────────────────────────────

    [Fact]
    public async Task CodeFix_Rewrites_Margin_Uniform_Thickness()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.Margin = new Thickness(8))|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Margin(8);
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Margin_FourArg_Thickness()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.Margin = new Thickness(1, 2, 3, 4))|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Margin(1, 2, 3, 4);
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyzer_Fires_But_CodeFix_Suppressed_For_Opaque_Margin_RHS()
    {
        // RHS is a variable reference, not a Thickness constructor literal —
        // we can't safely translate, so the analyzer fires (the trap is real)
        // but no codefix is offered. The verifier confirms this by leaving
        // TestCode == FixedCode: the warning persists, and no rewrite occurs.
        var code = Stubs + @"
class C
{
    void M(Thickness margin)
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => fe.Margin = margin)|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Suppressed_When_An_Instance_Property_Is_Written_Twice_And_One_Is_Null()
    {
        // The instance-shape half of the per-key fixability rule, and a bug that predates the
        // attached work. Only ONE diagnostic: the null write is deliberately not reported,
        // because ApplyModifiers skips a null modifier value (Reconciler.cs — `m.AccessKey is
        // not null` gates the write). But it shares a property NAME with the reported one, so
        // before the fixable set was made per-key the fix converted it too and emitted
        // `.AccessKey("F").AccessKey(null)` — which compiles, and silently stops performing
        // the explicit clear.
        var code = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => { fe.AccessKey = ""F""; fe.AccessKey = null; })|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Attached properties ─────────────────────────────────────────────
    //
    // The second syntactic shape behind REACTOR_POOL_001: an attached-property write is
    // `Owner.SetPROP(x, v)` — an invocation, not an assignment — so none of the tests above
    // exercise any of this path.

    private const string AttachedStubs = @"
using System;
using Microsoft.UI.Reactor;

#nullable enable

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        public static void SetName(object target, string value) { }
        public static void SetHelpText(object target, string value) { }
        public static void SetPositionInSet(object target, int value) { }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public static class ToolTipService
    {
        public static void SetToolTip(object target, object? value) { }
        public static void SetPlacement(object target, int value) { }
    }

    public static class TitleBar
    {
        public static void SetIsDragRegion(object target, bool value) { }
    }

    // Attached owners with no pool-reset entry — the real-world call sites in
    // docs/_pipeline/apps/layout and samples/apps/widget-creator. Must stay silent.
    public static class Canvas
    {
        public static void SetLeft(object target, double value) { }
        public static void SetTop(object target, double value) { }
    }

    public static class ScrollViewer
    {
        public static void SetVerticalScrollBarVisibility(object target, int value) { }
        public static void SetVerticalScrollMode(object target, int value) { }
    }

    // The .Set receiver, for the same reason as the instance-property stubs: POOL_001 only
    // claims a pool round-trip for a control ElementPool recycles, and Button is one.
    public class Button
    {
        public double Width;
        public string Label = string.Empty;
        public Button Child = null!;
    }

    // Not in ElementPool.PoolableTypes — the counterpart receiver for the de-escalation.
    public class CheckBox
    {
    }
}

namespace Microsoft.UI.Reactor.Layout
{
    public static class FlexPanel
    {
        public static void SetGrow(object target, double value) { }
        public static void SetMinWidth(object target, double value) { }
        public static void SetMinHeight(object target, double value) { }
    }
}

namespace Contoso.Ui
{
    // Same simple name, unrelated namespace — the modifier rewrite has nothing to do
    // with this type, so it must stay silent.
    public static class AutomationProperties
    {
        public static void SetName(object target, string value) { }
    }
}

namespace Microsoft.UI.Reactor
{
    public class FakeElement
    {
        public FakeElement Set(Action<Microsoft.UI.Xaml.Controls.Button> configure) { configure(new Microsoft.UI.Xaml.Controls.Button()); return this; }
    }

    // Same DSL shape, but its control is not pooled.
    public class UnpooledElement
    {
        public UnpooledElement Set(Action<Microsoft.UI.Xaml.Controls.CheckBox> configure) { configure(new Microsoft.UI.Xaml.Controls.CheckBox()); return this; }
    }

    public static class UnpooledElementExtensions
    {
        public static UnpooledElement AutomationName(this UnpooledElement el, string v) => el;
    }

    public static class FakeElementExtensions
    {
        public static FakeElement Width(this FakeElement el, double v) => el;
        public static FakeElement AutomationName(this FakeElement el, string v) => el;
        public static FakeElement HelpText(this FakeElement el, string v) => el;
        public static FakeElement PositionInSet(this FakeElement el, int position, int size) => el;
        public static FakeElement ToolTip(this FakeElement el, string v) => el;
        // Mirrors the real two-argument overload, which writes ToolTipService.Placement too.
        public static FakeElement ToolTip(this FakeElement el, string v, int placement) => el;
        public static FakeElement ToolTipPlacement(this FakeElement el, int v) => el;
        public static FakeElement IsDragRegion(this FakeElement el, bool v) => el;
        public static FakeElement Flex(this FakeElement el, double grow = 0) => el;
    }
}
";

    [Theory]
    // One per owner represented in ModifierTable.AttachedProperties, so a regression that
    // drops a whole owner (e.g. the semantic namespace pin rejecting it) is caught here
    // rather than only by the table-driven theory in PoolResetSetConsistencyTests.
    [InlineData(@"Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, ""Save"")")]
    [InlineData(@"Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, ""Save (Ctrl+S)"")")]
    [InlineData(@"Microsoft.UI.Xaml.Controls.TitleBar.SetIsDragRegion(fe, false)")]
    [InlineData(@"Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(fe, 1)")]
    public async Task Fires_For_Attached_Setter_On_The_Lambda_Parameter(string call)
    {
        var source = AttachedStubs + $@"
class C
{{
    void M()
    {{
        var el = new FakeElement();
        {{|REACTOR_POOL_001:el.Set(fe => {call})|}};
    }}
}}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Attached_Setter_Reports_ModifierAvailable_On_An_Unpooled_Receiver()
    {
        // REACTOR_POOL_001's claim is "CleanElement clears this on pool return", so it needs a
        // receiver the pool recycles. CheckBox is not in ElementPool.PoolableTypes, so the
        // attached write falls to REACTOR_MOD_002 — same advice, without the false claim. The
        // FakeElement line is the positive control: both receivers, one id each, in one body,
        // so a regression that collapses the distinction fails whichever way it collapses.
        var source = AttachedStubs + @"
class C
{
    void M()
    {
        var pooled = new FakeElement();
        {|REACTOR_POOL_001:pooled.Set(fe => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, ""Save""))|};

        var unpooled = new UnpooledElement();
        {|REACTOR_MOD_002:unpooled.Set(cb => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cb, ""Save""))|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Attached_Setter_Through_A_Cast()
    {
        // The WinUI setters are typed on DependencyObject/UIElement, so real call sites
        // sometimes cast the lambda parameter (docs/_pipeline/apps/layout does exactly this
        // for Canvas). A cast does not change which object is written to, so it must not
        // become an escape hatch from the rule.
        var source = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName((object)fe, ""Save""))|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    // Wrappers that do not change WHICH object is written to must all still match. Each of
    // these is a distinct arm of SetLambdaHelpers.IsLambdaParameterReference.
    [InlineData(@"AutomationProperties.SetName((fe), ""Save"")")]                 // parenthesized
    [InlineData(@"AutomationProperties.SetName(fe!, ""Save"")")]                  // null-forgiving
    [InlineData(@"AutomationProperties.SetName((object)(fe!), ""Save"")")]        // cast over both
    public async Task Fires_For_Attached_Setter_Through_A_Target_Wrapper(string call)
    {
        // Also exercises the unqualified owner name (`AutomationProperties.SetName`) reached
        // through a using directive — the other arm of the owner extraction, alongside the
        // fully-qualified form the rest of these tests use. The usings live inside the
        // namespace body because the stubs above already opened namespaces at file scope.
        var source = AttachedStubs + $@"
namespace TestApp
{{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Xaml.Automation;

    class C
    {{
        void M()
        {{
            var el = new FakeElement();
            {{|REACTOR_POOL_001:el.Set(fe => {call})|}};
        }}
    }}
}}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Attached_Setter_In_A_Block_Body_Alongside_Other_Statements()
    {
        // Detection is wider than the fix: an attached write is no less lost for sharing a
        // block with a statement the fix cannot convert.
        var source = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe =>
        {
            var label = ""Save"";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, label);
        })|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    // The regression corpus. Each of these is a real, legitimate call site shape that the
    // invocation-matching must leave alone.
    //
    // Different target — the write does not reach the pooled control the .Set configures.
    [InlineData(@"Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(other, ""Save"")")]
    [InlineData(@"Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe.Child, ""Save"")")]
    // Same simple name, unrelated namespace.
    [InlineData(@"Contoso.Ui.AutomationProperties.SetName(fe, ""Save"")")]
    // Attached owners with no pool-reset entry (docs/_pipeline/apps/layout,
    // samples/ReactorGallery, samples/apps/widget-creator).
    [InlineData(@"Microsoft.UI.Xaml.Controls.Canvas.SetLeft((object)fe, 10)")]
    [InlineData(@"Microsoft.UI.Xaml.Controls.Canvas.SetTop((object)fe, 10)")]
    [InlineData(@"Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(fe, 1)")]
    [InlineData(@"Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(fe, 1)")]
    // A null write is not expressible through the modifier — ApplyModifiers skips a null
    // value, so suggesting the rewrite would change behaviour. All the wrapped forms of
    // null must be caught, not just the bare literal.
    [InlineData(@"Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, null)")]
    [InlineData(@"Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, (string?)null)")]
    [InlineData(@"Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, default)")]
    // Named arguments are rejected: they may be written out of order, which would make the
    // positional target/value read wrong.
    [InlineData(@"Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(target: fe, value: ""Save"")")]
    public async Task No_Diagnostic_For_Attached_Setter(string call)
    {
        var source = AttachedStubs + $@"
class C
{{
    void M(FakeElement other)
    {{
        var el = new FakeElement();
        el.Set(fe => {call});
    }}
}}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Attached_Setter_In_A_NonReactor_Set_Helper()
    {
        // Same guard as the assignment arm: the '.AutomationName(...)' modifiers only exist
        // for Reactor elements, so a lookalike '.Set' must not be reported.
        var source = AttachedStubs + @"
class C
{
    void M(RawAttachedThing r)
    {
        r.Set(x => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(x, ""Save""));
    }
}

public class RawAttachedThing
{
    public RawAttachedThing Set(System.Action<RawAttachedThing> configure) { configure(this); return this; }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Attached_ToolTip()
    {
        var before = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(b => Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(b, ""This is a native tooltip""))|};
    }
}";

        var after = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.ToolTip(""This is a native tooltip"");
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_A_Block_Mixing_Instance_And_Attached_Writes()
    {
        var before = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:{|REACTOR_POOL_001:el.Set(fe => { fe.Width = 10; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, ""Save""); })|}|};
    }
}";

        var after = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        el.Width(10).AutomationName(""Save"");
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    // Reported, but deliberately not auto-fixed. TestCode == FixedCode asserts the diagnostic
    // survives AND that no rewrite is offered — flipping any of these to AutoFix: true in
    // ModifierTable would break this test.
    //
    // Arity: SetPositionInSet writes one DP, .PositionInSet(position, size) writes two.
    [InlineData(@"Microsoft.UI.Xaml.Automation.AutomationProperties.SetPositionInSet(fe, 2)")]
    // N:1: every FlexPanel property funnels into one .Flex(...) that replaces the whole
    // FlexAttached record.
    [InlineData(@"Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(fe, 1)")]
    // Type: SetToolTip takes object, .ToolTip takes string — `tip` is an object here, so the
    // rewrite would not compile.
    [InlineData(@"Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, tip)")]
    // A maybe-null string would compile, but `.ToolTip(maybeTip)` silently performs no write
    // when the value IS null (ApplyModifiers skips a null modifier value) — the same
    // behaviour change the literal-null gate exists to prevent.
    [InlineData(@"Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, maybeTip)")]
    // The value references the lambda parameter, which does not survive the rewrite.
    [InlineData(@"Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, fe.Label)")]
    // Setter/property name divergence: FlexPanel.SetMinWidth writes FlexMinWidthProperty.
    // Flipping either row to AutoFix: true would rewrite it to .Flex(value), which sets
    // `grow`, not `minWidth`.
    [InlineData(@"Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(fe, 50)")]
    [InlineData(@"Microsoft.UI.Reactor.Layout.FlexPanel.SetMinHeight(fe, 50)")]
    public async Task Analyzer_Fires_But_CodeFix_Suppressed_For_Attached_Setter(string call)
    {
        var code = AttachedStubs + $@"
class C
{{
    void M(object tip, string? maybeTip)
    {{
        var el = new FakeElement();
        {{|REACTOR_POOL_001:el.Set(fe => {call})|}};
    }}
}}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Suppressed_When_A_Duplicate_Setter_Is_Not_Uniformly_Fixable()
    {
        // Both bags the analyzer hands the fix are keyed by setter name, not by occurrence.
        // Without the per-key conjunction in the analyzer, the fixable verdict earned by the
        // string literal would authorize the object-valued sibling too, and the fix would emit
        // `.ToolTip("Save").ToolTip(tip)` — which does not compile.
        var code = AttachedStubs + @"
class C
{
    void M(object tip)
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:{|REACTOR_POOL_001:el.Set(fe => { Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, ""Save""); Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, tip); })|}|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Suppressed_When_A_Duplicate_Setter_Writes_Null()
    {
        // Only ONE diagnostic: the null write is deliberately not reported. But it shares a
        // setter name with the reported one, so without the per-key conjunction the fix would
        // convert it too and emit `.ToolTip(null)` — a call ApplyModifiers ignores, silently
        // dropping an explicit clear.
        var code = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.Set(fe => { Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, ""Save""); Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, null); })|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Suppressed_When_The_Receiver_Already_Applies_The_Same_Attached_Modifier()
    {
        // Modifiers run after setters, so `.AutomationName("old")` currently wins and the name
        // renders as "old". Rewriting to `.AutomationName("old").AutomationName("new")` makes
        // the last call win instead. Same precedence-inversion guard the instance shape has,
        // reached through the attached path.
        var code = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.AutomationName(""old"").Set(fe => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, ""new""))|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Suppressed_When_The_Receiver_Already_Sets_Placement_Through_ToolTip()
    {
        // Modifiers run after setters, so today the receiver's `.ToolTip("Save", 1)` wins and
        // the placement renders as 1. Rewriting to `.ToolTip("Save", 1).ToolTipPlacement(2)`
        // makes the last modifier win instead, flipping it to 2 — a behaviour change, not a
        // refactor. A plain name comparison misses this because the conflicting modifier is
        // called `ToolTip`, not `ToolTipPlacement`.
        var code = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:el.ToolTip(""Save"", 1).Set(fe => Microsoft.UI.Xaml.Controls.ToolTipService.SetPlacement(fe, 2))|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Suppressed_When_A_Block_Mixes_Fixable_And_Unfixable_Attached_Writes()
    {
        // All-or-nothing: converting only the fixable half would leave a residual .Set and
        // move the extracted write from the setter phase into the modifier phase.
        var code = AttachedStubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();
        {|REACTOR_POOL_001:{|REACTOR_POOL_001:el.Set(fe => { Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, ""Save""); Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(fe, 1); })|}|};
    }
}";

        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Contravariance and the .Set lambda ────────────────────────────────────────────
    //
    // A review of this PR raised REACTOR_POOL_001 misclassifying a *contravariant* `.Set`
    // lambda: because `Action<T>` is contravariant, the argument would ostensibly be free to
    // declare a BASE type (an `Action<Control>` lambda passed to a `Set(Action<Button>)`
    // overload), <see cref="PoolResetSetAnalyzer"/>'s receiver check would read `Control` off
    // that parameter, and a genuinely pooled receiver would be downgraded to
    // `REACTOR_MOD_002`.
    //
    // The premise does not hold, and the two tests below are the measurement rather than the
    // argument. Variance governs *delegate conversions*, not *anonymous-function*
    // conversions: C# requires an explicitly typed lambda's parameter types to match the
    // delegate's exactly, so the claimed call site does not compile at all. The one argument
    // shape that does convert contravariantly — a delegate-typed value — carries no lambda
    // syntax, so `SetLambdaHelpers.GetSingleLambdaParameter` returns null and the analyzer
    // returns before it classifies anything. Neither route reaches the receiver check with a
    // base type, so neither can produce the downgrade.
    //
    // Both tests are written so the refutation can fail: if a future C# relaxed the parameter
    // match, the first would report the expected compiler errors as absent rather than
    // quietly pass.

    [Fact]
    public async Task Contravariance_Does_Not_Permit_A_Base_Typed_Set_Lambda()
    {
        // `object` is the strongest available vehicle for the claim — it is a base of every
        // receiver type, so if variance did reach lambdas, this is the case that would
        // succeed most readily.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();

        // The claimed call site. CS1678 (""declared as type 'object' but should be
        // 'Button'"") and CS1661 (""the parameter types do not match the delegate parameter
        // types"") are the compiler refusing it: there is no base-typed .Set lambda for the
        // receiver check to misread.
        el.Set((object {|CS1678:fe|}) {|CS1661:=>|} { });

        // Positive control, same overload, same compilation: the contravariant conversion IS
        // available here. Action<object> converts to Action<Button> and this raises nothing.
        // So the line above is rejected for being a lambda, not for lacking the conversion —
        // without this control the CS errors above would be equally consistent with variance
        // simply being unavailable on FakeElement.Set.
        Action<object> handler = o => { };
        el.Set(handler);
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Set_With_A_Delegate_Value_Is_Not_Classified_At_All()
    {
        // The delegate-typed value is the only argument shape a contravariant conversion can
        // take. It is not lambda syntax, so GetSingleLambdaParameter returns null and the
        // analyzer returns at the top of the .Set handler — before the receiver check, and
        // before either REACTOR_POOL_001 or the REACTOR_MOD_002 downgrade is chosen. The
        // claimed misclassification has no site at which to occur.
        //
        // The silent half is non-vacuous only because of the second .Set: the identical
        // write, as a lambda, in the SAME compilation, still fires. Neuter the analyzer and
        // that marker fails — so the silence above is a measured property of the
        // delegate-value shape and not of an inert analyzer.
        var source = Stubs + @"
class C
{
    void M()
    {
        var el = new FakeElement();

        Action<Microsoft.UI.Xaml.Controls.Button> handler = fe => fe.MaxHeight = 260;
        el.Set(handler);

        {|REACTOR_POOL_001:el.Set(fe => fe.MaxHeight = 260)|};
    }
}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
