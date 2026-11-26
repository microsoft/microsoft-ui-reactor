using System.Numerics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Animation;

/// <summary>
/// Spec 066 §9 rows 1 and 4: names the modifier to use when WinUI rejects a facade write or the
/// handoff on an element Reactor owns. The call sites use it as an exception filter that returns
/// false, so the exception and the reconcile error it produces are unchanged.
/// </summary>
internal static class FacadeConflictDiagnostic
{
    internal const string SetterOperation = "SetterFacadeWrite";
    internal const string HandoffOperation = "CompositionVisualHandoff";

    private const string Modifiers =
        "Use the modifier instead: '.Rotation(...)', '.Scale(...)', '.CenterPoint(...)', '.OpacityTransition(...)', "
        + "'.ScaleTransition(...)', '.RotationTransition(...)' or '.TranslationTransition(...)'. RotationAxis, "
        + "TransformMatrix and UIElement.StartAnimation have no modifier: animate with '.Keyframes(...)' or '.Animate(...)'. "
        + "See REACTOR_ANIM_004.";

    /// <summary>
    /// A <c>.Set(...)</c> setter, or the <paramref name="source"/> user callback, threw
    /// <see cref="UnauthorizedAccessException"/> on a control whose composition visual Reactor
    /// took. Always returns false.
    /// </summary>
    internal static bool ReportRejectedSetter(object control, string source = "'.Set(...)' setter")
    {
        if (control is UIElement element && DiagnosticLog.IsWarningEnabled && ElementPool.IsVisualTaken(element))
        {
            DiagnosticLog.Warning(LogCategory.Reactor, SetterOperation,
                "A " + source + " on a " + element.GetType().Name + " wrote a UIElement facade transform "
                + "(Rotation, Scale, CenterPoint, RotationAxis, TransformMatrix), a XAML implicit transition or called "
                + "UIElement.StartAnimation after Reactor took the element's composition visual for a transition, "
                + "a transform modifier or a compositor feature, and WinUI rejected it. " + Modifiers);
        }
        return false;
    }

    /// <summary>
    /// <c>ElementCompositionPreview.GetElementVisual</c> threw <see cref="UnauthorizedAccessException"/>
    /// on an element Reactor owns, because user code left a facade or a XAML transition that blocks
    /// the handoff. Always returns false.
    /// </summary>
    internal static bool ReportBlockedHandoff(UIElement element)
    {
        if (!DiagnosticLog.IsWarningEnabled
            || element.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState)
            return false;
        DiagnosticLog.Warning(LogCategory.Reactor, HandoffOperation,
            "Reactor could not take the composition visual of a " + element.GetType().Name + " for a transition, "
            + "a transform or a compositor feature, because " + Blockers(element) + " set on it, most likely by a "
            + "'.Set(...)' setter. " + Modifiers);
        return false;
    }

    // Reads only properties that stay readable before the handoff.
    private static string Blockers(UIElement element)
    {
        var found = new List<string>(2);
        try
        {
            if (element.RotationAxis != Vector3.UnitZ) found.Add("RotationAxis");
            if (!element.TransformMatrix.IsIdentity) found.Add("TransformMatrix");
            if (element.OpacityTransition is not null) found.Add("OpacityTransition");
            if (element.ScaleTransition is not null) found.Add("ScaleTransition");
            if (element.RotationTransition is not null) found.Add("RotationTransition");
            if (element.TranslationTransition is not null) found.Add("TranslationTransition");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or global::System.Runtime.InteropServices.COMException)
        {
            // Reported below as unknown.
        }
        return found.Count switch
        {
            0 => "a UIElement facade transform or a XAML implicit transition is",
            1 => "UIElement." + found[0] + " is",
            _ => "UIElement." + string.Join(" and UIElement.", found) + " are",
        };
    }
}
