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
        if (cmd.AccessKey is not null) btn.AccessKey = cmd.AccessKey;
        if (cmd.Accelerator is not null)
        {
            btn.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Key = cmd.Accelerator.Key,
                Modifiers = cmd.Accelerator.Modifiers,
            });
        }
    }

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
