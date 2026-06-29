using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Mount-based fixtures for Phase 5 commanding coverage (spec 027 Tier 4).
/// Each fixture mounts a command-driven control, raises the native Click / toggle
/// event, and verifies the <see cref="Command"/> runs plus that Description /
/// AccessKey metadata flowed through to the mounted control.
/// </summary>
internal static class CommandingCoverageFixtures
{
    private static int _primaryClickCount;

    internal class SplitButtonCommandInvokesExecute(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            _primaryClickCount = 0;
            var cmd = new Command
            {
                Label = "Save",
                Execute = () => _primaryClickCount++,
                Description = "Saves the current doc",
                AccessKey = "S",
            };

            var host = H.CreateHost();
            host.Mount(ctx => SplitButton(cmd).Set(sb => sb.Name = "splitCmdBtn"));
            await Harness.Render();

            var sb = H.FindControl<SplitButton>(b => b.Name == "splitCmdBtn");
            H.Check("SplitButton_Command_Mounted", sb is not null);
            H.Check("SplitButton_Command_LabelContent", sb is not null && (sb.Content as string) == "Save");
            H.Check("SplitButton_Command_IsEnabled", sb is not null && sb.IsEnabled);
            H.Check("SplitButton_Command_AccessKeyFlowed", sb is not null && sb.AccessKey == "S");
        }
    }

    internal class HyperlinkButtonCommandInvokesExecute(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Details", Execute = () => count++ };

            var host = H.CreateHost();
            host.Mount(ctx => HyperlinkButton(cmd).Set(b => b.Name = "hlCmdBtn"));
            await Harness.Render();

            var hb = H.FindControl<HyperlinkButton>(b => b.Name == "hlCmdBtn");
            H.Check("HyperlinkButton_Command_Mounted", hb is not null);
            H.Check("HyperlinkButton_Command_Content", hb is not null && (hb.Content as string) == "Details");
            H.Check("HyperlinkButton_Command_Enabled", hb is not null && hb.IsEnabled);
        }
    }

    internal class ToggleButtonCommandFiresOnToggle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Bold", Execute = () => count++ };

            var host = H.CreateHost();
            host.Mount(ctx => ToggleButton(cmd).Set(b => b.Name = "togCmdBtn"));
            await Harness.Render();

            var tb = H.FindControl<ToggleButton>(b => b.Name == "togCmdBtn");
            H.Check("ToggleButton_Command_Mounted", tb is not null);
            if (tb is not null)
            {
                // OnToggled binds to Click, which fires for real user toggles
                // (mouse, keyboard, and AutomationPeer.Invoke) — programmatic
                // IsChecked writes don't, by design. Simulate user toggles via
                // the toggle automation pattern.
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(tb);
                var toggle = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)
                    as Microsoft.UI.Xaml.Automation.Provider.IToggleProvider;
                toggle?.Toggle();
                toggle?.Toggle();
            }
            H.Check("ToggleButton_Command_InvokedOnEachToggle", count == 2);
        }
    }

    internal class RepeatButtonCommandInvokesExecute(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cmd = new Command
            {
                Label = "Tick",
                Execute = () => { },
                Description = "Tick helper",
                AccessKey = "T",
            };

            var host = H.CreateHost();
            host.Mount(ctx => RepeatButton(cmd).Set(b => b.Name = "repCmdBtn"));
            await Harness.Render();

            var rb = H.FindControl<RepeatButton>(b => b.Name == "repCmdBtn");
            H.Check("RepeatButton_Command_Mounted", rb is not null);
            H.Check("RepeatButton_Command_AccessKeyFlowed", rb is not null && rb.AccessKey == "T");
            H.Check("RepeatButton_Command_IsEnabled", rb is not null && rb.IsEnabled);
        }
    }

    internal class DisabledCommandDisablesControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cmd = new Command { Label = "Save", Execute = () => { }, CanExecute = false };

            var host = H.CreateHost();
            host.Mount(ctx => SplitButton(cmd).Set(sb => sb.Name = "disabledSplit"));
            await Harness.Render();

            var sb = H.FindControl<SplitButton>(b => b.Name == "disabledSplit");
            H.Check("DisabledCmd_Mounted", sb is not null);
            H.Check("DisabledCmd_DisablesControl", sb is not null && !sb.IsEnabled);
        }
    }

    /// <summary>
    /// Issue #133 regression: a custom-content button bound via the
    /// <c>.Command(command)</c> modifier must re-apply <c>command.IsEnabled</c> to the
    /// live control on every update — not capture it once at construction. Mounts an
    /// icon-style (custom content) button whose command flips from enabled to disabled
    /// across a state-driven re-render and asserts the reused control's IsEnabled tracks it.
    /// </summary>
    internal class CustomContentCommandReappliesIsEnabledOnUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (disabled, setDisabled) = ctx.UseState(false);
                var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = !disabled };
                return VStack(
                    Button("toggleCmdState", () => setDisabled(true)),
                    Button(TextBlock("Run")).Command(cmd).Set(b => b.Name = "cmdContentBtn"));
            });
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "cmdContentBtn");
            H.Check("CmdContent_Mounted", btn is not null);
            H.Check("CmdContent_InitiallyEnabled", btn is not null && btn.IsEnabled);

            H.ClickButton("toggleCmdState");
            await Harness.Render();

            var btn2 = H.FindControl<Button>(b => b.Name == "cmdContentBtn");
            H.Check("CmdContent_Reused", ReferenceEquals(btn, btn2));
            H.Check("CmdContent_DisabledAfterUpdate", btn2 is not null && !btn2.IsEnabled);
        }
    }

    /// <summary>
    /// The HyperlinkButton / RepeatButton / ToggleButton <c>.Command()</c> paths apply
    /// IsEnabled solely through the command-apply descriptor entry (they have no record
    /// IsEnabled prop like ButtonElement). When the bound command's <c>CanExecute</c> flips
    /// across a re-render, <see cref="Command"/> is no longer structurally equal modulo
    /// delegates, so the reconciler runs Update and the <c>OneWay&lt;Command?&gt;</c> entry
    /// re-applies <c>ApplyButtonBaseCommon</c> (issue #153 — typed Command property; replaces
    /// the per-render Setters array that previously forced the re-run). Mount each, flip
    /// CanExecute across a re-render, and assert the reused live control becomes disabled.
    /// </summary>
    internal class HyperlinkButtonCommandReappliesIsEnabledOnUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (disabled, setDisabled) = ctx.UseState(false);
                var cmd = new Command { Label = "Run", Execute = () => { }, CanExecute = !disabled };
                return VStack(
                    Button("toggleHl", () => setDisabled(true)),
                    HyperlinkButton("Run").Command(cmd).Set(b => b.Name = "hlReapplyBtn"));
            });
            await Harness.Render();

            var hb = H.FindControl<HyperlinkButton>(b => b.Name == "hlReapplyBtn");
            H.Check("HlReapply_InitiallyEnabled", hb is not null && hb.IsEnabled);

            H.ClickButton("toggleHl");
            await Harness.Render();

            var hb2 = H.FindControl<HyperlinkButton>(b => b.Name == "hlReapplyBtn");
            H.Check("HlReapply_Reused", ReferenceEquals(hb, hb2));
            H.Check("HlReapply_DisabledAfterUpdate", hb2 is not null && !hb2.IsEnabled);
        }
    }

    internal class RepeatButtonCommandReappliesIsEnabledOnUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (disabled, setDisabled) = ctx.UseState(false);
                var cmd = new Command { Label = "Tick", Execute = () => { }, CanExecute = !disabled };
                return VStack(
                    Button("toggleRep", () => setDisabled(true)),
                    RepeatButton("Tick").Command(cmd).Set(b => b.Name = "repReapplyBtn"));
            });
            await Harness.Render();

            var rb = H.FindControl<RepeatButton>(b => b.Name == "repReapplyBtn");
            H.Check("RepReapply_InitiallyEnabled", rb is not null && rb.IsEnabled);

            H.ClickButton("toggleRep");
            await Harness.Render();

            var rb2 = H.FindControl<RepeatButton>(b => b.Name == "repReapplyBtn");
            H.Check("RepReapply_Reused", ReferenceEquals(rb, rb2));
            H.Check("RepReapply_DisabledAfterUpdate", rb2 is not null && !rb2.IsEnabled);
        }
    }

    internal class ToggleButtonCommandReappliesIsEnabledOnUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (disabled, setDisabled) = ctx.UseState(false);
                var cmd = new Command { Label = "Bold", Execute = () => { }, CanExecute = !disabled };
                return VStack(
                    Button("toggleTog", () => setDisabled(true)),
                    ToggleButton("Bold").Command(cmd).Set(b => b.Name = "togReapplyBtn"));
            });
            await Harness.Render();

            var tb = H.FindControl<ToggleButton>(b => b.Name == "togReapplyBtn");
            H.Check("TogReapply_InitiallyEnabled", tb is not null && tb.IsEnabled);

            H.ClickButton("toggleTog");
            await Harness.Render();

            var tb2 = H.FindControl<ToggleButton>(b => b.Name == "togReapplyBtn");
            H.Check("TogReapply_Reused", ReferenceEquals(tb, tb2));
            H.Check("TogReapply_DisabledAfterUpdate", tb2 is not null && !tb2.IsEnabled);
        }
    }

    /// <summary>
    /// PR review M1: a disabled command bound via <c>.Command()</c> must not override
    /// <c>.IsDisabledFocusable()</c> — the button stays IsEnabled=true (reachable via Tab,
    /// click suppressed by the trampoline) and dimmed (Opacity 0.4). Pinned in both modifier
    /// orderings since the fix is descriptor/record-owned, not capture-order dependent.
    /// </summary>
    internal class CommandDisabledFocusableStaysFocusable(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cmd = new Command { Label = "Submit", Execute = () => { }, CanExecute = false };

            var host = H.CreateHost();
            host.Mount(ctx => Button(TextBlock("Submit"))
                .Command(cmd)
                .IsDisabledFocusable()
                .Set(b => b.Name = "cmdDfBtn"));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "cmdDfBtn");
            H.Check("CmdDf_Mounted", btn is not null);
            // Disabled command + IsDisabledFocusable: must stay enabled (focusable) despite the
            // disabled command — the command setter must not clobber the descriptor coercion.
            H.Check("CmdDf_StaysFocusable", btn is not null && btn.IsEnabled);
            H.Check("CmdDf_Dimmed", btn is not null && global::System.Math.Abs(btn.Opacity - 0.4) < 0.001);
        }
    }

    internal class CommandDisabledFocusableStaysFocusableReverseOrder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cmd = new Command { Label = "Submit", Execute = () => { }, CanExecute = false };

            var host = H.CreateHost();
            host.Mount(ctx => Button(TextBlock("Submit"))
                .IsDisabledFocusable()
                .Command(cmd)
                .Set(b => b.Name = "cmdDfRevBtn"));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "cmdDfRevBtn");
            H.Check("CmdDfRev_Mounted", btn is not null);
            H.Check("CmdDfRev_StaysFocusable", btn is not null && btn.IsEnabled);
            H.Check("CmdDfRev_Dimmed", btn is not null && global::System.Math.Abs(btn.Opacity - 0.4) < 0.001);
        }
    }

    /// <summary>
    /// Issue #153: the <c>Button(Command)</c> factory lowers Command to a typed property,
    /// applied by a descriptor entry. When the bound command changes across a re-render, the
    /// command metadata (AccessKey, IsEnabled) must update on the reused live control.
    /// </summary>
    internal class BoundButtonCommandChangeUpdatesMetadata(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (flipped, setFlipped) = ctx.UseState(false);
                var cmd = flipped
                    ? new Command { Label = "Open", Execute = () => { }, AccessKey = "D", CanExecute = false }
                    : new Command { Label = "Open", Execute = () => { }, AccessKey = "S", CanExecute = true };
                return VStack(
                    Button("flipCmd", () => setFlipped(true)),
                    Button(cmd).Set(b => b.Name = "cmdChangeBtn"));
            });
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "cmdChangeBtn");
            H.Check("CmdChange_Mounted", btn is not null);
            H.Check("CmdChange_InitialAccessKey", btn is not null && btn.AccessKey == "S");
            H.Check("CmdChange_InitiallyEnabled", btn is not null && btn.IsEnabled);

            H.ClickButton("flipCmd");
            await Harness.Render();

            var btn2 = H.FindControl<Button>(b => b.Name == "cmdChangeBtn");
            H.Check("CmdChange_Reused", ReferenceEquals(btn, btn2));
            H.Check("CmdChange_AccessKeyUpdated", btn2 is not null && btn2.AccessKey == "D");
            H.Check("CmdChange_DisabledAfterUpdate", btn2 is not null && !btn2.IsEnabled);
        }
    }

    /// <summary>
    /// Issue #153 fast-path proof: when a command-bound button re-renders with a Command that
    /// is structurally equal modulo its Execute/ExecuteAsync delegates (a fresh instance each
    /// render with identical rendered fields but a new closure), <see cref="Element.ShallowEquals"/>
    /// returns true and the reconciler skips the command-apply entry entirely. Observable proof:
    /// <c>ApplyButtonBaseCommon</c> removes+re-adds a NEW <c>KeyboardAccelerator</c> instance when
    /// it runs, so a reference-equal accelerator across the re-render proves it did NOT run.
    /// </summary>
    internal class BoundButtonUnchangedCommandSkipsReapply(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                // Fresh Command each render: identical rendered fields, brand-new Execute
                // delegate. Structurally equal modulo delegates ⇒ ShallowEquals fast-paths.
                var cmd = new Command
                {
                    Label = "Open",
                    Execute = () => { },
                    Accelerator = new KeyboardAcceleratorData(
                        global::Windows.System.VirtualKey.O, global::Windows.System.VirtualKeyModifiers.Control),
                    Description = "Open a file",
                };
                return VStack(
                    Button("bumpFastPath", () => setN(n + 1)),
                    Button(cmd));  // no .Set — a fresh Setters array each render would defeat ShallowEquals
            });
            await Harness.Render();

            var btn = H.FindControl<Button>(b => (b.Content as string) == "Open");
            H.Check("FastPath_Mounted", btn is not null && btn.KeyboardAccelerators.Count == 1);
            var accel0 = btn is { KeyboardAccelerators.Count: 1 } ? btn.KeyboardAccelerators[0] : null;

            H.ClickButton("bumpFastPath");
            await Harness.Render();

            var btn2 = H.FindControl<Button>(b => (b.Content as string) == "Open");
            H.Check("FastPath_Reused", ReferenceEquals(btn, btn2));
            H.Check("FastPath_SkippedReapply",
                btn2 is not null && accel0 is not null
                && btn2.KeyboardAccelerators.Count == 1
                && ReferenceEquals(btn2.KeyboardAccelerators[0], accel0));
        }
    }

    /// <summary>
    /// Issue #153 (M1) precedence pin: a raw <c>.Set(...)</c> setter applies after the typed
    /// Command's descriptor metadata, so it overrides command-derived metadata regardless of where
    /// it sits in the fluent chain. Both <c>.Set(...).Command(cmd)</c> and <c>.Command(cmd).Set(...)</c>
    /// must resolve AccessKey to the Setter's value (the documented "Setters apply last / win" rule).
    /// </summary>
    internal class BoundButtonSetterOverridesCommandMetadata(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cmd = new Command { Label = "Save", Execute = () => { }, AccessKey = "S" };

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                // Setter before .Command — Setter must still win (applied last).
                Button("setThenCmd").Set(b => b.AccessKey = "X").Command(cmd),
                // .Command before Setter — Setter wins (normal chain order).
                Button("cmdThenSet").Command(cmd).Set(b => b.AccessKey = "Y")));
            await Harness.Render();

            var setThenCmd = H.FindControl<Button>(b => (b.Content as string) == "setThenCmd");
            var cmdThenSet = H.FindControl<Button>(b => (b.Content as string) == "cmdThenSet");
            H.Check("Precedence_SetThenCmd_Mounted", setThenCmd is not null);
            H.Check("Precedence_SetThenCmd_SetterWins", setThenCmd is not null && setThenCmd.AccessKey == "X");
            H.Check("Precedence_CmdThenSet_Mounted", cmdThenSet is not null);
            H.Check("Precedence_CmdThenSet_SetterWins", cmdThenSet is not null && cmdThenSet.AccessKey == "Y");
        }
    }

    /// <summary>
    /// Issue #153 (M3): the <c>SplitButton(Command)</c> typed property is applied by a descriptor
    /// entry; when the bound command changes across a re-render, the command metadata (AccessKey,
    /// IsEnabled) must update on the reused live control. Mirrors
    /// <see cref="BoundButtonCommandChangeUpdatesMetadata"/>.
    /// </summary>
    internal class BoundSplitButtonCommandChangeUpdatesMetadata(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (flipped, setFlipped) = ctx.UseState(false);
                var cmd = flipped
                    ? new Command { Label = "Save", Execute = () => { }, AccessKey = "D", CanExecute = false }
                    : new Command { Label = "Save", Execute = () => { }, AccessKey = "S", CanExecute = true };
                return VStack(
                    Button("flipSplitCmd", () => setFlipped(true)),
                    SplitButton(cmd).Set(b => b.Name = "splitCmdChangeBtn"));
            });
            await Harness.Render();

            var sb = H.FindControl<SplitButton>(b => b.Name == "splitCmdChangeBtn");
            H.Check("SplitCmdChange_Mounted", sb is not null);
            H.Check("SplitCmdChange_InitialAccessKey", sb is not null && sb.AccessKey == "S");
            H.Check("SplitCmdChange_InitiallyEnabled", sb is not null && sb.IsEnabled);

            H.ClickButton("flipSplitCmd");
            await Harness.Render();

            var sb2 = H.FindControl<SplitButton>(b => b.Name == "splitCmdChangeBtn");
            H.Check("SplitCmdChange_Reused", ReferenceEquals(sb, sb2));
            H.Check("SplitCmdChange_AccessKeyUpdated", sb2 is not null && sb2.AccessKey == "D");
            H.Check("SplitCmdChange_DisabledAfterUpdate", sb2 is not null && !sb2.IsEnabled);
        }
    }

    /// <summary>
    /// Issue #153 (M3): same live command-change reapply check as
    /// <see cref="BoundSplitButtonCommandChangeUpdatesMetadata"/>, for <c>ToggleSplitButton(Command)</c>.
    /// </summary>
    internal class BoundToggleSplitButtonCommandChangeUpdatesMetadata(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (flipped, setFlipped) = ctx.UseState(false);
                var cmd = flipped
                    ? new Command { Label = "Pin", Execute = () => { }, AccessKey = "D", CanExecute = false }
                    : new Command { Label = "Pin", Execute = () => { }, AccessKey = "S", CanExecute = true };
                return VStack(
                    Button("flipToggleSplitCmd", () => setFlipped(true)),
                    ToggleSplitButton(cmd).Set(b => b.Name = "toggleSplitCmdChangeBtn"));
            });
            await Harness.Render();

            var tsb = H.FindControl<ToggleSplitButton>(b => b.Name == "toggleSplitCmdChangeBtn");
            H.Check("ToggleSplitCmdChange_Mounted", tsb is not null);
            H.Check("ToggleSplitCmdChange_InitialAccessKey", tsb is not null && tsb.AccessKey == "S");
            H.Check("ToggleSplitCmdChange_InitiallyEnabled", tsb is not null && tsb.IsEnabled);

            H.ClickButton("flipToggleSplitCmd");
            await Harness.Render();

            var tsb2 = H.FindControl<ToggleSplitButton>(b => b.Name == "toggleSplitCmdChangeBtn");
            H.Check("ToggleSplitCmdChange_Reused", ReferenceEquals(tsb, tsb2));
            H.Check("ToggleSplitCmdChange_AccessKeyUpdated", tsb2 is not null && tsb2.AccessKey == "D");
            H.Check("ToggleSplitCmdChange_DisabledAfterUpdate", tsb2 is not null && !tsb2.IsEnabled);
        }
    }

    /// <summary>
    /// Issue #153 (PR review): when a command-bound button is re-rendered <em>without</em> a Command
    /// (the typed Command transitions to null on the reused live control), the descriptor's command
    /// entry must clear the stale command metadata — tooltip / UIA HelpText, AccessKey, and the
    /// command-added <see cref="KeyboardAccelerator"/> — rather than leaving it stuck.
    /// </summary>
    internal class BoundButtonCommandClearedWhenRemoved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (removed, setRemoved) = ctx.UseState(false);
                return VStack(
                    Button("removeCmd", () => setRemoved(true)),
                    removed
                        ? Button("Open").Set(b => b.Name = "cmdClearBtn")
                        : Button(new Command
                        {
                            Label = "Open",
                            Execute = () => { },
                            AccessKey = "S",
                            Accelerator = new KeyboardAcceleratorData(
                                global::Windows.System.VirtualKey.O, global::Windows.System.VirtualKeyModifiers.Control),
                            Description = "Open a file",
                        }).Set(b => b.Name = "cmdClearBtn"));
            });
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "cmdClearBtn");
            H.Check("CmdClear_Mounted", btn is not null);
            H.Check("CmdClear_InitialAccessKey", btn is not null && btn.AccessKey == "S");
            H.Check("CmdClear_InitialAccelerator", btn is not null && btn.KeyboardAccelerators.Count == 1);
            H.Check("CmdClear_InitialToolTip", btn is not null && ToolTipService.GetToolTip(btn) is not null);

            H.ClickButton("removeCmd");
            await Harness.Render();

            var btn2 = H.FindControl<Button>(b => b.Name == "cmdClearBtn");
            H.Check("CmdClear_Reused", ReferenceEquals(btn, btn2));
            H.Check("CmdClear_AccessKeyCleared", btn2 is not null && string.IsNullOrEmpty(btn2.AccessKey));
            H.Check("CmdClear_AcceleratorCleared", btn2 is not null && btn2.KeyboardAccelerators.Count == 0);
            H.Check("CmdClear_ToolTipCleared", btn2 is not null && ToolTipService.GetToolTip(btn2) is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #637 — bare record-init / `with` Command binding equivalence.
    //  A `new XxxElement { Command = cmd }` (no factory, no `.Command()` modifier)
    //  must invoke the command on click/toggle AND apply IsEnabled identically to
    //  the factory path, and must preserve Button's IsDisabledFocusable coercion.
    //  These mount the LIVE control and raise real Click/Toggle events — the unit
    //  tests pin the record shape; these prove the trampoline is actually wired and
    //  fires for the bare-init path (the former "metadata-only silent footgun").
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Issue #637: a bare <c>new ButtonElement { Command = cmd }</c> (no factory / modifier)
    /// wires the Click trampoline and invokes the command — previously the bare record carried
    /// metadata but never dispatched on click.
    /// </summary>
    internal class BareInitButtonCommandInvokesExecute(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Save", Execute = () => count++, AccessKey = "S" };

            var host = H.CreateHost();
            host.Mount(ctx => new ButtonElement("Save") { Command = cmd }.Set(b => b.Name = "bareCmdBtn"));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "bareCmdBtn");
            H.Check("BareInitBtn_Mounted", btn is not null);
            H.Check("BareInitBtn_Enabled", btn is not null && btn.IsEnabled);
            H.Check("BareInitBtn_AccessKeyFlowed", btn is not null && btn.AccessKey == "S");

            H.ClickButton("Save");
            H.Check("BareInitBtn_CommandInvokedOnClick", count == 1);
        }
    }

    /// <summary>
    /// Issue #637: a bare <c>new ToggleButtonElement { Command = cmd }</c> invokes the command on
    /// each real user toggle (check + uncheck) via the toggle trampoline's Command fallback.
    /// </summary>
    internal class BareInitToggleButtonCommandFiresOnToggle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Bold", Execute = () => count++ };

            var host = H.CreateHost();
            host.Mount(ctx => new ToggleButtonElement("Bold") { Command = cmd }.Set(b => b.Name = "bareTogBtn"));
            await Harness.Render();

            var tb = H.FindControl<ToggleButton>(b => b.Name == "bareTogBtn");
            H.Check("BareInitTog_Mounted", tb is not null);
            H.Check("BareInitTog_Enabled", tb is not null && tb.IsEnabled);
            if (tb is not null)
            {
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(tb);
                var toggle = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)
                    as Microsoft.UI.Xaml.Automation.Provider.IToggleProvider;
                toggle?.Toggle();
                toggle?.Toggle();
            }
            H.Check("BareInitTog_CommandInvokedOnEachToggle", count == 2);
        }
    }

    /// <summary>
    /// Issue #637: <c>plain with { Command = cmd }</c> is equivalent to the factory — attaching a
    /// command to a previously plain button via a <c>with</c>-expression wires dispatch on click.
    /// </summary>
    internal class WithCommandButtonInvokesExecute(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Go", Execute = () => count++ };
            var plain = new ButtonElement("Go");
            var bound = plain with { Command = cmd };

            var host = H.CreateHost();
            host.Mount(ctx => bound.Set(b => b.Name = "withCmdBtn"));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "withCmdBtn");
            H.Check("WithCmdBtn_Mounted", btn is not null);
            H.ClickButton("Go");
            H.Check("WithCmdBtn_CommandInvokedOnClick", count == 1);
        }
    }

    /// <summary>
    /// Issue #637: a bare-init Button with a disabled command applies <c>IsEnabled=false</c> to the
    /// live control (the command's enabled state is folded into <c>EffectiveIsEnabled</c>), so the
    /// command never runs while disabled.
    /// </summary>
    internal class BareInitButtonDisabledCommandDisablesControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Save", Execute = () => count++, CanExecute = false };

            var host = H.CreateHost();
            host.Mount(ctx => new ButtonElement("Save") { Command = cmd }.Set(b => b.Name = "bareDisBtn"));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "bareDisBtn");
            H.Check("BareInitDis_Mounted", btn is not null);
            H.Check("BareInitDis_Disabled", btn is not null && !btn.IsEnabled);
            H.ClickButton("Save"); // ClickButton is gated on IsEnabled — disabled button won't invoke
            H.Check("BareInitDis_NotInvokedWhileDisabled", count == 0);
        }
    }

    /// <summary>
    /// Issue #637 acceptance: the bare-init path must preserve Button's IsDisabledFocusable coercion.
    /// A disabled command + <c>IsDisabledFocusable = true</c> on a <c>new ButtonElement { ... }</c>
    /// must keep the control <c>IsEnabled=true</c> (reachable via Tab) and dimmed (Opacity 0.4), and
    /// the click trampoline must suppress the command (IsDisabledFocusable early-return) — exactly as
    /// the factory/modifier path does.
    /// </summary>
    internal class BareInitButtonDisabledFocusableCoercionPreserved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Submit", Execute = () => count++, CanExecute = false };

            var host = H.CreateHost();
            host.Mount(ctx => new ButtonElement("Submit") { Command = cmd, IsDisabledFocusable = true }
                .Set(b => b.Name = "bareDfBtn"));
            await Harness.Render();

            var btn = H.FindControl<Button>(b => b.Name == "bareDfBtn");
            H.Check("BareInitDf_Mounted", btn is not null);
            H.Check("BareInitDf_StaysFocusable", btn is not null && btn.IsEnabled);
            H.Check("BareInitDf_Dimmed", btn is not null && global::System.Math.Abs(btn.Opacity - 0.4) < 0.001);
            H.ClickButton("Submit"); // IsEnabled is true ⇒ Invoke raises Click ⇒ trampoline suppresses the command
            H.Check("BareInitDf_ClickSuppressed", count == 0);
        }
    }

    /// <summary>
    /// Issue #637: bare-init Hyperlink/Repeat/Split buttons invoke their command on a real Invoke
    /// (primary click) — proving the HandCodedEvent subscription gate now treats the typed Command
    /// as a callback source on the bare-init path for all the click-style elements.
    /// </summary>
    internal class BareInitHyperlinkRepeatSplitInvokeExecute(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int hl = 0, rp = 0, sp = 0;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new HyperlinkButtonElement("HLgo") { Command = new Command { Label = "HLgo", Execute = () => hl++ } }.Set(b => b.Name = "bareHlGo"),
                new RepeatButtonElement("RPgo") { Command = new Command { Label = "RPgo", Execute = () => rp++ } }.Set(b => b.Name = "bareRpGo"),
                new SplitButtonElement("SPgo") { Command = new Command { Label = "SPgo", Execute = () => sp++ } }.Set(b => b.Name = "bareSpGo")));
            await Harness.Render();

            InvokeBase(H.FindControl<HyperlinkButton>(b => b.Name == "bareHlGo"));
            InvokeBase(H.FindControl<RepeatButton>(b => b.Name == "bareRpGo"));
            InvokeBase(H.FindControl<SplitButton>(b => b.Name == "bareSpGo"));

            H.Check("BareInitInvoke_Hyperlink", hl == 1);
            H.Check("BareInitInvoke_Repeat", rp == 1);
            H.Check("BareInitInvoke_Split", sp == 1);
        }

        private static void InvokeBase(Microsoft.UI.Xaml.UIElement? b)
        {
            if (b is null) return;
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(b);
            (peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)?.Invoke();
        }
    }

    /// <summary>
    /// Issue #637: the bare-init path applies <c>Command.IsEnabled</c> on the live control for ALL
    /// six command-capable elements. Mounts each of the remaining five (Button is covered separately)
    /// plus ToggleSplitButton with a disabled command and asserts the control mounts disabled.
    /// </summary>
    internal class BareInitAllElementsApplyDisabledFromCommand(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            static Command Dis(string l) => new Command { Label = l, Execute = () => { }, CanExecute = false };

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new ButtonElement("BT") { Command = Dis("BT") }.Set(b => b.Name = "bareAllBt"),
                new HyperlinkButtonElement("HL") { Command = Dis("HL") }.Set(b => b.Name = "bareAllHl"),
                new RepeatButtonElement("RP") { Command = Dis("RP") }.Set(b => b.Name = "bareAllRp"),
                new ToggleButtonElement("TG") { Command = Dis("TG") }.Set(b => b.Name = "bareAllTg"),
                new SplitButtonElement("SP") { Command = Dis("SP") }.Set(b => b.Name = "bareAllSp"),
                new ToggleSplitButtonElement("TS") { Command = Dis("TS") }.Set(b => b.Name = "bareAllTs")));
            await Harness.Render();

            var bt = H.FindControl<Button>(b => b.Name == "bareAllBt");
            var hl = H.FindControl<HyperlinkButton>(b => b.Name == "bareAllHl");
            var rp = H.FindControl<RepeatButton>(b => b.Name == "bareAllRp");
            var tg = H.FindControl<ToggleButton>(b => b.Name == "bareAllTg");
            var sp = H.FindControl<SplitButton>(b => b.Name == "bareAllSp");
            var ts = H.FindControl<ToggleSplitButton>(b => b.Name == "bareAllTs");
            H.Check("BareInitAll_ButtonDisabled", bt is not null && !bt.IsEnabled);
            H.Check("BareInitAll_HyperlinkDisabled", hl is not null && !hl.IsEnabled);
            H.Check("BareInitAll_RepeatDisabled", rp is not null && !rp.IsEnabled);
            H.Check("BareInitAll_ToggleDisabled", tg is not null && !tg.IsEnabled);
            H.Check("BareInitAll_SplitDisabled", sp is not null && !sp.IsEnabled);
            H.Check("BareInitAll_ToggleSplitDisabled", ts is not null && !ts.IsEnabled);
        }
    }

    /// <summary>
    /// Issue #637 acceptance (d) for ToggleSplitButton at the LIVE tier: a bare record-init
    /// <c>new ToggleSplitButtonElement("…") { Command = cmd }</c> with no <c>OnIsCheckedChanged</c>
    /// dispatches the command on each real toggle. The <c>.Controlled</c> callback falls back to
    /// invoking the typed Command when the user callback is null, so a direct <c>IsChecked</c> flip
    /// (treated as real input — no armed echo on an uncontrolled mount) runs Execute. Closes the
    /// previously unproven live-toggle gap for this element.
    /// </summary>
    internal class BareInitToggleSplitButtonCommandFiresOnToggle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "TSgo", Execute = () => count++ };

            var host = H.CreateHost();
            host.Mount(ctx => new ToggleSplitButtonElement("TSgo") { Command = cmd }.Set(b => b.Name = "bareTsBtn"));
            await Harness.Render();

            var tsb = H.FindControl<ToggleSplitButton>(b => b.Name == "bareTsBtn");
            H.Check("BareInitTs_Mounted", tsb is not null);
            H.Check("BareInitTs_Enabled", tsb is not null && tsb.IsEnabled);
            if (tsb is not null)
            {
                // A direct IsChecked flip is real user input (no armed echo on an uncontrolled
                // mount), so IsCheckedChanged fires and the command dispatches — check + uncheck.
                tsb.IsChecked = !tsb.IsChecked;
                await Harness.Render();
                tsb.IsChecked = !tsb.IsChecked;
                await Harness.Render();
            }
            H.Check("BareInitTs_CommandInvokedOnEachToggle", count == 2);
        }
    }

    /// <summary>
    /// Issue #710 review (M4) — the none → command transition at the LIVE tier. A plain
    /// <c>Button("Run")</c> (no command, untagged, Click unsubscribed) is re-rendered as a
    /// command-bound button across a state-driven update. The reconciler reuses the control and
    /// subscribes the Click handler ON UPDATE (HasCallbacks false→true), so a real click then
    /// dispatches the command — proving subscribe-on-update + dispatch, not a manual Invoke.
    /// </summary>
    internal class BareInitNoneToCommandDispatchesOnClickAfterUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            var cmd = new Command { Label = "Run", Execute = () => count++ };

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (bound, setBound) = ctx.UseState(false);
                return VStack(
                    Button("toggleM4Bind", () => setBound(true)),
                    bound
                        ? new ButtonElement("Run") { Command = cmd }
                        : Button("Run"));
            });
            await Harness.Render();

            var before = H.FindControl<Button>(b => (b.Content as string) == "Run");
            H.Check("M4None_Mounted", before is not null);

            // No command yet: the plain button is untagged / Click unsubscribed, so clicking is inert.
            H.ClickButton("Run");
            await Harness.Render();
            H.Check("M4None_InertBeforeBind", count == 0);

            // Re-render WITH the command bound — the reconciler subscribes Click on update.
            H.ClickButton("toggleM4Bind");
            await Harness.Render();

            var after = H.FindControl<Button>(b => (b.Content as string) == "Run");
            H.Check("M4None_Reused", before is not null && ReferenceEquals(before, after));

            // The now-bound command dispatches on a real click (subscribe-on-update worked).
            H.ClickButton("Run");
            await Harness.Render();
            H.Check("M4None_DispatchesAfterBind", count == 1);
        }
    }

    /// <summary>
    /// Issue #710 review (M2) — an isolated <c>IsDisabledFocusable</c> flip must re-apply at the
    /// LIVE tier. A bare <c>new ButtonElement("Submit") { IsEnabled = false }</c> (hard-disabled:
    /// IsEnabled=false, full opacity) flips ONLY IsDisabledFocusable across a state-driven update.
    /// No <c>.Set</c> is used, so the empty Setters array stays reference-stable and
    /// IsDisabledFocusable is the single differing field — without the ShallowEquals fix the
    /// fast-path skipped this and left the control hard-disabled; now the descriptor re-applies the
    /// focusable-dim coercion (IsEnabled=true + Opacity=0.4). The control is reused, so this is a
    /// true re-apply, not a remount. Twin of the debounce M1 re-disable guard.
    /// </summary>
    internal class IsDisabledFocusableReappliesOnIsolatedFlip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (focusable, setFocusable) = ctx.UseState(false);
                return VStack(
                    Button("toggleIdf", () => setFocusable(true)),
                    // No .Set — keep Setters reference-stable so ONLY IsDisabledFocusable differs
                    // across the update (a .Set would allocate a fresh Setters array each render and
                    // defeat the isolation, masking the fast-path fix).
                    new ButtonElement("Submit") { IsEnabled = false, IsDisabledFocusable = focusable });
            });
            await Harness.Render();

            var before = H.FindControl<Button>(b => (b.Content as string) == "Submit");
            H.Check("IdfReapply_Mounted", before is not null);
            H.Check("IdfReapply_InitiallyHardDisabled", before is not null && !before.IsEnabled);

            H.ClickButton("toggleIdf");
            await Harness.Render();

            var after = H.FindControl<Button>(b => (b.Content as string) == "Submit");
            H.Check("IdfReapply_Reused", before is not null && ReferenceEquals(before, after));
            // The isolated flip re-applied: focusable-dim coercion (IsEnabled stays true, dimmed).
            H.Check("IdfReapply_FocusableAfterUpdate", after is not null && after.IsEnabled);
            H.Check("IdfReapply_DimmedAfterUpdate", after is not null && global::System.Math.Abs(after.Opacity - 0.4) < 0.001);
        }
    }
}
