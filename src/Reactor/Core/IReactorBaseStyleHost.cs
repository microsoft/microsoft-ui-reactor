using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// A Reactor-owned native control whose default look lives in an explicit base
/// <see cref="Style"/> rather than a <c>generic.xaml</c> default style (Reactor ships no XAML
/// files), such as the spec 065 settings cards.
/// </summary>
/// <remarks>
/// <see cref="Reconciler.ApplyThemeBindings"/> writes a synthesized style to
/// <see cref="FrameworkElement.Style"/>. For a built-in control that only layers on top of the
/// control's default style, but for one of these controls it would discard the look itself.
/// So the synthesized style is built <c>BasedOn</c> <see cref="BaseStyle"/>, and
/// <see cref="Reconciler.ClearThemeBindings"/> restores <see cref="BaseStyle"/> instead of
/// clearing the style.
/// </remarks>
internal interface IReactorBaseStyleHost
{
    /// <summary>The style that carries the control's default look.</summary>
    Style BaseStyle { get; }

    /// <summary>A unique, stable name for <see cref="BaseStyle"/>, part of the themed-style cache key.</summary>
    string BaseStyleKey { get; }

    /// <summary>The XAML name of <see cref="BaseStyle"/>'s <c>TargetType</c>; the themed style
    /// must target the same type to be <c>BasedOn</c> it.</summary>
    string BaseStyleTargetType { get; }
}
