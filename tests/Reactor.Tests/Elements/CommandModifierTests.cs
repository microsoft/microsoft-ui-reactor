using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Tests for the <c>.Command(Command)</c> fluent modifier (issue #133), which binds a
/// <see cref="Command"/>'s enabled state, click handler, and metadata onto an
/// already-built clickable element. This closes the custom-content gap where
/// <c>Button(content, onClick)</c> had no command binding and callers had to re-thread
/// <c>.IsEnabled(command.IsEnabled)</c> by hand.
/// </summary>
public class CommandModifierTests
{
    // ── (a) The modifier applies command.IsEnabled ──────────────────

    [Fact]
    public void Command_On_CustomContent_Button_Applies_IsEnabled_True()
    {
        var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = true };

        var el = Button(TextBlock("Run")).Command(cmd);

        Assert.True(el.EffectiveIsEnabled);
    }

    [Fact]
    public void Command_On_CustomContent_Button_Applies_IsEnabled_False_When_Disabled()
    {
        var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        var el = Button(TextBlock("Run")).Command(cmd);

        Assert.False(el.EffectiveIsEnabled);
    }

    [Fact]
    public void Command_Composes_With_CustomContent_Button()
    {
        var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        var el = Button(TextBlock("Run")).Command(cmd);

        Assert.False(el.EffectiveIsEnabled);
        Assert.NotNull(el.ContentElement);
    }

    [Fact]
    public void Command_Sets_Typed_Command_Without_Appending_Setter()
    {
        var cmd = new Command { Label = "Run", Execute = () => { } };

        var before = Button(TextBlock("Run"));
        var after = before.Command(cmd);

        // issue #153 — the modifier lifts Command to a typed property instead of
        // appending a per-render Setters lambda. The reconciler applies the command's
        // metadata field-aware from the typed Command property.
        Assert.Equal(before.Setters.Length, after.Setters.Length);
        Assert.Same(cmd, after.Command);
    }

    // ── (b) IsEnabled is re-applied on update when command flips ─────

    [Fact]
    public void Command_Tracks_IsEnabled_Across_Renders()
    {
        // Simulate the UseCommand IsExecuting flip: the SAME render expression with a
        // command whose IsEnabled flips must produce an element whose IsEnabled tracks
        // the current command state — not a value captured once at construction.
        static ButtonElement Render(Command c) => Button(TextBlock("Run")).Command(c);

        var enabled = new Command { Label = "Run", Execute = () => { }, CanExecute = true };
        var disabled = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        Assert.True(Render(enabled).EffectiveIsEnabled);
        Assert.False(Render(disabled).EffectiveIsEnabled);
    }

    [Fact]
    public void Command_Modifier_Reapplies_On_Change_Via_Typed_Property()
    {
        // issue #153 — the modifier no longer allocates a fresh Setters array each
        // render (the per-render ApplyButtonBaseCommon lambda is gone). Re-application
        // on update is driven by the typed Command property + the descriptor's
        // modulo-delegates comparer: a changed command (e.g. a CanExecute flip) is not
        // ShallowEqual, so the reconciler still re-applies IsEnabled. (For custom-content
        // buttons ContentElement is non-null, which already forces ShallowEquals false.)
        var enabled = new Command { Label = "Run", Execute = () => { }, CanExecute = true };
        var disabled = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        var first = Button(TextBlock("Run")).Command(enabled);
        var second = Button(TextBlock("Run")).Command(disabled);

        // The modifier does not allocate fresh Setters (both share Array.Empty).
        Assert.Same(first.Setters, second.Setters);
        // A flipped command is not ShallowEqual, so the reconciler re-applies the effects.
        Assert.False(Element.ShallowEquals(first, second));
        Assert.True(first.EffectiveIsEnabled);
        Assert.False(second.EffectiveIsEnabled);
    }

    // ── (c) Dispatch flows from the typed Command (issue #637) ───────
    //     The modifier no longer bakes OnClick/OnIsCheckedChanged — the
    //     click/toggle trampoline invokes the command. These assert the
    //     record wiring + that the bound command dispatches; the live
    //     click→Execute path is pinned by the Commanding selftests.

    [Fact]
    public void Command_Wires_Click_To_Execute()
    {
        int count = 0;
        var cmd = new Command { Label = "Run", Execute = () => count++ };

        var el = Button(TextBlock("Run")).Command(cmd);
        Assert.Null(el.OnClick);              // issue #637 — dispatch via typed Command, not a baked closure
        Assert.Same(cmd, el.Command);
        CommandBindings.Invoke(el.Command!);  // what the click trampoline does when OnClick is null
        Assert.Equal(1, count);
    }

    [Fact]
    public void Command_Wires_Click_To_ExecuteAsync_When_No_Sync_Execute()
    {
        int count = 0;
        var cmd = new Command { Label = "Run", ExecuteAsync = () => { count++; return Task.CompletedTask; } };

        var el = Button(TextBlock("Run")).Command(cmd);
        Assert.Null(el.OnClick);
        CommandBindings.Invoke(el.Command!);

        Assert.Equal(1, count);
    }

    // ── Non-Button clickables ───────────────────────────────────────

    [Fact]
    public void Command_Wires_HyperlinkButton_Click()
    {
        int count = 0;
        var cmd = new Command { Label = "Details", Execute = () => count++ };

        var el = HyperlinkButton("Details").Command(cmd);
        Assert.Null(el.OnClick);
        Assert.Same(cmd, el.Command);
        CommandBindings.Invoke(el.Command!);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Command_Wires_RepeatButton_Click()
    {
        int count = 0;
        var cmd = new Command { Label = "Tick", Execute = () => count++ };

        var el = RepeatButton("Tick").Command(cmd);
        Assert.Null(el.OnClick);
        CommandBindings.Invoke(el.Command!);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Command_Wires_ToggleButton_OnEachToggle()
    {
        int count = 0;
        var cmd = new Command { Label = "Bold", Execute = () => count++ };

        var el = ToggleButton("Bold").Command(cmd);
        Assert.Null(el.OnIsCheckedChanged);   // issue #637 — toggle trampoline invokes the command
        Assert.Same(cmd, el.Command);
        // The live trampoline fires the command on each toggle (check + uncheck) — see selftests.
        CommandBindings.Invoke(el.Command!);
        CommandBindings.Invoke(el.Command!);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Command_On_AppBarButton_Maps_Execute_And_IsEnabled()
    {
        int count = 0;
        var cmd = new Command { Label = "Save", Execute = () => count++, CanExecute = false };

        var el = AppBarButton("Save").Command(cmd);

        Assert.False(el.IsEnabled);
        Assert.NotNull(el.OnClick);
        el.OnClick!();
        Assert.Equal(1, count);
    }

    // ── (M3) AppBarButton routes through CommandBindings.Invoke so async-only
    //         commands (ExecuteAsync, no sync Execute) fire instead of no-opping ──

    [Fact]
    public void Command_On_AppBarButton_Modifier_Wires_Click_To_ExecuteAsync_When_No_Sync_Execute()
    {
        int count = 0;
        var cmd = new Command { Label = "Save", ExecuteAsync = () => { count++; return Task.CompletedTask; } };

        var el = AppBarButton("Save").Command(cmd);
        Assert.NotNull(el.OnClick);
        el.OnClick!();

        Assert.Equal(1, count);
    }

    [Fact]
    public void AppBarButton_Command_Factory_Wires_Click_To_ExecuteAsync_When_No_Sync_Execute()
    {
        int count = 0;
        var cmd = new Command { Label = "Save", ExecuteAsync = () => { count++; return Task.CompletedTask; } };

        var el = AppBarButton(cmd);
        Assert.NotNull(el.OnClick);
        el.OnClick!();

        Assert.Equal(1, count);
    }

    // ── (M1) A disabled command must not override .IsDisabledFocusable() ─────
    //         The element keeps IsDisabledFocusable regardless of modifier order;
    //         the live-control coercion (IsEnabled stays true / reachable via Tab)
    //         is pinned by the CommandModifierDisabledFocusable* selftest fixtures.

    [Fact]
    public void Command_Before_IsDisabledFocusable_Keeps_DisabledFocusable()
    {
        var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        var el = Button(TextBlock("Run")).Command(cmd).IsDisabledFocusable();

        Assert.True(el.IsDisabledFocusable);
    }

    [Fact]
    public void IsDisabledFocusable_Before_Command_Keeps_DisabledFocusable()
    {
        var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        var el = Button(TextBlock("Run")).IsDisabledFocusable().Command(cmd);

        Assert.True(el.IsDisabledFocusable);
    }

    // ── (M1, PR-review) The Button(Command) factory must defer IsEnabled to the
    //    descriptor too (folded into EffectiveIsEnabled, applyIsEnabled:false), so a
    //    disabled command chained with .IsDisabledFocusable() stays in the tab order —
    //    same guarantee as the modifier and the bare record-init (issue #637).

    [Fact]
    public void Button_Command_Factory_Applies_IsEnabled_False_When_Disabled()
    {
        var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        var el = Button(cmd);

        // issue #637 — the record IsEnabled field is left at its default (true); the
        // command's disabled state is folded into the descriptor via EffectiveIsEnabled.
        Assert.False(el.EffectiveIsEnabled);
    }

    [Fact]
    public void Button_Command_Factory_With_IsDisabledFocusable_Keeps_DisabledFocusable()
    {
        var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = false };

        var el = Button(cmd).IsDisabledFocusable();

        Assert.True(el.IsDisabledFocusable);
    }

    // ── (M6, issue #637 review) The .Command() modifier makes the command fully take over:
    //    any conflicting OnClick / toggle callback already on the element is cleared so the
    //    command — not a stale handler — dispatches. This restores the pre-#637 command-wins
    //    semantics (the brief #637 callback-wins behavior was never released). The bare
    //    record-init path keeps its trampoline rule (explicit callback wins) — see
    //    CommandTypedPropertyTests.BareInit_BothPresent_CallbackWins.

    [Fact]
    public void Command_Modifier_Clears_Conflicting_OnClick_Command_Wins()
    {
        int onClickCount = 0, cmdCount = 0;
        var cmd = new Command { Label = "Run", Execute = () => cmdCount++ };

        var el = Button("Run", () => onClickCount++).Command(cmd);

        Assert.Null(el.OnClick);              // the modifier cleared the conflicting click handler
        Assert.Same(cmd, el.Command);
        // OnClick null ⇒ the click trampoline invokes the command, not the original onClick.
        CommandBindings.Invoke(el.Command!);
        Assert.Equal(1, cmdCount);
        Assert.Equal(0, onClickCount);
    }

    [Fact]
    public void Command_Modifier_Clears_Conflicting_HyperlinkButton_OnClick()
    {
        var cmd = new Command { Label = "Go", Execute = () => { } };

        var el = HyperlinkButton("Go", onClick: () => { }).Command(cmd);

        Assert.Null(el.OnClick);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void Command_Modifier_Clears_Conflicting_RepeatButton_OnClick()
    {
        var cmd = new Command { Label = "Tick", Execute = () => { } };

        var el = RepeatButton("Tick", () => { }).Command(cmd);

        Assert.Null(el.OnClick);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void Command_Modifier_Clears_Conflicting_ToggleButton_Callbacks_Command_Wins()
    {
        int toggleCount = 0, cmdCount = 0;
        var cmd = new Command { Label = "Bold", Execute = () => cmdCount++ };

        var el = ToggleButton("Bold", onIsCheckedChanged: _ => toggleCount++).Command(cmd);

        Assert.Null(el.OnIsCheckedChanged);   // both toggle callbacks cleared
        Assert.Null(el.OnCheckedStateChanged);
        Assert.Same(cmd, el.Command);
        CommandBindings.Invoke(el.Command!);
        Assert.Equal(1, cmdCount);
        Assert.Equal(0, toggleCount);
    }

    [Fact]
    public void Command_Modifier_Clears_ThreeState_Toggle_Callback()
    {
        // ThreeStateToggleButton sets OnCheckedStateChanged; .Command() clears it (and
        // OnIsCheckedChanged) so the command takes over dispatch.
        var cmd = new Command { Label = "Tri", Execute = () => { } };

        var el = ThreeStateToggleButton("Tri", onCheckedStateChanged: _ => { }).Command(cmd);

        Assert.Null(el.OnCheckedStateChanged);
        Assert.Null(el.OnIsCheckedChanged);
        Assert.Same(cmd, el.Command);
    }
}
