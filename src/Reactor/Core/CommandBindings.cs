using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared binding helpers for wiring a <see cref="Command"/> into command-capable
/// WinUI controls (<see cref="ButtonBase"/> derivatives, <see cref="SwipeItem"/>, …).
/// Keeps the per-control factory overloads thin: apply label/onClick at construction
/// time and defer Description / Icon / Accelerator / AccessKey to a mount-time setter
/// so per-site overrides (e.g. <c>.AccessKey("X")</c> after <c>.Command(cmd)</c>) win
/// via the normal modifier-after-command ordering.
/// </summary>
internal static class CommandBindings
{
    /// <summary>
    /// Applies command metadata that is common to every command-capable WinUI control:
    /// <see cref="Control.IsEnabled"/>, <see cref="ToolTipService.ToolTip"/>,
    /// <see cref="UIElement.AccessKey"/>, and <see cref="UIElement.KeyboardAccelerators"/>.
    /// Accepts <see cref="Control"/> so it can target both <see cref="ButtonBase"/>
    /// derivatives and WinUI controls that don't derive from ButtonBase
    /// (e.g. <see cref="SplitButton"/>, <see cref="ToggleSplitButton"/>).
    /// </summary>
    internal static void ApplyButtonBaseCommon(Control btn, Command cmd)
    {
        btn.IsEnabled = cmd.IsEnabled;
        if (cmd.Description is not null)
        {
            ToolTipService.SetToolTip(btn, cmd.Description);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(btn, cmd.Description);
        }
        else if (cmd.Accelerator is not null && !string.IsNullOrEmpty(cmd.Label))
        {
            // No description, but the button is bound to a chord. Setting an
            // explicit tooltip (using the command Label as the fallback)
            // suppresses WinUI's auto-generated bare-chord tooltip ("Ctrl+O")
            // which is uninformative on its own and has been observed to
            // stick on screen when the UI thread is busy. Auto-tooltip
            // generation only kicks in when ToolTipService.ToolTip is
            // genuinely unset, so any non-null value here defeats it. We
            // intentionally don't set HelpText: the visible Label is already
            // exposed to assistive tech, no need to duplicate.
            ToolTipService.SetToolTip(btn, cmd.Label);
            btn.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.HelpTextProperty);
        }
        else
        {
            // SECURITY (TASK-072): when a Command transitions Description from
            // non-null to null, the previously-set tooltip and UIA HelpText
            // would otherwise persist as stale values. Clear them.
            ToolTipService.SetToolTip(btn, null);
            btn.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.HelpTextProperty);
        }
        if (cmd.AccessKey is not null) btn.AccessKey = cmd.AccessKey;
        else btn.AccessKey = "";

        // Remove any prior command-added accelerator before adding the new one, so
        // rerunning this setter on update/reconcile doesn't stack duplicates that
        // would cause the command to fire multiple times per chord.
        if (_commandAccelerators.TryGetValue(btn, out var prior))
        {
            btn.KeyboardAccelerators.Remove(prior);
            _commandAccelerators.Remove(btn);
        }
        if (cmd.Accelerator is not null)
        {
            var accel = new KeyboardAccelerator
            {
                Key = cmd.Accelerator.Key,
                Modifiers = cmd.Accelerator.Modifiers,
            };
            btn.KeyboardAccelerators.Add(accel);
            _commandAccelerators.Add(btn, accel);

            // Suppress WinUI's auto-generated bare-chord tooltip ("Ctrl+O") when
            // the command has no Description to anchor it. Without a label, the
            // floating "Ctrl+O" tooltip is uninformative on its own, and has been
            // observed to stick on screen when the UI thread is briefly busy
            // (the dismiss animation can't run). Callers that want the chord
            // visible should set cmd.Description ("Open Folder", say) — WinUI
            // shows that as the explicit tooltip and the chord remains
            // discoverable via the keyboard hint overlay.
            if (cmd.Description is null)
                btn.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            else
                btn.ClearValue(UIElement.KeyboardAcceleratorPlacementModeProperty);
        }
        else
        {
            btn.ClearValue(UIElement.KeyboardAcceleratorPlacementModeProperty);
        }
    }

    private static readonly ConditionalWeakTable<Control, KeyboardAccelerator> _commandAccelerators = new();

    /// <summary>
    /// Invokes <see cref="Command.Execute"/> or fires-and-forgets
    /// <see cref="Command.ExecuteAsync"/>. Used by factory overloads that need to
    /// wire a click handler from a bare <see cref="Command"/>.
    /// </summary>
    internal static void Invoke(Command cmd)
    {
        if (cmd.Execute is not null) cmd.Execute();
        else if (cmd.ExecuteAsync is not null) _ = cmd.ExecuteAsync();
    }
}
