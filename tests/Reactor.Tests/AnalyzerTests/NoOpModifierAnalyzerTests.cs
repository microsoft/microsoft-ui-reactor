using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

using AnalyzerVerifier = CSharpAnalyzerVerifier<NoOpModifierAnalyzer, DefaultVerifier>;

/// <summary>
/// Tests for <see cref="NoOpModifierAnalyzer"/> (<c>REACTOR_MOD_003</c>) and
/// <see cref="NoOpModifierCodeFix"/>: a generic common modifier applied to an element whose mounted
/// control is outside the set <c>Reconciler.ApplyModifiers</c> writes it to, so the value is
/// silently dropped.
/// <para>
/// The negatives are the point of the file. This rule fires on code that <b>compiles</b>, so a
/// false positive is a warning on correct code — worse than the bug. Each one pins a specific gate:
/// the control allow-list itself, the generic-vs-type-specific overload split, an unresolvable
/// receiver, a missing generator attribute, and the polymorphic XAML-interop host.
/// </para>
/// </summary>
public class NoOpModifierAnalyzerTests
{
    // A Reactor-shaped surface: the WinUI hierarchy the gate is expressed in, the wrapper/descriptor
    // attributes the analyzer reads the mounted control from, the generic modifiers on
    // `Microsoft.UI.Reactor.ElementExtensions`, and the shape modifiers the fix rewrites to.
    private const string Stubs = @"
namespace System.Runtime.CompilerServices { public static class IsExternalInit { } }

namespace Microsoft.UI.Xaml
{
    public class UIElement { }
    public class FrameworkElement : UIElement { }
    public struct Thickness { public Thickness(double u) { } }
    public struct CornerRadius { public CornerRadius(double u) { } }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush { }
    public class SolidColorBrush : Brush { }
}

namespace Microsoft.UI.Xaml.Controls
{
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Media;

    public class Control : FrameworkElement { }
    public class Border : FrameworkElement { }
    public class Panel : FrameworkElement { }
    public class Grid : Panel { }
    public class StackPanel : Panel { }
    public class RelativePanel : Panel { }
    public class Canvas : Panel { }
    public class Button : Control { }
    public class TextBlock : FrameworkElement { }
    public class RichTextBlock : FrameworkElement { }
    public class Image : FrameworkElement { }
}

namespace Microsoft.UI.Reactor.Layout
{
    // FlexPanel is a Panel but NOT a StackPanel, so ApplyModifiers drops Padding on it.
    public class FlexPanel : Microsoft.UI.Xaml.Controls.Panel { }
}

namespace Microsoft.UI.Xaml.Shapes
{
    using Microsoft.UI.Xaml;

    public class Shape : FrameworkElement { }
    public class Rectangle : Shape { }
    public class Ellipse : Shape { }
    public class Line : Shape { }
    public class Path : Shape { }
}

namespace Microsoft.UI.Reactor.Wrappers
{
    using System;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class GenerateReactorWrapperAttribute : Attribute
    {
        public GenerateReactorWrapperAttribute(Type controlType) { ControlType = controlType; }
        public Type ControlType { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class GenerateReactorDescriptorAttribute : Attribute
    {
        public GenerateReactorDescriptorAttribute(Type controlType) { ControlType = controlType; }
        public Type ControlType { get; }
    }
}

namespace Microsoft.UI.Reactor.Core
{
    using Microsoft.UI.Reactor.Wrappers;
    using WinShapes = Microsoft.UI.Xaml.Shapes;
    using WinUI = Microsoft.UI.Xaml.Controls;

    public abstract record Element { }

    public sealed record ThemeRef(string ResourceKey);

    [GenerateReactorWrapper(typeof(WinShapes.Rectangle))]
    public record RectangleElement : Element { }

    [GenerateReactorWrapper(typeof(WinShapes.Ellipse))]
    public record EllipseElement : Element { }

    [GenerateReactorDescriptor(typeof(WinShapes.Line))]
    public record LineElement : Element { }

    [GenerateReactorDescriptor(typeof(WinShapes.Path))]
    public record PathElement : Element { }

    // A shape whose only Fill comes from a look-alike extension class, not ElementExtensions.
    [GenerateReactorDescriptor(typeof(WinShapes.Ellipse))]
    public record TriangleElement : Element { }

    // A user-defined element derived from a wrapped one. It declares no Set of its own, so the
    // inherited signature says nothing authoritative about what it mounts.
    public record RoundedRectangleElement : RectangleElement { }

    // Derived AND declares its own Set, so the mounted control is known — but the only Fill it can
    // reach returns the base type, so a rename would narrow the chain.
    public record TintedRectangleElement : RectangleElement { }

    [GenerateReactorDescriptor(typeof(WinUI.Border))]
    public record BorderElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.StackPanel))]
    public record StackPanelElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.Grid))]
    public record GridElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.RelativePanel))]
    public record RelativePanelElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.Canvas))]
    public record CanvasElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.Button))]
    public record ButtonElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.TextBlock))]
    public record TextBlockElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.RichTextBlock))]
    public record RichTextBlockElement : Element { }

    [GenerateReactorDescriptor(typeof(WinUI.Image))]
    public record ImageElement : Element { }

    // Generic element: its Set is declared as Set<T>(this TemplatedListElement<T>, ...), so the
    // declared receiver only matches the constructed one on the original definitions.
    [GenerateReactorDescriptor(typeof(WinUI.Image))]
    public record TemplatedListElement<T> : Element { }

    [GenerateReactorDescriptor(typeof(global::Microsoft.UI.Reactor.Layout.FlexPanel))]
    public record FlexElement : Element { }

    // Two Set overloads naming different controls: the mounted control is ambiguous.
    public record AmbiguousElement : Element { }

    // XamlInterop's host: declared as the base, mounted as whatever the caller supplied.
    [GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
    public record XamlHostElement : Element { }

    // Hand-written handler with no Set overload: the mounted control is unknown.
    public record CardElement : Element { }

    // Its only Set comes from a look-alike extension class, not ElementExtensions.
    public record LookAlikeElement : Element { }

    public static class Factories
    {
        public static RectangleElement Rectangle() => new();
        public static EllipseElement Ellipse() => new();
        public static LineElement Line() => new();
        public static PathElement Path() => new();
        public static TriangleElement Triangle() => new();
        public static RoundedRectangleElement RoundedRectangle() => new();
        public static TintedRectangleElement TintedRectangle() => new();
        public static BorderElement Border() => new();
        public static StackPanelElement VStack() => new();
        public static GridElement Grid() => new();
        public static RelativePanelElement RelativePanel() => new();
        public static CanvasElement Canvas() => new();
        public static ButtonElement Button() => new();
        public static TextBlockElement Text(string s) => new();
        public static RichTextBlockElement RichTextBlock() => new();
        public static ImageElement Image(string s) => new();
        public static TemplatedListElement<T> TemplatedList<T>() => new();
        public static FlexElement Flex() => new();
        public static AmbiguousElement Ambiguous() => new();
        public static XamlHostElement XamlHost() => new();
        public static CardElement Card() => new();
        public static LookAlikeElement LookAlike() => new();
    }
}

namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Media;
    using WinShapes = Microsoft.UI.Xaml.Shapes;
    using WinUI = Microsoft.UI.Xaml.Controls;

    public static class BrushHelper
    {
        public static SolidColorBrush Parse(string color) => new();
    }

    public static class ElementExtensions
    {
        // Reactor's `Set` escape hatch. Its Action<TControl> argument is where the analyzer reads
        // the mounted control from — the generator attributes are not visible to consumers.
        public static RectangleElement Set(this RectangleElement el, Action<WinShapes.Rectangle> configure) => el;
        public static EllipseElement Set(this EllipseElement el, Action<WinShapes.Ellipse> configure) => el;
        public static LineElement Set(this LineElement el, Action<WinShapes.Line> configure) => el;
        public static PathElement Set(this PathElement el, Action<WinShapes.Path> configure) => el;
        public static TriangleElement Set(this TriangleElement el, Action<WinShapes.Ellipse> configure) => el;
        public static TintedRectangleElement Set(this TintedRectangleElement el, Action<WinShapes.Rectangle> configure) => el;
        public static TintedRectangleElement Tint(this TintedRectangleElement el, double amount) => el;
        public static BorderElement Set(this BorderElement el, Action<WinUI.Border> configure) => el;
        public static StackPanelElement Set(this StackPanelElement el, Action<WinUI.StackPanel> configure) => el;
        public static GridElement Set(this GridElement el, Action<WinUI.Grid> configure) => el;
        public static RelativePanelElement Set(this RelativePanelElement el, Action<WinUI.RelativePanel> configure) => el;
        public static CanvasElement Set(this CanvasElement el, Action<WinUI.Canvas> configure) => el;
        public static ButtonElement Set(this ButtonElement el, Action<WinUI.Button> configure) => el;
        public static TextBlockElement Set(this TextBlockElement el, Action<WinUI.TextBlock> configure) => el;
        public static RichTextBlockElement Set(this RichTextBlockElement el, Action<WinUI.RichTextBlock> configure) => el;
        public static ImageElement Set(this ImageElement el, Action<WinUI.Image> configure) => el;
        public static TemplatedListElement<T> Set<T>(this TemplatedListElement<T> el, Action<WinUI.Image> configure) => el;
        public static FlexElement Set(this FlexElement el, Action<global::Microsoft.UI.Reactor.Layout.FlexPanel> configure) => el;
        public static AmbiguousElement Set(this AmbiguousElement el, Action<WinUI.Grid> configure) => el;
        public static AmbiguousElement Set(this AmbiguousElement el, Action<WinUI.Border> configure) => el;
        public static XamlHostElement Set(this XamlHostElement el, Action<Microsoft.UI.Xaml.FrameworkElement> configure) => el;

        // Yoga box model: the element-specific equivalent of Padding on a FlexElement. Overload
        // shapes mirror Padding's exactly, which is what makes the rename fix sound.
        public static FlexElement FlexPadding(this FlexElement el, double uniform) => el;
        public static FlexElement FlexPadding(this FlexElement el, double horizontal, double vertical) => el;
        public static FlexElement FlexPadding(this FlexElement el, double left, double top, double right, double bottom) => el;

        // Generic common modifiers — the ones ApplyModifiers gates on a control type.
        public static T Background<T>(this T el, string color) where T : Element => el;
        public static T Background<T>(this T el, Brush brush) where T : Element => el;
        public static T Background<T>(this T el, ThemeRef theme) where T : Element => el;
        public static T Foreground<T>(this T el, Brush brush) where T : Element => el;
        public static T BorderBrush<T>(this T el, Brush brush) where T : Element => el;
        public static T BorderThickness<T>(this T el, double thickness) where T : Element => el;
        public static T CornerRadius<T>(this T el, double radius) where T : Element => el;
        public static T Padding<T>(this T el, double uniform) where T : Element => el;
        public static T Padding<T>(this T el, double horizontal, double vertical) where T : Element => el;
        public static T Padding<T>(this T el, double left = 0.0, double top = 0.0, double right = 0.0, double bottom = 0.0) where T : Element => el;
        public static T FontSize<T>(this T el, double size) where T : Element => el;

        // Generic, but ungated in ModifierTable (see GateOnlyInReconciler).
        public static T IsEnabled<T>(this T el, bool enabled = true) where T : Element => el;

        // Generic and not in ModifierTable at all.
        public static T Size<T>(this T el, double w, double h) where T : Element => el;

        // Type-specific overload: writes the record property directly, so it never goes through
        // ApplyModifiers' control gate.
        public static RichTextBlockElement FontSize(this RichTextBlockElement el, double size) => el;

        // Shape modifiers the did-you-mean fix rewrites to.
        public static RectangleElement Fill(this RectangleElement el, Brush brush) => el;
        public static EllipseElement Fill(this EllipseElement el, Brush brush) => el;
        public static PathElement Fill(this PathElement el, Brush brush) => el;
        public static LineElement Stroke(this LineElement el, Brush brush) => el;
        public static PathElement Stroke(this PathElement el, Brush brush) => el;
        public static LineElement StrokeThickness(this LineElement el, double thickness) => el;
        public static PathElement StrokeThickness(this PathElement el, double thickness) => el;
    }

    // A user-defined extension class inside Reactor's namespace root, declaring a Set overload
    // that looks exactly like the framework's.
    public static class LookAlikeExtensions
    {
        public static LookAlikeElement Set(this LookAlikeElement el, Action<WinShapes.Rectangle> configure) => el;

        // A Fill/Stroke that are not Reactor's. The replacement lookup must not treat them as
        // framework truth, or the fix would rewrite to someone else's method. (TriangleElement's
        // Set is on ElementExtensions, so the mounted control still resolves and the diagnostic
        // still fires — it just cannot name a replacement.)
        public static TriangleElement Fill(this TriangleElement el, Brush brush) => el;
        public static TriangleElement Stroke(this TriangleElement el, Brush brush) => el;
    }
}

namespace Other
{
    using Microsoft.UI.Reactor.Core;

    // A non-Reactor fluent `Background` on a Reactor element: same name, different declaring type.
    public static class ThirdPartyExtensions
    {
        public static T Background<T>(this T el, int argb) where T : Element => el;
    }
}
";

    // Both helpers verify compiler errors rather than suppressing them. That is deliberate and
    // it is the opposite of what the did-you-mean analyzer tests in this folder do
    // (FuzzyFactoryName, MissingFactoryArgument, StringForElementArgument, …), which set
    // CompilerDiagnostics.None because their whole job is diagnosing code that does NOT compile.
    // REACTOR_MOD_003 is the other kind: it only ever fires on code that compiles and type-checks
    // — that is the entire premise of the rule, a call that binds fine and is then silently
    // dropped at runtime. So a snippet here that fails to compile makes its test meaningless:
    // "no diagnostic reported" would mean "nothing bound", not "the analyzer stayed silent".
    // This was not hypothetical — a `Background(default)` case was passing that way (CS0121,
    // never bound) until it was caught. Leave these as Errors.
    private static CSharpAnalyzerTest<NoOpModifierAnalyzer, DefaultVerifier> MakeAnalyzerTest(string body)
    {
        var test = new CSharpAnalyzerTest<NoOpModifierAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            CompilerDiagnostics = CompilerDiagnostics.Errors,
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    private static CSharpCodeFixTest<NoOpModifierAnalyzer, NoOpModifierCodeFix, DefaultVerifier> MakeFixTest(
        string body, string fixedBody)
    {
        var test = new CSharpCodeFixTest<NoOpModifierAnalyzer, NoOpModifierCodeFix, DefaultVerifier>
        {
            TestCode = Stubs + body,
            FixedCode = Stubs + fixedBody,
            CompilerDiagnostics = CompilerDiagnostics.Errors,
        };
        test.DisabledDiagnostics.Add("CS1591");
        return test;
    }

    private static string App(string members) => @"
namespace TestApp
{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml.Media;
    using static Microsoft.UI.Reactor.Core.Factories;

    static class C
    {
" + members + @"
    }
}";

    // ── Positives ───────────────────────────────────────────────────

    [Fact]
    public async Task Fires_For_Background_On_A_Rectangle()
    {
        var body = App(@"
        internal static Element M() => Rectangle().{|REACTOR_MOD_003:Background|}(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_The_Gallery_Canvas_Chain()
    {
        // The exact shape from samples/ReactorGallery/ControlPages/Layout/CanvasPage.cs:25 — the
        // modifier is mid-chain, and `Size<T>` preserves the concrete RectangleElement receiver.
        var body = App(@"
        internal static Element M() => Rectangle().Size(80, 80).{|REACTOR_MOD_003:Background|}(""#FF6B6B"").Size(80, 80);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reports_Modifier_Element_Gate_And_Shape_Suggestion_As_Message_Arguments()
    {
        var body = App(@"
        internal static Element M() => Rectangle().{|#0:Background|}(""#FF6B6B"");");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "Background",
                    "RectangleElement",
                    "Panel, Control, or Border",
                    ". Rectangle is a Shape, which is painted with 'Fill' — did you mean '.Fill(...)'?"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Background_On_An_Ellipse_Brush_Overload()
    {
        var body = App(@"
        internal static Element M(Brush b) => Ellipse().{|REACTOR_MOD_003:Background|}(b);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Background_On_A_Line_And_Suggests_Stroke()
    {
        // LineElement has no Fill modifier, so the candidate list falls through to Stroke rather
        // than emitting a call that does not exist.
        var body = App(@"
        internal static Element M(Brush b) => Line().{|#0:Background|}(b);");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "Background",
                    "LineElement",
                    "Panel, Control, or Border",
                    ". Line is a Shape, which is painted with 'Stroke' — did you mean '.Stroke(...)'?"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_BorderThickness_On_A_Path_And_Suggests_StrokeThickness()
    {
        var body = App(@"
        internal static Element M() => Path().{|#0:BorderThickness|}(2);");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "BorderThickness",
                    "PathElement",
                    "Control or Border",
                    ". Path is a Shape, which is painted with 'StrokeThickness' — did you mean '.StrokeThickness(...)'?"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_A_Generic_Element_Whose_Set_Is_Declared_Generically()
    {
        // `Set<T>(this TemplatedListElement<T>, Action<Image>)` reduced against
        // TemplatedListElement<string> reports `TemplatedListElement<T>` as its declared receiver,
        // because ReducedFrom drops the type arguments inferred during reduction. Comparing the
        // original definitions is what keeps generic elements in scope.
        var body = App(@"
        internal static Element M() => TemplatedList<string>().{|REACTOR_MOD_003:CornerRadius|}(4);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_A_Derived_Element_With_Its_Own_Set_But_Offers_No_Narrowing_Fix()
    {
        // TintedRectangleElement declares its own Set, so the mounted control IS known. But the
        // only Fill it can reach is the base's, which returns RectangleElement — applying it would
        // narrow the expression and break `.Tint(...)` further down the chain, so the diagnostic
        // reports the replacement without offering a rewrite.
        var body = App(@"
        internal static Element M() => TintedRectangle().{|#0:Background|}(""#FF6B6B"").Tint(1);");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "Background",
                    "TintedRectangleElement",
                    "Panel, Control, or Border",
                    ". Rectangle is a Shape, which is painted with 'Fill'"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Background_On_A_TextBlock_With_The_Border_Hint()
    {
        // Not a shape: no rename can help, so the message points at the structural fix instead.
        var body = App(@"
        internal static Element M() => Text(""hi"").{|#0:Background|}(""#FF6B6B"");");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "Background",
                    "TextBlockElement",
                    "Panel, Control, or Border",
                    ". Wrap it in a Border(...) to paint a background behind this element"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_CornerRadius_On_An_Image()
    {
        var body = App(@"
        internal static Element M() => Image(""a.png"").{|REACTOR_MOD_003:CornerRadius|}(4);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Padding_On_Grid_Or_RelativePanel()
    {
        var body = App(@"
        internal static Element G() => Grid().Padding(16);
        internal static Element R() => RelativePanel().Padding(16);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Foreground_On_A_Border()
    {
        var body = App(@"
        internal static Element M(Brush b) => Border().{|REACTOR_MOD_003:Foreground|}(b);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Padding_On_A_Flex_And_Suggests_FlexPadding()
    {
        // FlexPanel is a Panel but not a StackPanel, so Padding is dropped — and the Yoga box model
        // already exposes the equivalent, so this gets an element-specific did-you-mean.
        var body = App(@"
        internal static Element M() => Flex().{|#0:Padding|}(16);");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "Padding",
                    "FlexElement",
                    "Control, Border, Grid, StackPanel, RelativePanel, or TextBlock",
                    ". 'FlexPadding' is the equivalent on FlexElement — did you mean '.FlexPadding(...)'?"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_A_Shape_Modifier_With_No_Replacement_And_Offers_No_Suggestion()
    {
        // CornerRadius has no shape counterpart in ShapeReplacements, so the diagnostic still
        // reports the silent drop but names no alternative.
        var body = App(@"
        internal static Element M() => Rectangle().{|#0:CornerRadius|}(4);");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "CornerRadius",
                    "RectangleElement",
                    "Control, Border, Grid, StackPanel, or RelativePanel",
                    ""));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reports_A_Shape_Replacement_Without_Did_You_Mean_When_No_Fix_Is_Possible()
    {
        // There is no Fill(ThemeRef), so the message must still teach that shapes paint with Fill
        // while dropping the "did you mean" phrasing — there is nothing to click.
        var body = App(@"
        internal static Element M(ThemeRef t) => Rectangle().{|#0:Background|}(t);");

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "Background",
                    "RectangleElement",
                    "Panel, Control, or Border",
                    ". Rectangle is a Shape, which is painted with 'Fill'"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negatives (false-positive gating) ───────────────────────────

    [Fact]
    public async Task Does_Not_Fire_For_Background_On_A_Border()
    {
        var body = App(@"
        internal static Element M() => Border().Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Background_On_A_Panel()
    {
        var body = App(@"
        internal static Element V() => VStack().Background(""#FF6B6B"");
        internal static Element G() => Grid().Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Any_Gated_Modifier_On_A_Control()
    {
        var body = App(@"
        internal static Element M(Brush b) =>
            Button().Background(b).Foreground(b).BorderBrush(b).BorderThickness(1).CornerRadius(4).Padding(8).FontSize(14);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Border_Box_Modifiers_On_A_Border()
    {
        var body = App(@"
        internal static Element M(Brush b) => Border().BorderBrush(b).BorderThickness(1).CornerRadius(4).Padding(8);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Padding_On_A_StackPanel()
    {
        var body = App(@"
        internal static Element M() => VStack().Padding(16);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_CornerRadius_On_Concrete_Border_Box_Panels()
    {
        var body = App(@"
        internal static Element G() => Grid().CornerRadius(4);
        internal static Element S() => VStack().CornerRadius(4);
        internal static Element R() => RelativePanel().CornerRadius(4);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Still_Fires_For_Padding_And_CornerRadius_On_Other_Panel_Subclasses()
    {
        var body = App(@"
        internal static Element P() => Canvas().{|REACTOR_MOD_003:Padding|}(8);
        internal static Element R() => Canvas().{|REACTOR_MOD_003:CornerRadius|}(4);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Foreground_Or_FontSize_On_A_TextBlock()
    {
        var body = App(@"
        internal static Element M(Brush b) => Text(""hi"").Foreground(b).FontSize(14);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_FontSize_On_A_RichTextBlock_Type_Specific_Overload()
    {
        // RichTextBlock is neither Control nor TextBlock, so the GENERIC FontSize<T> would be
        // dropped — but `.FontSize(14)` binds the type-specific RichTextBlockElement overload,
        // which writes the record directly. This also guards that overload's continued existence:
        // delete it and the call rebinds to the generic modifier and this test fails.
        var body = App(@"
        internal static Element M() => RichTextBlock().FontSize(14);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_An_Ungated_Modifier()
    {
        // IsEnabled is Control-gated in ApplyModifiers but carries a null ControlGate (see
        // ModifierTable.GateOnlyInReconciler); a null gate is never read as "reaches everything",
        // it means "not classified for this direction" and is skipped.
        var body = App(@"
        internal static Element M() => Rectangle().IsEnabled(false);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_A_Modifier_Outside_The_Table()
    {
        var body = App(@"
        internal static Element M() => Rectangle().Size(80, 80);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_A_Generic_Receiver()
    {
        var body = App(@"
        internal static T Style<T>(T el) where T : Element => el.Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_A_Receiver_Typed_As_Element()
    {
        var body = App(@"
        internal static Element M(Element el) => el.Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_When_Set_Comes_From_A_Look_Alike_Extension_Class()
    {
        // A user-defined Set helper under Microsoft.UI.Reactor.* is not the framework's
        // control-type evidence, and it is not covered by the Set/descriptor guard either — so it
        // must not be trusted.
        var body = App(@"
        internal static Element M() => LookAlike().Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Suggest_A_Replacement_That_Is_Not_Reactors()
    {
        // TriangleElement is a shape and its Set is Reactor's, so the drop is still reported — but
        // its only Fill/Stroke come from a look-alike extension class. Treating those as the
        // framework's would rewrite to someone else's method, or emit an ambiguous call. So the
        // diagnostic names no replacement and no fix is offered.
        var body = @"
namespace TestApp2
{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml.Media;
    using static Microsoft.UI.Reactor.Core.Factories;

    static class C3
    {
        internal static Element M(Brush b) => Triangle().{|#0:Background|}(b);
    }
}";

        var test = MakeAnalyzerTest(body);
        test.ExpectedDiagnostics.Add(
            AnalyzerVerifier.Diagnostic(NoOpModifierAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(
                    "Background",
                    "TriangleElement",
                    "Panel, Control, or Border",
                    ". Wrap it in a Border(...) to paint a background behind this element"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_An_Element_With_No_Set_Overload()
    {
        // CardElement is a hand-written composite: nothing declares its mounted control, so the
        // analysis has no ground truth and must stay silent.
        var body = App(@"
        internal static Element M() => Card().Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_A_Derived_Element_That_Only_Inherits_Set()
    {
        // RoundedRectangleElement declares no Set of its own. Inheriting the base's
        // Set(RectangleElement, Action<Rectangle>) is NOT evidence about what it mounts — nothing
        // stops a derived element being registered against a different control — so the analysis
        // has no ground truth and stays silent.
        var body = App(@"
        internal static Element M() => RoundedRectangle().Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_When_The_Set_Overloads_Name_Different_Controls()
    {
        // Two applicable Set overloads disagree about the mounted control, so it is unknown.
        var body = App(@"
        internal static Element M() => Ambiguous().Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Padding_On_A_RichTextBlock()
    {
        // RichTextBlock is none of Control, Border, StackPanel or TextBlock, so the control gate
        // says "dropped" — but RichTextBlockElement's descriptor reads the common Padding slot
        // itself and writes RichTextBlock.PaddingProperty, so the value DOES reach the control.
        // Reporting here would be a false positive on correct code.
        var body = App(@"
        internal static Element M() => RichTextBlock().Padding(8);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_Padding_On_A_TextBlock()
    {
        // A TextBlock is not a Control, but ApplyModifiers has its own arm for it (issue #950), so
        // .Padding IS applied. The sibling Fires_For_Background_On_A_TextBlock_With_The_Border_Hint
        // proves this same receiver is otherwise reportable, so a silently-broken analyzer cannot
        // make this pass.
        var body = App(@"
        internal static Element M() => Text(""hi"").Padding(8);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_The_Polymorphic_XamlInterop_Host()
    {
        // XamlHostElement declares FrameworkElement, but hosts whatever the caller supplied — which
        // may well be a Panel or Control at runtime.
        var body = App(@"
        internal static Element M() => XamlHost().Background(""#FF6B6B"");");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_A_Non_Reactor_Background_Extension()
    {
        var body = @"
namespace TestApp
{
    using Other;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    static class C2
    {
        internal static Element M() => Rectangle().Background(0x00FF6B6B);
    }
}";

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix ────────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Rewrites_The_Brush_Overload_As_A_Rename()
    {
        var body = App(@"
        internal static Element M(Brush b) => Rectangle().{|REACTOR_MOD_003:Background|}(b);");
        var fixedBody = App(@"
        internal static Element M(Brush b) => Rectangle().Fill(b);");

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Wraps_The_Color_String_In_BrushHelper_Parse()
    {
        // The shape modifiers take a Brush while the common modifier has a string overload, so a
        // bare rename would not compile. BrushHelper.Parse is exactly what Background(string) does
        // internally, which keeps the rewrite behaviour-preserving.
        var body = App(@"
        internal static Element M() => Rectangle().{|REACTOR_MOD_003:Background|}(""#FF6B6B"");");
        var fixedBody = App(@"
        internal static Element M() => Rectangle().Fill(BrushHelper.Parse(""#FF6B6B""));");

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Preserves_A_Comment_On_The_Argument()
    {
        // The colour string is wrapped in BrushHelper.Parse(...), and any comment attached to it
        // has to travel with it rather than being dropped by the rewrite. It stays attached to the
        // argument as a whole, which is where it was.
        //
        // NOTE: an inline /* */ comment here binds as trailing trivia of the `(` token, which the
        // rewrite never touches, so on its own this case does NOT exercise the argument's own
        // trivia. The sibling test below covers the placement that does; keep both.
        var body = App(@"
        internal static Element M() => Rectangle().{|REACTOR_MOD_003:Background|}(/* brand red */ ""#FF6B6B"");");
        var fixedBody = App(@"
        internal static Element M() => Rectangle().Fill(/* brand red */ BrushHelper.Parse(""#FF6B6B""));");

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Preserves_A_Line_Comment_Above_The_Argument()
    {
        // A line comment on its own line binds as *leading trivia of the argument's own first
        // token*, so this is the placement that a `WithoutTrivia()` on the argument would delete.
        // The inline sibling above cannot catch that: its comment lives on the `(`. Mutating the
        // fix to `argument.WithoutTrivia()` leaves that one green and fails this one.
        var body = App(@"
        internal static Element M() => Rectangle().{|REACTOR_MOD_003:Background|}(
            // brand red
            ""#FF6B6B"");");
        var fixedBody = App(@"
        internal static Element M() => Rectangle().Fill(
            // brand red
            BrushHelper.Parse(""#FF6B6B""));");

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_A_Line_To_Stroke()
    {
        var body = App(@"
        internal static Element M() => Line().{|REACTOR_MOD_003:Background|}(""#FF6B6B"");");
        var fixedBody = App(@"
        internal static Element M() => Line().Stroke(BrushHelper.Parse(""#FF6B6B""));");

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Renames_BorderThickness_To_StrokeThickness()
    {
        // A pure rename: BorderThickness<T>(double) and StrokeThickness(this PathElement, double)
        // have identical parameter lists, so the argument passes straight through.
        var body = App(@"
        internal static Element M() => Path().{|REACTOR_MOD_003:BorderThickness|}(2);");
        var fixedBody = App(@"
        internal static Element M() => Path().StrokeThickness(2);");

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Renames_Padding_To_FlexPadding_Across_Arities()
    {
        var body = App(@"
        internal static Element One() => Flex().{|REACTOR_MOD_003:Padding|}(16);
        internal static Element Two() => Flex().{|REACTOR_MOD_003:Padding|}(8, 4);
        internal static Element Four() => Flex().{|REACTOR_MOD_003:Padding|}(1, 2, 3, 4);");
        var fixedBody = App(@"
        internal static Element One() => Flex().FlexPadding(16);
        internal static Element Two() => Flex().FlexPadding(8, 4);
        internal static Element Four() => Flex().FlexPadding(1, 2, 3, 4);");

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Is_Not_Offered_For_Named_Arguments()
    {
        // `.Background(color: "#fff")` cannot be renamed to `.Fill(color: ...)` — the shape
        // modifier's parameter is `brush`. Likewise `.Padding(top: 8)` binds the four-parameter
        // optional overload, while FlexPadding's four-parameter overload has no defaults.
        var body = App(@"
        internal static Element Colour() => Rectangle().{|REACTOR_MOD_003:Background|}(color: ""#FF6B6B"");
        internal static Element Side() => Flex().{|REACTOR_MOD_003:Padding|}(top: 8);");

        await MakeFixTest(body, body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Does_Not_Fire_For_A_Constant_Null_Argument()
    {
        // `.Background(x)` merges through VisualModifiers.Merge — `other.Background ?? Background`
        // — so a null reads as "not supplied" and the call is inert on ANY receiver, not because
        // of the control gate. And the rewrite would not be equivalent: `el with { Fill = null }`
        // becomes Optional.Of(null), an explicit set-to-null that CLEARS the brush. Reporting here
        // would name the wrong cause and offer a behaviour-changing fix.
        //
        // Every line must be a *typed* constant null. A bare `.Background(null)` or
        // `.Background(default)` is ambiguous across the string/Brush/ThemeRef overloads (CS0121)
        // and never binds to the Reactor modifier at all — asserting "no diagnostic" on one of
        // those would pass for the wrong reason. MakeAnalyzerTest verifies compiler errors (see
        // the note on it), which is what stops this test going vacuous that way: if any line here
        // stops compiling, the test fails instead of silently proving nothing.
        var body = App(@"
        internal static Element Cast() => Rectangle().Background((Brush)null);
        internal static Element TypedBrush() => Rectangle().Background(default(Brush));
        internal static Element TypedString() => Rectangle().Background(default(string));");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Bare_Null_And_Default_Do_Not_Bind_The_Modifier_At_All()
    {
        // Pins the premise of the test above: these two spellings are overload-ambiguous, so they
        // never reach the analyzer. Recorded as a compiler expectation so that if the DSL ever
        // gains a disambiguating overload, this fails and the constant-null coverage above gets
        // revisited rather than quietly losing two cases. It also fails if MakeAnalyzerTest ever
        // stops verifying compiler errors, since an expected CS0121 would then go unmatched.
        var body = App(@"
        internal static Element Bare() => Rectangle().{|CS0121:Background|}(null);
        internal static Element Def() => Rectangle().{|CS0121:Background|}(default);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Still_Fires_For_A_Possibly_Null_Brush_Variable()
    {
        // Control for the guard above: only *syntactically* constant nulls are skipped. A variable
        // that merely might be null at runtime is undecidable, so the diagnostic stands — the guard
        // must not be over-broad.
        var body = App(@"
        internal static Element M(Brush b) => Rectangle().{|REACTOR_MOD_003:Background|}(b);");

        await MakeAnalyzerTest(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Qualifies_BrushHelper_When_The_Reactor_Namespace_Is_Not_Imported()
    {
        // Every other code-fix test has `using Microsoft.UI.Reactor;`, so BrushHelper resolves in
        // one segment and the multi-segment qualification path never runs. Here the extension
        // methods come in via `using static ElementExtensions`, which does NOT bring BrushHelper
        // into scope — so the emitted call has to stay qualified, and the rewritten document has
        // to round-trip. (A qualified callee built as MemberAccess(ParseTypeName(...), name)
        // produces correct text but a tree shape the parser never emits; this fix uses
        // ParseExpression over the whole dotted string, which is why it round-trips.)
        var body = @"
namespace TestApp4
{
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml.Media;
    using static Microsoft.UI.Reactor.ElementExtensions;
    using static Microsoft.UI.Reactor.Core.Factories;

    static class C5
    {
        internal static Element M() => Rectangle().{|REACTOR_MOD_003:Background|}(""#FF6B6B"");
    }
}";
        var fixedBody = @"
namespace TestApp4
{
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Xaml.Media;
    using static Microsoft.UI.Reactor.ElementExtensions;
    using static Microsoft.UI.Reactor.Core.Factories;

    static class C5
    {
        internal static Element M() => Rectangle().Fill(Microsoft.UI.Reactor.BrushHelper.Parse(""#FF6B6B""));
    }
}";

        await MakeFixTest(body, fixedBody).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Is_Not_Offered_For_A_Constant_Null_Argument()
    {
        var body = App(@"
        internal static Element M() => Rectangle().Background((Brush)null);");

        await MakeFixTest(body, body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Is_Not_Offered_For_The_ThemeRef_Overload()
    {
        // No Fill(ThemeRef) counterpart exists. The diagnostic still reports; FixedCode equal to
        // TestCode asserts no fix was registered (a registered fix would change the source).
        var body = App(@"
        internal static Element M(ThemeRef t) => Rectangle().{|REACTOR_MOD_003:Background|}(t);");

        await MakeFixTest(body, body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Is_Not_Offered_For_A_Non_Shape_Receiver()
    {
        var body = App(@"
        internal static Element M() => Text(""hi"").{|REACTOR_MOD_003:Background|}(""#FF6B6B"");");

        await MakeFixTest(body, body).RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Drift guard: the suggested shape modifiers must really exist ──

    [Theory]
    [InlineData(new string[0], "no control type")]
    [InlineData(new[] { "Control" }, "Control")]
    [InlineData(new[] { "Control", "Border" }, "Control or Border")]
    [InlineData(new[] { "Panel", "Control", "Border" }, "Panel, Control, or Border")]
    [InlineData(new[] { "Control", "Border", "StackPanel" }, "Control, Border, or StackPanel")]
    [InlineData(new[] { "Control", "Border", "StackPanel", "TextBlock" }, "Control, Border, StackPanel, or TextBlock")]
    public void Humanize_Renders_Every_Gate_Arity(string[] gate, string expected) =>
        Assert.Equal(expected, NoOpModifierAnalyzer.Humanize(gate));

    /// <summary>
    /// Every shape element in the live Reactor assembly must expose at least one of the
    /// <see cref="NoOpModifierAnalyzer.ShapeReplacements"/> candidates as a real
    /// <c>ElementExtensions</c> method, otherwise the analyzer would offer — and the code fix would
    /// emit — a call that does not compile.
    /// </summary>
    /// <remarks>
    /// Reflection only reads metadata; no WinUI object is constructed, so this is safe in the
    /// headless test host. Deleting <c>Fill(this RectangleElement, Brush)</c> fails this test.
    /// </remarks>
    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Test-only contract guard: enumerates the Reactor assembly's element types and the ElementExtensions surface by design. This host is never trimmed; behaviour-neutral.")]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "Test-only contract guard: reflects the public static methods of ElementExtensions, resolved by name from the Reactor assembly. Intentional and JIT-only; behaviour-neutral.")]
    public void Every_Shape_Element_Has_A_Resolvable_Shape_Replacement()
    {
        var elementExtensions = typeof(Element).Assembly.GetType("Microsoft.UI.Reactor.ElementExtensions");
        Assert.NotNull(elementExtensions);

        var modifiers = elementExtensions!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .ToLookup(m => m.Name, StringComparer.Ordinal);

        var candidates = NoOpModifierAnalyzer.ShapeReplacements.Values
            .SelectMany(names => names)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var shapeElements = 0;
        var missing = new global::System.Collections.Generic.List<string>();

        // Projected + filtered in the pipeline rather than with an in-loop `continue`
        // (CodeQL cs/linq/missed-where), which also avoids resolving the control twice.
        var shapes = typeof(Element).Assembly.GetTypes()
            .Where(t => typeof(Element).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Select(element => (Element: element, Control: MountedControl(element)))
            .Where(pair => pair.Control is not null
                           && typeof(global::Microsoft.UI.Xaml.Shapes.Shape).IsAssignableFrom(pair.Control))
            .OrderBy(pair => pair.Element.Name, StringComparer.Ordinal);

        foreach (var (element, control) in shapes)
        {
            shapeElements++;

            var hasReplacement = candidates.Any(name => modifiers[name].Any(m =>
            {
                var parameters = m.GetParameters();
                return parameters.Length >= 1 && parameters[0].ParameterType.IsAssignableFrom(element);
            }));

            if (!hasReplacement)
            {
                missing.Add(
                    $"{element.Name} mounts {control!.Name} (a Shape) but ElementExtensions declares none of " +
                    $"[{string.Join("|", candidates)}] for it — REACTOR_MOD_003 would suggest a modifier that " +
                    "does not exist, or silently stop suggesting one.");
            }
        }

        Assert.True(missing.Count == 0, string.Join("\n  ", missing));

        // Self-validation: Rectangle/Ellipse/Line/Path. If the attribute walk ever stops resolving,
        // the loop would no-op and the guard would pass vacuously.
        Assert.True(
            shapeElements >= 4,
            $"Expected at least 4 shape elements but found {shapeElements} — the shape-replacement guard " +
            "may be running vacuously.");
    }

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075",
        Justification = "Test-only contract guard: reads the generator attribute off a type enumerated by the surrounding Assembly.GetTypes scan. Behaviour-neutral.")]
    private static Type? MountedControl(Type element)
    {
        for (var current = element; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetCustomAttributesData())
            {
                var name = attribute.AttributeType.Name;
                if (name is not ("GenerateReactorWrapperAttribute" or "GenerateReactorDescriptorAttribute")
                    || attribute.ConstructorArguments.Count < 1)
                    continue;

                if (attribute.ConstructorArguments[0].Value is Type control)
                    return control;
            }
        }

        return null;
    }
}
