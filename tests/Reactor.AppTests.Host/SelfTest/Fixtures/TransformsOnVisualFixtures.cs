using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 066 layer 4: <c>.Scale</c>, <c>.Rotation</c>, <c>.Translation</c> and <c>.CenterPoint</c>
/// always write the composition visual (#1630). The handoff is lazy, an identity value never takes
/// it (§3 invariant 2), and translation is enabled only for a declared Translation channel, so a
/// facade <c>Translation</c> drag survives a handoff (§4.3, #1642).
/// </summary>
/// <remarks>
/// No oracle here reads the visual before the assertion that it was taken, because reading it takes it.
/// </remarks>
internal static class TransformsOnVisualFixtures
{
    private const double Left = 50, Top = 50;

    private static BorderElement Target() => Border(TextBlock(" "))
        .Width(100).Height(40)
        .Background(new SolidColorBrush(Microsoft.UI.Colors.Red))
        .HAlign(HorizontalAlignment.Left)
        .Margin(Left, Top, 0, 0);

    private static bool HasTranslation(Visual visual)
        => visual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded;

    private static Vector3 VisualTranslation(Visual visual)
        => visual.Properties.TryGetVector3("Translation", out var t) == CompositionGetValueStatus.Succeeded ? t : new Vector3(float.NaN);

    private static bool Near(double a, double b) => Math.Abs(a - b) <= 2;

    private static readonly (string Name, Func<BorderElement, BorderElement> Identity, Func<BorderElement, BorderElement> Value)[] Transforms =
    [
        ("Scale", static e => e.Scale(1), static e => e.Scale(2)),
        ("Rotation", static e => e.Rotation(0), static e => e.Rotation(30)),
        ("Translation", static e => e.Translation(0, 0, 0), static e => e.Translation(10, 0, 0)),
        ("CenterPoint", static e => e.CenterPoint(Vector3.Zero), static e => e.CenterPoint(new Vector3(10, 5, 0))),
    ];

    /// <summary>
    /// An identity transform on a fresh control, mounted and updated, never takes the visual, so a
    /// later facade setter still works. The positive control is the same modifier with a value.
    /// </summary>
    internal sealed class LazyHandoff(Harness h) : CompositorTransitionFixtures.TransitionFixture(h, "TransformsOnVisual_LazyHandoff")
    {
        protected override async Task Run()
        {
            foreach (var (name, identity, value) in Transforms)
            {
                string p = Name + "_" + name;
                await Mount(identity(Target()));
                await Update(identity(Target().Opacity(0.9)));
                H.Check(p + "_IdentityKeepsFacades", !ElementPool.IsVisualTaken(Control));
                // The setter writes a facade the modifier doesn't declare.
                Func<BorderElement, BorderElement> setter = name == "Rotation"
                    ? static e => e.Set(static b => b.Scale = new Vector3(1.5f, 1.5f, 1))
                    : static e => e.Set(static b => b.Rotation = 10);
                Exception? thrown = null;
                try { await Update(identity(setter(Target()))); }
                catch (Exception ex) { thrown = ex; }
                H.Check(p + "_LaterSetterWorks", thrown is null
                    && (name == "Rotation" ? Control.Scale == new Vector3(1.5f, 1.5f, 1) : Control.Rotation == 10),
                    thrown?.GetType().Name ?? $"scale={Control.Scale} rotation={Control.Rotation}");

                await Mount(value(Target()));
                H.Check(p + "_PositiveControl_ValueTakesVisual", ElementPool.IsVisualTaken(Control));
            }
        }
    }

    /// <summary>
    /// The four modifiers write the visual, and the facades read identity (§9 row 2). The pose
    /// is the same as the facade path drew: scale and translation about a (0,0) pivot.
    /// </summary>
    internal sealed class Channel(Harness h) : CompositorTransitionFixtures.TransitionFixture(h, "TransformsOnVisual_Channel")
    {
        protected override async Task Run()
        {
            await Mount(Target().Scale(2).Rotation(30).Translation(10, 5, 0).CenterPoint(new Vector3(4, 2, 0)));
            H.Check(Name + "_VisualTaken", ElementPool.IsVisualTaken(Control));
            H.Check(Name + "_FacadesIdentity", Control.Scale == Vector3.One && Control.Rotation == 0
                && Control.CenterPoint == Vector3.Zero && Control.Translation == Vector3.Zero,
                $"scale={Control.Scale} rotation={Control.Rotation} center={Control.CenterPoint} translation={Control.Translation}");
            var visual = Visual;
            H.Check(Name + "_VisualHoldsValues", visual.Scale == new Vector3(2, 2, 1)
                && Math.Abs(visual.RotationAngleInDegrees - 30) < 0.01f && visual.CenterPoint == new Vector3(4, 2, 0)
                && VisualTranslation(visual) == new Vector3(10, 5, 0),
                $"scale={visual.Scale} rotation={visual.RotationAngleInDegrees} center={visual.CenterPoint} translation={VisualTranslation(visual)}");

            await Mount(Target().Scale(2).Translation(10, 5, 0));
            var box = await Box();
            H.Check(Name + "_Pose", box is { } b && Near(b.X0, Left + 10) && Near(b.Y0, Top + 5)
                && Near(b.X1 - b.X0, 200) && Near(b.Y1 - b.Y0, 80), $"box={box}");

            // Unset returns the visual to identity; the control keeps its visual.
            await Update(Target());
            bool taken = ElementPool.IsVisualTaken(Control);
            H.Check(Name + "_UnsetResetsVisual", taken && Visual.Scale == Vector3.One && VisualTranslation(Visual) == Vector3.Zero,
                $"taken={taken} scale={Visual.Scale} translation={VisualTranslation(Visual)}");
        }
    }

    /// <summary>
    /// The effective-identity rule (§4): an identity modifier over a setter that left the facade
    /// non-identity still takes the visual and wins, as the modifier always did. The positive
    /// control is the setter alone.
    /// </summary>
    internal sealed class IdentityPrecedence(Harness h) : CompositorTransitionFixtures.TransitionFixture(h, "TransformsOnVisual_IdentityPrecedence")
    {
        protected override async Task Run()
        {
            Func<BorderElement, BorderElement> setter = static e => e.Set(static b => b.Scale = new Vector3(2, 2, 1));
            await Mount(setter(Target()));
            var alone = await Box();
            H.Check(Name + "_PositiveControl_SetterScales", alone is { } a && Near(a.X1 - a.X0, 200), $"box={alone}");

            await Mount(setter(Target()).Scale(1));
            var box = await Box();
            H.Check(Name + "_ModifierWins", box is { } b && Near(b.X1 - b.X0, 100), $"box={box}");
            H.Check(Name + "_VisualTaken", ElementPool.IsVisualTaken(Control));
        }
    }

    /// <summary>
    /// A drag card writes <c>fe.Translation</c> from a handler and declares <c>.Scale(pressed ? 0.97f : 1)</c>.
    /// The press takes the visual, and the drag must keep working: Reactor neither moves the facade
    /// nor enables translation, because no Translation channel is declared (§4.3 rule 1, #1642).
    /// A hover in and out through <c>.InteractionStates</c> without translation is the same.
    /// With <c>.Translation()</c> declared, a facade value written before the handoff still moves
    /// to the visual and the modifier owns it (rule 2).
    /// </summary>
    internal sealed class Drag(Harness h) : CompositorTransitionFixtures.TransitionFixture(h, "TransformsOnVisual_Drag")
    {
        protected override async Task Run()
        {
            await Mount(Target().Scale(1));
            Control.Translation = new Vector3(40, 0, 0);
            await TransformsMatrixRecorder.Settle();
            await Update(Target().Scale(0.97f));
            H.Check(Name + "_Press_VisualTaken", ElementPool.IsVisualTaken(Control));
            H.Check(Name + "_Press_FacadeKept", Control.Translation == new Vector3(40, 0, 0), $"translation={Control.Translation}");
            H.Check(Name + "_Press_TranslationNotEnabled", !HasTranslation(Visual), $"visual={VisualTranslation(Visual)}");
            Control.Translation = new Vector3(60, 0, 0);
            await TransformsMatrixRecorder.Settle();
            var box = await Box();
            H.Check(Name + "_Press_DragApplies", box is { } b && Near(b.X0, Left + 60) && Near(b.X1 - b.X0, 97), $"box={box}");

            await Mount(Target().InteractionStates(static b => b.PointerOver(scale: 1.1f), Curve.Linear(1)));
            Control.Translation = new Vector3(40, 0, 0);
            Reconciler.SimulatePointerOverForTests(Control, isOver: true);
            await TransformsMatrixRecorder.Settle();
            Reconciler.SimulatePointerOverForTests(Control, isOver: false);
            await TransformsMatrixRecorder.Settle(200);
            H.Check(Name + "_Hover_VisualTaken", ElementPool.IsVisualTaken(Control));
            H.Check(Name + "_Hover_FacadeKept", Control.Translation == new Vector3(40, 0, 0), $"translation={Control.Translation}");
            H.Check(Name + "_Hover_TranslationNotEnabled", !HasTranslation(Visual), $"visual={VisualTranslation(Visual)}");

            await Mount(Target());
            Control.Translation = new Vector3(7, 0, 0);
            await TransformsMatrixRecorder.Settle();
            await Update(Target().Translation(10, 0, 0));
            H.Check(Name + "_Declared_FacadeMigrated", Control.Translation == Vector3.Zero
                && VisualTranslation(Visual) == new Vector3(10, 0, 0), $"facade={Control.Translation} visual={VisualTranslation(Visual)}");
            var declared = await Box();
            H.Check(Name + "_Declared_ModifierOwns", declared is { } d && Near(d.X0, Left + 10), $"box={declared}");
        }
    }

    /// <summary>
    /// §9 row 1 for a transform-only element: a <c>.Set</c> or <c>.OnMount</c> facade write after the
    /// handoff throws, with the warning naming the modifier. The negative control is the same
    /// write without a transform, which works and stays silent.
    /// </summary>
    internal sealed class Diagnostic(Harness h) : CompositorTransitionFixtures.TransitionFixture(h, "TransformsOnVisual_Diagnostic")
    {
        private void Warns(string name, bool condition, string detail)
        {
            if (global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) H.Check(name, condition, detail);
            else H.Skip(name, "EventListener callbacks don't flow under NativeAOT");
        }

        protected override async Task Run()
        {
            using var listener = new DiagnosticsListener();

            static BorderElement WithSetter(float rotation, bool scale)
            {
                var e = Target().Set(b => b.Rotation = rotation);
                return scale ? e.Scale(2) : e;
            }

            await Mount(WithSetter(10, scale: true));
            H.Check(Name + "_Setter_MountMigrates", ElementPool.IsVisualTaken(Control) && Math.Abs(Visual.RotationAngleInDegrees - 10) < 0.01f);
            listener.Reset();
            Exception? setter = null;
            try { await Update(WithSetter(20, scale: true)); }
            catch (Exception ex) { setter = ex; }
            H.Check(Name + "_Setter_Throws", setter is UnauthorizedAccessException, setter?.GetType().Name ?? "none");
            Warns(Name + "_Setter_Warns", listener.Describe().Contains("W:Reactor/" + FacadeConflictDiagnostic.SetterOperation, StringComparison.Ordinal), listener.Describe());

            listener.Reset();
            Exception? onMount = null;
            try { await Mount(Target().Scale(2).OnMount(static fe => fe.Rotation = 10)); }
            catch (Exception ex) { onMount = ex; }
            H.Check(Name + "_OnMount_Throws", onMount is UnauthorizedAccessException, onMount?.GetType().Name ?? "none");
            Warns(Name + "_OnMount_Warns", listener.Describe().Contains("W:Reactor/" + FacadeConflictDiagnostic.SetterOperation, StringComparison.Ordinal), listener.Describe());

            await Mount(WithSetter(10, scale: false));
            listener.Reset();
            await Update(WithSetter(20, scale: false));
            await Mount(Target().OnMount(static fe => fe.Scale = new Vector3(2, 2, 1)));
            H.Check(Name + "_NegativeControl_PlainWritesSilent", Control.Scale == new Vector3(2, 2, 1) && listener.Describe() == "-", listener.Describe());
        }
    }
}
