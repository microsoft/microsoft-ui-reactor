using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the Phase 5 commanding coverage extension (spec 027 Tier 4).
/// Verify each new command-capable factory accepts a <see cref="Command"/> and
/// mirrors Label, IsEnabled, and Execute into the resulting element.
/// </summary>
public class CommandingCoverageTests
{
    private static Command MakeCmd(Action? onExec = null, bool canExecute = true, string? description = null, string? accessKey = null) =>
        new Command
        {
            Label = "Save",
            Execute = onExec,
            CanExecute = canExecute,
            Description = description,
            AccessKey = accessKey,
        };

    // ── Button ──────────────────────────────────────────────────────

    [Fact]
    public void Button_Command_SetsLabelAndIsEnabled()
    {
        var cmd = MakeCmd();
        var el = Button(cmd);

        Assert.Equal("Save", el.Label);
        Assert.True(el.EffectiveIsEnabled);
    }

    [Fact]
    public void Button_Command_DisablesWhenCanExecuteFalse()
    {
        var cmd = MakeCmd(canExecute: false);
        var el = Button(cmd);
        Assert.False(el.EffectiveIsEnabled);
    }

    [Fact]
    public void Button_Command_DispatchInvokesExecute()
    {
        int count = 0;
        var cmd = MakeCmd(() => count++);
        var el = Button(cmd);
        Assert.Null(el.OnClick);              // issue #637 — dispatch via the typed Command
        CommandBindings.Invoke(el.Command!);  // what the click trampoline does when OnClick is null
        Assert.Equal(1, count);
    }

    // ── HyperlinkButton ─────────────────────────────────────────────

    [Fact]
    public void HyperlinkButton_Command_SetsContentAndClick()
    {
        int count = 0;
        var cmd = MakeCmd(() => count++);
        var el = HyperlinkButton(cmd);

        Assert.Equal("Save", el.Content);
        Assert.Null(el.OnClick);
        CommandBindings.Invoke(el.Command!);
        Assert.Equal(1, count);
    }

    // ── RepeatButton ────────────────────────────────────────────────

    [Fact]
    public void RepeatButton_Command_SetsLabelAndClick()
    {
        int count = 0;
        var cmd = MakeCmd(() => count++);
        var el = RepeatButton(cmd);

        Assert.Equal("Save", el.Label);
        Assert.Null(el.OnClick);
        CommandBindings.Invoke(el.Command!);
        Assert.Equal(1, count);
    }

    // ── ToggleButton (fires on each toggle — Option A) ──────────────

    [Fact]
    public void ToggleButton_Command_FiresOnEachToggle()
    {
        int count = 0;
        var cmd = MakeCmd(() => count++);
        var el = ToggleButton(cmd);

        Assert.Null(el.OnIsCheckedChanged);   // issue #637 — the toggle trampoline invokes the command
        // The live trampoline fires the command on each toggle (check + uncheck) — see selftests.
        CommandBindings.Invoke(el.Command!);
        CommandBindings.Invoke(el.Command!);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ToggleButton_Command_InitialCheckedParam()
    {
        var el = ToggleButton(MakeCmd(), isChecked: true);
        Assert.True(el.IsChecked);
    }

    // ── SplitButton ─────────────────────────────────────────────────

    [Fact]
    public void SplitButton_Command_SetsLabelAndPrimaryClick()
    {
        int count = 0;
        var cmd = MakeCmd(() => count++);
        var el = SplitButton(cmd);

        Assert.Equal("Save", el.Label);
        Assert.Null(el.OnClick);
        CommandBindings.Invoke(el.Command!);
        Assert.Equal(1, count);
    }

    [Fact]
    public void SplitButton_Command_AcceptsFlyout()
    {
        var flyout = TextBlock("menu");
        var el = SplitButton(MakeCmd(), flyout: flyout);
        Assert.Same(flyout, el.Flyout);
    }

    // ── ToggleSplitButton ───────────────────────────────────────────

    [Fact]
    public void ToggleSplitButton_Command_FiresOnEachToggle()
    {
        int count = 0;
        var cmd = MakeCmd(() => count++);
        var el = ToggleSplitButton(cmd);

        Assert.Null(el.OnIsCheckedChanged);
        CommandBindings.Invoke(el.Command!);
        CommandBindings.Invoke(el.Command!);
        Assert.Equal(2, count);
    }

    // ── ExecuteAsync fire-and-forget ────────────────────────────────

    [Fact]
    public void Button_Command_ExecuteAsync_InvokedWhenExecuteNull()
    {
        bool called = false;
        var cmd = new Command
        {
            Label = "Save",
            ExecuteAsync = () => { called = true; return global::System.Threading.Tasks.Task.CompletedTask; },
        };
        var el = Button(cmd);
        Assert.Null(el.OnClick);
        CommandBindings.Invoke(el.Command!);
        Assert.True(called);
    }
}
