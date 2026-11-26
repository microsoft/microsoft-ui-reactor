using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

internal static class ForegroundModifier
{
    internal enum UpdateAction { None, Reapply, RemovalHandled, RemovalRestorePrepared, NewTarget }

    // A skipped render can declare a new equivalent brush. The native control retains the
    // instance the common modifier actually wrote, even if WinRT projects a new wrapper.
    private static DependencyProperty? s_appliedForegroundProperty;

    internal static DependencyProperty? GetProperty(FrameworkElement fe) => fe switch
    {
        WinUI.Control _ => WinUI.Control.ForegroundProperty,
        WinUI.TextBlock _ => WinUI.TextBlock.ForegroundProperty,
        WinUI.RichTextBlock _ => WinUI.RichTextBlock.ForegroundProperty,
        WinUI.IconElement _ => WinUI.IconElement.ForegroundProperty,
        _ => null,
    };

    internal static UpdateAction PrepareUpdate(IV1HandlerEntry entry, UIElement control, Element previous, Element current,
        Brush? oldModifier, Brush? modifier)
    {
        // The built-in descriptor writes the element's explicit Foreground. A custom handler
        // for the same element and control types can decide to leave that declaration unapplied.
        if (control is not WinUI.RichTextBlock richText || previous is not RichTextBlockElement oldRichText
            || current is not RichTextBlockElement newRichText
            || entry is not V1HandlerAdapter<RichTextBlockElement, WinUI.RichTextBlock> adapter
            || !adapter.IsBackedByDescriptor(RichTextBlockElement.Descriptor))
            return UpdateAction.None;

        if (modifier is not null)
        {
            // The generated property entry can overwrite an otherwise unchanged modifier.
            return ReferenceEquals(oldModifier, modifier) && !Equals(oldRichText.Foreground, newRichText.Foreground)
                ? UpdateAction.Reapply
                : UpdateAction.None;
        }

        // Without an explicit brush, common modifier removal clears the value after setters (#950).
        if (oldModifier is null || newRichText.Foreground is not { } explicitForeground)
            return UpdateAction.None;

        // Restore before native setters so they can take precedence. If an interaction state
        // is covering the applied brush, update what it will restore when it ends.
        if (s_appliedForegroundProperty is { } property)
        {
            var applied = richText.GetValue(property) as Brush;
            richText.ClearValue(property);
            if (applied is not null)
            {
                if (InteractionStateApplier.TryReplaceInteractionForegroundRestoreValue(richText, applied, explicitForeground))
                    return UpdateAction.RemovalRestorePrepared;
                if (ReferenceEquals(richText.ReadLocalValue(WinUI.RichTextBlock.ForegroundProperty), applied))
                    richText.Foreground = explicitForeground;
            }
        }
        return UpdateAction.RemovalHandled;
    }

    internal static void Apply(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, UpdateAction update,
        bool preserveInteractionForeground)
    {
        var oldForeground = update == UpdateAction.NewTarget ? null : oldM?.Foreground;
        var foreground = m.Foreground;
        if (update is UpdateAction.RemovalHandled or UpdateAction.RemovalRestorePrepared
            || (oldForeground is null && foreground is null)
            || GetProperty(fe) is not { } property)
            return;

        if (foreground is not null)
        {
            if (ReferenceEquals(oldForeground, foreground) && update != UpdateAction.Reapply) return;
            if (!preserveInteractionForeground
                || !InteractionStateApplier.TryUpdateInteractionForegroundRestoreValue(fe, property, foreground))
                fe.SetValue(property, foreground);
            if (fe is WinUI.RichTextBlock)
            {
                var appliedProperty = s_appliedForegroundProperty ??= DependencyProperty.RegisterAttached(
                    "ReactorAppliedRichTextForeground", typeof(Brush), typeof(ForegroundModifier), new PropertyMetadata(null));
                fe.SetValue(appliedProperty, foreground);
            }
        }
        else
        {
            if (!preserveInteractionForeground
                || !InteractionStateApplier.TryUpdateInteractionForegroundRestoreValue(fe, property, DependencyProperty.UnsetValue))
                fe.ClearValue(property);
            ClearApplied(fe);
        }
    }

    internal static void ClearApplied(FrameworkElement control)
    {
        if (control is WinUI.RichTextBlock && s_appliedForegroundProperty is { } property)
            control.ClearValue(property);
    }
}
