using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared binding helpers for wiring a <see cref="Command"/> into command-capable
/// WinUI controls (<see cref="ButtonBase"/> derivatives, <see cref="SplitButton"/>, …).
/// Keeps the per-control factory overloads thin: the factory/modifier wires label +
/// click and stores the command on the element's typed <c>Command</c> property; the
/// reconciler applies its Description / Icon / Accelerator / AccessKey / enabled metadata
/// field-aware through the <see cref="OneWayCommand{TElement,TControl}"/> descriptor entry
/// (issue #153). Raw <c>.Set(...)</c> setters run after every descriptor prop, so an explicit
/// <c>.Set(b =&gt; b.AccessKey = "X")</c> overrides command-derived metadata regardless of where
/// it appears in the fluent chain — the documented "<c>.Set</c> wins / applied last" rule.
/// </summary>
internal static class CommandBindings
{
    /// <summary>
    /// Applies command metadata that is common to every command-capable WinUI control:
    /// <see cref="Control.IsEnabled"/>, the <c>ToolTipService.ToolTip</c> attached property,
    /// <see cref="UIElement.AccessKey"/>, and <see cref="UIElement.KeyboardAccelerators"/>.
    /// Accepts <see cref="Control"/> so it can target both <see cref="ButtonBase"/>
    /// derivatives and WinUI controls that don't derive from ButtonBase
    /// (e.g. <see cref="SplitButton"/>, <see cref="ToggleSplitButton"/>).
    /// </summary>
    /// <param name="btn">The live command-capable control to apply metadata to.</param>
    /// <param name="cmd">The command whose metadata (enabled state, tooltip, accelerator, access key) is applied.</param>
    /// <param name="applyIsEnabled">
    /// When false, the command's <see cref="Command.IsEnabled"/> is NOT written to the
    /// control. Callers whose element already drives <see cref="Control.IsEnabled"/> through
    /// a descriptor prop pass false so this setter doesn't clobber descriptor-owned coercion
    /// — notably <see cref="ButtonElement"/>'s <c>IsDisabledFocusable</c>, which must keep a
    /// disabled button <see cref="Control.IsEnabled"/>=true (reachable via Tab) even when the
    /// bound command is disabled. (issue #133, PR review M1)
    /// </param>
    internal static void ApplyButtonBaseCommon(Control btn, Command cmd, bool applyIsEnabled = true)
    {
        if (applyIsEnabled) btn.IsEnabled = cmd.IsEnabled;
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
            // Set placement mode BEFORE adding the accelerator. WinUI captures the
            // auto-tooltip-generation decision at the moment the accelerator is
            // added; setting mode=Hidden afterward didn't reliably clear the
            // already-generated chord tooltip ("Ctrl+O") on x64-emulated WinUI 3
            // self-contained — it could persist and stick when the UI thread was
            // briefly busy. Setting mode first keeps the auto tooltip from ever
            // being generated. Callers that want the chord visible set
            // cmd.Description — WinUI shows that as the explicit tooltip and the
            // chord remains discoverable via the keyboard hint overlay.
            if (cmd.Description is null)
                btn.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            else
                btn.ClearValue(UIElement.KeyboardAcceleratorPlacementModeProperty);

            var accel = new KeyboardAccelerator
            {
                Key = cmd.Accelerator.Key,
                Modifiers = cmd.Accelerator.Modifiers,
            };
            btn.KeyboardAccelerators.Add(accel);
            _commandAccelerators.Add(btn, accel);
        }
        else
        {
            btn.ClearValue(UIElement.KeyboardAcceleratorPlacementModeProperty);
        }
    }

    private static readonly ConditionalWeakTable<Control, KeyboardAccelerator> _commandAccelerators = new();

    /// <summary>
    /// Clears the metadata a prior <see cref="Command"/> applied to <paramref name="btn"/> when the
    /// element's command transitions to <c>null</c> — either an in-place re-render (e.g.
    /// <c>Button(cmd)</c> → <c>Button("x")</c>) or a pooled control rented for a command-less button.
    /// Without this, the previously-set tooltip / UIA HelpText, AccessKey, command-added
    /// <see cref="KeyboardAccelerator"/>, and placement mode would stick on the live control (issue
    /// #153, PR review). <see cref="Control.IsEnabled"/> is intentionally NOT reset: the element's
    /// own IsEnabled descriptor prop drives it.
    /// </summary>
    internal static void ClearButtonCommandMetadata(Control btn)
    {
        ToolTipService.SetToolTip(btn, null);
        btn.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.HelpTextProperty);
        btn.AccessKey = "";
        if (_commandAccelerators.TryGetValue(btn, out var prior))
        {
            btn.KeyboardAccelerators.Remove(prior);
            _commandAccelerators.Remove(btn);
        }
        btn.ClearValue(UIElement.KeyboardAcceleratorPlacementModeProperty);
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

    /// <summary>
    /// Returns a non-null delegate when <paramref name="cmd"/> can actually be invoked
    /// (it carries an <see cref="Command.Execute"/> or <see cref="Command.ExecuteAsync"/>),
    /// otherwise <c>null</c>. Used by the command-capable button trampolines as the
    /// click-dispatch <em>fallback</em> and as the HandCodedEvent / Controlled
    /// subscription gate when a button is bound <b>only</b> through the typed
    /// <see cref="Command"/> property — i.e. a bare <c>new XxxElement(cmd.Label) { Command = cmd }</c>
    /// record-init with no <c>OnClick</c>/<c>OnIsCheckedChanged</c> handler (issue #637).
    /// A command with neither delegate has nothing to dispatch, so the event stays
    /// unsubscribed (zero cost), while its metadata still flows through
    /// <see cref="OneWayCommand{TElement,TControl}"/>.
    /// </summary>
    internal static Delegate? Invokable(Command? cmd) =>
        cmd is null ? null : (Delegate?)cmd.Execute ?? cmd.ExecuteAsync;

    /// <summary>
    /// The effective click/dispatch callback for a command-capable button element: the explicit
    /// user callback (<c>OnClick</c> / a toggle handler) when present, otherwise the typed command's
    /// invokable delegate (<see cref="Invokable"/>). A single shared primitive so an element's
    /// <c>HasCallbacks</c> and its HandCodedEvent / Controlled subscription gate can never drift —
    /// both treat a delegate-less command (metadata only, no Execute/ExecuteAsync) as "no callback"
    /// (issue #637 review M2).
    /// </summary>
    internal static Delegate? EffectiveCallback(Delegate? userCallback, Command? cmd) =>
        userCallback ?? Invokable(cmd);

    /// <summary>
    /// Registers the typed <see cref="Command"/> descriptor entry shared by every
    /// command-capable button element (issue #153). On mount it applies
    /// <see cref="ApplyButtonBaseCommon"/>; on update it re-applies only when a rendered
    /// command field changed (delegate fields ignored via
    /// <see cref="CommandModuloDelegatesComparer"/>). When the command transitions to <c>null</c>
    /// (in-place re-render or a pooled control reused without a command) the setter clears the
    /// stale command metadata via <see cref="ClearButtonCommandMetadata"/>. Pass
    /// <paramref name="applyIsEnabled"/>=false when the element already drives
    /// <see cref="Control.IsEnabled"/> through its own descriptor prop (e.g.
    /// <see cref="ButtonElement"/>'s <c>IsDisabledFocusable</c>-coerced entry), so the command apply
    /// does not clobber that coercion.
    /// </summary>
    internal static global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TElement, TControl> OneWayCommand<TElement, TControl>(
        this global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TElement, TControl> d,
        Func<TElement, Command?> getCommand,
        bool applyIsEnabled = true)
        where TElement : Element
        where TControl : Control, new()
        => applyIsEnabled
            ? d.OneWay<Command?>(
                getCommand,
                static (c, cmd) => { if (cmd is not null) ApplyButtonBaseCommon(c, cmd, applyIsEnabled: true); else ClearButtonCommandMetadata(c); },
                CommandModuloDelegatesComparer.Instance)
            : d.OneWay<Command?>(
                getCommand,
                static (c, cmd) => { if (cmd is not null) ApplyButtonBaseCommon(c, cmd, applyIsEnabled: false); else ClearButtonCommandMetadata(c); },
                CommandModuloDelegatesComparer.Instance);

    /// <summary>
    /// Structural equality for two <see cref="Command"/> values that <b>ignores</b> the
    /// <see cref="Command.Execute"/> / <see cref="Command.ExecuteAsync"/> delegate fields
    /// (issue #153, same rationale as #151). Dispatch goes through the click trampoline,
    /// which reads the latest <see cref="Command"/> off the element Tag at invoke time, so
    /// delegate identity is irrelevant to dispatch correctness — only the rendered metadata
    /// (label, enabled state, tooltip, accelerator, access key) determines whether the
    /// command-applied control state must be re-written. Two commands that differ only in
    /// their delegates therefore produce identical visuals and can skip reconcile entirely.
    /// <para>
    /// Enabled state is compared via the <em>derived</em> <see cref="Command.IsEnabled"/>
    /// (<c>CanExecute &amp;&amp; !IsExecuting &amp;&amp; !IsDebouncing</c>) rather than its raw inputs, so a
    /// debounce-window flip (a <c>UseCommand(DebounceMs:)</c> command toggling
    /// <see cref="Command.IsDebouncing"/>) registers as a change and forces the button to
    /// re-disable / re-enable on the transition. Steady-state, non-debouncing renders still
    /// compare equal, so the #153 fast-path skip stays intact (issue #637 review M1).
    /// </para>
    /// </summary>
    internal static bool CommandsEqual(Command? a, Command? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Label == b.Label
            && a.IsEnabled == b.IsEnabled
            && a.Description == b.Description
            && a.AccessKey == b.AccessKey
            && Equals(a.Icon, b.Icon)
            && Equals(a.Accelerator, b.Accelerator);
    }

    /// <summary>
    /// <see cref="IEqualityComparer{T}"/> wrapper over <see cref="CommandsEqual"/> for the
    /// descriptor's <c>OneWay&lt;Command?&gt;</c> diff: the command-apply entry only re-runs
    /// <see cref="ApplyButtonBaseCommon"/> when the command changed in a rendered field, never
    /// when only its delegates changed across renders.
    /// </summary>
    internal sealed class CommandModuloDelegatesComparer : IEqualityComparer<Command?>
    {
        internal static readonly CommandModuloDelegatesComparer Instance = new();
        public bool Equals(Command? a, Command? b) => CommandsEqual(a, b);
        public int GetHashCode(Command? c) =>
            c is null ? 0 : global::System.HashCode.Combine(c.Label, c.IsEnabled, c.Description, c.AccessKey, c.Icon, c.Accelerator);
    }
}
