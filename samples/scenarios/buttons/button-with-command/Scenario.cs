// id: button-with-command
// intent: button driven by a Command object (label, execute, canExecute)
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("ButtonWithCommand", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (runs, setRuns) = UseState(0);
        var save = new Command
        {
            Label = runs < 3 ? $"Save ({3 - runs} left)" : "Done",
            Execute = () => setRuns(runs + 1),
            CanExecute = runs < 3
        };

        return VStack(12, Button(save), TextBlock($"Executed: {runs}"))
            .Padding(24);
    }
}