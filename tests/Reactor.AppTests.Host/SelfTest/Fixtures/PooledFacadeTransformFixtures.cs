using System.Numerics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1614: removing a poolable control that carries a non-identity facade transform
/// (<c>.Rotation</c>, <c>.Scale</c>, <c>.CenterPoint</c>, <c>.Translation</c>) must not throw,
/// and the control handed to the next renter must have identity transforms.
/// </summary>
/// <remarks>
/// WinUI treats <c>RenderTransform</c> and the facade properties as mutually exclusive, so
/// <c>ElementPool.CleanElement</c>'s <c>ClearValue(RenderTransformProperty)</c> threw
/// <c>UnauthorizedAccessException</c> while a facade was engaged, and the host fell back to its
/// error content. Each case mounts the transformed control, removes it (the pool return), then
/// mounts a plain control of the same type (the rent) and checks whether it is the recycled
/// instance.
/// </remarks>
internal static class PooledFacadeTransformFixtures
{
    internal sealed class Removal(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed record Expect(bool Facade = true, bool Recycled = true, bool LocalRenderTransform = false);

        public override async Task RunAsync()
        {
            var plain = new Expect();
            await RunCase<WinUI.Border>("BorderRotation", BorderOf, e => e.Rotation(45f), IsBorderWith, plain);
            await RunCase<WinUI.Border>("BorderScale", BorderOf, e => e.Scale(1.2f), IsBorderWith, plain);
            await RunCase<WinUI.Border>("BorderCenterPoint",
                BorderOf, e => e.CenterPoint(new Vector3(50, 20, 0)), IsBorderWith, plain);
            await RunCase<WinUI.Border>("BorderTranslation", BorderOf, e => e.Translation(12, 18, 0), IsBorderWith, plain);
            await RunCase<WinUI.TextBlock>("TextBlockAll",
                text => TextBlock(text),
                e => e.Rotation(30f).Scale(new Vector3(1.5f, 1.8f, 1)).Translation(4, 5, 0)
                    .CenterPoint(new Vector3(20, 10, 0)),
                static (tb, text) => tb.Text == text, plain);
            await RunCase<WinUI.StackPanel>("StackPanelRotation",
                text => VStack(TextBlock(text)), e => e.Rotation(90f),
                static (sp, text) => sp.Children.Count == 1 && sp.Children[0] is WinUI.TextBlock c && c.Text == text,
                plain);

            // An implicit transform transition locks RenderTransform for the control's lifetime,
            // with or without a facade value, so the pool must drop the control rather than
            // recycle it or touch RenderTransform on it.
            var slow = new Vector3Transition { Duration = TimeSpan.FromSeconds(10) };
            await RunCase<WinUI.Border>("BorderTransitioned", BorderOf,
                e => e.ScaleTransition(slow).RotationTransition(TimeSpan.FromSeconds(10))
                    .TranslationTransition(slow).Scale(1.2f).Rotation(45f).Translation(12, 18, 0),
                IsBorderWith, new Expect(Recycled: false));
            await RunCase<WinUI.Border>("BorderTransitionOnly", BorderOf, e => e.ScaleTransition(),
                IsBorderWith, new Expect(Facade: false, Recycled: false));
            await RunCase<WinUI.Border>("BorderRotationTransitionOnly", BorderOf, e => e.RotationTransition(),
                IsBorderWith, new Expect(Facade: false, Recycled: false));
            await RunCase<WinUI.Border>("BorderTranslationTransitionOnly", BorderOf, e => e.TranslationTransition(),
                IsBorderWith, new Expect(Facade: false, Recycled: false));

            // The lock outlives the transition, so a control whose transition was attached and
            // then removed passes the pool's transition check. Clearing its RenderTransform then
            // throws, and the pool must drop the control instead of failing the reconcile.
            await RunCase<WinUI.Border>("BorderTransitionRemoved", BorderOf,
                e => ((BorderElement)e).Scale(1.2f).Set(b =>
                {
                    b.ScaleTransition = new Vector3Transition();
                    b.ScaleTransition = null;
                }),
                IsBorderWith, new Expect(Recycled: false));

            // No modifier writes these two facades, so only .Set can leave them non-identity.
            await RunCase<WinUI.Border>("BorderTransformMatrix", BorderOf,
                e => ((BorderElement)e).Set(b =>
                {
                    b.TransformMatrix = Matrix4x4.CreateTranslation(5, 5, 0);
                    b.RotationAxis = Vector3.UnitX;
                }),
                IsBorderWith, plain);

            // A local RenderTransform (only reachable through .Set) is cleared on return.
            await RunCase<WinUI.Border>("BorderRenderTransform", BorderOf,
                e => ((BorderElement)e).Set(b => b.RenderTransform = new RotateTransform { Angle = 10 }),
                IsBorderWith, new Expect(Facade: false, LocalRenderTransform: true));
        }

        private static Element BorderOf(string text) => Border(TextBlock(text));

        private static bool IsBorderWith(WinUI.Border b, string text) => b.Child is WinUI.TextBlock c && c.Text == text;

        private async Task RunCase<TControl>(
            string name,
            Func<string, Element> build,
            Func<Element, Element> transform,
            Func<TControl, string, bool> match,
            Expect expect)
            where TControl : FrameworkElement
        {
            string prefix = "PooledFacadeTransform_" + name;
            string next = prefix + "_Next";
            string firstText = prefix + "_First";
            string secondText = prefix + "_Second";
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var button = Button(next, () => setPhase(phase + 1));
                return phase switch
                {
                    0 => VStack(button, transform(build(firstText))),
                    1 => VStack(button),
                    _ => VStack(button, build(secondText)),
                };
            });

            await Harness.Render();
            var first = H.FindControl<TControl>(c => match(c, firstText));
            H.Check(prefix + "_Mounted", first is not null);
            if (first is null) return;
            if (expect.Facade) H.Check(prefix + "_FacadeEngaged", !IsIdentity(first), Describe(first));
            if (expect.LocalRenderTransform)
                H.Check(prefix + "_RenderTransformSet", first.ReadLocalValue(UIElement.RenderTransformProperty) is RotateTransform);
            H.Check(prefix + "_NotTainted", !ElementPool.IsVisualTaken(first));

            // Removal is the pool return. Before the fix CleanElement threw here and the host
            // replaced its whole content with the error fallback, taking the button with it.
            H.ClickButton(next);
            await Harness.Render();
            var hostKept = H.FindButton(next) is not null;
            H.Check(prefix + "_RemovalKeptHost", hostKept);
            if (!hostKept) return;
            H.Check(prefix + "_Removed", H.FindControl<TControl>(c => match(c, firstText)) is null);

            H.ClickButton(next);
            await Harness.Render();
            var second = H.FindControl<TControl>(c => match(c, secondText));
            H.Check(prefix + "_Remounted", second is not null);
            if (second is null) return;
            if (!expect.Recycled)
            {
                H.Check(prefix + "_NotRecycled", !ReferenceEquals(first, second));
                return;
            }

            // The pool is LIFO per type, so a plain remount of the same type rents the control
            // the removal returned. Without this the identity checks below could pass on a
            // freshly constructed control.
            H.Check(prefix + "_Recycled", ReferenceEquals(first, second));
            H.Check(prefix + "_IdentityAfterRent", IsIdentity(second), Describe(second));
            H.Check(prefix + "_RenderTransformCleared",
                ReferenceEquals(second.ReadLocalValue(UIElement.RenderTransformProperty), DependencyProperty.UnsetValue));

            // Resetting the facades released the lock: RenderTransform is writable again.
            bool writable;
            try
            {
                second.RenderTransform = new RotateTransform { Angle = 10 };
                second.ClearValue(UIElement.RenderTransformProperty);
                writable = true;
            }
            catch (UnauthorizedAccessException) { writable = false; }
            H.Check(prefix + "_RenderTransformWritable", writable);
        }

        private static bool IsIdentity(UIElement e) =>
            e.Rotation == 0
            && e.Scale == Vector3.One
            && e.Translation == Vector3.Zero
            && e.CenterPoint == Vector3.Zero
            && e.RotationAxis == Vector3.UnitZ
            && e.TransformMatrix.IsIdentity;

        private static string Describe(UIElement e) =>
            $"rotation={e.Rotation} scale={e.Scale} translation={e.Translation} center={e.CenterPoint} " +
            $"axis={e.RotationAxis} matrixIdentity={e.TransformMatrix.IsIdentity}";
    }
}
