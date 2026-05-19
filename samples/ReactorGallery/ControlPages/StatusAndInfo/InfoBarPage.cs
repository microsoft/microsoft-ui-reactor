using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.StatusAndInfo;

class InfoBarPage : Component
{
    public override Element Render()
    {
        var (showClosable, setShowClosable) = UseState(true);

        return ScrollView(
            VStack(16,
                PageHeader("InfoBar",
                    "A dismissible bar for displaying essential app-level messages."),

                // Phase 8.1 — severity fluents (spec 039 §17.4).
                SampleCard("Severity fluents",
                    VStack(8,
                        InfoBar("Informational", "This is an informational message.")
                            .Informational()
                            .Closable(false),
                        InfoBar("Success", "Operation completed successfully.")
                            .Success()
                            .Closable(false),
                        InfoBar("Warning", "Please review before proceeding.")
                            .Warning()
                            .Closable(false),
                        InfoBar("Error", "Something went wrong.")
                            .Error()
                            .Closable(false)
                    ),
                    @"InfoBar(""Success"", ""Completed!"").Success().Closable(false)
InfoBar(""Warning"", ""Review..."").Warning().Closable(false)
InfoBar(""Error"",   ""Failed!""  ).Error()  .Closable(false)
InfoBar(""Info"",    ""FYI…""     ).Informational().Closable(false)"),

                SampleCard("Closable InfoBar",
                    VStack(8,
                        showClosable
                            ? InfoBar("Closable", "Click the close button to dismiss.").Closable()
                            : TextBlock("InfoBar was closed.").Foreground(Theme.SecondaryText),
                        Button("Reset", () => setShowClosable(true))
                    ),
                    @"InfoBar(""Closable"", ""Click close to dismiss."").Closable()")
            ).Margin(36, 24, 36, 36)
        );
    }
}
