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
}
