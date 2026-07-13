using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Patterns;

class CommandsPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (status, setStatus) = UseState("Ready");
        var (fires, setFires) = UseState(0);

        // Sync command bound straight to a Button via the Button(Command) factory.
        var increment = new Command { Label = "Increment", Execute = () => setCount(count + 1) };

        // Async command: UseCommand auto-tracks IsExecuting and guards re-entrance,
        // so the button disables itself while the operation runs.
        var save = UseCommand(new Command
        {
            Label = "Save",
            ExecuteAsync = async () =>
            {
                setStatus("Saving...");
                await Task.Delay(1500);
                setStatus("Saved!");
            }
        });

        // Debounced command: leading-edge; must flow through UseCommand so the
        // debounce window persists across renders.
        var debounced = UseCommand(new Command
        {
            Label = "Debounced (1s)",
            Execute = () => setFires(fires + 1),
            DebounceMs = 1000,
        });

        return ScrollView(VStack(16,
            PageHeader("Commands", "Bind actions declaratively with Command — enablement, async tracking, and debounce come built in."),

            SampleCard("Sync command",
                VStack(8,
                    Button(increment),
                    TextBlock($"Count: {count}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
var increment = new Command { Label = ""Increment"", Execute = () => setCount(count + 1) };
Button(increment)   // Label + Execute + IsEnabled all flow from the Command
"),

            SampleCard("Async command — UseCommand tracks IsExecuting",
                VStack(8,
                    HStack(8,
                        Button(save),
                        save.IsExecuting ? ProgressRing().Size(20, 20) : Empty()),
                    TextBlock($"Status: {status}").Foreground(Theme.SecondaryText),
                    Caption($"IsExecuting: {save.IsExecuting}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
var save = UseCommand(new Command
{
    Label = ""Save"",
    ExecuteAsync = async () =>
    {
        setStatus(""Saving...""); await Task.Delay(1500); setStatus(""Saved!"");
    }
});
Button(save)   // auto-disables for the lifetime of the async lambda
"),

            SampleCard("Debounced command — DebounceMs",
                VStack(8,
                    Button(debounced),
                    TextBlock($"Accepted fires: {fires}").Foreground(Theme.SecondaryText),
                    Caption("Rapid clicks within 1s collapse to a single fire; the button disables during the window.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// DebounceMs only debounces when wrapped by UseCommand (it persists the window).
var run = UseCommand(new Command { Label = ""Run"", Execute = Run, DebounceMs = 1000 });
Button(run)
")
        ).Margin(36, 24, 36, 36));
    }
}
