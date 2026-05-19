using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

class CommandingTestDemo : Component
{
    public override Element Render()
    {
        var (log, setLog) = UseState<string[]>([]);

        void Log(string msg) => setLog([.. log, msg]);

        // StandardCommand — pre-built with label, icon, accelerator
        var copyCmd = StandardCommand.Copy(() => Log("Copy executed (Ctrl+C)"));
        var pasteCmd = StandardCommand.Paste(() => Log("Paste executed (Ctrl+V)"));
        var saveCmd = StandardCommand.Save(() => Log("Save executed (Ctrl+S)"));

        // Custom Command with accelerator
        var customCmd = new Command
        {
            Label = "Custom Action",
            Execute = () => Log("Custom action executed (Ctrl+Shift+K)"),
            Accelerator = Accelerator(global::Windows.System.VirtualKey.K,
                global::Windows.System.VirtualKeyModifiers.Control | global::Windows.System.VirtualKeyModifiers.Shift),
        };

        // Async command with UseCommand for IsExecuting tracking
        var asyncCmd = UseCommand(new Command
        {
            Label = "Async Task",
            ExecuteAsync = async () =>
            {
                Log("Async started...");
                await Task.Delay(1500);
                Log("Async completed!");
            },
        });

        // Parameterized command
        var deleteCmd = new Command<string>
        {
            Label = "Delete",
            Execute = item => Log($"Deleted: {item}"),
        };

        var clearLogCmd = new Command
        {
            Label = "Clear Log",
            Execute = () => setLog([]),
            CanExecute = log.Length > 0,
        };

        return CommandHost([copyCmd, pasteCmd, saveCmd, customCmd],
            VStack(12,
                Heading("Commanding Demo"),

                // Standard commands
                SubHeading("StandardCommand (built-in label, icon, accelerator)"),
                HStack(8,
                    Button(copyCmd),
                    Button(pasteCmd),
                    Button(saveCmd)
                ),

                // Custom command with accelerator
                SubHeading("Command (custom accelerator: Ctrl+Shift+K)"),
                Button(customCmd),

                // Async command
                SubHeading("UseCommand (async with IsExecuting tracking)"),
                HStack(8,
                    Button(asyncCmd),
                    When(asyncCmd.IsExecuting, () =>
                        ProgressRing().Width(20).Height(20).IsActive(true))
                ),

                // Parameterized command
                SubHeading("Parameterized Command<T>"),
                HStack(8,
                    Button("Delete 'Alpha'", () => deleteCmd.Execute?.Invoke("Alpha")),
                    Button("Delete 'Beta'", () => deleteCmd.Execute?.Invoke("Beta"))
                ),

                // Log
                SubHeading("Event Log"),
                HStack(8,
                    Button(clearLogCmd),
                    TextBlock($"{log.Length} event(s)").Foreground(TertiaryText)
                        .VAlign(VerticalAlignment.Center)
                ),
                VStack(2,
                    log.TakeLast(10).Select(entry =>
                        Caption($"\u2192 {entry}").Foreground(SecondaryText)
                    ).ToArray()
                )
            )
        );
    }
}
