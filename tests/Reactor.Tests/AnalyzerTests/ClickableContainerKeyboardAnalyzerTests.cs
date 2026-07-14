using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="ClickableContainerKeyboardAnalyzer"/> (<c>REACTOR_A11Y_004</c>) and its
/// <see cref="ClickableContainerKeyboardCodeFix"/>. A non-focusable container carrying an
/// actionable <c>.OnTapped</c> but no <c>.IsTabStop(true)</c> is mouse/touch-hittable yet skipped
/// by Tab. Only <c>.IsTabStop(true)</c> (or <c>.IsTabStop()</c>) suppresses; <c>.TabIndex</c> and
/// <c>.OnKeyDown</c> do not put a non-Control container in the tab order, so they still warn.
///
/// The analyzer is purely syntactic, so the stubs only need to make the fluent chain compile —
/// generic <c>where T : Element</c> modifiers mirror the real DSL shape (a chain of
/// <c>dynamic</c> would reject the lambda arguments passed to <c>.OnTapped</c>).
/// </summary>
public class ClickableContainerKeyboardAnalyzerTests
{
    private const string Stubs = @"
namespace Stubs
{
    public abstract class Element { }
    public sealed class BorderElement : Element { }
    public sealed class GridElement : Element { }
    public sealed class StackElement : Element { }
    public sealed class CanvasElement : Element { }
    public sealed class RectangleElement : Element { }
    public sealed class EllipseElement : Element { }
    public sealed class ButtonElement : Element { }
    public sealed class TextBlockElement : Element { }

    public sealed class TappedArgs { public bool Handled { get; set; } }
    public sealed class KeyArgs { public bool Handled { get; set; } }

    public static class Factories
    {
        public static BorderElement Border(Element child) => new BorderElement();
        public static GridElement Grid(params Element[] children) => new GridElement();
        public static CanvasElement Canvas(params Element[] children) => new CanvasElement();
        public static StackElement HStack(params Element[] children) => new StackElement();
        public static StackElement VStack(params Element[] children) => new StackElement();
        public static RectangleElement Rectangle() => new RectangleElement();
        public static EllipseElement Ellipse() => new EllipseElement();
        public static ButtonElement Button(string text, System.Action onClick) => new ButtonElement();
        public static TextBlockElement TextBlock(string text) => new TextBlockElement();
    }

    public static class ElementExtensions
    {
        public static T OnTapped<T>(this T el, System.Action<object, TappedArgs> handler) where T : Element => el;
        public static T OnDoubleTapped<T>(this T el, System.Action<object, TappedArgs> handler) where T : Element => el;
        public static T OnKeyDown<T>(this T el, System.Action<object, KeyArgs> handler) where T : Element => el;
        public static T IsTabStop<T>(this T el, bool value = true) where T : Element => el;
        public static T TabIndex<T>(this T el, int index) where T : Element => el;
        public static T Padding<T>(this T el, double value) where T : Element => el;
        public static T Grid<T>(this T el, int row = 0, int column = 0) where T : Element => el;
    }
}
";

    private static string Wrap(string body) => Stubs + @"
namespace App
{
    using Stubs;
    using static Stubs.Factories;

    public static class C
    {
        public static void Open() { }
        public static readonly Stubs.TappedArgs Sink = new Stubs.TappedArgs();

        public static Element Build() =>
            " + body + @";
    }
}";

    private static Task VerifyAsync(string body) =>
        new CSharpAnalyzerTest<ClickableContainerKeyboardAnalyzer, DefaultVerifier>
        {
            TestCode = Wrap(body),
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positives ──────────────────────────────────────────────────────

    [Fact]
    public Task Border_With_OnTapped_And_No_Focus_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, __) => Open())");

    [Fact]
    public Task Grid_With_OnTapped_And_No_Focus_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Grid(TextBlock(""hi""))|}.OnTapped((_, __) => Open())");

    [Fact]
    public Task HStack_With_OnTapped_And_No_Focus_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:HStack(TextBlock(""hi""))|}.OnTapped((_, __) => Open())");

    [Fact]
    public Task VStack_With_OnTapped_And_No_Focus_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:VStack(TextBlock(""hi""))|}.OnTapped((_, __) => Open())");

    [Fact]
    public Task Canvas_With_OnTapped_And_No_Focus_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Canvas(TextBlock(""hi""))|}.OnTapped((_, __) => Open())");

    [Fact]
    public Task Rectangle_With_OnTapped_And_No_Focus_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Rectangle()|}.OnTapped((_, __) => Open())");

    [Fact]
    public Task Ellipse_With_OnTapped_And_No_Focus_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Ellipse()|}.OnTapped((_, __) => Open())");

    [Fact]
    public Task Border_With_OnTapped_Before_Other_Modifiers_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, __) => Open()).Padding(4)");

    [Fact]
    public Task Border_With_Actionable_Block_Tap_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, e) => { Open(); e.Handled = true; })");

    // A pure swallow tap followed by a real actionable tap still needs keyboard focus.
    [Fact]
    public Task Border_With_Swallow_Then_Actionable_Tap_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, e) => e.Handled = true).OnTapped((_, __) => Open())");

    // `.Handled = true` on something other than the tap event-args parameter is not a swallow.
    [Fact]
    public Task Border_With_Handled_On_Other_Receiver_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, e) => Sink.Handled = true)");

    // ── Negatives: an explicit keyboard-focus affordance suppresses ────

    [Fact]
    public Task Border_With_IsTabStop_No_Diagnostic() =>
        VerifyAsync(@"Border(TextBlock(""hi"")).OnTapped((_, __) => Open()).IsTabStop(true)");

    // `.IsTabStop()` with the argument omitted defaults to true, so it still suppresses.
    [Fact]
    public Task Border_With_IsTabStop_NoArg_No_Diagnostic() =>
        VerifyAsync(@"Border(TextBlock(""hi"")).OnTapped((_, __) => Open()).IsTabStop()");

    // `.IsTabStop(false)` explicitly removes the element from the tab order — it must NOT suppress.
    [Fact]
    public Task Border_With_IsTabStop_False_Still_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, __) => Open()).IsTabStop(false)");

    // Last-wins: a trailing `.IsTabStop(false)` overrides an earlier `.IsTabStop(true)`, so the
    // element is left out of the tab order and the rule still fires.
    [Fact]
    public Task Border_With_IsTabStop_True_Then_False_Still_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, __) => Open()).IsTabStop(true).IsTabStop(false)");

    // Reverse order: a trailing `.IsTabStop(true)` wins, so the element is reachable — no diagnostic.
    [Fact]
    public Task Border_With_IsTabStop_False_Then_True_No_Diagnostic() =>
        VerifyAsync(@"Border(TextBlock(""hi"")).OnTapped((_, __) => Open()).IsTabStop(false).IsTabStop(true)");

    // The idiomatic focusable-container shape in this codebase pairs the two; `.IsTabStop` suppresses.
    [Fact]
    public Task Border_With_IsTabStop_And_OnKeyDown_No_Diagnostic() =>
        VerifyAsync(@"Border(TextBlock(""hi"")).OnTapped((_, __) => Open()).IsTabStop(true).OnKeyDown((_, __) => Open())");

    // `.TabIndex(n)` is NOT a suppressor: the reconciler applies TabIndex only to Controls, so on a
    // non-Control container it is a no-op that never adds a tab stop — the container is still
    // unreachable, so the rule must still fire.
    [Fact]
    public Task Border_With_TabIndex_Only_Still_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, __) => Open()).TabIndex(0)");

    // `.OnKeyDown` alone wires key handling but does not add the container to the tab order, so a
    // keyboard user still can't focus it — the rule must still fire; the fix is `.IsTabStop(true)`.
    [Fact]
    public Task Border_With_OnKeyDown_Only_Still_Fires() =>
        VerifyAsync(@"{|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, __) => Open()).OnKeyDown((_, __) => Open())");

    // ── Negatives: a focusable control is already in the tab order ─────

    [Fact]
    public Task Button_With_OnTapped_No_Diagnostic() =>
        VerifyAsync(@"Button(""Save"", () => Open()).OnTapped((_, __) => Open())");

    // ── Negatives: a pure event-swallow tap is not an actionable command ─

    [Fact]
    public Task Border_With_Pure_Handled_Swallow_No_Diagnostic() =>
        VerifyAsync(@"Border(TextBlock(""hi"")).OnTapped((_, e) => e.Handled = true)");

    [Fact]
    public Task Border_With_Handled_Swallow_Block_No_Diagnostic() =>
        VerifyAsync(@"Border(TextBlock(""hi"")).OnTapped((_, e) => { e.Handled = true; })");

    // ── Near-misses that almost trip the syntactic fast path ───────────

    // Matches the container fast path (Border factory) but carries no tap handler.
    [Fact]
    public Task Border_Without_Tap_No_Diagnostic() =>
        VerifyAsync(@"Border(TextBlock(""hi"")).Padding(4)");

    // `.Grid(row:..)` is the attached-layout modifier — a member access, not the bare
    // Grid factory — so the IdentifierNameSyntax gate must not treat it as a container.
    [Fact]
    public Task Grid_Attached_Modifier_Is_Not_The_Factory_No_Diagnostic() =>
        VerifyAsync(@"TextBlock(""hi"").Grid(row: 1).OnTapped((_, __) => Open())");

    // The tap handler is applied to a local, not inline on the factory result; a
    // syntactic factory-anchored walk cannot see it (accepted false negative, safe direction).
    [Fact]
    public async Task Border_Stored_In_Local_Then_Tapped_No_Diagnostic()
    {
        var src = Stubs + @"
namespace App
{
    using Stubs;
    using static Stubs.Factories;

    public static class C
    {
        public static void Open() { }

        public static Element Build()
        {
            var card = Border(TextBlock(""hi""));
            return card.OnTapped((_, __) => Open());
        }
    }
}";
        await new CSharpAnalyzerTest<ClickableContainerKeyboardAnalyzer, DefaultVerifier>
        {
            TestCode = src,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code-fix round-trips ───────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Appends_IsTabStop()
    {
        var before = Stubs + @"
namespace App
{
    using Stubs;
    using static Stubs.Factories;

    public static class C
    {
        public static void Open() { }

        public static Element Build()
        {
            return {|REACTOR_A11Y_004:Border(TextBlock(""hi""))|}.OnTapped((_, __) => Open());
        }
    }
}";

        var after = Stubs + @"
namespace App
{
    using Stubs;
    using static Stubs.Factories;

    public static class C
    {
        public static void Open() { }

        public static Element Build()
        {
            return Border(TextBlock(""hi"")).OnTapped((_, __) => Open()).IsTabStop(true);
        }
    }
}";

        await new CSharpCodeFixTest<ClickableContainerKeyboardAnalyzer, ClickableContainerKeyboardCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Appends_After_Full_Chain()
    {
        var before = Stubs + @"
namespace App
{
    using Stubs;
    using static Stubs.Factories;

    public static class C
    {
        public static void Open() { }

        public static Element Build()
        {
            return {|REACTOR_A11Y_004:Grid(TextBlock(""hi""))|}.OnTapped((_, __) => Open()).OnDoubleTapped((_, __) => Open());
        }
    }
}";

        var after = Stubs + @"
namespace App
{
    using Stubs;
    using static Stubs.Factories;

    public static class C
    {
        public static void Open() { }

        public static Element Build()
        {
            return Grid(TextBlock(""hi"")).OnTapped((_, __) => Open()).OnDoubleTapped((_, __) => Open()).IsTabStop(true);
        }
    }
}";

        await new CSharpCodeFixTest<ClickableContainerKeyboardAnalyzer, ClickableContainerKeyboardCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
