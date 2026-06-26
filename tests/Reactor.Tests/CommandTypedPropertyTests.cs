using Microsoft.UI.Reactor.Core;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #153 — Command is lifted to a typed <c>Command?</c> record property on the
/// command-capable button elements (Button, HyperlinkButton, RepeatButton, ToggleButton,
/// SplitButton, ToggleSplitButton). The command factories no longer allocate a per-render
/// <c>Setters</c> array + lambda, and <see cref="Element.ShallowEquals"/> fast-paths
/// command-bound buttons whose Command is unchanged (reference-equal OR structurally equal
/// modulo the Execute/ExecuteAsync delegates).
///
/// These are pure C# record tests — no WinUI thread required. Live mount/update behaviour is
/// covered by the Commanding selftest fixtures (CommandingCoverageFixtures.cs).
/// </summary>
public class CommandTypedPropertyTests
{
    private static Command MakeCmd(Action? execute = null) => new()
    {
        Label = "Save",
        Execute = execute ?? (() => { }),
        Icon = new SymbolIconData("Save"),
        Accelerator = new KeyboardAcceleratorData(VirtualKey.S, VirtualKeyModifiers.Control),
        AccessKey = "S",
        Description = "Save the file",
    };

    // ════════════════════════════════════════════════════════════════
    //  (a) Command factories allocate NO Setters array
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Button_Command_AllocatesNoSetters()
    {
        var cmd = MakeCmd();
        var el = Button(cmd);
        Assert.Empty(el.Setters);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void HyperlinkButton_Command_AllocatesNoSetters()
    {
        var cmd = MakeCmd();
        var el = HyperlinkButton(cmd);
        Assert.Empty(el.Setters);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void RepeatButton_Command_AllocatesNoSetters()
    {
        var cmd = MakeCmd();
        var el = RepeatButton(cmd);
        Assert.Empty(el.Setters);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void ToggleButton_Command_AllocatesNoSetters()
    {
        var cmd = MakeCmd();
        var el = ToggleButton(cmd);
        Assert.Empty(el.Setters);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void SplitButton_Command_AllocatesNoSetters()
    {
        var cmd = MakeCmd();
        var el = SplitButton(cmd);
        Assert.Empty(el.Setters);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void ToggleSplitButton_Command_AllocatesNoSetters()
    {
        var cmd = MakeCmd();
        var el = ToggleSplitButton(cmd);
        Assert.Empty(el.Setters);
        Assert.Same(cmd, el.Command);
    }

    [Fact]
    public void Command_Factories_ShareReferenceEqualEmptySetters()
    {
        // Array.Empty<T>() is reference-shared, so the ShallowEquals
        // ReferenceEquals(Setters, Setters) guard stays true across two
        // command-bound buttons that carry no extra setters.
        var cmd = MakeCmd();
        var a = Button(cmd);
        var b = Button(cmd);
        Assert.Same(a.Setters, b.Setters);
    }

    // ════════════════════════════════════════════════════════════════
    //  (b) ShallowEquals fast-paths unchanged commands
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShallowEquals_True_When_Command_ReferenceEqual()
    {
        var cmd = MakeCmd();
        Assert.True(Element.ShallowEquals(Button(cmd), Button(cmd)));
        Assert.True(Element.ShallowEquals(HyperlinkButton(cmd), HyperlinkButton(cmd)));
        Assert.True(Element.ShallowEquals(RepeatButton(cmd), RepeatButton(cmd)));
        Assert.True(Element.ShallowEquals(ToggleButton(cmd), ToggleButton(cmd)));
    }

    [Fact]
    public void ShallowEquals_True_When_Command_StructurallyEqual_ModuloDelegates()
    {
        // Two distinct Command instances with identical rendered metadata but
        // DIFFERENT Execute delegates — the per-render closure case. ShallowEquals
        // must still fast-path because the rendered fields are unchanged.
        int x = 0, y = 0;
        var cmdA = MakeCmd(() => x++);
        var cmdB = MakeCmd(() => y++);
        Assert.NotSame(cmdA, cmdB);
        Assert.NotSame(cmdA.Execute, cmdB.Execute);

        Assert.True(Element.ShallowEquals(Button(cmdA), Button(cmdB)));
        Assert.True(Element.ShallowEquals(HyperlinkButton(cmdA), HyperlinkButton(cmdB)));
        Assert.True(Element.ShallowEquals(RepeatButton(cmdA), RepeatButton(cmdB)));
        Assert.True(Element.ShallowEquals(ToggleButton(cmdA), ToggleButton(cmdB)));
    }

    [Fact]
    public void ShallowEquals_True_When_Command_StructurallyEqual_ModuloAsyncDelegate()
    {
        var cmdA = new Command { Label = "Run", ExecuteAsync = async () => { await Task.Yield(); } };
        var cmdB = new Command { Label = "Run", ExecuteAsync = async () => { await Task.Delay(1); } };
        Assert.NotSame(cmdA.ExecuteAsync, cmdB.ExecuteAsync);
        Assert.True(Element.ShallowEquals(Button(cmdA), Button(cmdB)));
    }

    [Fact]
    public void ShallowEquals_False_When_Command_Label_Differs()
    {
        var cmdA = MakeCmd();
        var cmdB = cmdA with { Label = "Save As" };
        // Label also flows to the Button content, so this would be unequal anyway —
        // assert specifically through the command compare with matching content.
        Assert.False(Element.ShallowEquals(Button(cmdA), Button(cmdB)));
    }

    [Fact]
    public void ShallowEquals_False_When_Command_AccessKey_Differs()
    {
        // AccessKey does NOT flow to a record field — only to the typed Command —
        // so this isolates the CommandsEqual contribution.
        var cmdA = MakeCmd();
        var cmdB = cmdA with { AccessKey = "X" };
        Assert.False(Element.ShallowEquals(Button(cmdA), Button(cmdB)));
        Assert.False(Element.ShallowEquals(HyperlinkButton(cmdA), HyperlinkButton(cmdB)));
        Assert.False(Element.ShallowEquals(RepeatButton(cmdA), RepeatButton(cmdB)));
        Assert.False(Element.ShallowEquals(ToggleButton(cmdA), ToggleButton(cmdB)));
    }

    [Fact]
    public void ShallowEquals_False_When_Command_Description_Differs()
    {
        var cmdA = MakeCmd();
        var cmdB = cmdA with { Description = "Different tooltip" };
        Assert.False(Element.ShallowEquals(Button(cmdA), Button(cmdB)));
    }

    [Fact]
    public void ShallowEquals_False_When_Command_CanExecute_Differs()
    {
        var cmdA = MakeCmd();
        var cmdB = cmdA with { CanExecute = false };
        Assert.False(Element.ShallowEquals(Button(cmdA), Button(cmdB)));
    }

    [Fact]
    public void ShallowEquals_False_When_OneSideHasNoCommand()
    {
        var cmd = MakeCmd();
        var withCmd = Button(cmd);
        var noCmd = Button("Save");
        Assert.False(Element.ShallowEquals(withCmd, noCmd));
    }

    // issue #153 (L1) — Split / ToggleSplit fast-path the command arm too, so all six
    // command-capable buttons memoize consistently.
    [Fact]
    public void ShallowEquals_True_For_Split_And_ToggleSplit_When_Command_Unchanged()
    {
        var cmd = MakeCmd();
        // reference-equal command
        Assert.True(Element.ShallowEquals(SplitButton(cmd), SplitButton(cmd)));
        Assert.True(Element.ShallowEquals(ToggleSplitButton(cmd), ToggleSplitButton(cmd)));

        // structurally-equal-modulo-delegates (fresh Execute closure each render)
        var cmdA = MakeCmd() with { Execute = () => { } };
        var cmdB = MakeCmd() with { Execute = () => { } };
        Assert.True(Element.ShallowEquals(SplitButton(cmdA), SplitButton(cmdB)));
        Assert.True(Element.ShallowEquals(ToggleSplitButton(cmdA), ToggleSplitButton(cmdB)));
    }

    [Fact]
    public void ShallowEquals_False_For_Split_And_ToggleSplit_When_Command_AccessKey_Differs()
    {
        var cmdA = MakeCmd();
        var cmdB = cmdA with { AccessKey = "X" };
        Assert.False(Element.ShallowEquals(SplitButton(cmdA), SplitButton(cmdB)));
        Assert.False(Element.ShallowEquals(ToggleSplitButton(cmdA), ToggleSplitButton(cmdB)));
    }

    // ════════════════════════════════════════════════════════════════
    //  CommandsEqual unit semantics (internal, via InternalsVisibleTo)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CommandsEqual_IgnoresExecuteDelegates()
    {
        var a = MakeCmd(() => { });
        var b = MakeCmd(() => { });
        Assert.True(CommandBindings.CommandsEqual(a, b));
    }

    [Fact]
    public void CommandsEqual_BothNull_True_OneNull_False()
    {
        Assert.True(CommandBindings.CommandsEqual(null, null));
        Assert.False(CommandBindings.CommandsEqual(MakeCmd(), null));
        Assert.False(CommandBindings.CommandsEqual(null, MakeCmd()));
    }

    [Fact]
    public void CommandsEqual_ComparesAcceleratorAndIcon()
    {
        var a = MakeCmd();
        var diffAccel = a with { Accelerator = new KeyboardAcceleratorData(VirtualKey.X, VirtualKeyModifiers.Control) };
        var diffIcon = a with { Icon = new SymbolIconData("Open") };
        Assert.False(CommandBindings.CommandsEqual(a, diffAccel));
        Assert.False(CommandBindings.CommandsEqual(a, diffIcon));
    }

    // ════════════════════════════════════════════════════════════════
    //  (d) Dispatch is uniform via the typed Command (issue #637).
    //      The factory/modifier no longer bake OnClick — the click
    //      trampoline invokes the command when OnClick is null. These
    //      assert the record-level wiring + dispatch helpers; the live
    //      click→Execute path is covered by the Commanding selftest
    //      fixtures (CommandingCoverageFixtures.cs).
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Button_Command_Factory_DoesNotBake_OnClick()
    {
        // The per-construct OnClick closure is gone (issue #637) — dispatch flows
        // from the typed Command via the click trampoline instead.
        var cmd = new Command { Label = "Go", Execute = () => { } };
        var el = Button(cmd);
        Assert.Null(el.OnClick);
        Assert.Same(cmd, el.Command);
        Assert.True(el.HasCallbacks);
    }

    [Fact]
    public void ToggleButton_Command_Factory_DoesNotBake_OnIsCheckedChanged()
    {
        var cmd = new Command { Label = "T", Execute = () => { } };
        var el = ToggleButton(cmd);
        Assert.Null(el.OnIsCheckedChanged);
        Assert.Same(cmd, el.Command);
        Assert.True(el.HasCallbacks);
    }

    [Fact]
    public void Invokable_Returns_Execute_Then_ExecuteAsync_Else_Null()
    {
        Action exec = () => { };
        Func<Task> execAsync = () => Task.CompletedTask;
        // Execute is preferred; ExecuteAsync is the fallback; Execute wins when both present.
        Assert.Same(exec, CommandBindings.Invokable(new Command { Label = "a", Execute = exec }));
        Assert.Same(execAsync, CommandBindings.Invokable(new Command { Label = "a", ExecuteAsync = execAsync }));
        Assert.Same(exec, CommandBindings.Invokable(new Command { Label = "a", Execute = exec, ExecuteAsync = execAsync }));
        // No delegate ⇒ nothing to dispatch ⇒ null (the event stays unsubscribed, zero cost).
        Assert.Null(CommandBindings.Invokable(new Command { Label = "a" }));
        Assert.Null(CommandBindings.Invokable(null));
    }

    [Fact]
    public void Invoke_Runs_Sync_Execute()
    {
        int count = 0;
        CommandBindings.Invoke(new Command { Label = "Go", Execute = () => count++ });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Invoke_Runs_Async_Execute_When_No_Sync()
    {
        var tcs = new TaskCompletionSource();
        CommandBindings.Invoke(new Command { Label = "Go", ExecuteAsync = () => { tcs.SetResult(); return Task.CompletedTask; } });
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(tcs.Task.IsCompletedSuccessfully);
    }

    // ════════════════════════════════════════════════════════════════
    //  (e) issue #637 — uniform binding: factory, .Command() modifier,
    //      and a bare `new XxxElement { Command = cmd }` record-init are
    //      equivalent (same Command, same HasCallbacks, ShallowEquals-match).
    //      The public `init` accessor is what makes the bare-init path legal.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void BareInit_And_With_Compile_Via_Public_Init()
    {
        // Acceptance: the Command init accessor is public again, so these compile.
        var cmd = MakeCmd();
        var bare = new ButtonElement("Save") { Command = cmd };
        var withed = new ButtonElement("Save") with { Command = cmd };
        Assert.Same(cmd, bare.Command);
        Assert.Same(cmd, withed.Command);
    }

    [Fact]
    public void BareInit_Equivalent_To_Factory_AllSix()
    {
        var cmd = MakeCmd();
        Assert.True(Element.ShallowEquals(Button(cmd), new ButtonElement("Save") { Command = cmd }));
        Assert.True(Element.ShallowEquals(HyperlinkButton(cmd), new HyperlinkButtonElement("Save") { Command = cmd }));
        Assert.True(Element.ShallowEquals(RepeatButton(cmd), new RepeatButtonElement("Save") { Command = cmd }));
        Assert.True(Element.ShallowEquals(ToggleButton(cmd), new ToggleButtonElement("Save") { Command = cmd }));
        Assert.True(Element.ShallowEquals(SplitButton(cmd), new SplitButtonElement("Save") { Command = cmd }));
        Assert.True(Element.ShallowEquals(ToggleSplitButton(cmd), new ToggleSplitButtonElement("Save") { Command = cmd }));
    }

    [Fact]
    public void Modifier_Equivalent_To_Factory_ForBareLabelButtons()
    {
        var cmd = MakeCmd();
        // .Command() on a plain label button matches the Xxx(cmd) factory exactly.
        Assert.True(Element.ShallowEquals(Button(cmd), Button("Save").Command(cmd)));
        Assert.True(Element.ShallowEquals(HyperlinkButton(cmd), HyperlinkButton("Save").Command(cmd)));
        Assert.True(Element.ShallowEquals(RepeatButton(cmd), RepeatButton("Save").Command(cmd)));
        Assert.True(Element.ShallowEquals(ToggleButton(cmd), ToggleButton("Save").Command(cmd)));
    }

    [Fact]
    public void HasCallbacks_True_For_BareInit_Command_AllSix()
    {
        var cmd = MakeCmd();
        Assert.True(new ButtonElement("Save") { Command = cmd }.HasCallbacks);
        Assert.True(new HyperlinkButtonElement("Save") { Command = cmd }.HasCallbacks);
        Assert.True(new RepeatButtonElement("Save") { Command = cmd }.HasCallbacks);
        Assert.True(new ToggleButtonElement("Save") { Command = cmd }.HasCallbacks);
        Assert.True(new SplitButtonElement("Save") { Command = cmd }.HasCallbacks);
        Assert.True(new ToggleSplitButtonElement("Save") { Command = cmd }.HasCallbacks);
    }

    [Fact]
    public void HasCallbacks_False_For_Plain_Buttons_With_No_Command_Or_Handler()
    {
        // The #153 fast-path Tag refresh is gated on HasCallbacks: a handler-less,
        // command-less button must NOT be tagged (the win the issue protects).
        Assert.False(Button("Save").HasCallbacks);
        Assert.False(HyperlinkButton("Save").HasCallbacks);
        Assert.False(RepeatButton("Save").HasCallbacks);
        Assert.False(ToggleButton("Save").HasCallbacks);
        Assert.False(SplitButton("Save").HasCallbacks);
        Assert.False(ToggleSplitButton("Save").HasCallbacks);
    }

    [Fact]
    public void HasCallbacks_Transition_Command_To_None_Breaks_FastPath_Gate()
    {
        // Button(cmd) → Button("x") flips HasCallbacks true→false, so the reconciler's
        // `oldEl.HasCallbacks == newEl.HasCallbacks` gate fails and a full Update runs
        // (which clears the stale command metadata) — same as the pre-#637 OnClick path.
        var cmd = MakeCmd();
        Assert.True(Button(cmd).HasCallbacks);
        Assert.False(Button("Save").HasCallbacks);
    }

    [Fact]
    public void Button_EffectiveIsEnabled_Folds_Command_Across_All_Paths()
    {
        var disabled = new Command { Label = "Save", Execute = () => { }, CanExecute = false };
        var enabled = new Command { Label = "Save", Execute = () => { }, CanExecute = true };

        // Factory, modifier, and bare-init all disable identically when the command is disabled.
        Assert.False(Button(disabled).EffectiveIsEnabled);
        Assert.False(Button("Save").Command(disabled).EffectiveIsEnabled);
        Assert.False(new ButtonElement("Save") { Command = disabled }.EffectiveIsEnabled);

        // Enabled command ⇒ enabled.
        Assert.True(Button(enabled).EffectiveIsEnabled);
        Assert.True(new ButtonElement("Save") { Command = enabled }.EffectiveIsEnabled);

        // Explicit record IsEnabled=false wins regardless of the command.
        Assert.False(new ButtonElement("Save") { IsEnabled = false, Command = enabled }.EffectiveIsEnabled);

        // No command ⇒ just the record field.
        Assert.True(new ButtonElement("Save").EffectiveIsEnabled);
        Assert.False(new ButtonElement("Save") { IsEnabled = false }.EffectiveIsEnabled);
    }

    // ════════════════════════════════════════════════════════════════
    //  (f) Allocation budget — the #153 win, re-pinned for #637.
    //      Dropping the factory-baked OnClick closure (issue #637) HALVED
    //      the per-construct allocation (≈176 B → ≈88 B on ARM64). The
    //      tightened ceiling FAILS if a per-render closure/array is
    //      reintroduced (which would push it back to ≈176 B).
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Button_Command_PerConstruct_Allocation_UnderBudget()
    {
        var cmd = MakeCmd();

        // Warm-up: JIT the factory + GC.GetAllocatedBytesForCurrentThread path.
        for (int i = 0; i < 1000; i++)
            GC.KeepAlive(Button(cmd));

        const int N = 50_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < N; i++)
            GC.KeepAlive(Button(cmd));
        long after = GC.GetAllocatedBytesForCurrentThread();

        double perConstruct = (after - before) / (double)N;

        // ≈88 B after #637 (just the record). A reintroduced OnClick closure (≈88 B)
        // would push this to ≈176 B, so a 140 B ceiling catches the regression while
        // leaving CI-jitter headroom.
        Assert.True(perConstruct < 140,
            $"Button(command) per-construct allocation regressed: {perConstruct:F1} B (expected < 140 B).");
    }
}
