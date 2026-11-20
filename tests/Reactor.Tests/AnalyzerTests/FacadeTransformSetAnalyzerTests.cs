using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="FacadeTransformSetAnalyzer"/> (<c>REACTOR_ANIM_004</c>) and
/// <see cref="FacadeTransformSetCodeFix"/>, spec 066 §9.1.
/// </summary>
public class FacadeTransformSetAnalyzerTests
{
    // Stubs mirror the real shapes: the facades are declared on Microsoft.UI.Xaml.UIElement, the
    // .Set overload takes the concrete WinUI control, and every modifier is a generic extension
    // in Microsoft.UI.Reactor, like ElementExtensions.
    private const string Stubs = @"#nullable enable
using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Composition
{
    public class CompositionAnimation { }
}

namespace Microsoft.UI.Xaml
{
    public class ScalarTransition { public TimeSpan Duration { get; set; } }
    public class Vector3Transition { public TimeSpan Duration { get; set; } }
    public class Transform { }
    public class UIElement
    {
        public float Rotation { get; set; }
        public Vector3 Scale { get; set; }
        public Vector3 CenterPoint { get; set; }
        public Vector3 RotationAxis { get; set; }
        public Matrix4x4 TransformMatrix { get; set; }
        public Vector3 Translation { get; set; }
        public double Opacity { get; set; }
        public ScalarTransition? OpacityTransition { get; set; }
        public ScalarTransition? RotationTransition { get; set; }
        public Vector3Transition? ScaleTransition { get; set; }
        public Vector3Transition? TranslationTransition { get; set; }
        public void StartAnimation(Microsoft.UI.Composition.CompositionAnimation animation) { }
        public void StopAnimation(Microsoft.UI.Composition.CompositionAnimation animation) { }
    }
    public class FrameworkElement : UIElement { }
}

namespace Microsoft.UI.Xaml.Shapes
{
    public class Path : Microsoft.UI.Xaml.FrameworkElement { public Microsoft.UI.Xaml.Transform? RenderTransform { get; set; } }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Border : Microsoft.UI.Xaml.FrameworkElement { }
    public class ScrollViewer : Microsoft.UI.Xaml.FrameworkElement { }
    // A user control that hides the facade with its own property.
    public class Spinner : Border { public new float Rotation { get; set; } }
}

namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Xaml.Controls;

    public abstract class Element { }
    public class BorderElement : Element { public BorderElement Set(Action<Border> configure) => this; }
    public class PathElement : Element { public PathElement Set(Action<Microsoft.UI.Xaml.Shapes.Path> configure) => this; }
    public class SpinnerElement : Element { public SpinnerElement Set(Action<Spinner> configure) => this; }

    // A user type with its own transform-named properties.
    public class Gizmo { public float Rotation { get; set; } public Vector3 Scale { get; set; } }
    public class GizmoElement : Element { public GizmoElement Set(Action<Gizmo> configure) => this; }

    public enum Curve { Ease }

    public static class Factories
    {
        public static BorderElement Border() => new BorderElement();
        public static PathElement Path() => new PathElement();
        public static SpinnerElement Spinner() => new SpinnerElement();
        public static GizmoElement Gizmo() => new GizmoElement();
    }

    public static class ElementExtensions
    {
        public static T Animate<T>(this T el, Curve curve) where T : Element => el;
        public static T InteractionStates<T>(this T el) where T : Element => el;
        public static T Transition<T>(this T el) where T : Element => el;
        public static T Keyframes<T>(this T el, string name) where T : Element => el;
        public static T LayoutAnimation<T>(this T el) where T : Element => el;
        public static T SpringLayoutAnimation<T>(this T el) where T : Element => el;
        public static T ScrollLinked<T>(this T el, ScrollViewer viewer) where T : Element => el;
        public static T Translation<T>(this T el, float x, float y, float z) where T : Element => el;
        public static T Rotation<T>(this T el, float degrees) where T : Element => el;
        public static T Scale<T>(this T el, Vector3 scale) where T : Element => el;
        public static T Scale<T>(this T el, float uniform) where T : Element => el;
        public static T CenterPoint<T>(this T el, Vector3 center) where T : Element => el;
        public static T Opacity<T>(this T el, double opacity) where T : Element => el;
        public static T OpacityTransition<T>(this T el, TimeSpan? duration = null) where T : Element => el;
        public static T RotationTransition<T>(this T el, TimeSpan? duration = null) where T : Element => el;
        public static T ScaleTransition<T>(this T el, Vector3Transition? transition = null) where T : Element => el;
        public static T TranslationTransition<T>(this T el, Vector3Transition? transition = null) where T : Element => el;
    }
}
";

    private static Task VerifyAsync(string body, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<FacadeTransformSetAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            MarkupOptions = MarkupOptions.UseFirstDescriptor,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static Task VerifyFixAsync(string before, string after) =>
        new CSharpCodeFixTest<FacadeTransformSetAnalyzer, FacadeTransformSetCodeFix, DefaultVerifier>
        {
            TestCode = Stubs + before,
            FixedCode = Stubs + after,
            // The rule has two descriptors (the Translation arm has its own message) under one
            // id; markup only checks id, span and severity, which both share.
            MarkupOptions = MarkupOptions.UseFirstDescriptor,
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positives: each facade member, each trigger ─────────────────────────

    [Fact]
    public async Task Rotation_With_LayoutAnimation_Reports_And_Fixes_To_Rotation()
    {
        await VerifyFixAsync(
            @"class C { Element M() => Border().LayoutAnimation().Set(fe => {|REACTOR_ANIM_004:fe.Rotation = 45|}); }",
            @"class C { Element M() => Border().LayoutAnimation().Rotation(45); }");
    }

    [Fact]
    public async Task Message_Names_The_Write_The_Trigger_And_The_Modifier()
    {
        await VerifyAsync(
            @"class C { Element M() => Border().LayoutAnimation().Set(fe => {|#0:fe.Rotation = 45|}); }",
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("'.Set(...)' writes 'UIElement.Rotation', but '.LayoutAnimation(...)' on the same element takes its composition visual, after which WinUI rejects the write. Use '.Rotation(degrees)' instead."));
    }

    [Fact]
    public async Task Scale_In_A_Single_Statement_Block_Fixes_When_The_Trigger_Follows_The_Set()
    {
        await VerifyFixAsync(
            @"class C { Element M() => Border().Set(fe => { {|REACTOR_ANIM_004:fe.Scale = new Vector3(2, 2, 1)|}; }).Keyframes(""pulse""); }",
            @"class C { Element M() => Border().Scale(new Vector3(2, 2, 1)).Keyframes(""pulse""); }");
    }

    [Fact]
    public async Task CenterPoint_With_A_Parenthesized_Lambda_Fixes()
    {
        await VerifyFixAsync(
            @"class C { Element M() => Border().Transition().Set((fe) => {|REACTOR_ANIM_004:fe.CenterPoint = new(10, 5, 0)|}); }",
            @"class C { Element M() => Border().Transition().CenterPoint(new(10, 5, 0)); }");
    }

    [Fact]
    public async Task Transitions_Report_With_Removal_Advice_And_No_Fix()
    {
        // On main the .XxxTransition() modifiers set the same XAML property, which blocks the
        // handoff too (#1623), so the rule must not steer to them until spec 066 layer 2.
        var source = @"
class C
{
    Element A() => Border().InteractionStates().Set(fe => {|#0:fe.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(200) }|});
    Element B() => Border().SpringLayoutAnimation().Set(fe => {|#1:fe.RotationTransition = new ScalarTransition()|});
    Element C2() => Border().LayoutAnimation().Set(fe => {|#2:fe.ScaleTransition = new Vector3Transition()|});
    Element D(Microsoft.UI.Xaml.Controls.ScrollViewer sv) => Border().ScrollLinked(sv).Set(fe => {|#3:fe.TranslationTransition = new Vector3Transition()|});
}";
        await VerifyAsync(
            source,
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning).WithLocation(0)
                .WithMessage("'.Set(...)' writes 'UIElement.OpacityTransition', but '.InteractionStates(...)' on the same element takes its composition visual, after which WinUI rejects the write. Remove it, because '.OpacityTransition(...)' sets the same XAML transition and also blocks the handoff. Animate the change with '.Animate(...)' instead."),
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning).WithLocation(1),
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning).WithLocation(2),
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning).WithLocation(3));

        var unfixed = Markup(source, 4);
        await VerifyFixAsync(unfixed, unfixed);
    }

    [Fact]
    public async Task RotationAxis_TransformMatrix_And_StartAnimation_Report_Without_A_Fix()
    {
        // Every write in a block is reported, and none of these has a modifier.
        var source = @"
class C
{
    Element M(Microsoft.UI.Composition.CompositionAnimation a) => Border().Animate(Curve.Ease).Set(fe =>
    {
        {|#0:fe.RotationAxis = Vector3.UnitX|};
        {|#1:fe.TransformMatrix = Matrix4x4.Identity|};
        {|#2:fe.StartAnimation(a)|};
    });
}";
        await VerifyAsync(
            source,
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning).WithLocation(0)
                .WithMessage("'.Set(...)' writes 'UIElement.RotationAxis', but '.Animate(...)' on the same element takes its composition visual, after which WinUI rejects the write. No modifier covers it, so animate it with '.Keyframes(...)' or '.Animate(...)', or take the visual with ElementCompositionPreview.GetElementVisual in '.OnMount(...)'."),
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning).WithLocation(1),
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning).WithLocation(2)
                .WithMessage("'.Set(...)' calls 'UIElement.StartAnimation', but '.Animate(...)' on the same element takes its composition visual, after which WinUI rejects the call. Start the animation on the visual instead, taken with ElementCompositionPreview.GetElementVisual in '.OnMount(...)', or use '.Keyframes(...)' or '.Animate(...)'."));

        var unfixed = Markup(source, 3);
        await VerifyFixAsync(unfixed, unfixed);
    }

    [Fact]
    public async Task Translation_With_Translation_Modifier_Reports_Its_Own_Message_And_No_Fix()
    {
        // #1642: no visual-owning modifier is needed, .Translation(...) alone conflicts with the write.
        var source = @"class C { Element M() => Border().Translation(0, 0, 4).Set(fe => {|#0:fe.Translation = new Vector3(100, 0, 0)|}); }";
        await VerifyAsync(
            source,
            new DiagnosticResult(FacadeTransformSetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("'.Set(...)' writes 'UIElement.Translation', which conflicts with '.Translation(...)' on this element. The modifier overrides it on mount, and WinUI ignores it once translation is enabled on the composition visual. Remove the write or merge the value into the existing '.Translation(...)' call."));

        var unfixed = Markup(source, 1);
        await VerifyFixAsync(unfixed, unfixed);
    }

    // ── Diagnostic without a fix ────────────────────────────────────────────

    [Fact]
    public async Task Multi_Statement_Body_Reports_But_Does_Not_Fix()
    {
        var source = @"
class C
{
    Element M() => Border().LayoutAnimation().Set(fe =>
    {
        {|REACTOR_ANIM_004:fe.Rotation = 45|};
        fe.Opacity = 0.5;
    });
}";
        await VerifyFixAsync(source, source);
    }

    [Fact]
    public async Task Earlier_Same_Modifier_Blocks_The_Fix()
    {
        // .Rotation(10) runs after the setter and wins today; moving 45 into a later
        // .Rotation(45) would make it win instead.
        var source = @"class C { Element M() => Border().Rotation(10).LayoutAnimation().Set(fe => {|REACTOR_ANIM_004:fe.Rotation = 45|}); }";
        await VerifyFixAsync(source, source);
    }

    [Fact]
    public async Task Animate_On_The_Chain_Blocks_The_Fix()
    {
        // The .Set value is migrated to the visual without animating; .Rotation(45) would animate in.
        var source = @"class C { Element M() => Border().Animate(Curve.Ease).Set(fe => {|REACTOR_ANIM_004:fe.Rotation = 45|}); }";
        await VerifyFixAsync(source, source);
    }

    [Fact]
    public async Task Right_Hand_Side_Reading_The_Parameter_Does_Not_Fix()
    {
        // Case A would not compile once the lambda is gone. Case B would compile, but 'fe' would
        // then bind to the field and read 90 instead of the control's rotation.
        var source = @"
class C
{
    Microsoft.UI.Xaml.Controls.Border fe = new Microsoft.UI.Xaml.Controls.Border { Rotation = 90 };

    Element A() => Border().LayoutAnimation().Set(b => {|REACTOR_ANIM_004:b.CenterPoint = new Vector3(b.Rotation, 0, 0)|});
    Element B() => Border().LayoutAnimation().Set(fe => {|REACTOR_ANIM_004:fe.Rotation = fe.Rotation + 5|});
}";
        await VerifyFixAsync(source, source);
    }

    [Fact]
    public async Task Comment_Or_Directive_In_The_Lambda_Does_Not_Fix()
    {
        var source = @"
class C
{
    Element A() => Border().LayoutAnimation().Set(fe =>
    {
        // tilt the badge
        {|REACTOR_ANIM_004:fe.Rotation = 45|};
    });

    Element B() => Border().LayoutAnimation().Set(fe =>
#if DEBUG
        fe.Rotation = 45
#else
        {|REACTOR_ANIM_004:fe.Rotation = 0|}
#endif
    );
}";
        await VerifyFixAsync(source, source);
    }

    [Fact]
    public async Task Ambiguous_Target_Typed_Scale_Does_Not_Fix()
    {
        // .Scale has float and Vector3 overloads, so '.Scale(new(2, 2, 1))' would not bind.
        var source = @"class C { Element M() => Border().LayoutAnimation().Set(fe => {|REACTOR_ANIM_004:fe.Scale = new(2, 2, 1)|}); }";
        await VerifyFixAsync(source, source);
    }

    private static string Markup(string source, int count)
    {
        for (var i = 0; i < count; i++)
            source = source.Replace("{|#" + i + ":", "{|REACTOR_ANIM_004:");
        return source;
    }
    // ── Negatives (spec 066 §9.1) ───────────────────────────────────────────

    [Fact]
    public async Task Opacity_Is_Not_A_Facade_Transform()
    {
        await VerifyAsync(@"class C { Element M() => Border().Animate(Curve.Ease).Set(fe => fe.Opacity = 0.5); }");
    }

    [Fact]
    public async Task RenderTransform_Is_Not_Matched()
    {
        await VerifyAsync(@"class C { Element M() => Path().Animate(Curve.Ease).Set(p => p.RenderTransform = new Transform()); }");
    }

    [Fact]
    public async Task User_Type_With_Transform_Named_Properties_Is_Not_Matched()
    {
        await VerifyAsync(@"class C { Element M() => Gizmo().Animate(Curve.Ease).Set(g => { g.Rotation = 5; g.Scale = Vector3.One; }); }");
    }

    [Fact]
    public async Task Member_Hiding_The_Facade_Does_Not_Resolve_To_UIElement()
    {
        await VerifyAsync(@"class C { Element M() => Spinner().Animate(Curve.Ease).Set(s => s.Rotation = 5); }");
    }

    [Fact]
    public async Task Write_To_A_Captured_Control_Is_Not_Matched()
    {
        await VerifyAsync(@"class C { Element M(Microsoft.UI.Xaml.Controls.Border other) => Border().Animate(Curve.Ease).Set(fe => other.Rotation = 5); }");
    }

    [Fact]
    public async Task Chain_Without_A_Visual_Owning_Modifier_Is_Not_Matched()
    {
        // .Translation(...) is not visual-owning today, so it only gates the Translation arm.
        await VerifyAsync(@"class C { Element M() => Border().Translation(0, 0, 4).Set(fe => { fe.Rotation = 5; fe.ScaleTransition = new Vector3Transition(); }); }");
    }

    [Fact]
    public async Task Translation_Without_Translation_Modifier_Is_Not_Matched()
    {
        // Translation stays writable after the handoff, so a visual-owning modifier alone is fine.
        await VerifyAsync(@"class C { Element M() => Border().Animate(Curve.Ease).Set(fe => fe.Translation = new Vector3(1, 0, 0)); }");
    }

    [Fact]
    public async Task StopAnimation_Is_Exempt()
    {
        await VerifyAsync(@"class C { Element M(Microsoft.UI.Composition.CompositionAnimation a) => Border().Set(b => b.StopAnimation(a)).LayoutAnimation(); }");
    }

    [Fact]
    public async Task Same_Named_Non_Reactor_Modifier_Does_Not_Trigger()
    {
        await VerifyAsync(@"
namespace Other
{
    public static class Wiggles { public static BorderElement Animate(this BorderElement el, int speed) => el; }

    class C { Element M() => Border().Animate(3).Set(fe => fe.Rotation = 5); }
}");
    }

    [Fact]
    public async Task Non_Reactor_Set_Helper_Does_Not_Trigger()
    {
        await VerifyAsync(@"
namespace Other
{
    public class Thing : Element { public Thing Set(Action<Microsoft.UI.Xaml.Controls.Border> configure) => this; }

    class C { Element M() => new Thing().Animate(Curve.Ease).Set(fe => fe.Rotation = 5); }
}");
    }

    [Fact]
    public async Task Modifier_On_A_Split_Chain_Is_An_Accepted_False_Negative()
    {
        await VerifyAsync(@"
class C
{
    Element M()
    {
        var animated = Border().Animate(Curve.Ease);
        return animated.Set(fe => fe.Rotation = 5);
    }
}");
    }
}
