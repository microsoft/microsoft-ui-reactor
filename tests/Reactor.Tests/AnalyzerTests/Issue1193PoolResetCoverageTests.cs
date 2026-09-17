using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Behavioural regressions for issue #1193 — the properties <c>ElementPool.CleanElement</c>
/// resets in its type dispatch, which no consistency invariant could see until
/// <see cref="CleanElementScan"/> was widened to the whole method.
/// </summary>
/// <remarks>
/// <para>
/// The scan/table tests elsewhere assert that these properties are <em>classified</em>. These
/// assert what a user actually gets at the call site, which is the only thing the issue is
/// about — a classification nothing reports is indistinguishable from no classification.
/// </para>
/// <para>
/// <b>Why MOD_002 and not POOL_001.</b> The report originally suspected a lost value on pool
/// reuse. It is not: <c>CleanElement</c> runs on pool <em>return</em>, and the next mount
/// re-applies setters — <c>DescriptorHandler.Mount</c> rents the control and ends in
/// <c>ApplySetters</c>, and <c>Element.SettersEqual</c> keeps any element carrying setters on the
/// Update path, which ends in <c>ApplySetters</c> too. Where a <c>.Set</c> write really is
/// discarded is a modifier set → unset transition, because <c>ApplyModifiers</c> runs after
/// <c>ApplySetters</c>; the <c>Issue950TextBlockPaddingFixture.ModifierResetOutranksASetterWrite</c>
/// selftest pins that and names MOD_002 as its diagnostic. So the fix here is that these
/// properties are <em>reported at all</em> — several produced no diagnostic whatsoever — not that
/// they were reported at the wrong severity.
/// </para>
/// </remarks>
public class Issue1193PoolResetCoverageTests
{
    /// <summary>
    /// Receivers spanning every case the rows added or corrected for #1193 distinguish:
    /// a pooled non-Control (<c>TextBlock</c>), a pooled <c>Control</c> (<c>Button</c>), the
    /// receiver whose arm resets nothing relevant (<c>RichTextBlock</c>), and <c>Viewbox</c>.
    /// </summary>
    private const string Stubs = @"
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor;

namespace Microsoft.UI.Xaml
{
    public class DependencyObject { }
    public class UIElement : DependencyObject { public bool IsHitTestVisible; public bool IsTabStop; }
    public class FrameworkElement : UIElement { }
}

namespace Microsoft.UI.Xaml.Media
{
    public class FontFamily { public FontFamily(string name) {} }
}

namespace Microsoft.UI.Xaml.Controls
{
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

public class Control : FrameworkElement { }

// TextBlock is NOT a Control in WinUI. It also declares Stretch/StretchDirection/IsActive here
// purely so the negative-control arms COMPILE — the point of those arms is that the analyzer
// stays silent because no `.Stretch(...)` / `.IsActive(...)` modifier exists for
// TextBlockElement, not because the write failed to type-check.
public class TextBlock : FrameworkElement
{
    public double FontSize;
    public FontFamily FontFamily;
    public int Stretch;
    public int StretchDirection;
    public bool IsActive;
}

public class RichTextBlock : FrameworkElement { public double FontSize; }
public class Button : Control { }
public class Viewbox : FrameworkElement { public int Stretch; public int StretchDirection; }
public class ProgressRing : Control { public bool IsActive; }
}

namespace Microsoft.UI.Reactor
{
using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

public class Element { }

public class TextBlockElement : Element
{
    public TextBlockElement Set(Action<TextBlock> configure) => this;
}

public class RichTextBlockElement : Element
{
    public RichTextBlockElement Set(Action<RichTextBlock> configure) => this;
}

public class ButtonElement : Element
{
    public ButtonElement Set(Action<Button> configure) => this;
}

public class ViewboxElement : Element
{
    public ViewboxElement Set(Action<Viewbox> configure) => this;
}

public class ProgressRingElement : Element
{
    public ProgressRingElement Set(Action<ProgressRing> configure) => this;
}

public static class Mods
{
    public static T FontSize<T>(this T el, double v) where T : Element => el;
    public static T IsHitTestVisible<T>(this T el, bool v = true) where T : Element => el;
    public static T IsTabStop<T>(this T el, bool v = true) where T : Element => el;
    public static TextBlockElement FontFamily(this TextBlockElement el, FontFamily v) => el;
    public static RichTextBlockElement FontSize(this RichTextBlockElement el, double v) => el;
    public static ViewboxElement Stretch(this ViewboxElement el, int v) => el;
    public static ViewboxElement StretchDirection(this ViewboxElement el, int v) => el;
    public static ProgressRingElement IsActive(this ProgressRingElement el, bool v = true) => el;
}
}
";

    private static Task VerifyAsync(string body) =>
        new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + "\nclass C\n{\n    void M()\n    {\n" + body + "\n    }\n}",
        }.RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The issue's own repro, verbatim: one <c>.Set</c> body writing both font properties.
    /// Both are reset by <c>CleanElement</c>'s <c>case TextBlock tb:</c> arm, and the issue's
    /// complaint was that compiling it produced no diagnostic naming either.
    /// </summary>
    /// <remarks>
    /// Expectations are declared per property rather than through a single inline marker,
    /// because the analyzer reports the body <em>once per write</em> — two diagnostics over the
    /// same span. A single marker would have passed on either one alone, which is exactly the
    /// half-fix this test needs to be able to reject.
    /// </remarks>
    [Fact]
    public async Task Reports_The_Issues_TextBlock_Font_Repro()
    {
        var test = new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + @"
class C
{
    void M()
    {
        var el = new Microsoft.UI.Reactor.TextBlockElement();
        {|#0:el.Set(tb =>
        {
            tb.FontSize = 16;
            tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(""Segoe Fluent Icons"");
        })|};
    }
}",
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(PoolResetSetAnalyzer.ModifierAvailableDiagnosticId, DiagnosticSeverity.Info)
                .WithLocation(0)
                .WithArguments("FontFamily", ".FontFamily(...)"));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(PoolResetSetAnalyzer.ModifierAvailableDiagnosticId, DiagnosticSeverity.Info)
                .WithLocation(0)
                .WithArguments("FontSize", ".FontSize(...)"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The issue's explicit "must not over-correct" case: <c>CleanElement</c>'s
    /// <c>case RichTextBlock rtb:</c> arm clears only <c>Blocks</c>, so a font write here must not
    /// acquire a pool-return claim it never had.
    /// </summary>
    /// <remarks>
    /// Paired with the TextBlock test above rather than asserted alone: the two differ only in the
    /// receiver, so together they show the analyzer discriminating rather than reporting the same
    /// id for everything. This is the row that would break first if someone "fixed" #1193 by
    /// marking the font properties pool-reset without a receiver gate.
    /// </remarks>
    [Fact]
    public async Task Does_Not_Escalate_The_Same_Font_Write_On_A_RichTextBlock()
    {
        await VerifyAsync(@"
        var el = new Microsoft.UI.Reactor.RichTextBlockElement();
        {|REACTOR_MOD_002:el.Set(rtb => rtb.FontSize = 12)|};");
    }

    /// <summary>
    /// The verified defect. <c>IsHitTestVisible</c> produced <b>no</b> diagnostic at all, because
    /// it sat in <c>ModifierTable.DeliberatelyExcluded</c> claiming no modifier existed while
    /// <c>.IsHitTestVisible(bool)</c> was in <c>ElementExtensions.cs</c> the whole time.
    /// </summary>
    [Fact]
    public async Task Reports_IsHitTestVisible_Which_Was_Excluded_On_A_False_Claim()
    {
        await VerifyAsync(@"
        var el = new Microsoft.UI.Reactor.ButtonElement();
        {|REACTOR_MOD_002:el.Set(b => b.IsHitTestVisible = false)|};");
    }

    /// <summary>
    /// <c>Viewbox</c>'s two properties, neither of which was reported before: <c>Stretch</c> was
    /// excluded as a "Viewbox-only modifier" — a description of what <c>elementTypes</c> is for,
    /// not a reason — and <c>StretchDirection</c> was in no table at all.
    /// </summary>
    /// <remarks>
    /// Paired with a <c>TextBlockElement</c> arm that must stay silent, so the
    /// <c>elementTypes: ViewboxElementOnly</c> gate is proved load-bearing. Without that arm the
    /// positive one passes just as well on an ungated row, which would report on receivers with
    /// no <c>.Stretch</c> modifier and emit a fix that does not compile.
    /// </remarks>
    [Theory]
    [InlineData("Stretch")]
    [InlineData("StretchDirection")]
    public async Task Reports_The_Viewbox_Properties_Only_Where_The_Modifier_Exists(string property)
    {
        await VerifyAsync($@"
        var el = new Microsoft.UI.Reactor.ViewboxElement();
        {{|REACTOR_MOD_002:el.Set(vb => vb.{property} = 1)|}};

        var text = new Microsoft.UI.Reactor.TextBlockElement();
        text.Set(tb => tb.{property} = 1);");
    }

    /// <summary>
    /// The false positive the #1193 derivation exposed — repaired in the pool rather than in the
    /// analyzer, so both receivers report `POOL_001`.
    /// </summary>
    /// <remarks>
    /// <c>CleanElement</c> used to clear <c>IsTabStop</c> only under
    /// <c>if (fe is Control tabStopControl)</c>, but WinUI 3 declares the property on
    /// <c>UIElement</c> and <c>ApplyModifiers</c> writes it ungated — so <c>.IsTabStop(false)</c>
    /// reached a pooled <c>TextBlock</c> that the pool then never reset, and the diagnostic's
    /// promise was false there. Narrowing the gate to <c>Control</c> would have made the
    /// diagnostic honest while leaving the leak; clearing on <c>fe</c> closes the leak and keeps
    /// the unrestricted claim true. Both arms therefore assert <c>POOL_001</c>, and the
    /// <c>TextBlock</c> arm is the one that regresses if the clear is narrowed again.
    /// </remarks>
    [Fact]
    public async Task Reports_IsTabStop_On_Pooled_Control_And_Non_Control_Receivers()
    {
        await VerifyAsync(@"
        var text = new Microsoft.UI.Reactor.TextBlockElement();
        {|REACTOR_POOL_001:text.Set(tb => tb.IsTabStop = false)|};

        var button = new Microsoft.UI.Reactor.ButtonElement();
        {|REACTOR_POOL_001:button.Set(b => b.IsTabStop = false)|};");
    }

    /// <summary>
    /// <c>ProgressRing.IsActive</c>, the third row that had no table entry at all, and the
    /// negative control that makes the <c>elementTypes</c> gate load-bearing.
    /// </summary>
    /// <remarks>
    /// The positive arm alone would pass just as happily if the row lost its
    /// <c>elementTypes: ProgressRingElementOnly</c> — an ungated <c>ModifierInfo("IsActive")</c>
    /// still reports on a <c>ProgressRing</c>, while also reporting on every receiver that has no
    /// <c>.IsActive</c> modifier and so producing a fix that does not compile. The
    /// <c>TextBlockElement</c> arm is what fails in that case, and the same pairing is applied to
    /// the Viewbox rows above.
    /// </remarks>
    [Fact]
    public async Task Reports_IsActive_Only_Where_The_Modifier_Exists()
    {
        await VerifyAsync(@"
        var ring = new Microsoft.UI.Reactor.ProgressRingElement();
        {|REACTOR_MOD_002:ring.Set(pr => pr.IsActive = false)|};

        var text = new Microsoft.UI.Reactor.TextBlockElement();
        text.Set(tb => tb.IsActive = false);");
    }
}
