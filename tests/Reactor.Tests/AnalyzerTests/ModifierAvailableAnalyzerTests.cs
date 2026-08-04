using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="PoolResetSetAnalyzer"/> (<c>REACTOR_MOD_002</c>).
/// <para>
/// The gating tests are the point of this file. <c>ApplyModifiers</c> applies
/// <c>Padding</c>/<c>CornerRadius</c>/<c>BorderThickness</c>/<c>BorderBrush</c>/<c>Background</c>
/// only to specific runtime control types, while WinUI declares those DPs on more types than
/// that. Suggesting the modifier on a receiver the reconciler skips would produce a rewrite
/// that compiles and silently does nothing — the regression that had to be reverted from
/// ValueList.cs (a Grid) and CellComponent.cs (a TextBlock).
/// </para>
/// </summary>
public class ModifierAvailableAnalyzerTests
{
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Xaml
{
    public class UIElement { public bool IsHitTestVisible { get; set; } }
    public class FrameworkElement : UIElement { }
    public struct Thickness { public Thickness(double u) { } }
    public struct CornerRadius { public CornerRadius(double u) { } }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush { }
}

namespace Microsoft.UI.Xaml.Controls
{
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Media;

    // The common modifier allow-lists intentionally name only the concrete controls
    // whose properties ApplyModifiers writes.
    public class Control : FrameworkElement
    {
        public Thickness Padding { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Thickness BorderThickness { get; set; }
        public Brush BorderBrush { get; set; }
        public Brush Background { get; set; }
        public bool IsEnabled { get; set; }
        public HorizontalAlignment HorizontalContentAlignment { get; set; }
    }

    public class Border : FrameworkElement
    {
        public Thickness Padding { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Brush Background { get; set; }
    }

    public class Panel : FrameworkElement
    {
        public Brush Background { get; set; }
    }

    // Grid is a Panel: Background, Padding, and CornerRadius apply; Border* do not.
    public class Grid : Panel
    {
        public Thickness Padding { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Thickness BorderThickness { get; set; }
    }

    // StackPanel is in both Padding's and CornerRadius's allow-lists.
    public class StackPanel : Panel
    {
        public Thickness Padding { get; set; }
        public CornerRadius CornerRadius { get; set; }
    }

    public class RelativePanel : Panel
    {
        public Thickness Padding { get; set; }
        public CornerRadius CornerRadius { get; set; }
    }

    // TextBlock is not a Control, but ApplyModifiers grew its own Padding arm for issue #950.
    public class TextBlock : FrameworkElement
    {
        public Thickness Padding { get; set; }
    }

    public class Button : Control { }

    // Not in ElementPool.PoolableTypes, unlike Button. Same base, so every control gate that
    // names Control admits both — which is precisely why poolability cannot be read off a gate.
    public class CheckBox : Control { }
}

namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;

    public record ButtonElement;
    public record CheckBoxElement;
    public record GridElement;
    public record StackElement;
    public record RelativePanelElement;
    public record BorderElement;
    public record TextBlockElement;

    public static class Ext
    {
        public static ButtonElement Set(this ButtonElement el, Action<Button> configure) => el;
        public static CheckBoxElement Set(this CheckBoxElement el, Action<CheckBox> configure) => el;
        public static GridElement Set(this GridElement el, Action<Grid> configure) => el;
        public static StackElement Set(this StackElement el, Action<StackPanel> configure) => el;
        public static RelativePanelElement Set(this RelativePanelElement el, Action<RelativePanel> configure) => el;
        public static BorderElement Set(this BorderElement el, Action<Border> configure) => el;
        public static TextBlockElement Set(this TextBlockElement el, Action<TextBlock> configure) => el;

        // Modifier stubs so the code-fix tests' FixedCode compiles.
        public static T IsEnabled<T>(this T el, bool enabled = true) => el;
        public static T Padding<T>(this T el, double uniform) => el;
        public static T Padding<T>(this T el, double l, double t, double r, double b) => el;
        public static T CornerRadius<T>(this T el, double radius) => el;
        public static T BorderThickness<T>(this T el, double thickness) => el;
        public static T Background<T>(this T el, Microsoft.UI.Xaml.Media.Brush brush) => el;
        public static T HorizontalContentAlignment<T>(this T el, Microsoft.UI.Xaml.HorizontalAlignment a) => el;
    }
}
";

    // ---- ungated properties ----

    [Fact]
    public async Task Fires_For_IsEnabled()
    {
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => c.IsEnabled = false)|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Each_Modifier_Backed_Write_In_A_Block()
    {
        // Two reportable writes in one body -> two diagnostics on the same invocation.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c => { c.IsEnabled = false; c.Padding = new Thickness(4); })|}|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ---- gating: the reason this analyzer needs a receiver check ----

    [Fact]
    public async Task Fires_For_Padding_On_Control()
    {
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => c.Padding = new Thickness(8))|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Padding_On_Grid_And_RelativePanel()
    {
        // Grid is pooled and RelativePanel is not, so the same gated write reports under two
        // different ids. That pairing is the point: it pins rule selection to the receiver
        // rather than to the property, which a same-id pair could not distinguish.
        var source = Stubs + @"
class C
{
    GridElement G(GridElement g) => {|REACTOR_POOL_001:g.Set(x => x.Padding = new Thickness(8))|};
    RelativePanelElement R(RelativePanelElement r) => {|REACTOR_MOD_002:r.Set(x => x.Padding = new Thickness(8))|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_IsEnabled_On_Button_But_Not_On_Unpooled_CheckBox()
    {
        // IsEnabled declares no control gate at all, so nothing about the gates distinguishes
        // these two receivers — both are Controls and both are written by ApplyModifiers. Only
        // ElementPool.PoolableTypes separates them: Button is recycled, CheckBox is not. Without
        // the poolable mirror both report POOL_001, and the Warning on CheckBox asserts that the
        // write is unwound on pool return for a control the pool never touches.
        var source = Stubs + @"
class C
{
    ButtonElement B(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => c.IsEnabled = false)|};
    CheckBoxElement K(CheckBoxElement k) => {|REACTOR_MOD_002:k.Set(c => c.IsEnabled = false)|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Background_On_Grid_But_Not_On_Unpooled_RelativePanel()
    {
        // Background's gate names Panel, and CleanElement really does clear Background for every
        // Panel — so the arm-coverage gate cannot separate these two, and a poolResetGate listing
        // base names could not either without misdescribing what the Panel arm does. RelativePanel
        // is excluded because it is never recycled, which is a fact about the receiver's own type.
        var source = Stubs + @"
class C
{
    GridElement G(GridElement g, Microsoft.UI.Xaml.Media.Brush br) => {|REACTOR_POOL_001:g.Set(x => x.Background = br)|};
    RelativePanelElement R(RelativePanelElement r, Microsoft.UI.Xaml.Media.Brush br) => {|REACTOR_MOD_002:r.Set(x => x.Background = br)|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Padding_On_TextBlock()
    {
        // TextBlock is not a Control, yet ApplyModifiers has a dedicated Padding arm for it
        // (issue #950), so '.Padding(...)' really does reach the control and the rewrite is
        // sound. Together with the concrete-panel cases, this pins the gate to the supported
        // types rather than "anything that has a Padding property".
        var source = Stubs + @"
class C
{
    TextBlockElement M(TextBlockElement t) => {|REACTOR_POOL_001:t.Set(x => x.Padding = new Thickness(8))|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_BorderThickness_On_Grid()
    {
        var source = Stubs + @"
class C
{
    GridElement M(GridElement g) => g.Set(x => x.BorderThickness = new Thickness(1));
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Background_On_Grid()
    {
        // Background's allow-list DOES include Panel, so Grid is fine here. Proves the
        // gate is per-property rather than one shared predicate. Uses a non-null value so
        // the null guard cannot be what makes this pass.
        var source = Stubs + @"
class C
{
    GridElement M(GridElement g, Microsoft.UI.Xaml.Media.Brush brush)
        => {|REACTOR_POOL_001:g.Set(x => x.Background = brush)|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Padding_And_CornerRadius_On_Concrete_Panels()
    {
        // RelativePanel still fires — the gate admits it, so the modifier really does reach the
        // control — but as REACTOR_MOD_002, because ElementPool never recycles a RelativePanel
        // and POOL_001's "unwound on pool return" clause would be false for it.
        var source = Stubs + @"
class C
{
    StackElement A(StackElement s) => {|REACTOR_POOL_001:s.Set(x => x.Padding = new Thickness(4))|};
    StackElement B(StackElement s) => {|REACTOR_POOL_001:s.Set(x => x.CornerRadius = new CornerRadius(4))|};
    GridElement G(GridElement g) => {|REACTOR_POOL_001:g.Set(x => x.CornerRadius = new CornerRadius(4))|};
    RelativePanelElement R(RelativePanelElement r) => {|REACTOR_MOD_002:r.Set(x => x.CornerRadius = new CornerRadius(4))|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ---- near misses ----

    [Fact]
    public async Task Does_Not_Fire_For_Null_Assignment()
    {
        // `.Background(null)` is not equivalent to `.Set(x => x.Background = null)`:
        // ApplyModifiers reads a null modifier value as "not supplied" and only clears the
        // property when the previous render had one. Suggesting the rewrite would change
        // behaviour, so a null/default RHS is skipped. Real site:
        // samples/ReactorGallery/ControlPages/Media/ParallaxViewPage.cs.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Set(c => c.Background = null);
    ButtonElement N(ButtonElement b) => b.Set(c => c.Background = default);
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_ContentAlignment_And_Background_On_Control()
    {
        // Guards that the map lookup is by exact property name and that a Control
        // receiver satisfies both the ungated and the gated arms. The two writes also
        // report under different ids since issue #985 — Background is pool-reset
        // (REACTOR_POOL_001), the content-alignment pair is not (REACTOR_MOD_002) — which
        // pins the severity split to the property rather than to the receiver.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_MOD_002:b.Set(c => c.HorizontalContentAlignment = HorizontalAlignment.Left)|};
    ButtonElement N(ButtonElement b, Microsoft.UI.Xaml.Media.Brush brush) => {|REACTOR_POOL_001:b.Set(c => c.Background = brush)|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Unmapped_Property()
    {
        // Name has no modifier today — must stay silent (it is the single most common
        // .Set property in the repo, so a false positive here would be very loud).
        var source = Stubs.Replace(
            "public class Button : Control { }",
            "public class Button : Control { public string Name { get; set; } }") + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Set(c => c.Name = ""x"");
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_RequestedTheme()
    {
        // Owned by RequestedThemeSetAnalyzer (REACTOR_THEME_003) — must not double-report.
        var source = Stubs.Replace(
            "public class Button : Control { }",
            "public class Button : Control { public int RequestedTheme { get; set; } }") + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Set(c => c.RequestedTheme = 1);
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Assignment_To_Captured_Object()
    {
        // Only the lambda's own parameter is the configured control.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b, Microsoft.UI.Xaml.Controls.Button other)
        => b.Set(c => other.IsEnabled = false);
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Unrelated_Set_Helper()
    {
        // A user-defined .Set with the same shape on a non-Reactor type must be ignored.
        var source = @"
using System;

class Thing { public bool IsEnabled { get; set; } }
static class Ext2 { public static T Set<T>(this T t, Action<Thing> f) => t; }

class C
{
    string M(string s) => s.Set(t => t.IsEnabled = false);
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ---- code fix ----
    //
    // PoolResetSetCodeFix declares both REACTOR_POOL_001 and REACTOR_MOD_002 as fixable.
    // These prove the MOD_002 half actually rewrites, which was previously wired but never
    // exercised — the fix looked up the shared ModifierTable, so a mistake there would have
    // surfaced only in a consumer's IDE. Several of them moved to REACTOR_POOL_001 with
    // issue #985; the fixer path is identical for both ids, and
    // CodeFix_Chains_Across_Both_Rule_Ids_In_One_Body still holds the mixed-id case.

    [Fact]
    public async Task CodeFix_Rewrites_Ungated_Property()
    {
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => c.IsEnabled = false)|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.IsEnabled(false);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Translates_Thickness_For_Gated_Padding()
    {
        // Padding is Thickness-typed but the modifier takes doubles, so the fix has to
        // unpack the constructor arguments rather than pass the RHS through. Receiver is a
        // Button (a Control), so the gate admits it.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => c.Padding = new Thickness(8))|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Padding(8);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Multi_Statement_Block_Into_A_Chain()
    {
        // The support the original No_Diagnostic_For_Block_Bodied_Lambda_With_Multiple_Statements
        // comment anticipated: "the codefix ... would need to extract the matched assignment
        // while preserving the rest of the body".
        //
        // Resolved by converting the WHOLE body rather than extracting one statement, so
        // nothing is left behind to preserve. That is exactly N applications of the
        // single-assignment rewrite and carries no new risk — whereas a partial extraction
        // would move one write from the setter phase into the modifier phase and change its
        // order relative to the statements left in .Set.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c => { c.IsEnabled = false; c.Padding = new Thickness(4); })|}|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.IsEnabled(false).Padding(4);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Preserves_Source_Order_In_The_Chain()
    {
        // Chain order follows source order, so any ordering the author relied on between the
        // writes survives the rewrite. Same two properties as above, written the other way
        // round, must produce the reversed chain.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c => { c.Padding = new Thickness(4); c.IsEnabled = false; })|}|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Padding(4).IsEnabled(false);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Chains_Across_Both_Rule_Ids_In_One_Body()
    {
        // The common real-world shape: a pool-reset property (REACTOR_POOL_001) sharing a body
        // with a modifier-available one (REACTOR_MOD_002). One invocation, two rule IDs, and
        // the harness hands the fixer each diagnostic separately — so this is the case that
        // proves each diagnostic carries the complete reported set rather than just its own.
        //
        // The MOD_002 half is HorizontalContentAlignment, not IsEnabled: issue #985 moved
        // IsEnabled (and Padding / CornerRadius / Border* / Background) to POOL_001, which
        // would have quietly turned this into a same-id test that no longer exercises the
        // cross-id path it is named for. The content-alignment pair is the remaining ungated,
        // non-pool-reset entry in ModifierTable.
        var stubs = Stubs
            .Replace(
                "public class FrameworkElement : UIElement { }",
                "public class FrameworkElement : UIElement { public double MaxHeight { get; set; } }")
            .Replace(
                "public static T IsEnabled<T>(this T el, bool enabled = true) => el;",
                "public static T IsEnabled<T>(this T el, bool enabled = true) => el;\n        public static T MaxHeight<T>(this T el, double value) => el;");
        var before = stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_MOD_002:b.Set(c => { c.MaxHeight = 260; c.HorizontalContentAlignment = HorizontalAlignment.Left; })|}|};
}";
        var after = stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.MaxHeight(260).HorizontalContentAlignment(HorizontalAlignment.Left);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_Block_Mixing_Visibility_With_A_Modifier_Property()
    {
        // Visibility is owned by REACTOR_VIS_001 and SetVisibilityCodeFix, so it is never part
        // of a modifier chain. Two fixers see this one invocation and both must decline:
        // SetVisibilityCodeFix because the body is multi-statement, this one because
        // Visibility is absent from the reported set. Neither may half-convert it.
        var source = Stubs.Replace(
            "public class UIElement { public bool IsHitTestVisible { get; set; } }",
            "public class UIElement { public bool IsHitTestVisible { get; set; } public Visibility Visibility { get; set; } }")
            .Replace(
            "public enum HorizontalAlignment { Left, Center, Right, Stretch }",
            "public enum HorizontalAlignment { Left, Center, Right, Stretch }\n    public enum Visibility { Visible, Collapsed }") + @"
class C
{
    ButtonElement M(ButtonElement b)
        => {|REACTOR_VIS_001:{|REACTOR_POOL_001:b.Set(c => { c.Visibility = Visibility.Collapsed; c.IsEnabled = false; })|}|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Keeps_Last_Write_Wins_For_A_Repeated_Property()
    {
        // Two writes to one property. The chain is order-preserving and LayoutModifiers.Merge
        // resolves collisions as `other ?? this`, so the later call wins — matching .Set's
        // last-write-wins. Locks that equivalence in: a chain built in reverse, or a merge
        // with the opposite precedence, would silently change the rendered value.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c => { c.Padding = new Thickness(4); c.Padding = new Thickness(8); })|}|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Padding(4).Padding(8);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_FontSize_On_RichTextBlock_Via_The_Type_Specific_Overload()
    {
        // RichTextBlock is neither Control nor TextBlock, so ApplyModifiers never writes the
        // GENERIC .FontSize(n) to it — but RichTextBlockElement declares its own overload, and
        // that route is sound. The two gates are OR'd precisely so this case is reachable.
        var source = FontStubs + @"
class C
{
    RichTextBlockElement M(RichTextBlockElement r) => {|REACTOR_MOD_002:r.Set(x => x.FontSize = 14)|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_FontSize_On_A_Receiver_With_Neither_Route()
    {
        // ContentPresenter is the shape the OR must still reject: not Control, not TextBlock,
        // and ContentPresenterElement declares no type-specific overload — so BOTH routes fail
        // and the generic modifier would silently write nothing. Without this the OR could be
        // "always pass" and the FontSize test above would not prove anything.
        var source = FontStubs + @"
class C
{
    ContentPresenterElement M(ContentPresenterElement p) => p.Set(x => x.FontSize = 14);
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Stubs for the font gates: a RichTextBlock and a ContentPresenter, both deriving from
    /// FrameworkElement (as in real WinUI) so neither satisfies the Control|TextBlock gate,
    /// with a type-specific overload declared only for RichTextBlockElement.
    /// </summary>
    private const string FontStubs = @"
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Xaml
{
    public class UIElement { }
    public class FrameworkElement : UIElement { }
}

namespace Microsoft.UI.Xaml.Controls
{
    using Microsoft.UI.Xaml;
    public class Control : FrameworkElement { public double FontSize { get; set; } }
    public class TextBlock : FrameworkElement { public double FontSize { get; set; } }
    // Neither of these is a Control or a TextBlock, but both expose the FontSize DP.
    public class RichTextBlock : FrameworkElement { public double FontSize { get; set; } }
    public class ContentPresenter : FrameworkElement { public double FontSize { get; set; } }
}

namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Xaml.Controls;

    public record RichTextBlockElement;
    public record ContentPresenterElement;

    public static class FontExt
    {
        public static RichTextBlockElement Set(this RichTextBlockElement el, Action<RichTextBlock> configure) => el;
        public static ContentPresenterElement Set(this ContentPresenterElement el, Action<ContentPresenter> configure) => el;

        public static T FontSize<T>(this T el, double size) => el;
        // The type-specific overload that makes the suggestion sound on a RichTextBlock.
        public static RichTextBlockElement FontSize(this RichTextBlockElement el, double size) => el;
    }
}
";

    [Fact]
    public async Task Fires_For_A_Type_Specific_Modifier_On_A_Derived_Element()
    {
        // Element records are not sealed, and an extension declared on RichTextBlockElement
        // is equally callable on a type derived from it — so the element gate walks the base
        // chain. Matching the exact name would drop the diagnostic on a receiver where the
        // rewrite compiles perfectly well.
        var source = FontStubs.Replace(
            "public record RichTextBlockElement;",
            "public record RichTextBlockElement;\n    public record StyledRichTextBlockElement : RichTextBlockElement;") + @"
class C
{
    RichTextBlockElement M(StyledRichTextBlockElement r) => {|REACTOR_MOD_002:r.Set(x => x.FontSize = 14)|};
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_A_Same_Named_Element_Outside_The_Reactor_Namespace()
    {
        // The base-chain walk still pins the namespace, so an unrelated type that merely
        // shares a name does not satisfy the gate. Without that guard, widening from exact
        // name matching would start firing on foreign types.
        var source = FontStubs.Replace(
            "public record ContentPresenterElement;",
            "public record ContentPresenterElement;") + @"
namespace Other
{
    using System;
    using Microsoft.UI.Xaml.Controls;

    // Same simple name as the Reactor element, different namespace.
    public record RichTextBlockElement;

    public static class OtherExt
    {
        public static RichTextBlockElement Set(this RichTextBlockElement el, Action<RichTextBlock> configure) => el;
        public static RichTextBlockElement FontSize(this RichTextBlockElement el, double size) => el;
    }

    class D
    {
        RichTextBlockElement M(RichTextBlockElement r) => r.Set(x => x.FontSize = 14);
    }
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_When_The_Value_References_The_Lambda_Parameter()
    {
        // The RHS is copied verbatim into the modifier call, but the lambda parameter does not
        // survive the rewrite — the lambda is deleted. Converting this would emit
        // `b.IsEnabled(c.Opacity > 0)`, where `c` is unbound: CS0103.
        var source = Stubs.Replace(
            "public class UIElement { public bool IsHitTestVisible { get; set; } }",
            "public class UIElement { public bool IsHitTestVisible { get; set; } public double Opacity { get; set; } }") + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => c.IsEnabled = c.Opacity > 0)|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_When_The_Receiver_Already_Applies_The_Same_Modifier()
    {
        // Setters and modifiers run in different phases and modifiers run SECOND: ApplySetters
        // is called from inside the mount/update dispatch, ApplyModifiers after it returns. So
        // `.IsEnabled(true).Set(c => c.IsEnabled = false)` renders true — the modifier wins —
        // while the naive rewrite `.IsEnabled(true).IsEnabled(false)` renders false, because
        // the modifier merge is last-wins. Converting would silently invert the result.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.IsEnabled(true).Set(c => c.IsEnabled = false)|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Still_Offers_The_Rewrite_For_An_Unrelated_Preceding_Modifier()
    {
        // The precedence guard must be scoped to the SAME modifier. A different one in the
        // receiver chain does not interact, so the fix should still be offered — otherwise the
        // guard would suppress most real call sites, which are already fluent chains.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Padding(8).Set(c => c.IsEnabled = false)|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Padding(8).IsEnabled(false);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("(Microsoft.UI.Xaml.Media.Brush)null")]
    [InlineData("((Microsoft.UI.Xaml.Media.Brush)null)")]
    [InlineData("(Microsoft.UI.Xaml.Media.Brush)null!")]
    [InlineData("default(Microsoft.UI.Xaml.Media.Brush)")]
    public async Task No_Diagnostic_For_A_Wrapped_Null_Assignment(string nullExpression)
    {
        // ApplyModifiers skips a null modifier value, so `.Background(null)` does not perform
        // the write that `.Set(x => x.Background = null)` does. A bare-literal test misses the
        // cast / parenthesised / null-forgiving spellings, each of which still assigns null.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.Set(c => c.Background = " + nullExpression + @");
}";
        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_CornerRadius_And_BorderThickness_Struct_Values()
    {
        // The struct-unpacking branch of TryBuildModifierArguments handles four property names
        // but Margin/Padding were the only ones covered. CornerRadius takes the OTHER struct
        // type, so it exercises the `structName` selection that Padding cannot.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c => { c.CornerRadius = new CornerRadius(6); c.BorderThickness = new Thickness(2); })|}|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b.CornerRadius(6).BorderThickness(2);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_A_CornerRadius_Value_It_Cannot_Unpack()
    {
        // Only the 1-arg and 4-arg constructor forms map onto modifier overloads. A variable
        // (or any other shape) has no safe translation, so the diagnostic stands unfixed
        // rather than the fix guessing at a conversion.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b, CornerRadius radius) => {|REACTOR_POOL_001:b.Set(c => c.CornerRadius = radius)|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Carries_Comments_Onto_The_Chain()
    {
        // Matches Roslyn's UseObjectInitializer, which carries each matched statement's
        // leading trivia onto the element that statement becomes. Here that element is the '.'
        // introducing the modifier call — a legal comment position, and safe for '//' because
        // the statement's own line break travels with it.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c =>
    {
        // theme spec says disabled until loaded
        c.IsEnabled = false;
        c.Padding = new Thickness(4);
    })|}|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b
        // theme spec says disabled until loaded
        .IsEnabled(false)
        .Padding(4);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Preserves_A_Trailing_Comment_Between_The_Calls()
    {
        // Carrying the PRECEDING token's trailing trivia (not just the statement's own leading
        // trivia) is what gives a same-line trailing comment a home. It is attached to the end
        // of the previous call — the same slot Roslyn uses when it hangs a statement's
        // semicolon trivia off the separator comma — so the raw rewrite reads
        //     .IsEnabled(false) // only until the theme loads
        // and Roslyn's formatter then normalises it onto its own line. Either way the comment
        // survives and stays between the two calls it separated as statements. Only the FINAL
        // statement's trailing comment has nowhere to go — see the decline test below.
        var before = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c =>
    {
        c.IsEnabled = false; // only until the theme loads
        c.Padding = new Thickness(4);
    })|}|};
}";
        var after = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => b
        .IsEnabled(false)
        // only until the theme loads
        .Padding(4);
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_When_A_Trailing_Comment_Has_Nowhere_To_Go()
    {
        // Roslyn can park its last statement's trailing trivia on the last initializer element
        // because a '}' follows. A chain has no such slot: this comment would land immediately
        // before the enclosing ';' and comment it out. Declining beats relocating it to a line
        // the author did not write it on.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c =>
    {
        c.IsEnabled = false;
        c.Padding = new Thickness(4); // matches the design token
    })|}|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_When_A_Comment_Dangles_Before_The_Closing_Brace()
    {
        // Leading trivia of '}' — not attached to any statement, so no step would carry it.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:{|REACTOR_POOL_001:b.Set(c =>
    {
        c.IsEnabled = false;
        c.Padding = new Thickness(4);
        // TODO: revisit once theming lands
    })|}|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_Body_Containing_A_Preprocessor_Directive()
    {
        // Conditionally-compiled statements are inactive *trivia*, not members of
        // block.Statements — so the completeness check cannot see them and the rewrite would
        // delete the whole #if along with the code inside it. Only the directive guard
        // catches this. Here the release build sees one statement (Padding) and would happily
        // emit `.Padding(4)`, silently dropping the DEBUG-only IsEnabled write.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c =>
    {
#if DEBUG
        c.IsEnabled = false;
#endif
        c.Padding = new Thickness(4);
    })|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_Block_Containing_A_Non_Assignment_Statement()
    {
        // The dangerous shape. GetLambdaAssignments filters the block down to assignment
        // statements, so a method call is invisible to it — but the fix replaces the WHOLE
        // invocation. Without a completeness check the rewrite silently deletes c.Focus().
        var source = Stubs.Replace(
            "public bool IsEnabled { get; set; }",
            "public bool IsEnabled { get; set; } public void Focus() { }") + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => { c.IsEnabled = false; c.Focus(); })|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_Block_Assigning_To_A_Different_Receiver()
    {
        // The analyzer only reports writes whose receiver is the lambda parameter, but the
        // reported set it hands the fix is keyed by property NAME. A second write to the same
        // property on a captured variable matches that name without being reported, so a
        // name-only check would fold `other.IsEnabled` into the chain and drop it.
        var source = Stubs + @"
class C
{
    ButtonElement M(ButtonElement b, Microsoft.UI.Xaml.Controls.Button other)
        => {|REACTOR_POOL_001:b.Set(c => { c.IsEnabled = false; other.IsEnabled = true; })|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_Mixed_Block_With_An_Unmapped_Statement()
    {
        // One convertible assignment sharing a body with a statement that has no modifier.
        // Converting only the first would leave a residual .Set and move that write into the
        // modifier phase, reordering it against what stays behind — so the fix declines and
        // the diagnostic stands unfixed. FixedCode is identical to TestCode.
        var source = Stubs.Replace(
            "public class Button : Control { }",
            "public class Button : Control { public string Name { get; set; } }") + @"
class C
{
    ButtonElement M(ButtonElement b) => {|REACTOR_POOL_001:b.Set(c => { c.IsEnabled = false; c.Name = ""x""; })|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Declines_Mixed_Block_With_A_Gated_Out_Statement()
    {
        // The subtle one. Both properties are in the modifier table, but the receiver is a
        // Grid: Background applies to a Panel, BorderThickness does not. Only Background is
        // reported. The fix must not "helpfully" convert the BorderThickness write as well —
        // that is precisely the silent no-op the gating exists to prevent, and it is why the
        // analyzer passes the reported property names through Diagnostic.Properties rather
        // than letting the fix re-derive candidates from the table.
        var source = Stubs + @"
class C
{
    GridElement M(GridElement g, Microsoft.UI.Xaml.Media.Brush brush)
        => {|REACTOR_POOL_001:g.Set(x => { x.Background = brush; x.BorderThickness = new Thickness(4); })|};
}";
        await new CSharpCodeFixTest<PoolResetSetAnalyzer, PoolResetSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
