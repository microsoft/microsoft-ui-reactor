using Microsoft.UI.Input;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Microsoft.UI.Reactor.Core;

internal static class TextBoxSubmitBehavior
{
    internal static void Ensure(TextBox control, TextBoxElement element)
    {
        if (element.OnSubmitted is null) return;
        var state = Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>(control);
        if (state.SubmitWired) return;
        state.SubmitWired = true;
        control.PreviewKeyDown += OnPreviewKeyDown;
        control.TextCompositionStarted += static (c, _) =>
            Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>(c).IsComposing = true;
        control.TextCompositionEnded += static (c, _) =>
            Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>(c).IsComposing = false;
        control.LostFocus += static (c, _) =>
            Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>((TextBox)c).IsComposing = false;
        control.Unloaded += static (c, _) =>
            Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>((TextBox)c).IsComposing = false;
    }

    private static void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || args.Key != VirtualKey.Enter) return;
        args.Handled = TrySubmit((TextBox)sender, IsDown(VirtualKey.Shift),
            IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), args.KeyStatus.WasKeyDown);
    }

    private static bool IsDown(VirtualKey key)
        => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    internal static bool TrySubmit(TextBox control, bool shift, bool controlKey, bool alt, bool repeat)
    {
        if (shift || controlKey || alt || control.IsReadOnly || !control.IsEnabled
            || Reconciler.GetElementTag(control) is not TextBoxElement { OnSubmitted: { } submit }
            || Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>(control).IsComposing)
            return false;
        if (!repeat) submit(control.Text);
        return true;
    }
}
